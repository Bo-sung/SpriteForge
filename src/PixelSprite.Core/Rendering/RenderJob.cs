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
    public IEnumerable<SpriteFrame> Execute(
        RenderOptions renderOpts, PixelArtOptions pixelOpts, Action<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(renderOpts);
        ArgumentNullException.ThrowIfNull(pixelOpts);

        // Materialized rather than lazily yielded: the Assimp scene is an unmanaged pointer and C#
        // iterators cannot carry pointer locals across yield boundaries. The frame set is small.
        return RenderAll(renderOpts, pixelOpts, progress);
    }

    private static unsafe List<SpriteFrame> RenderAll(
        RenderOptions renderOpts, PixelArtOptions pixelOpts, Action<string>? progress)
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

            int animIndex = animSource->MNumAnimations > 0 ? 0 : -1;
            string animName = ResolveAnimName(animSource, animIndex);
            int frameCount = ResolveFrameCount(animSource, animIndex, renderOpts);

            IReadOnlyList<float> yaws = DirectionScheduler.GetYaws(renderOpts.Directions);

            renderer = new OffscreenRenderer(renderOpts.RenderSize, renderOpts.RenderSize);

            for (int dir = 0; dir < yaws.Count; dir++)
            {
                float yaw = yaws[dir];
                for (int f = 0; f < frameCount; f++)
                {
                    float time = renderOpts.Fps > 0 ? (float)f / renderOpts.Fps : 0f;

                    using var hires = animScene is not null
                        ? renderer.RenderFrame(*scene, *animScene, animIndex, yaw, renderOpts.CamPitch, time, renderOpts)
                        : renderer.RenderFrame(*scene, animIndex, yaw, renderOpts.CamPitch, time, renderOpts);
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

            assimp.Dispose();
        }
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
