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

    private bool _disposed;

    private const string VertexShaderSource =
        "#version 330 core\n" +
        "layout(location = 0) in vec3 aPos;\n" +
        "layout(location = 1) in vec3 aNormal;\n" +
        "layout(location = 2) in vec3 aColor;\n" +
        "uniform mat4 uMvp;\n" +
        "uniform mat4 uModel;\n" +
        "out vec3 vNormal;\n" +
        "out vec3 vColor;\n" +
        "void main()\n" +
        "{\n" +
        "    gl_Position = uMvp * vec4(aPos, 1.0);\n" +
        "    vNormal = mat3(uModel) * aNormal;\n" +
        "    vColor = aColor;\n" +
        "}\n";

    private const string FragmentShaderSource =
        "#version 330 core\n" +
        "in vec3 vNormal;\n" +
        "in vec3 vColor;\n" +
        "uniform vec3 uLightDir;\n" +
        "uniform vec3 uBaseColor;\n" +
        "out vec4 FragColor;\n" +
        "void main()\n" +
        "{\n" +
        "    vec3 n = normalize(vNormal);\n" +
        "    float diff = max(dot(n, normalize(-uLightDir)), 0.0);\n" +
        "    float ambient = 0.35;\n" +
        "    // TODO: texture sampling\n" +
        "    vec3 color = uBaseColor * vColor * (ambient + (1.0 - ambient) * diff);\n" +
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

        // Compute scene bounds (in corrected space) to frame the camera.
        var meshes = ExtractMeshes(scene, animationScene, animIndex, time);
        Bounds bounds = ComputeBounds(meshes, axisFix);
        (Matrix4x4 view, Matrix4x4 proj) = BuildCamera(bounds, yaw, pitch, opts);

        _gl.UseProgram(_program);
        var lightDir = new Vector3(-0.4f, -0.7f, -0.6f);
        _gl.Uniform3(_uLightDir, lightDir.X, lightDir.Y, lightDir.Z);

        _gl.BindVertexArray(_vao);

        foreach (CpuMesh mesh in meshes)
        {
            Matrix4x4 model = axisFix;
            Matrix4x4 mvp = model * view * proj;
            // Per-mesh base tint resolved from the material's diffuse color (or a gray fallback).
            Vector3 baseColor = mesh.BaseColor;
            _gl.Uniform3(_uBaseColor, baseColor.X, baseColor.Y, baseColor.Z);
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

        const uint stride = 9 * sizeof(float);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);

        SetMatrix(_uMvp, mvp);
        SetMatrix(_uModel, model);

        _gl.DrawElements(Silk.NET.OpenGL.PrimitiveType.Triangles, (uint)mesh.Indices.Length, DrawElementsType.UnsignedInt, (void*)0);
    }

    private void SetMatrix(int location, Matrix4x4 m)
    {
        // System.Numerics.Matrix4x4 is row-major; GLSL expects column-major, so transpose on upload.
        Span<float> data = stackalloc float[16]
        {
            m.M11, m.M21, m.M31, m.M41,
            m.M12, m.M22, m.M32, m.M42,
            m.M13, m.M23, m.M33, m.M43,
            m.M14, m.M24, m.M34, m.M44,
        };
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
    private CpuMesh[] ExtractMeshes(Scene scene, Scene animScene, int animIndex, float time)
    {
        if (scene.MRootNode is null || scene.MNumMeshes == 0)
        {
            return Array.Empty<CpuMesh>();
        }

        // Global transform per node, with animation applied where channels exist.
        var nodeGlobals = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        Dictionary<string, NodeAnimSampler>? samplers = BuildSamplers(animScene, animIndex, time);
        AccumulateNodeTransforms(scene.MRootNode, Matrix4x4.Identity, samplers, nodeGlobals);

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

    private Dictionary<string, NodeAnimSampler>? BuildSamplers(Scene scene, int animIndex, float time)
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

        return samplers;
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

        // Interleave position + normal + vertex color. Colors default to white so the per-mesh
        // material base color carries the hue; the white attribute keeps the GLSL multiply a no-op.
        // TODO: read actual per-vertex colors from mesh->MColors[0] when a model provides them.
        var vertices = new float[vertexCount * 9];
        for (int i = 0; i < vertexCount; i++)
        {
            int o = i * 9;
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
        return new CpuMesh(vertices, indices.ToArray(), baseColor);
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

    private static Bounds ComputeBounds(CpuMesh[] meshes, Matrix4x4 axisFix)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        foreach (CpuMesh mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Length; i += 9)
            {
                var p = new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                p = Vector3.Transform(p, axisFix);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }

        if (!any)
        {
            return new Bounds(Vector3.Zero, 1f);
        }

        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 1e-3f);
        return new Bounds(center, radius);
    }

    private static (Matrix4x4 view, Matrix4x4 proj) BuildCamera(
        Bounds bounds, float yaw, float pitch, RenderOptions opts)
    {
        float yawRad = yaw * MathF.PI / 180f;
        float pitchRad = pitch * MathF.PI / 180f;

        // Orbit the camera around the model center at a distance derived from the bounding radius.
        float distance = bounds.Radius / MathF.Max(opts.CamZoom, 1e-3f) * 2.6f;
        var dir = new Vector3(
            MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            MathF.Sin(pitchRad),
            MathF.Cos(pitchRad) * MathF.Cos(yawRad));
        Vector3 eye = bounds.Center + (dir * distance);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, bounds.Center, Vector3.UnitY);

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
        _gl.Dispose();
        _window.Dispose();
    }

    /// <summary>Bounding sphere of the posed model, used for camera framing.</summary>
    private readonly record struct Bounds(Vector3 Center, float Radius);

    /// <summary>
    /// Skinned CPU geometry: interleaved [px,py,pz, nx,ny,nz, r,g,b] vertices, triangle indices,
    /// and the mesh's material base color (used as a per-mesh tint uniform).
    /// </summary>
    private sealed class CpuMesh(float[] vertices, uint[] indices, Vector3 baseColor) : IDisposable
    {
        public float[] Vertices { get; } = vertices;

        public uint[] Indices { get; } = indices;

        public Vector3 BaseColor { get; } = baseColor;

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
