# Changelog

All notable changes to PixelSprite CLI are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [1.2.0] - 2026-06-28

### Added
- **Equipment / attachment system** — weapons and armor can now be equipped onto the
  character before rendering, modelled on Unreal Engine's skeletal-mesh sockets and
  `MasterPoseComponent` (and Unity's parent-constraint / child-socket patterns).
  - `--equip <manifest.json>` loads a JSON manifest of attachments.
  - **Socket mode** (default): a static mesh placed rigidly at `offset × boneGlobal`
    every frame, for items held in the hand (swords, shields). The mesh tracks the
    bone with no skinning.
  - **Master-pose mode** (`useMasterPose: true`): a skinned mesh sharing the
    character's bone names, skinned with the character's per-frame bone poses, for
    body-fitting gear (armor, helmets).
  - Manifest `file` paths resolve relative to the manifest directory and are verified
    at load time; `offset.rotation` is in degrees.
- New types: `Attachment`, `EquipmentManifest`, `EquipmentManifestLoader`,
  `SocketTransform`, `AttachmentScene`.
- New tests: `SocketTransformTests` (SRT order, socket/bone composition),
  `EquipmentManifestLoaderTests` (parsing, path resolution, validation).

### Fixed
- **Static meshes no longer collapse to the origin** — `BuildMesh` previously ignored
  the owning node's global transform for unskinned meshes, so a weapon parented under a
  hand bone (or any static prop in a multi-node scene) rendered at `(0,0,0)`. Static
  meshes are now placed at their owning node's global transform, which is also the
  foundation the socket-attachment feature builds on.

## [1.1.0] - 2026-06-28

### Fixed
- **Renderer MVP transpose** — the model-view-projection matrix was uploaded in
  the wrong order for System.Numerics' row-vector convention, so GLSL applied `M`
  instead of `M^T`. Small/symmetric models still looked plausible, but real Mixamo
  rigs (large, asymmetric) collapsed into a "bowtie" of stretched triangles. The
  matrix is now uploaded correctly and complex rigs render properly.

### Added
- **Root-motion detection** — `RootMotion.Analyze` finds the root/hips channel with
  the most horizontal travel and reports its distance. `--check-root-motion` prints
  the report and exits without rendering; `--in-place` pins the root node's XZ
  translation to its start so the character stays centred (vertical bob kept).
- **Engine-aligned camera coordinate system** — a spherical camera in a Y-up
  (Unity-style) frame by default, or Z-up (Unreal-style) via `--up-axis z`. New
  controls: `--cam-yaw` (base azimuth/facing for direction 0), `--cam-distance`
  (explicit camera distance, 0 = auto), and `--cam-target` (look-at pan offset).
- **Diffuse texture sampling** — meshes carry UVs and each material's diffuse
  texture is loaded and sampled in the shader, so characters render with their
  actual skins. Embedded textures (e.g. Mixamo FBX) are matched by file name and
  decoded with SkiaSharp; external files are loaded best-effort. Meshes without a
  diffuse texture fall back to the material/vertex color.

### Changed
- **Directions render by rotating the model** — the camera and light now stay fixed
  at the front and the model rotates about its vertical centre axis per direction.
  This gives consistent screen-space lighting and rotation-invariant framing across
  directions, replacing the previous camera orbit.
- **Per-clip fixed framing** — camera framing is computed once from the union of the
  whole clip's bounds and reused for every frame and direction, removing
  frame-to-frame scale jitter and making `--in-place` visually meaningful.

## [1.0.0] - 2026-06-27

First release. A Windows-first .NET 8 CLI that renders a rigged FBX/GLB model from
2/4/8 directions via headless OpenGL and converts the frames to transparent-background
pixel-art sprite sheets.

### Added
- **Rendering** — headless GLFW/OpenGL 3.3 offscreen renderer (RGBA8 FBO, transparent
  clear, src-alpha blending), Assimp scene loading, CPU bone skinning with animation
  key interpolation, and per-mesh material-diffuse shading.
- **Pixel-art pipeline** — dominant-color block downscaling, alpha binarization with
  edge dilation, vendored Wu palette quantization, and morphological + jaggy cleanup.
- **Direction scheduling** — 2/4/8 evenly distributed yaw angles.
- **Packing** — sprite-sheet assembly (rows = directions, cols = frames) and a Unity
  friendly `metadata.json`.
- **Separate `--anim` retargeting** — apply an animation from a second file onto the
  model's skeleton by matching bone names.
- **CLI** — full option contract via System.CommandLine; `sheet` / `frames` / `both`
  output modes.
- **Distribution** — `build.ps1` produces a self-contained single-file `pixelsprite.exe`.
- Unit tests (20), a GitHub Actions CI workflow, README, and `samples/` (static cube +
  rigged/animated glTF).

### Known limitations
- Image-texture sampling is not yet implemented (shading uses material diffuse +
  vertex color); per-vertex color extraction is a TODO.
- The render path is verified on static and simple skinned/animated models; complex
  production rigs have not been exercised.
