using System.Numerics;
using PixelSprite.Core.Models;
using PixelSprite.Core.PixelArt;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;

namespace PixelSprite.Core.Rendering;

/// <summary>
/// Renders an Assimp scene off-screen into an RGBA8 framebuffer with a fully transparent background
/// and reads the result back as an <see cref="SKBitmap"/>. Uses a hidden GLFW window (via
/// Silk.NET.Windowing) for the OpenGL context, an FBO for the render target, and CPU vertex skinning
/// so the fragment shader stays simple.
/// </summary>
/// <remarks>
/// Architecture mirrors the Bo-sung/ComfyUI-FBX-ControlNet-Converter renderer: the scene is loaded
/// once (by the caller) and this renderer is reused across every (direction × frame) combination.
/// The context is never made visible (<c>IsVisible = false</c>). The color buffer is cleared to
/// (0,0,0,0) before each frame and alpha blending uses standard src-alpha / one-minus-src-alpha.
/// </remarks>
public sealed unsafe class OffscreenRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;

    private readonly IWindow _window;
    private readonly GL _gl;

    private readonly uint _fbo;
    private readonly uint _colorTex;
    private readonly uint _depthRbo;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;

    private readonly int _uMvp;
    private readonly int _uModel;
    private readonly int _uLightDir;
    private readonly int _uBaseColor;
    private readonly int _uHasTexture;
    private readonly int _uTex;

    // GL diffuse textures keyed by material index (0 = no texture); loaded lazily, deleted on dispose.
    private readonly Dictionary<uint, uint> _materialTextures = new();

    private bool _disposed;

    // Root-motion analysis is cached: a renderer instance processes a single animation per run.
    private bool _rootMotionComputed;
    private RootMotionInfo _rootMotion;

    // The whole-clip framing is computed once (union of all sampled frames) and reused.
    private bool _framingComputed;
    private Bounds _framing;

    // The root/hips bone used as the screen-centre anchor is resolved once per run and reused.
    private bool _rootBoneNameComputed;
    private string? _rootBoneName;

    private const string VertexShaderSource =
        "#version 330 core\n" +
        "layout(location = 0) in vec3 aPos;\n" +
        "layout(location = 1) in vec3 aNormal;\n" +
        "layout(location = 2) in vec3 aColor;\n" +
        "layout(location = 3) in vec2 aUv;\n" +
        "uniform mat4 uMvp;\n" +
        "uniform mat4 uModel;\n" +
        "out vec3 vNormal;\n" +
        "out vec3 vColor;\n" +
        "out vec2 vUv;\n" +
        "void main()\n" +
        "{\n" +
        "    gl_Position = uMvp * vec4(aPos, 1.0);\n" +
        "    vNormal = mat3(uModel) * aNormal;\n" +
        "    vColor = aColor;\n" +
        "    vUv = aUv;\n" +
        "}\n";

    private const string FragmentShaderSource =
        "#version 330 core\n" +
        "in vec3 vNormal;\n" +
        "in vec3 vColor;\n" +
        "in vec2 vUv;\n" +
        "uniform vec3 uLightDir;\n" +
        "uniform vec3 uBaseColor;\n" +
        "uniform sampler2D uTex;\n" +
        "uniform int uHasTexture;\n" +
        "out vec4 FragColor;\n" +
        "void main()\n" +
        "{\n" +
        "    vec3 n = normalize(vNormal);\n" +
        "    float diff = max(dot(n, normalize(-uLightDir)), 0.0);\n" +
        "    float ambient = 0.6;\n" + // flatter light so albedo/texture reads clearly in the sprite
        "    vec4 tex = texture(uTex, vec2(vUv.x, 1.0 - vUv.y));\n" +
        "    vec3 albedo = (uHasTexture == 1) ? tex.rgb : (uBaseColor * vColor);\n" +
        "    vec3 color = albedo * (ambient + (1.0 - ambient) * diff);\n" +
        "    FragColor = vec4(color, 1.0);\n" +
        "}\n";

    /// <summary>Creates an off-screen renderer with the given square/rectangular render resolution.</summary>
    /// <param name="width">Render width in pixels.</param>
    /// <param name="height">Render height in pixels.</param>
    public OffscreenRenderer(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Render dimensions must be positive.");
        }

        _width = width;
        _height = height;

        // Hidden GLFW window provides a headless OpenGL 3.3 core context.
        var options = WindowOptions.Default;
        options.IsVisible = false;
        options.Size = new Silk.NET.Maths.Vector2D<int>(width, height);
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3));
        options.ShouldSwapAutomatically = false;

        _window = Window.Create(options);
        _window.Initialize();
        _gl = _window.CreateOpenGL();

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        (_fbo, _colorTex, _depthRbo) = CreateFramebuffer();
        _program = CreateProgram();
        _uMvp = _gl.GetUniformLocation(_program, "uMvp");
        _uModel = _gl.GetUniformLocation(_program, "uModel");
        _uLightDir = _gl.GetUniformLocation(_program, "uLightDir");
        _uBaseColor = _gl.GetUniformLocation(_program, "uBaseColor");
        _uHasTexture = _gl.GetUniformLocation(_program, "uHasTexture");
        _uTex = _gl.GetUniformLocation(_program, "uTex");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
    }

    /// <summary>
    /// Renders one animation frame from one camera direction and returns it as an unpremultiplied
    /// RGBA8888 bitmap with a transparent background.
    /// </summary>
    /// <param name="scene">The Assimp scene (loaded once by the caller).</param>
    /// <param name="animIndex">Animation index to evaluate; negative or out-of-range uses the bind pose.</param>
    /// <param name="yaw">Camera horizontal rotation in degrees.</param>
    /// <param name="pitch">Camera vertical angle in degrees.</param>
    /// <param name="time">Animation timestamp in seconds.</param>
    /// <param name="opts">Render options (zoom, projection, up axis).</param>
    /// <returns>A new bitmap; the caller owns and must dispose it.</returns>
    public SKBitmap RenderFrame(Scene scene, int animIndex, float yaw, float pitch, float time, RenderOptions opts)
        => RenderFrame(scene, scene, animIndex, yaw, pitch, time, opts);

    /// <summary>
    /// Renders one frame using an animation taken from a SEPARATE scene (<paramref name="animationScene"/>),
    /// retargeted onto <paramref name="scene"/>'s skeleton by matching node/bone names. Geometry and the
    /// node hierarchy come from <paramref name="scene"/>; only animation channels are read from
    /// <paramref name="animationScene"/> (pass the same scene for both to use an embedded animation).
    /// </summary>
    public SKBitmap RenderFrame(Scene scene, Scene animationScene, int animIndex, float yaw, float pitch, float time, RenderOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.ClearColor(0f, 0f, 0f, 0f); // fully transparent background
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        // Resolve the up axis correction so Z-up sources stand upright.
        Matrix4x4 axisFix = opts.UpAxis == UpAxis.Z
            ? Matrix4x4.CreateRotationX(-MathF.PI / 2f)
            : Matrix4x4.Identity;

        var meshes = ExtractMeshes(scene, animationScene, animIndex, time, opts.InPlace, out Dictionary<string, Matrix4x4> nodeGlobals);

        // Whole-clip framing yields a constant zoom (radius) so scale never pops across the sheet. The
        // CENTRE is re-anchored on THIS frame's root/hips bone position so the character's body stays
        // pinned to screen centre across every frame and direction (no swimming) — the game-sprite
        // convention. With --in-place the root's horizontal travel is already pinned, so this also kills
        // the residual vertical/offset drift. Falls back to the clip's AABB centre for static props.
        Bounds framing = GetGlobalFraming(scene, animationScene, animIndex, opts);
        Vector3 anchor = GetRootWorldPosition(scene, animationScene, animIndex, nodeGlobals, axisFix) ?? framing.Center;
        Bounds bounds = new Bounds(anchor, framing.Radius);

        // Each direction rotates the MODEL about its vertical centre axis; the camera and light stay
        // fixed at the front. This keeps framing and screen-space lighting identical across directions
        // (the standard directional-sprite convention) instead of orbiting the camera around the model.
        // Direction angle plus the base --cam-yaw offset (the facing of direction 0).
        float yawRad = (yaw + opts.CamYaw) * MathF.PI / 180f;
        Matrix4x4 model = axisFix
            * Matrix4x4.CreateTranslation(-bounds.Center)
            * Matrix4x4.CreateRotationY(yawRad)
            * Matrix4x4.CreateTranslation(bounds.Center);

        // Fixed front camera (yaw 0). The bounding sphere is rotation-invariant, so framing is stable.
        (Matrix4x4 view, Matrix4x4 proj) = BuildCamera(bounds, 0f, pitch, opts);
        Matrix4x4 mvp = model * view * proj;

        _gl.UseProgram(_program);
        var lightDir = new Vector3(-0.4f, -0.7f, -0.6f);
        _gl.Uniform3(_uLightDir, lightDir.X, lightDir.Y, lightDir.Z);
        _gl.Uniform1(_uTex, 0); // diffuse sampler reads from texture unit 0
        _gl.BindVertexArray(_vao);

        foreach (CpuMesh mesh in meshes)
        {
            // Per-mesh base tint resolved from the material's diffuse color (or a gray fallback).
            Vector3 baseColor = mesh.BaseColor;
            _gl.Uniform3(_uBaseColor, baseColor.X, baseColor.Y, baseColor.Z);

            if (mesh.TextureId != 0)
            {
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, mesh.TextureId);
                _gl.Uniform1(_uHasTexture, 1);
            }
            else
            {
                _gl.Uniform1(_uHasTexture, 0);
            }

            UploadAndDraw(mesh, mvp, model);
        }

        SKBitmap bitmap = ReadPixels();

        foreach (CpuMesh m in meshes)
        {
            m.Dispose();
        }

        return bitmap;
    }

    private (uint fbo, uint colorTex, uint depthRbo) CreateFramebuffer()
    {
        uint fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        uint colorTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, colorTex);
        _gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)_width, (uint)_height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, colorTex, 0);

        uint depthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)_width, (uint)_height);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, depthRbo);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("Off-screen framebuffer is not complete.");
        }

        return (fbo, colorTex, depthRbo);
    }

    private uint CreateProgram()
    {
        uint vs = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fs = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vs);
        _gl.AttachShader(program, fs);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"Shader program link failed: {log}");
        }

        _gl.DetachShader(program, vs);
        _gl.DetachShader(program, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }

        return shader;
    }

    private void UploadAndDraw(CpuMesh mesh, Matrix4x4 mvp, Matrix4x4 model)
    {
        // Interleaved vertex data: position (3) + normal (3) + color (3).
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* v = mesh.Vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(mesh.Vertices.Length * sizeof(float)),
                v,
                BufferUsageARB.DynamicDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* i = mesh.Indices)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(mesh.Indices.Length * sizeof(uint)),
                i,
                BufferUsageARB.DynamicDraw);
        }

        const uint stride = 11 * sizeof(float);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (void*)(9 * sizeof(float)));
        _gl.EnableVertexAttribArray(3);

        SetMatrix(_uMvp, mvp);
        SetMatrix(_uModel, model);

        _gl.DrawElements(Silk.NET.OpenGL.PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, (void*)0);
    }

    private void SetMatrix(int location, Matrix4x4 m)
    {
        // System.Numerics uses the row-vector convention (clip = v_row * m), so the GLSL matrix must be
        // m^T. We upload m's fields in System.Numerics order and let GL transpose on upload (transpose:
        // true reinterprets these as rows, yielding m^T). NOTE: getting this wrong only distorts large,
        // asymmetric models — small/symmetric ones still look plausible.
        Span<float> data = stackalloc float[16]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        };
        // Natural (row-major) order read as column-major yields m^T — the column-vector form GLSL needs.
        _gl.UniformMatrix4(location, 1, false, data);
    }

    private SKBitmap ReadPixels()
    {
        var info = new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var pixels = new byte[_width * _height * 4];

        fixed (byte* p = pixels)
        {
            _gl.ReadPixels(0, 0, (uint)_width, (uint)_height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        // OpenGL returns rows bottom-to-top; flip vertically into the bitmap.
        int rowBytes = _width * 4;
        nint dst = bitmap.GetPixels();
        for (int y = 0; y < _height; y++)
        {
            int srcRow = (_height - 1 - y) * rowBytes;
            var src = new ReadOnlySpan<byte>(pixels, srcRow, rowBytes);
            var dstSpan = new Span<byte>((void*)(dst + (y * rowBytes)), rowBytes);
            src.CopyTo(dstSpan);
        }

        return bitmap;
    }

    // -- Assimp scene extraction + CPU skinning ------------------------------------------------

    /// <summary>
    /// Extracts every mesh as skinned, triangulated CPU geometry for the given animation time. Geometry
    /// and the node hierarchy come from <paramref name="scene"/>; animation channels come from
    /// <paramref name="animScene"/> (matched to nodes by name, enabling separate-file retargeting).
    /// </summary>
    private CpuMesh[] ExtractMeshes(Scene scene, Scene animScene, int animIndex, float time, bool inPlace)
        => ExtractMeshes(scene, animScene, animIndex, time, inPlace, out _);

    /// <summary>
    /// Extracts every mesh as skinned, triangulated CPU geometry for the given animation time, and also
    /// returns the per-node global transforms used to build it (reused for root-bone lookups). Geometry
    /// and the node hierarchy come from <paramref name="scene"/>; animation channels come from
    /// <paramref name="animScene"/> (matched to nodes by name, enabling separate-file retargeting).
    /// </summary>
    private CpuMesh[] ExtractMeshes(
        Scene scene, Scene animScene, int animIndex, float time, bool inPlace,
        out Dictionary<string, Matrix4x4> nodeGlobals)
    {
        nodeGlobals = BuildNodeGlobals(scene, animScene, animIndex, time, inPlace);

        if (scene.MRootNode is null || scene.MNumMeshes == 0)
        {
            return Array.Empty<CpuMesh>();
        }

        var result = new List<CpuMesh>((int)scene.MNumMeshes);
        for (uint mi = 0; mi < scene.MNumMeshes; mi++)
        {
            Mesh* mesh = scene.MMeshes[mi];
            if (mesh is null || mesh->MNumVertices == 0)
            {
                continue;
            }

            result.Add(BuildMesh(scene, mesh, nodeGlobals));
        }

        return result.ToArray();
    }

    /// <summary>Per-node global transforms (hierarchy from <paramref name="scene"/>) with the animation at
    /// <paramref name="time"/> applied where channels exist in <paramref name="animScene"/>.</summary>
    private Dictionary<string, Matrix4x4> BuildNodeGlobals(
        Scene scene, Scene animScene, int animIndex, float time, bool inPlace)
    {
        var nodeGlobals = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        Dictionary<string, NodeAnimSampler>? samplers = BuildSamplers(animScene, animIndex, time, inPlace);
        AccumulateNodeTransforms(scene.MRootNode, Matrix4x4.Identity, samplers, nodeGlobals);
        return nodeGlobals;
    }

    private Dictionary<string, NodeAnimSampler>? BuildSamplers(Scene scene, int animIndex, float time, bool inPlace)
    {
        if (animIndex < 0 || animIndex >= scene.MNumAnimations || scene.MAnimations is null)
        {
            return null;
        }

        Animation* anim = scene.MAnimations[animIndex];
        if (anim is null)
        {
            return null;
        }

        double ticksPerSecond = anim->MTicksPerSecond != 0 ? anim->MTicksPerSecond : 25.0;
        double durationTicks = anim->MDuration > 0 ? anim->MDuration : 1.0;
        double timeInTicks = time * ticksPerSecond;
        double animTick = durationTicks > 0 ? timeInTicks % durationTicks : 0.0;

        var samplers = new Dictionary<string, NodeAnimSampler>(StringComparer.Ordinal);
        for (uint c = 0; c < anim->MNumChannels; c++)
        {
            NodeAnim* channel = anim->MChannels[c];
            if (channel is null)
            {
                continue;
            }

            string name = channel->MNodeName.AsString;
            samplers[name] = NodeAnimSampler.Sample(channel, animTick);
        }

        // Strip root motion: pin the root/hips node's horizontal translation to its start position so
        // the character stays centered (the vertical bob is preserved).
        if (inPlace)
        {
            RootMotionInfo rm = GetRootMotion(anim);
            if (rm.HasMotion && samplers.TryGetValue(rm.Node, out NodeAnimSampler rootSampler))
            {
                samplers[rm.Node] = rootSampler.StripHorizontal(rm.ReferenceXZ.X, rm.ReferenceXZ.Y);
            }
        }

        return samplers;
    }

    /// <summary>Lazily analyzes (and caches) the active animation's root motion; the clip never changes per run.</summary>
    private RootMotionInfo GetRootMotion(Animation* anim)
    {
        if (!_rootMotionComputed)
        {
            _rootMotion = RootMotion.Analyze(anim);
            _rootMotionComputed = true;
        }

        return _rootMotion;
    }

    /// <summary>
    /// Resolves (and caches) the name of the bone to anchor at screen centre for game sprites: the
    /// structural root/hips bone, found by name (e.g. Mixamo's <c>mixamorig:Hips</c>), falling back to
    /// the bone with the most horizontal travel when no conventionally-named pelvis exists.
    /// </summary>
    private string? GetRootBoneName(Scene scene, Scene animScene, int animIndex)
    {
        if (_rootBoneNameComputed)
        {
            return _rootBoneName;
        }

        _rootBoneName = FindRootBoneName(scene);
        if (string.IsNullOrEmpty(_rootBoneName)
            && animIndex >= 0 && animIndex < animScene.MNumAnimations && animScene.MAnimations is not null)
        {
            _rootBoneName = GetRootMotion(animScene.MAnimations[animIndex]).Node;
            if (string.IsNullOrEmpty(_rootBoneName))
            {
                _rootBoneName = null;
            }
        }

        _rootBoneNameComputed = true;
        return _rootBoneName;
    }

    /// <summary>Depth-first search for a pelvis/hips node by name, after stripping any namespace prefix.</summary>
    private static unsafe string? FindRootBoneName(Scene scene)
        => scene.MRootNode is null ? null : FindRootBoneNameRecursive(scene.MRootNode);

    private static unsafe string? FindRootBoneNameRecursive(Node* node)
    {
        if (node is null)
        {
            return null;
        }

        if (MatchPelvisName(node->MName.AsString) is { } direct)
        {
            return direct;
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            if (FindRootBoneNameRecursive(node->MChildren[i]) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Returns <paramref name="name"/> when it names a pelvis/hips (e.g. "Hips", "mixamorig:Hips"); else null.</summary>
    private static string? MatchPelvisName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string lower = name.ToLowerInvariant();
        int colon = lower.LastIndexOf(':');
        if (colon >= 0)
        {
            lower = colon + 1 < lower.Length ? lower[(colon + 1)..] : string.Empty;
        }

        return lower.Contains("hip") || lower.Contains("pelvis") ? name : null;
    }

    /// <summary>
    /// The root/hips bone's world position in the axis-corrected frame at <paramref name="time"/>, looked
    /// up from already-computed <paramref name="nodeGlobals"/>. Null when no root bone is detectable.
    /// </summary>
    private Vector3? GetRootWorldPosition(
        Scene scene, Scene animScene, int animIndex, Dictionary<string, Matrix4x4> nodeGlobals, Matrix4x4 axisFix)
    {
        string? bone = GetRootBoneName(scene, animScene, animIndex);
        if (string.IsNullOrEmpty(bone) || !nodeGlobals.TryGetValue(bone, out Matrix4x4 global))
        {
            return null;
        }

        return Vector3.Transform(global.Translation, axisFix);
    }

    /// <summary>Farthest any vertex of <paramref name="meshes"/> lies from <paramref name="root"/>, in the axis-corrected frame.</summary>
    private static float MaxVertexDistance(CpuMesh[] meshes, Matrix4x4 axisFix, Vector3 root)
    {
        float max = 0f;
        foreach (CpuMesh mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Length; i += 11)
            {
                var p = new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                p = Vector3.Transform(p, axisFix);
                float d = (p - root).Length();
                if (d > max)
                {
                    max = d;
                }
            }
        }

        return max;
    }

    private void AccumulateNodeTransforms(
        Node* node, Matrix4x4 parent,
        Dictionary<string, NodeAnimSampler>? samplers, Dictionary<string, Matrix4x4> output)
    {
        if (node is null)
        {
            return;
        }

        string name = node->MName.AsString;
        Matrix4x4 local = FromAssimp(node->MTransformation);
        if (samplers is not null && samplers.TryGetValue(name, out NodeAnimSampler sampler))
        {
            local = sampler.ToMatrix();
        }

        Matrix4x4 global = local * parent;
        if (!string.IsNullOrEmpty(name))
        {
            output[name] = global;
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            AccumulateNodeTransforms(node->MChildren[i], global, samplers, output);
        }
    }

    private CpuMesh BuildMesh(Scene scene, Mesh* mesh, Dictionary<string, Matrix4x4> nodeGlobals)
    {
        int vertexCount = (int)mesh->MNumVertices;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            positions[i] = mesh->MVertices[i];
            normals[i] = mesh->MNormals != null ? mesh->MNormals[i] : Vector3.UnitY;
        }

        // Apply bone skinning when present, otherwise leave vertices in mesh space.
        if (mesh->MNumBones > 0)
        {
            ApplySkinning(mesh, nodeGlobals, positions, normals);
        }

        // Optional UV set 0 for texture sampling (null when the mesh has no UVs).
        Vector3* uvs = mesh->MTextureCoords[0];

        // Interleave position + normal + vertex color (white) + uv. Vertex color stays white so the
        // texture or material base color carries the hue.
        var vertices = new float[vertexCount * 11];
        for (int i = 0; i < vertexCount; i++)
        {
            int o = i * 11;
            vertices[o + 0] = positions[i].X;
            vertices[o + 1] = positions[i].Y;
            vertices[o + 2] = positions[i].Z;
            Vector3 n = normals[i] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normals[i]);
            vertices[o + 3] = n.X;
            vertices[o + 4] = n.Y;
            vertices[o + 5] = n.Z;
            vertices[o + 6] = 1f;
            vertices[o + 7] = 1f;
            vertices[o + 8] = 1f;
            vertices[o + 9] = uvs != null ? uvs[i].X : 0f;
            vertices[o + 10] = uvs != null ? uvs[i].Y : 0f;
        }

        // Triangulated indices (scene is expected to be imported with aiProcess_Triangulate).
        var indices = new List<uint>((int)mesh->MNumFaces * 3);
        for (uint f = 0; f < mesh->MNumFaces; f++)
        {
            Face face = mesh->MFaces[f];
            if (face.MNumIndices != 3)
            {
                continue; // skip non-triangles
            }

            indices.Add(face.MIndices[0]);
            indices.Add(face.MIndices[1]);
            indices.Add(face.MIndices[2]);
        }

        Vector3 baseColor = GetDiffuseColor(scene, mesh->MMaterialIndex);
        uint textureId = GetOrLoadDiffuseTexture(scene, mesh->MMaterialIndex);
        return new CpuMesh(vertices, indices.ToArray(), baseColor, textureId);
    }

    /// <summary>Reads the diffuse/base-color texture path from a material's property list ("$tex.file").</summary>
    private static string GetDiffuseTexturePath(Scene scene, uint materialIndex)
    {
        if (scene.MMaterials is null || materialIndex >= scene.MNumMaterials)
        {
            return string.Empty;
        }

        Material* mat = scene.MMaterials[materialIndex];
        if (mat is null)
        {
            return string.Empty;
        }

        for (uint p = 0; p < mat->MNumProperties; p++)
        {
            MaterialProperty* prop = mat->MProperties[p];
            if (prop is null || prop->MData is null)
            {
                continue;
            }

            if (prop->MKey.AsString == "$tex.file")
            {
                // The value is an aiString: 4-byte length prefix followed by the path bytes.
                byte* d = prop->MData;
                uint len = *(uint*)d;
                if (len > 0 && len + 4 <= prop->MDataLength)
                {
                    return System.Text.Encoding.UTF8.GetString(d + 4, (int)len);
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Loads and caches a material's diffuse texture as a GL texture; returns 0 when there is none.</summary>
    private uint GetOrLoadDiffuseTexture(Scene scene, uint materialIndex)
    {
        if (_materialTextures.TryGetValue(materialIndex, out uint cached))
        {
            return cached;
        }

        uint texId = 0;
        using (SKBitmap? decoded = TryDecodeDiffuse(scene, materialIndex))
        {
            if (decoded is not null)
            {
                texId = UploadTexture(decoded);
            }
        }

        _materialTextures[materialIndex] = texId;
        return texId;
    }

    /// <summary>Decodes a material's diffuse image: embedded texture (matched by file name) or an external file.</summary>
    private SKBitmap? TryDecodeDiffuse(Scene scene, uint materialIndex)
    {
        string path = GetDiffuseTexturePath(scene, materialIndex);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        Silk.NET.Assimp.Texture* embedded = FindEmbeddedTexture(scene, path);
        if (embedded is not null)
        {
            return DecodeEmbedded(embedded);
        }

        // External file on disk (absolute or relative to the working directory) — best effort.
        return System.IO.File.Exists(path) ? SKBitmap.Decode(path) : null;
    }

    /// <summary>Finds an embedded texture matching a material path, by file name or Assimp's "*N" index form.</summary>
    private static Silk.NET.Assimp.Texture* FindEmbeddedTexture(Scene scene, string path)
    {
        if (scene.MTextures is null || scene.MNumTextures == 0)
        {
            return null;
        }

        if (path.StartsWith('*') && uint.TryParse(path.AsSpan(1), out uint idx) && idx < scene.MNumTextures)
        {
            return scene.MTextures[idx];
        }

        string target = BaseName(path);
        for (uint t = 0; t < scene.MNumTextures; t++)
        {
            Silk.NET.Assimp.Texture* et = scene.MTextures[t];
            if (et is not null && string.Equals(BaseName(et->MFilename.AsString), target, StringComparison.OrdinalIgnoreCase))
            {
                return et;
            }
        }

        return null;
    }

    /// <summary>Decodes an embedded texture: compressed bytes (height 0) via SkiaSharp, else raw BGRA texels.</summary>
    private static SKBitmap? DecodeEmbedded(Silk.NET.Assimp.Texture* et)
    {
        if (et->MHeight == 0)
        {
            // Compressed image (PNG/JPG): MWidth is the byte length of the data at MPcData.
            using SKData data = SKData.CreateCopy((IntPtr)et->PcData, (ulong)et->MWidth);
            return SKBitmap.Decode(data);
        }

        int w = (int)et->MWidth, h = (int)et->MHeight;
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Texel* texels = et->PcData;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Texel t = texels[(y * w) + x];
                bmp.SetPixel(x, y, new SKColor(t.R, t.G, t.B, t.A));
            }
        }

        return bmp;
    }

    /// <summary>Uploads an SKBitmap as an RGBA8 GL texture with mipmaps; returns the texture id.</summary>
    private uint UploadTexture(SKBitmap source)
    {
        SKBitmap rgba = source.ColorType == SKColorType.Rgba8888 ? source : source.Copy(SKColorType.Rgba8888);

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)rgba.Width, (uint)rgba.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)rgba.GetPixels());
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);

        if (!ReferenceEquals(rgba, source))
        {
            rgba.Dispose();
        }

        return tex;
    }

    private static string BaseName(string path)
    {
        int slash = path.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    /// <summary>Reads a material's diffuse color from its property list, falling back to neutral gray.</summary>
    private static Vector3 GetDiffuseColor(Scene scene, uint materialIndex)
    {
        var fallback = new Vector3(0.75f, 0.75f, 0.78f);
        if (scene.MMaterials is null || materialIndex >= scene.MNumMaterials)
        {
            return fallback;
        }

        Material* mat = scene.MMaterials[materialIndex];
        if (mat is null)
        {
            return fallback;
        }

        // Assimp's glTF/FBX importers expose the base/diffuse color under the "$clr.diffuse" key as
        // an array of floats. Scan the property list directly to avoid the marshaled GetMaterialColor.
        for (uint p = 0; p < mat->MNumProperties; p++)
        {
            MaterialProperty* prop = mat->MProperties[p];
            if (prop is null || prop->MData is null)
            {
                continue;
            }

            if (prop->MKey.AsString == "$clr.diffuse" && prop->MDataLength >= 3 * sizeof(float))
            {
                float* f = (float*)prop->MData;
                return new Vector3(f[0], f[1], f[2]);
            }
        }

        return fallback;
    }

    private void ApplySkinning(
        Mesh* mesh, Dictionary<string, Matrix4x4> nodeGlobals, Vector3[] positions, Vector3[] normals)
    {
        int vertexCount = positions.Length;
        var skinned = new Vector3[vertexCount];
        var skinnedNormals = new Vector3[vertexCount];
        var weightSums = new float[vertexCount];

        for (uint b = 0; b < mesh->MNumBones; b++)
        {
            Bone* bone = mesh->MBones[b];
            if (bone is null)
            {
                continue;
            }

            string boneName = bone->MName.AsString;
            if (!nodeGlobals.TryGetValue(boneName, out Matrix4x4 global))
            {
                continue;
            }

            Matrix4x4 offset = FromAssimp(bone->MOffsetMatrix);
            Matrix4x4 skin = offset * global;
            Matrix4x4 normalSkin = skin;
            normalSkin.M41 = normalSkin.M42 = normalSkin.M43 = 0f; // strip translation for normals

            for (uint w = 0; w < bone->MNumWeights; w++)
            {
                VertexWeight vw = bone->MWeights[w];
                int vi = (int)vw.MVertexId;
                float weight = vw.MWeight;
                skinned[vi] += Vector3.Transform(positions[vi], skin) * weight;
                skinnedNormals[vi] += Vector3.TransformNormal(normals[vi], normalSkin) * weight;
                weightSums[vi] += weight;
            }
        }

        for (int i = 0; i < vertexCount; i++)
        {
            if (weightSums[i] > 0f)
            {
                positions[i] = skinned[i] / weightSums[i];
                normals[i] = skinnedNormals[i];
            }
        }
    }

    /// <summary>
    /// Computes a single constant zoom (radius) for the whole clip so scale never pops across the sheet.
    /// The radius is sized to the farthest any vertex reaches from the root/hips bone (so nothing clips
    /// once the camera is centred on the root); it falls back to the AABB half-diagonal when no root bone
    /// is detectable. The centre is re-anchored per frame in <see cref="RenderFrame"/>. Cached after the
    /// first call. A static model (no animation) samples only its single pose.
    /// </summary>
    private Bounds GetGlobalFraming(Scene scene, Scene animScene, int animIndex, RenderOptions opts)
    {
        if (_framingComputed)
        {
            return _framing;
        }

        Matrix4x4 axisFix = opts.UpAxis == UpAxis.Z
            ? Matrix4x4.CreateRotationX(-MathF.PI / 2f)
            : Matrix4x4.Identity;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        float maxDistFromRoot = 0f;

        int samples = animIndex >= 0 ? 24 : 1;
        double durationSeconds = GetClipSeconds(animScene, animIndex);
        for (int s = 0; s < samples; s++)
        {
            float t = samples > 1 ? (float)(durationSeconds * s / (samples - 1)) : 0f;
            CpuMesh[] sampleMeshes = ExtractMeshes(scene, animScene, animIndex, t, opts.InPlace, out Dictionary<string, Matrix4x4> nodeGlobals);
            (Vector3 mn, Vector3 mx, bool ok) = ComputeAabb(sampleMeshes, axisFix);
            if (ok)
            {
                min = Vector3.Min(min, mn);
                max = Vector3.Max(max, mx);
                any = true;
            }

            // Track the farthest any vertex reaches from the root bone across the whole clip, so the zoom
            // fits every pose once the camera is centred on the root (nothing is clipped).
            if (GetRootWorldPosition(scene, animScene, animIndex, nodeGlobals, axisFix) is { } rootPos)
            {
                maxDistFromRoot = MathF.Max(maxDistFromRoot, MaxVertexDistance(sampleMeshes, axisFix, rootPos));
            }

            foreach (CpuMesh m in sampleMeshes)
            {
                m.Dispose();
            }
        }

        Bounds aabb = any ? BoundsFromAabb(min, max) : new Bounds(Vector3.Zero, 1f);
        // Root-anchored framing for game sprites: keep a constant zoom, but size it to the farthest vertex
        // from the root bone so no pose is clipped. The centre is re-anchored per frame by the caller.
        float radius = maxDistFromRoot > 1e-3f ? maxDistFromRoot : aabb.Radius;
        _framing = new Bounds(aabb.Center, radius);
        _framingComputed = true;
        return _framing;
    }

    private static unsafe double GetClipSeconds(Scene animScene, int animIndex)
    {
        if (animIndex < 0 || animIndex >= animScene.MNumAnimations || animScene.MAnimations is null)
        {
            return 0.0;
        }

        Animation* anim = animScene.MAnimations[animIndex];
        double ticksPerSecond = anim->MTicksPerSecond != 0 ? anim->MTicksPerSecond : 25.0;
        return anim->MDuration / ticksPerSecond;
    }

    private static (Vector3 min, Vector3 max, bool any) ComputeAabb(CpuMesh[] meshes, Matrix4x4 axisFix)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        foreach (CpuMesh mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Length; i += 11)
            {
                var p = new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                p = Vector3.Transform(p, axisFix);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }

        return (min, max, any);
    }

    private static Bounds BoundsFromAabb(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 1e-3f);
        return new Bounds(center, radius);
    }

    private static (Matrix4x4 view, Matrix4x4 proj) BuildCamera(
        Bounds bounds, float yaw, float pitch, RenderOptions opts)
    {
        float yawRad = yaw * MathF.PI / 180f;
        float pitchRad = pitch * MathF.PI / 180f;

        // Camera distance: explicit --cam-distance override, else derived from the bounding radius and zoom.
        float distance = opts.CamDistance > 0f
            ? opts.CamDistance
            : bounds.Radius / MathF.Max(opts.CamZoom, 1e-3f) * 2.6f;

        // Look-at target: the model centre plus the --cam-target pan offset.
        Vector3 target = bounds.Center + opts.CamTarget;
        var dir = new Vector3(
            MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            MathF.Sin(pitchRad),
            MathF.Cos(pitchRad) * MathF.Cos(yawRad));
        Vector3 eye = target + (dir * distance);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);

        float near = MathF.Max(distance - (bounds.Radius * 2f), 0.01f);
        float far = distance + (bounds.Radius * 2f);

        Matrix4x4 proj;
        if (opts.Ortho)
        {
            float extent = bounds.Radius / MathF.Max(opts.CamZoom, 1e-3f) * 1.1f;
            proj = Matrix4x4.CreateOrthographic(extent * 2f, extent * 2f, near, far);
        }
        else
        {
            proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1f, near, far);
        }

        return (view, proj);
    }

    // Silk.NET.Assimp exposes aiMatrix4x4 as a System.Numerics.Matrix4x4 with the same element layout
    // (translation in the last column). System.Numerics uses the row-vector convention (translation in
    // the last row), so transpose to consume Assimp transforms with Vector3.Transform / matrix multiply.
    private static Matrix4x4 FromAssimp(Matrix4x4 m) => Matrix4x4.Transpose(m);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_colorTex);
        _gl.DeleteRenderbuffer(_depthRbo);
        foreach (uint tex in _materialTextures.Values)
        {
            if (tex != 0)
            {
                _gl.DeleteTexture(tex);
            }
        }

        _gl.Dispose();
        _window.Dispose();
    }

    /// <summary>Bounding sphere of the posed model, used for camera framing.</summary>
    private readonly record struct Bounds(Vector3 Center, float Radius);

    /// <summary>
    /// Skinned CPU geometry: interleaved [px,py,pz, nx,ny,nz, r,g,b, u,v] vertices, triangle indices,
    /// the mesh's material base color, and its diffuse GL texture id (0 = none).
    /// </summary>
    private sealed class CpuMesh(float[] vertices, uint[] indices, Vector3 baseColor, uint textureId) : IDisposable
    {
        public float[] Vertices { get; } = vertices;

        public uint[] Indices { get; } = indices;

        public Vector3 BaseColor { get; } = baseColor;

        public uint TextureId { get; } = textureId;

        public void Dispose()
        {
            // Arrays are GC-managed; nothing unmanaged to release. Present for symmetric usage.
        }
    }

    /// <summary>Sampled TRS for a single node at a single animation time.</summary>
    private readonly struct NodeAnimSampler(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        private readonly Vector3 _position = position;
        private readonly Quaternion _rotation = rotation;
        private readonly Vector3 _scale = scale;

        /// <summary>Returns a copy with the horizontal (X, Z) translation pinned to the given anchor (root-motion strip).</summary>
        public NodeAnimSampler StripHorizontal(float x, float z) =>
            new(new Vector3(x, _position.Y, z), _rotation, _scale);

        public Matrix4x4 ToMatrix() =>
            Matrix4x4.CreateScale(_scale)
            * Matrix4x4.CreateFromQuaternion(_rotation)
            * Matrix4x4.CreateTranslation(_position);

        public static NodeAnimSampler Sample(NodeAnim* channel, double tick)
        {
            Vector3 pos = SampleVector(channel->MPositionKeys, channel->MNumPositionKeys, tick, Vector3.Zero);
            Quaternion rot = SampleQuaternion(channel->MRotationKeys, channel->MNumRotationKeys, tick);
            Vector3 scale = SampleVector(channel->MScalingKeys, channel->MNumScalingKeys, tick, Vector3.One);
            return new NodeAnimSampler(pos, rot, scale);
        }

        private static Vector3 SampleVector(VectorKey* keys, uint count, double tick, Vector3 fallback)
        {
            if (count == 0)
            {
                return fallback;
            }

            if (count == 1)
            {
                return keys[0].MValue;
            }

            for (uint i = 0; i < count - 1; i++)
            {
                if (tick < keys[i + 1].MTime)
                {
                    double t0 = keys[i].MTime;
                    double t1 = keys[i + 1].MTime;
                    float f = t1 > t0 ? (float)((tick - t0) / (t1 - t0)) : 0f;
                    return Vector3.Lerp(keys[i].MValue, keys[i + 1].MValue, f);
                }
            }

            return keys[count - 1].MValue;
        }

        private static Quaternion SampleQuaternion(QuatKey* keys, uint count, double tick)
        {
            if (count == 0)
            {
                return Quaternion.Identity;
            }

            if (count == 1)
            {
                return ToNumerics(keys[0].MValue);
            }

            for (uint i = 0; i < count - 1; i++)
            {
                if (tick < keys[i + 1].MTime)
                {
                    double t0 = keys[i].MTime;
                    double t1 = keys[i + 1].MTime;
                    float f = t1 > t0 ? (float)((tick - t0) / (t1 - t0)) : 0f;
                    return Quaternion.Slerp(ToNumerics(keys[i].MValue), ToNumerics(keys[i + 1].MValue), f);
                }
            }

            return ToNumerics(keys[count - 1].MValue);
        }

        private static Quaternion ToNumerics(AssimpQuaternion q) => new(q.X, q.Y, q.Z, q.W);
    }
}
