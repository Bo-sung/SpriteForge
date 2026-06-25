# PixelSprite CLI — Project Memory

## What this is
A Windows-first standalone CLI tool that:
1. Loads a rigged FBX/GLB model + animation
2. Renders it from N directions (2/4/8) at high resolution via OpenGL offscreen
3. Downsamples to pixel art (dominant-color block voting + Wu quantization)
4. Outputs transparent-background sprite sheets and/or frame sequences as PNG

The rendering core is forked/extended from:
https://github.com/Bo-sung/ComfyUI-FBX-ControlNet-Converter
(C# / .NET 8, Silk.NET OpenGL, Assimp)

## Stack
- Language: C# / .NET 8, self-contained win-x64 publish
- 3D: Silk.NET.OpenGL + Silk.NET.Assimp (FBX/GLB load, skinning, anim eval)
- Image I/O: SkiaSharp (MIT — NOT SixLabors.ImageSharp, license concern)
- Palette quantization: nQuant.Core (Wu quantization, MIT)
- CLI parsing: System.CommandLine

## Solution layout
```
PixelSprite/
├── PixelSprite.sln
├── src/
│   ├── PixelSprite.Core/          # class library
│   │   ├── Rendering/
│   │   │   ├── OffscreenRenderer.cs     # OpenGL FBO, RGBA8, glClear alpha=0
│   │   │   ├── DirectionScheduler.cs    # computes yaw list for 2/4/8 dirs
│   │   │   └── RenderJob.cs             # one animation × all directions
│   │   ├── PixelArt/
│   │   │   ├── PixelArtProcessor.cs     # orchestrates steps below
│   │   │   ├── DominantDownscaler.cs    # block voting downscale
│   │   │   ├── PaletteQuantizer.cs      # wraps nQuant.Core Wu quantization
│   │   │   ├── AlphaBinarizer.cs        # threshold + edge dilation
│   │   │   └── ArtifactCleaner.cs       # morphological + jaggy cleanup
│   │   ├── Packing/
│   │   │   ├── SpriteSheetPacker.cs     # assembles final sheet PNG
│   │   │   └── MetadataWriter.cs        # writes metadata.json for Unity
│   │   └── Models/
│   │       ├── RenderOptions.cs
│   │       ├── PixelArtOptions.cs
│   │       ├── OutputOptions.cs
│   │       └── SpriteFrame.cs
│   └── PixelSprite.Cli/           # executable entry point
│       └── Program.cs             # System.CommandLine wiring
├── tests/
│   └── PixelSprite.Tests/
│       ├── DominantDownscalerTests.cs
│       ├── AlphaBinarizerTests.cs
│       └── PaletteQuantizerTests.cs
└── build.ps1                      # dotnet publish win-x64 self-contained
```

## Key algorithms to implement

### DominantDownscaler
Source: jenissimo/unfake.js algorithm (MIT), ported to C#.
Per block (renderW/spriteW × renderH/spriteH pixels):
1. Collect all pixels in block → Dictionary<(R,G,B,A), int> frequency map
   - Skip fully transparent pixels (A < alphaThreshold) when counting
2. Find dominant color (highest frequency)
3. If dominant_count / total_opaque_count > 0.05 threshold → use dominant color
4. Else → mean of opaque pixels (average R,G,B; set A=255)
5. If block has zero opaque pixels → output pixel is fully transparent (A=0)

### AlphaBinarizer  
Run AFTER downscale, BEFORE palette quantization:
1. For each pixel: A = (A >= alphaThreshold) ? 255 : 0
2. Edge dilation (optional, default on):
   - For each transparent pixel adjacent to an opaque pixel,
     copy the RGB of the nearest opaque neighbor (keep A=0)
   - Prevents dark fringing from OpenGL premultiplied alpha bleed

### PaletteQuantizer
- Wrap nQuant.Core WuQuantizer
- Respect fixedPalette: if provided, skip Wu and nearest-color-match only
- Only quantize opaque pixels; transparent pixels stay A=0

### ArtifactCleaner
- Morphological: erode then dilate (open) to remove isolated noise pixels
- Jaggy: if a pixel differs from all 4 cardinal neighbors → replace with majority neighbor color

### DirectionScheduler
- 2 dirs:  [0°, 180°]  (front, back)  
- 4 dirs:  [0°, 90°, 180°, 270°]  
- 8 dirs:  [0°, 45°, 90°, 135°, 180°, 225°, 270°, 315°]
- Yaw is added to base cam angle; pitch stays fixed (default 26.5° SC1-style)
- Output file naming: {animName}_dir{dirIndex:D2}_f{frameIndex:D4}.png

### SpriteSheetPacker
Layout: rows = directions, columns = frames
- Sheet width  = spriteW × frameCount
- Sheet height = spriteH × dirCount
- Also outputs metadata.json (see MetadataWriter)

### MetadataWriter — metadata.json schema
```json
{
  "spriteWidth": 48,
  "spriteHeight": 48,
  "directions": 8,
  "animations": [
    {
      "name": "Walk",
      "frameCount": 12,
      "fps": 12,
      "sheetRow": 0
    }
  ],
  "pivot": { "x": 0.5, "y": 0.0 }
}
```

## CLI contract (System.CommandLine)

```
pixelsprite.exe [options]

Required:
  --input   <path>   FBX or GLB file (skinned mesh + animation, or mesh only)

Optional:
  --anim    <path>   Separate animation-only FBX (retargeted by bone name)
  --directions <n>   2 | 4 | 8                          (default: 8)
  --render-size <n>  Offscreen render resolution (square) (default: 256)
  --sprite-size <n>  Final pixel art resolution (square)  (default: 48)
  --fps     <n>      Frame sampling rate                  (default: 12)
  --frames  <n>      Force exact frame count; 0 = whole clip (default: 0)
  --max-colors <n>   Palette color limit                  (default: 32)
  --palette <path>   Fixed palette PNG (skip Wu quantization)
  --alpha-threshold <n>  Binarization cutoff 0-255       (default: 128)
  --no-edge-dilate   Disable edge dilation
  --cleanup <list>   Comma-separated: morph,jaggy         (default: morph,jaggy)
  --output-mode <m>  sheet | frames | both                (default: sheet)
  --out     <path>   Output directory                     (default: ./output)
  --cam-pitch <f>    Camera vertical angle degrees        (default: 26.5)
  --cam-zoom  <f>    Zoom factor                          (default: 1.0)
  --ortho            Orthographic projection
  --up-axis  <s>     y | z                                (default: y)
  --verbose          Print per-frame progress
```

## Alpha / transparency rules (CRITICAL)
- OpenGL FBO must be RGBA8. glClearColor(0,0,0,0) before each frame.
- Enable GL_BLEND with premultiplied alpha during render.
- DominantDownscaler must produce A=0 for empty blocks.
- AlphaBinarizer runs immediately after downscale.
- Edge dilation copies RGB only — A stays 0 on transparent pixels.
- Final PNG must be saved with full alpha channel (SkiaSharp SKColorType.Rgba8888).
- Sprite sheet background must be transparent (not filled with any color).

## Output file naming
Frame sequence mode:
  {outDir}/{animName}_dir{D2}_f{D4}.png

Sheet mode:
  {outDir}/{animName}_sheet.png
  {outDir}/{animName}_metadata.json

## Dependencies (NuGet)
- Silk.NET.OpenGL
- Silk.NET.Windowing
- Silk.NET.GLFW  
- Silk.NET.Assimp
- SkiaSharp
- nQuant.Core
- System.CommandLine

## Build
```powershell
# build.ps1
dotnet publish src/PixelSprite.Cli/PixelSprite.Cli.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o ./bin
```

## Notes for Claude Code
- The rendering pipeline (Assimp load → bone eval → skinning → OpenGL draw) 
  follows the same pattern as Bo-sung/ComfyUI-FBX-ControlNet-Converter.
  Reuse that architecture: scene load once, loop over (direction × frame).
- OffscreenRenderer must NOT create a visible window. 
  Use GLFW offscreen context: glfwWindowHint(GLFW_VISIBLE, GLFW_FALSE).
- All pixel math uses float intermediates; convert to byte only at final write.
- Tests should use small synthetic bitmaps (e.g. 16×16 with known colors),
  not real FBX files.
