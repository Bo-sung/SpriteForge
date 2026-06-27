# Changelog

All notable changes to PixelSprite CLI are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

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
