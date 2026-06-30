# SpriteForge

**CLI Pipeline for Converting 3D Rigging Models into Pixel Art Sprite Sheets**

Inspired by the sprite creation method from StarCraft 1, it processes high-resolution 3D rendering → downsampling → pixel art post-processing → preparation for Unity import in a single command.

---

## Background

SC1 sprites were created by rendering high-resolution 3D models on Silicon Graphics workstations and downsampling pixel by pixel. SpriteForge recreates this pipeline with modern tools, adding automated post-processing to enable high-quality sprite creation independently.

---

## Key Features

### Automatic 3D to Pixel Art Conversion
Input FBX/GLB files, rotate cameras in specified directions to render off-screen images, then convert to pixel art and pack into sprite sheets.

### Directional Rendering (2 / 4 / 8 Directions)
Default is 8 directions (0° ~ 315°, 45° intervals). Supports 2 directions (front/back) and 4 directions as options. The vertical camera angle is set to 26.5° (matching SC1 isometric angle).

### Pixel Art Post-Processing Pipeline
Applies a processing layer ported from the [unfake.js](https://github.com/jenissimo/unfake.js) algorithm in C#, not just nearest-neighbor downsampling.

| Stage | Processing Content |
|---|---|
| Dominant Downscale | Selects representative colors through frequency voting within blocks to minimize boundary bleeding |
| Alpha Binarization | Divides semi-transparent boundary pixels into 0 or 255 to ensure transparent backgrounds |
| Edge Dilation | Fills RGB of transparent border pixels with adjacent opaque colors to prevent jagged edges |
| Palette Quantization | Limits color count using Wu Quantization (default 32 colors), allowing fixed palette specification |
| Artifact Cleanup | Combines noise removal (morphological) and jagged edge smoothing |

### Transparent Background Guarantee
Maintains alpha channels from rendering to output, ensuring backgrounds are always fully transparent (alpha 0).

### Direct Unity Import Ready
Outputs sprite sheets (PNG) along with pivot and frame information files.

### Extensible GUI Architecture
Designed with separate core libraries and CLI entry points for potential future GUI frontend integration without altering core functionality.

---

## Pipeline Flow

```
FBX / GLB
    │
    ▼
[OffscreenRenderer]  ← OpenGL offscreen, RGBA8, transparent background
    │  Hi-res frames × (direction count × animation frame count)
    ▼
[PixelArtProcessor]
    ├─ MorphClean       (pre-downscale noise removal on hi-res)
    ├─ DominantDownscale
    ├─ AlphaBinarize
    ├─ PaletteQuantize
    └─ JaggyClean
    │  Pixel art frames
    ▼
[SpriteSheetPacker]   ← rows = directions, columns = frames
    │
    ▼
output/
  Walk_sheet.png
  Walk_metadata.json
```

---

## Build & Run

**Requirements:** Windows, .NET 8 or higher

```powershell
# Build
./build.ps1

# Default run (8 directions, 48px, 32 colors)
./bin/spriteforge.exe --input "Knight.fbx"

# With options
./bin/spriteforge.exe `
  --input "Knight.fbx" `
  --anim "Knight_Walk.fbx" `
  --directions 8 `
  --render-size 256 `
  --sprite-size 48 `
  --fps 12 `
  --max-colors 32 `
  --output-mode both `
  --out "./output"
```

---

## CLI Options

| Option | Default | Description |
|---|---|---|
| `--input` | *(Required)* | Path to FBX or GLB file |
| `--anim` | — | Separate animation-only FBX (retargeted by bone name) |
| `--directions` | `8` | `2` / `4` / `8` |
| `--render-size` | `256` | Off-screen render resolution (square) |
| `--sprite-size` | `48` | Final pixel art resolution (square) |
| `--fps` | `12` | Frame sampling rate |
| `--frames` | `0` | Force exact frame count (`0` = entire clip) |
| `--max-colors` | `32` | Maximum palette color count |
| `--palette` | — | Fixed palette PNG (skips Wu quantization) |
| `--alpha-threshold` | `128` | Alpha binarization threshold (0–255) |
| `--no-edge-dilate` | — | Disable edge dilation |
| `--cleanup` | `morph,jaggy` | Comma-separated cleanup passes: `morph`, `jaggy` |
| `--output-mode` | `sheet` | `sheet` / `frames` / `both` |
| `--out` | `./output` | Output directory |
| `--cam-pitch` | `26.5` | Camera vertical angle (degrees) |
| `--cam-zoom` | `1.0` | Zoom factor |
| `--cam-yaw` | `0` | Base azimuth for direction 0 (rotates all directions about the up axis) |
| `--cam-distance` | `0` | Explicit camera distance in model units (`0` = automatic) |
| `--cam-target` | — | Look-at pan offset from model centre as `x,y,z` |
| `--ortho` | — | Orthographic projection |
| `--up-axis` | `y` | `y` (Unity) / `z` (Unreal) |
| `--in-place` | — | Remove root motion: keep the character centred |
| `--check-root-motion` | — | Report root motion in the animation, then exit |
| `--equip` | — | Equipment manifest JSON (socket / master-pose attachments) |
| `--retarget` | — | Retarget map JSON for cross-skeleton animation |
| `--list-bones` | — | Dump skeleton/node tree, then exit |
| `--verbose` | — | Print per-frame progress |

---

## Output Files

**Sprite Sheet Mode (`--output-mode sheet`)**
```
output/
  Walk_sheet.png        # rows = directions, columns = frames
  Walk_metadata.json    # metadata for Unity import
```

**Frame Sequence Mode (`--output-mode frames`)**
```
output/
  Walk_dir00_f0000.png  # direction 0, frame 0
  Walk_dir00_f0001.png
  ...
  Walk_dir07_f0011.png  # direction 7, frame 11
```

**metadata.json Structure**
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

---

## Technology Stack

| Role | Library | License |
|---|---|---|
| 3D Loading / Skinning | Silk.NET.Assimp | MIT |
| OpenGL Rendering | Silk.NET.OpenGL + GLFW | MIT |
| Image I/O | SkiaSharp | MIT |
| Palette Quantization | Wu quantizer (vendored) | MIT |
| CLI Parsing | System.CommandLine | MIT |
| Runtime | .NET 8 | MIT |

Pixel art post-processing algorithm independently implemented in C# based on [unfake.js](https://github.com/jenissimo/unfake.js) (MIT).

---

## Project Structure

```
SpriteForge/
├── src/
│   ├── SpriteForge.Core/       # Core library (reusable for GUI frontend)
│   │   ├── Rendering/          # OffscreenRenderer, DirectionScheduler, RenderJob
│   │   ├── PixelArt/           # Pixel art processing pipeline
│   │   ├── Packing/            # Sprite sheet packing and metadata
│   │   └── Models/             # Options and data classes
│   └── SpriteForge.Cli/        # CLI entry point
├── tests/
│   └── SpriteForge.Tests/      # Unit tests
└── build.ps1                   # win-x64 single-file build
```

---

## License

MIT
