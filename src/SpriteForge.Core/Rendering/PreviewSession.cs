using System.Collections.Concurrent;
using SpriteForge.Core.Models;
using SkiaSharp;
using Silk.NET.Assimp;
using AssimpApi = Silk.NET.Assimp.Assimp;

namespace SpriteForge.Core.Rendering;

/// <summary>Read-only facts about a loaded <see cref="PreviewSession"/> (animation timing, root motion).</summary>
public sealed record PreviewInfo
{
    /// <summary>True when the loaded source (or <c>--anim</c> file) has at least one animation clip.</summary>
    public bool HasAnimation { get; init; }

    /// <summary>The selected clip's name, or "Default" for a static mesh.</summary>
    public string AnimationName { get; init; } = "Default";

    /// <summary>Clip duration in seconds (0 for a static mesh).</summary>
    public double DurationSeconds { get; init; }

    /// <summary>Number of frames the clip samples at <see cref="Fps"/> (1 for a static mesh).</summary>
    public int FrameCount { get; init; } = 1;

    /// <summary>The sampling rate the frame count was derived at.</summary>
    public int Fps { get; init; }

    /// <summary>True when the clip translates the root/hips horizontally (suggest <c>--in-place</c>).</summary>
    public bool HasRootMotion { get; init; }

    /// <summary>Approximate horizontal root travel in model units.</summary>
    public float RootMotionTravel { get; init; }
}

/// <summary>
/// A managed facade over <see cref="OffscreenRenderer"/> for an interactive GUI: it loads the mesh,
/// optional animation, and optional equipment <em>once</em>, then renders single hi-res frames on
/// demand (one camera angle / one animation timestamp per call) — the render-on-change preview model.
/// </summary>
/// <remarks>
/// <para>
/// OpenGL has hard thread affinity: the GL context, the Assimp scenes, and the renderer are all created
/// and used on a single dedicated background thread owned by this session. Every public method marshals
/// its work onto that thread, so callers (e.g. a WPF UI thread) never touch GL directly. Dispose tears
/// the thread down and frees the unmanaged scenes on it.
/// </para>
/// <para>
/// Load-time options (<see cref="RenderOptions.Input"/>, <see cref="RenderOptions.Anim"/>,
/// <see cref="RenderOptions.RenderSize"/>) are fixed for the session's lifetime; changing any of them
/// requires a new session. Camera options (pitch, zoom, distance, target, ortho, up-axis, in-place) are
/// read per render call, so they can change freely between frames.
/// </para>
/// </remarks>
public sealed unsafe class PreviewSession : IDisposable
{
    private readonly RenderOptions _loadOpts;
    private readonly EquipmentManifest? _equipment;
    private readonly RetargetMap? _retarget;

    private readonly Thread _glThread;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // All of the following are owned by the GL thread.
    private AssimpApi _assimp = null!;
    private Scene* _scene;
    private Scene* _animScene;
    private readonly List<nint> _attachmentScenes = new();
    private readonly List<AttachmentScene> _attachments = new();
    private OffscreenRenderer _renderer = null!;
    private int _animIndex = -1;
    private PreviewInfo _info = new();
    private Exception? _loadError;

    private volatile bool _disposed;

    private PreviewSession(RenderOptions loadOpts, EquipmentManifest? equipment, RetargetMap? retarget)
    {
        _loadOpts = loadOpts;
        _equipment = equipment;
        _retarget = retarget;
        _glThread = new Thread(GlThreadMain)
        {
            IsBackground = true,
            Name = "SpriteForge-Preview-GL",
        };
    }

    /// <summary>Facts about the loaded clip, valid once <see cref="Load"/> returns.</summary>
    public PreviewInfo Info => _info;

    /// <summary>
    /// Loads the model (and optional animation / equipment / retarget map) and returns a ready session.
    /// Blocks until the GL thread has loaded the scenes and created the renderer.
    /// </summary>
    /// <exception cref="InvalidOperationException">Loading or renderer creation failed.</exception>
    public static PreviewSession Load(
        RenderOptions loadOpts, EquipmentManifest? equipment = null, RetargetMap? retarget = null)
    {
        ArgumentNullException.ThrowIfNull(loadOpts);

        var session = new PreviewSession(loadOpts, equipment, retarget);
        session._glThread.Start();
        session._ready.Task.GetAwaiter().GetResult();

        if (session._loadError is not null)
        {
            session._glThread.Join();
            throw new InvalidOperationException(
                $"Failed to load preview for '{loadOpts.Input}': {session._loadError.Message}",
                session._loadError);
        }

        return session;
    }

    /// <summary>
    /// Renders a single hi-res preview frame at the given orbit yaw and animation time. Camera fields are
    /// read from <paramref name="cameraOpts"/> (pitch/zoom/distance/target/ortho/up-axis/in-place).
    /// </summary>
    /// <param name="cameraOpts">Options whose camera fields are applied; load-time fields are ignored.</param>
    /// <param name="yawDegrees">Orbit yaw about the up axis (free, not snapped to a direction).</param>
    /// <param name="timeSeconds">Animation timestamp in seconds (ignored for a static mesh).</param>
    /// <returns>A new unpremultiplied RGBA bitmap with a transparent background; the caller disposes it.</returns>
    public Task<SKBitmap> RenderAsync(RenderOptions cameraOpts, float yawDegrees, float timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(cameraOpts);
        var tcs = new TaskCompletionSource<SKBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() =>
        {
            try
            {
                tcs.SetResult(RenderOnGlThread(cameraOpts, yawDegrees, timeSeconds));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Generates the full pixel-art sprite sheet for the loaded model via the normal
    /// <see cref="RenderJob"/> pipeline (all directions × frames, with the same equipment and retarget
    /// map this session was loaded with). Runs on its own renderer/context off the preview GL thread.
    /// </summary>
    /// <param name="opts">Full render options (directions, render size, fps, frames, camera).</param>
    /// <param name="pixelOpts">Pixel-art processing options.</param>
    /// <param name="progress">Optional per-frame progress callback.</param>
    /// <returns>The processed sprite frames; the caller owns and must dispose each.</returns>
    public Task<List<SpriteFrame>> GenerateSheetAsync(
        RenderOptions opts, PixelArtOptions pixelOpts, Action<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(pixelOpts);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // RenderJob owns its own scene load + GL context; isolate it on a worker thread so it never
        // races the preview context. The preview thread is idle while the user waits on a full sheet.
        return Task.Run(() =>
            new RenderJob().Execute(opts, pixelOpts, _equipment, _retarget, progress).ToList());
    }

    private SKBitmap RenderOnGlThread(RenderOptions opts, float yaw, float time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A separate animation scene (--anim) drives the two-scene retargeting overload; otherwise the
        // embedded animation (or bind pose when _animIndex < 0) uses the single-scene overload.
        return _animScene is not null
            ? _renderer.RenderFrame(*_scene, *_animScene, _animIndex, yaw, opts.CamPitch, time, opts, _attachments)
            : _renderer.RenderFrame(*_scene, _animIndex, yaw, opts.CamPitch, time, opts, _attachments);
    }

    private void Enqueue(Action work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _queue.Add(work);
    }

    private void GlThreadMain()
    {
        try
        {
            LoadOnGlThread();
        }
        catch (Exception ex)
        {
            _loadError = ex;
            _ready.TrySetResult(false);
            CleanupOnGlThread();
            return;
        }

        _ready.TrySetResult(true);

        foreach (Action work in _queue.GetConsumingEnumerable())
        {
            work();
        }

        CleanupOnGlThread();
    }

    private void LoadOnGlThread()
    {
        if (!System.IO.File.Exists(_loadOpts.Input))
        {
            throw new FileNotFoundException($"Input model not found: {_loadOpts.Input}", _loadOpts.Input);
        }

        if (!string.IsNullOrEmpty(_loadOpts.Anim) && !System.IO.File.Exists(_loadOpts.Anim))
        {
            throw new FileNotFoundException($"Animation file not found: {_loadOpts.Anim}", _loadOpts.Anim);
        }

        _assimp = AssimpApi.GetApi();
        uint flags = (uint)(PostProcessSteps.Triangulate
            | PostProcessSteps.GenerateSmoothNormals
            | PostProcessSteps.LimitBoneWeights
            | PostProcessSteps.JoinIdenticalVertices);

        _scene = _assimp.ImportFile(_loadOpts.Input, flags);
        if (_scene is null || _scene->MRootNode is null)
        {
            string err = _assimp.GetErrorStringS();
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? "unknown Assimp error" : err);
        }

        Scene* animSource = _scene;
        if (!string.IsNullOrEmpty(_loadOpts.Anim))
        {
            _animScene = _assimp.ImportFile(_loadOpts.Anim, flags);
            if (_animScene is null || _animScene->MNumAnimations == 0)
            {
                string err = _assimp.GetErrorStringS();
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(err) ? "no animations found in the animation file" : err);
            }

            animSource = _animScene;
        }

        _animIndex = animSource->MNumAnimations > 0 ? 0 : -1;

        if (_equipment is not null)
        {
            foreach (Attachment att in _equipment.Attachments)
            {
                Scene* attScene = _assimp.ImportFile(att.ResolvedFile, flags);
                if (attScene is null || attScene->MRootNode is null)
                {
                    string err = _assimp.GetErrorStringS();
                    throw new InvalidOperationException(
                        $"failed to load attachment '{att.Name}' ({att.ResolvedFile}): " +
                        (string.IsNullOrEmpty(err) ? "unknown Assimp error" : err));
                }

                _attachmentScenes.Add((nint)attScene);
                _attachments.Add(new AttachmentScene(*attScene, att));
            }
        }

        _renderer = new OffscreenRenderer(_loadOpts.RenderSize, _loadOpts.RenderSize);
        if (_retarget is not null)
        {
            _renderer.SetRetargetMap(_retarget);
        }

        _info = ComputeInfo(animSource);
    }

    private PreviewInfo ComputeInfo(Scene* animSource)
    {
        if (_animIndex < 0)
        {
            return new PreviewInfo { HasAnimation = false, FrameCount = 1, Fps = _loadOpts.Fps };
        }

        Animation* anim = animSource->MAnimations[_animIndex];
        double tps = anim->MTicksPerSecond != 0 ? anim->MTicksPerSecond : 25.0;
        double seconds = tps != 0 ? anim->MDuration / tps : 0.0;
        int frames = _loadOpts.Frames > 0
            ? _loadOpts.Frames
            : Math.Max(1, (int)Math.Round(seconds * _loadOpts.Fps));
        string name = anim->MName.AsString;
        RootMotionInfo rm = RootMotion.Analyze(anim);

        return new PreviewInfo
        {
            HasAnimation = true,
            AnimationName = string.IsNullOrEmpty(name) ? "Anim" : name,
            DurationSeconds = seconds,
            FrameCount = frames,
            Fps = _loadOpts.Fps,
            HasRootMotion = rm.HasMotion,
            RootMotionTravel = rm.TravelXZ,
        };
    }

    private void CleanupOnGlThread()
    {
        _renderer?.Dispose();

        if (_assimp is not null)
        {
            if (_scene is not null)
            {
                _assimp.FreeScene(_scene);
                _scene = null;
            }

            if (_animScene is not null)
            {
                _assimp.FreeScene(_animScene);
                _animScene = null;
            }

            foreach (nint att in _attachmentScenes)
            {
                _assimp.FreeScene((Scene*)att);
            }

            _attachmentScenes.Clear();
            _assimp.Dispose();
        }
    }

    /// <summary>Stops the GL thread and frees all unmanaged scenes and the renderer (on that thread).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();

        // The GL thread observes CompleteAdding, drains, and runs CleanupOnGlThread before exiting.
        if (_glThread.IsAlive)
        {
            _glThread.Join();
        }

        _queue.Dispose();
    }
}
