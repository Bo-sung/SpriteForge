# Third-Party Notices

SpriteForge is MIT-licensed (see `LICENSE`). The self-contained published executables
(`spriteforge.exe`, `pixelart.exe`) bundle native and managed third-party components,
listed below with their licenses. This file exists to satisfy the attribution/notice-
retention terms of the permissive licenses used by those components.

## Bundled in the published binaries

| Component | Used for | License |
|---|---|---|
| [Assimp](https://github.com/assimp/assimp) (via `Silk.NET.Assimp`) | FBX/GLB loading, skeleton/animation import | [BSD 3-Clause](https://github.com/assimp/assimp/blob/master/LICENSE) |
| [GLFW](https://www.glfw.org/) (via `Silk.NET.GLFW` / `Silk.NET.Windowing`) | Headless OpenGL context creation | [zlib/libpng License](https://www.glfw.org/license.html) |
| [Silk.NET](https://github.com/dotnet/Silk.NET) | .NET bindings for OpenGL/Assimp/GLFW | [MIT](https://github.com/dotnet/Silk.NET/blob/main/LICENSE.md) |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | All image I/O and pixel manipulation | [MIT](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) (wraps Google's [Skia](https://skia.org/), BSD-3-Clause) |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | CLI argument parsing (`spriteforge.exe`, `pixelart.exe`) | [MIT](https://github.com/dotnet/command-line-api/blob/main/LICENSE.md) |

## Not bundled (build/test-time only)

xunit, FluentAssertions, coverlet.collector, and Microsoft.NET.Test.Sdk are used only by
the test project and are never part of a published executable.

## Reimplemented algorithms (no third-party code included)

Two pixel-art passes are original C# implementations of published techniques/algorithms,
not ports of any specific licensed codebase:

- **Palette quantization** (`SpriteForge.Core/PixelArt/WuColorQuantizer.cs`) — Xiaolin Wu's
  greedy orthogonal bipartition quantizer (*Graphics Gems II*, 1991), written from the
  published algorithm description.
- **Dominant-color downscaling** (`SpriteForge.Core/PixelArt/DominantDownscaler.cs`) —
  reimplements the block-voting technique used by
  [jenissimo/unfake.js](https://github.com/jenissimo/unfake.js) (MIT), independently
  written in C# against SkiaSharp.

## Project history

The rendering core (offscreen OpenGL + Assimp skeleton/animation evaluation) originated
as a fork/extension of the same author's
[Bo-sung/ComfyUI-FBX-ControlNet-Converter](https://github.com/Bo-sung/ComfyUI-FBX-ControlNet-Converter)
(MIT). That project also bundled SixLabors.ImageSharp (Six Labors Split License, not
fully permissive); SpriteForge does not — all image I/O here uses SkiaSharp only.
