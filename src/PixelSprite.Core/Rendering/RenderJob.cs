using System.Globalization;
using System.Text;
using PixelSprite.Core.Models;
using PixelSprite.Core.PixelArt;
using Silk.NET.Assimp;
using AssimpApi = Silk.NET.Assimp.Assimp;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Runs the full render pipeline for one model: loads the scene once, then loops over every
/// direction × frame, rendering each and converting it to pixel art.
/// </summary>
public sealed class RenderJob
{
    /// <summary>
    /// Executes the pipeline and returns one <see cref="SpriteFrame"/> per (direction, frame),
    /// ordered direction-major then frame.
    /// </summary>
    /// <param name="renderOpts">Render options (input model, directions, resolution, fps, camera).</param>
    /// <param name="pixelOpts">Pixel-art options applied to every rendered frame.</param>
    /// <param name="progress">Optional callback for per-frame progress messages.</param>
    /// <returns>The materialized list of processed sprite frames.</returns>
    /// <summary>
    /// Executes the pipeline with no equipment. Equivalent to passing a null equipment manifest.
    /// </summary>
    public IEnumerable<SpriteFrame> Execute(
        RenderOptions renderOpts, PixelArtOptions pixelOpts, Action<string>? progress = null)
        => Execute(renderOpts, pixelOpts, equipment: null, progress);

    /// <summary>
    /// Executes the full pipeline: loads the scene once, optionally equips attachments from
    /// <paramref name="equipment"/>, then loops over every direction × frame.
    /// </summary>
    /// <param name="equipment">Optional equipment manifest (Unreal socket / master-pose attachments).</param>
    public IEnumerable<SpriteFrame> Execute(
        RenderOptions renderOpts, PixelArtOptions pixelOpts, EquipmentManifest? equipment, Action<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(renderOpts);
        ArgumentNullException.ThrowIfNull(pixelOpts);

        // Materialized rather than lazily yielded: the Assimp scene is an unmanaged pointer and C#
        // iterators cannot carry pointer locals across yield boundaries. The frame set is small.
        return RenderAll(renderOpts, pixelOpts, equipment, progress);
    }

    private static unsafe List<SpriteFrame> RenderAll(
        RenderOptions renderOpts, PixelArtOptions pixelOpts, EquipmentManifest? equipment, Action<string>? progress)
    {
        if (!System.IO.File.Exists(renderOpts.Input))
        {
            throw new FileNotFoundException($"Input model not found: {renderOpts.Input}", renderOpts.Input);
        }

        if (!string.IsNullOrEmpty(renderOpts.Anim) && !System.IO.File.Exists(renderOpts.Anim))
        {
            throw new FileNotFoundException($"Animation file not found: {renderOpts.Anim}", renderOpts.Anim);
        }

        var assimp = AssimpApi.GetApi();
        Scene* scene = null;
        Scene* animScene = null;
        List<IntPtr> attachmentScenes = new();
        var frames = new List<SpriteFrame>();
        OffscreenRenderer? renderer = null;
        var processor = new PixelArtProcessor();

        try
        {
            uint flags = (uint)(PostProcessSteps.Triangulate
                | PostProcessSteps.GenerateSmoothNormals
                | PostProcessSteps.LimitBoneWeights
                | PostProcessSteps.JoinIdenticalVertices);

            scene = assimp.ImportFile(renderOpts.Input, flags);
            if (scene is null || scene->MRootNode is null)
            {
                string err = assimp.GetErrorStringS();
                throw new InvalidOperationException(
                    $"Failed to load model '{renderOpts.Input}': {(string.IsNullOrEmpty(err) ? "unknown Assimp error" : err)}");
            }

            // Animation source: a separate --anim file (retargeted by bone name) if given, else the
            // animation embedded in the input model.
            Scene* animSource = scene;
            if (!string.IsNullOrEmpty(renderOpts.Anim))
            {
                animScene = assimp.ImportFile(renderOpts.Anim, flags);
                if (animScene is null || animScene->MNumAnimations == 0)
                {
                    string err = assimp.GetErrorStringS();
                    throw new InvalidOperationException(
                        $"Failed to load animation '{renderOpts.Anim}': {(string.IsNullOrEmpty(err) ? "no animations found" : err)}");
                }

                animSource = animScene;
                progress?.Invoke($"retargeting animation from {System.IO.Path.GetFileName(renderOpts.Anim)} by bone name.");
            }

            progress?.Invoke(DescribeAnimations(animSource, renderOpts));

            int animIndex = animSource->MNumAnimations > 0 ? 0 : -1;
            string animName = ResolveAnimName(animSource, animIndex);
            int frameCount = ResolveFrameCount(animSource, animIndex, renderOpts);

            if (animIndex >= 0)
            {
                progress?.Invoke(DescribeRootMotion(RootMotion.Analyze(animSource->MAnimations[animIndex]), renderOpts.InPlace));
            }

            IReadOnlyList<float> yaws = DirectionScheduler.GetYaws(renderOpts.Directions);

            // Load equipment attachment scenes once; they are reused across every direction × frame.
            // Each loaded scene is tracked in `attachmentScenes` for cleanup and wrapped for the renderer.
            var attachments = new List<AttachmentScene>();
            if (equipment is not null)
            {
                foreach (Attachment att in equipment.Attachments)
                {
                    Scene* attScene = assimp.ImportFile(att.ResolvedFile, flags);
                    if (attScene is null || attScene->MRootNode is null)
                    {
                        string err = assimp.GetErrorStringS();
                        throw new InvalidOperationException(
                            $"Failed to load attachment '{att.Name}' ({att.ResolvedFile}): " +
                            $"{(string.IsNullOrEmpty(err) ? "unknown Assimp error" : err)}");
                    }

                    attachmentScenes.Add((IntPtr)attScene);
                    attachments.Add(new AttachmentScene(*attScene, att));
                    progress?.Invoke(
                        att.UseMasterPose
                            ? $"equipped '{att.Name}' (master-pose)."
                            : $"equipped '{att.Name}' (socket -> {att.SocketBone}).");
                }
            }

            renderer = new OffscreenRenderer(renderOpts.RenderSize, renderOpts.RenderSize);

            for (int dir = 0; dir < yaws.Count; dir++)
            {
                float yaw = yaws[dir];
                for (int f = 0; f < frameCount; f++)
                {
                    float time = renderOpts.Fps > 0 ? (float)f / renderOpts.Fps : 0f;

                    using var hires = animScene is not null
                        ? renderer.RenderFrame(*scene, *animScene, animIndex, yaw, renderOpts.CamPitch, time, renderOpts, attachments)
                        : renderer.RenderFrame(*scene, animIndex, yaw, renderOpts.CamPitch, time, renderOpts, attachments);
                    var sprite = processor.Process(hires, pixelOpts);

                    frames.Add(new SpriteFrame
                    {
                        Bitmap = sprite,
                        DirectionIndex = dir,
                        FrameIndex = f,
                        AnimName = animName,
                    });

                    progress?.Invoke($"{animName}: dir {dir + 1}/{yaws.Count}, frame {f + 1}/{frameCount}");
                }
            }

            return frames;
        }
        catch
        {
            // Avoid leaking partial bitmaps if rendering throws midway.
            foreach (SpriteFrame frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }
        finally
        {
            renderer?.Dispose();
            if (scene is not null)
            {
                assimp.FreeScene(scene);
            }

            if (animScene is not null)
            {
                assimp.FreeScene(animScene);
            }

            foreach (IntPtr attPtr in attachmentScenes)
            {
                assimp.FreeScene((Scene*)attPtr);
            }

            assimp.Dispose();
        }
    }

    /// <summary>
    /// Loads the animation source (the <c>--anim</c> file if given, else the input model) and returns a
    /// human-readable root-motion report, without rendering. Used by <c>--check-root-motion</c>.
    /// </summary>
    public unsafe string CheckRootMotion(RenderOptions renderOpts)
    {
        ArgumentNullException.ThrowIfNull(renderOpts);

        string animFile = !string.IsNullOrEmpty(renderOpts.Anim) ? renderOpts.Anim! : renderOpts.Input;
        if (!System.IO.File.Exists(animFile))
        {
            throw new FileNotFoundException($"File not found: {animFile}", animFile);
        }

        var assimp = AssimpApi.GetApi();
        Scene* scene = null;
        try
        {
            scene = assimp.ImportFile(animFile, (uint)(PostProcessSteps.Triangulate | PostProcessSteps.LimitBoneWeights));
            if (scene is null)
            {
                throw new InvalidOperationException($"Failed to load '{animFile}': {assimp.GetErrorStringS()}");
            }

            string label = System.IO.Path.GetFileName(animFile);
            return scene->MNumAnimations == 0
                ? $"{label}: no animation found."
                : $"{label}: {DescribeRootMotion(RootMotion.Analyze(scene->MAnimations[0]), renderOpts.InPlace)}";
        }
        finally
        {
            if (scene is not null)
            {
                assimp.FreeScene(scene);
            }

            assimp.Dispose();
        }
    }

    /// <summary>Formats a one-line root-motion summary.</summary>
    private static string DescribeRootMotion(RootMotionInfo rm, bool inPlace)
    {
        if (!rm.HasMotion)
        {
            return $"root motion: none (in place; {rm.TravelXZ:F1} units of jitter)";
        }

        string action = inPlace ? "removed (--in-place)" : "pass --in-place to keep the character centered";
        return $"root motion: detected on '{rm.Node}' (~{rm.TravelXZ:F0} units of horizontal travel); {action}";
    }

    /// <summary>Number of frames to sample: an explicit override, or the clip duration × fps, or 1 if static.</summary>
    private static unsafe int ResolveFrameCount(Scene* scene, int animIndex, RenderOptions opts)
    {
        if (opts.Frames > 0)
        {
            return opts.Frames;
        }

        if (animIndex < 0)
        {
            return 1; // no animation -> single static pose
        }

        Animation* anim = scene->MAnimations[animIndex];
        double ticksPerSecond = anim->MTicksPerSecond != 0 ? anim->MTicksPerSecond : 25.0;
        double durationSeconds = anim->MDuration / ticksPerSecond;
        int count = (int)Math.Round(durationSeconds * opts.Fps);
        return Math.Max(1, count);
    }

    /// <summary>
    /// A <c>--verbose</c> diagnostic dump of every animation clip in <paramref name="scene"/>: its name, raw
    /// tick duration, raw ticks-per-second, the derived duration in seconds, and the frame count it would
    /// sample at <paramref name="opts"/>.<see cref="RenderOptions.Fps"/>. These are the exact values behind
    /// <see cref="ResolveFrameCount"/>, surfaced so tick-scale or wrong-clip issues are self-diagnosing
    /// (e.g. a 45-frame/30fps clip that renders as 14 frames).
    /// </summary>
    private static unsafe string DescribeAnimations(Scene* scene, RenderOptions opts)
    {
        uint n = scene->MNumAnimations;
        if (n == 0)
        {
            return "animations: none found (static mesh; a single frame will be rendered).";
        }

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("animations: ").Append(n.ToString(ci)).Append(" clip(s) found:");
        for (uint i = 0; i < n; i++)
        {
            Animation* anim = scene->MAnimations[i];
            if (anim is null)
            {
                sb.Append("\n  [").Append(i).Append("] <null animation>");
                continue;
            }

            string name = anim->MName.AsString;
            double durationTicks = anim->MDuration;
            double tpsRaw = anim->MTicksPerSecond;
            double tps = tpsRaw != 0 ? tpsRaw : 25.0;
            double seconds = tps != 0 ? durationTicks / tps : 0.0;
            int frames = Math.Max(1, (int)Math.Round(seconds * opts.Fps));
            string tpsNote = tpsRaw == 0 ? " (raw 0 -> fallback 25)" : string.Empty;

            sb.Append("\n  [").Append(i)
              .Append("] name='").Append(string.IsNullOrEmpty(name) ? "(unnamed)" : name)
              .Append("' channels=").Append(anim->MNumChannels)
              .Append(" duration=").Append(durationTicks.ToString("0.###", ci))
              .Append(" ticksPerSecond=").Append(tpsRaw.ToString("0.###", ci)).Append(tpsNote)
              .Append(" -> ").Append(seconds.ToString("0.###", ci))
              .Append("s -> ").Append(frames)
              .Append(" frame(s) @ ").Append(opts.Fps).Append(" fps");
        }

        sb.Append("\n  selection: clip [0] (index is currently hard-coded to 0); ");
        if (opts.Frames > 0)
        {
            sb.Append("--frames=").Append(opts.Frames).Append(" overrides the per-clip count above.");
        }
        else
        {
            sb.Append("frame count is derived from the selected clip's duration.");
        }

        return sb.ToString();
    }

    private static unsafe string ResolveAnimName(Scene* scene, int animIndex)
    {
        if (animIndex < 0)
        {
            return "Default";
        }

        string name = scene->MAnimations[animIndex]->MName.AsString;
        return string.IsNullOrEmpty(name) ? "Anim" : SanitizeName(name);
    }

    /// <summary>Strips characters that are unsafe in output file names (e.g. Assimp's "Armature|Walk").</summary>
    private static string SanitizeName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int n = 0;
        foreach (char c in name)
        {
            buffer[n++] = char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_';
        }

        return new string(buffer[..n]);
    }
}
