# SpriteForge

**Turn rigged 3D character models into pixel-art sprite sheets** — render from N fixed directions,
downsample with dominant-color block voting, quantize to a limited palette, and pack the result into a
sheet (or frame sequence) ready for Unity/Unreal import.

Inspired by how StarCraft 1's sprites were made: render a high-resolution 3D model from fixed
isometric directions, then downsample pixel by pixel. SpriteForge automates that pipeline with modern
tools (OpenGL offscreen rendering, a ported [unfake.js](https://github.com/jenissimo/unfake.js)-style
pixel-art post-process) and adds a desktop GUI on top for interactive setup.

## Contents

- [What's in this repo](#whats-in-this-repo)
- [Key Features](#key-features)
- [Pipeline Flow](#pipeline-flow)
- [Build & Run](#build--run)
- [CLI Reference](#cli-reference) — [`spriteforge.exe`](#spriteforgeexe) · [`pixelart.exe`](#pixelartexe)
- [Desktop GUI](#desktop-gui---spriteforgegui)
- [Output Files](#output-files)
- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [License](#license)

---

## What's in this repo

Three apps share one core library (`SpriteForge.Core`) — the 3D→sprite pipeline is implemented once and
reused by all three:

| App | Type | What it does |
|---|---|---|
| **`spriteforge.exe`** | CLI | The full pipeline: FBX/GLB → render N directions → pixel art → sprite sheet |
| **`pixelart.exe`** | CLI | Standalone pixel-art post-processing (palette, dither, outline) on existing images — no 3D involved |
| **`SpriteForge.Gui`** | WPF desktop app | Interactive: load a mesh, tune camera/animation/equipment with a live preview, generate the sheet, and play it back per direction — before committing to a batch CLI run |

---

## Key Features

### Automatic 3D → pixel art conversion
Input FBX/GLB (rigged mesh + animation, or a static mesh); the renderer rotates the **model** per
direction (camera and light stay fixed, so lighting/framing stay consistent) and produces transparent
offscreen frames, which are then converted to pixel art and packed into a sheet.

### Directional rendering (2 / 4 / 8 directions)
Default is 8 directions (0°–315°, 45° steps); 2 (front/back) and 4 are also supported. Default camera
pitch is 26.5° (SC1-style isometric). Framing is computed once per clip so scale/centering stay constant
across every frame and direction.

### Pixel-art post-processing pipeline
Ported from [unfake.js](https://github.com/jenissimo/unfake.js) and extended, applied in this order:

| Stage | What it does |
|---|---|
| Morphological Clean | Removes isolated noise specks before downscaling (erode → dilate) |
| Dominant Downscale | Per-block frequency voting for the representative color, minimizing edge bleed |
| Alpha Binarization | Hard-thresholds semi-transparent edge pixels to 0/255 for a clean transparent background |
| Edge Dilation | Copies RGB from adjacent opaque pixels into transparent border pixels to prevent dark fringing |
| Dither *(optional)* | Bayer (ordered) or Floyd–Steinberg (error-diffusion), applied against the same palette used for quantization |
| Palette Quantization | Wu quantization (default 32 colors) or a supplied fixed palette |
| Jaggy Clean | Replaces pixels that disagree with every cardinal neighbor |
| Outline *(optional)* | Draws an outer or inner silhouette outline as the final step, after cleanup |

### Transparent background, guaranteed
Alpha is preserved end to end; the OpenGL FBO clears to `(0,0,0,0)` and every post-process step treats
transparency as first-class (never filled with a background color).

### Equipment (weapons & armor) and cross-skeleton retargeting
Attach weapons/armor via a JSON manifest (Unreal-style sockets or master-pose skinning), and/or retarget
an animation authored for one skeleton onto a differently-named rig (joint mapping by bone name).

### Engine-style camera
Position (X/Y/Z pan), rotation (X/Y/Z — pitch/facing/roll), zoom, perspective/orthographic, and
Y-up/Z-up source axis — the same controls whether you're driving the CLI or the GUI.

### Interactive GUI on the same core
`SpriteForge.Gui` never reimplements rendering — it drives `SpriteForge.Core` through a managed preview
facade (`PreviewSession`) on a dedicated GL thread, so what you see in the live preview is the same
renderer the CLI uses for the final sheet.

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
    ├─ MorphClean
    ├─ DominantDownscale   (skipped if sprite-size 0 — used by pixelart.exe on already-pixel-art images)
    ├─ AlphaBinarize
    ├─ Dither              (optional: bayer | floyd)
    ├─ PaletteQuantize
    ├─ JaggyClean
    └─ Outline             (optional: outer | inner)
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

**Requirements:** Windows, .NET 8 SDK or higher

```powershell
# Build + publish the two self-contained single-file CLIs -> ./bin/spriteforge.exe, ./bin/pixelart.exe
./build.ps1

# spriteforge.exe: default run (8 directions, 48px, 32 colors)
./bin/spriteforge.exe --input "Knight.fbx"

# spriteforge.exe: with options
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

# pixelart.exe: re-process an existing image or a whole folder (no 3D)
./bin/pixelart.exe --input frame.png --output frame_out.png --max-colors 16 --outline --dither floyd
./bin/pixelart.exe --input ./frames/ --output ./frames_out/ --max-colors 16

# SpriteForge.Gui: desktop app (not published by build.ps1 — run via dotnet)
dotnet run --project src/SpriteForge.Gui
```

---

## CLI Reference

### `spriteforge.exe`

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

### `pixelart.exe`

Standalone image → pixel-art post-processing. `--input`/`--output` each accept a single file or a
folder (batch mode processes every `.png`/`.jpg`/`.jpeg`/`.bmp` in the folder).

| Option | Default | Description |
|---|---|---|
| `--input` | *(Required)* | Image file or folder to convert |
| `--output` | `./output` | Output file (single input) or folder (batch input) |
| `--sprite-size` | `0` | Downscale target resolution (square); `0` = skip downscaling (image is already pixel art) |
| `--max-colors` | `32` | Palette color limit for Wu quantization |
| `--palette` | — | Fixed palette PNG (skips Wu quantization) |
| `--alpha-threshold` | `128` | Alpha binarization cutoff 0–255 |
| `--no-edge-dilate` | — | Disable edge dilation |
| `--cleanup` | `morph,jaggy` | Comma-separated cleanup passes: `morph`, `jaggy` |
| `--outline` | — | Draw an outline around the silhouette |
| `--outline-color` | `#000000` | Outline color: `#RRGGBB` or `#AARRGGBB` |
| `--outline-type` | `outer` | `outer` / `inner` |
| `--dither` | `none` | `none` / `bayer` / `floyd` |
| `--verbose` | — | Print per-file progress |

---

## Desktop GUI — `SpriteForge.Gui`

A WPF tool built around the same core, for interactive setup before batch-generating with the CLI (or
generating directly from the GUI). Run with `dotnet run --project src/SpriteForge.Gui`.

- **Model** — browse a mesh and an optional separate animation file; set render size / fps / direction
  count; Load/Reload.
- **Camera** — engine-style transform: **position** (X/Y/Z pan), **rotation** (X = pitch, Y = facing/orbit
  yaw, Z = roll, degrees), **zoom**, plus orthographic, Z-up source, and in-place (remove root motion)
  toggles. Every change re-renders the live preview.
- **Timeline / Animation** — a frame scrubber plus transport controls (play/pause, stop, step,
  loop) to visually confirm the animation on the mesh.
- **Equipment** — load an equipment manifest JSON; toggle individual attachments on/off and see them on
  the character immediately.
- **Output** — generate the full sprite sheet, preview it inline, and save the sheet PNG + metadata.json.
- **2D result window** — plays the just-generated sheet back as an animation, one direction at a time
  (step through directions, play/pause, loop) — the same 8-direction sheet that gets saved, but viewed as
  motion instead of a static grid. Resizable against the 3D preview via a draggable splitter.

The GUI never reimplements the renderer: `SpriteForge.Core.Rendering.PreviewSession` wraps
`OffscreenRenderer` on a dedicated OpenGL thread for single-frame interactive preview, and "Generate
sheet" runs the exact same `RenderJob`/`PixelArtProcessor` pipeline the CLI uses.

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
  "pivot": { "x": 0.5, "y": 0.5 }
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
| Desktop GUI | WPF (.NET 8) | MIT |
| Runtime | .NET 8 | MIT |

Pixel art post-processing algorithm independently implemented in C# based on
[unfake.js](https://github.com/jenissimo/unfake.js) (MIT).

---

## Project Structure

```
SpriteForge/
├── src/
│   ├── SpriteForge.Core/       # Core library shared by all three apps
│   │   ├── Rendering/          # OffscreenRenderer, DirectionScheduler, RenderJob, PreviewSession
│   │   ├── PixelArt/           # Pixel-art processing pipeline (incl. DitherPass, OutlinePass)
│   │   ├── Packing/            # Sprite sheet packing and metadata
│   │   └── Models/             # Options and data classes
│   ├── SpriteForge.Cli/        # spriteforge.exe — the 3D pipeline CLI
│   ├── PixelArt.Cli/           # pixelart.exe — standalone pixel-art post-processing CLI
│   └── SpriteForge.Gui/        # WPF desktop app (MVVM around SpriteForge.Core)
│       ├── ViewModels/         # MainViewModel + per-panel view models (animation, equipment, output, sheet player)
│       ├── Views/              # Panel UserControls
│       └── Services/           # PreviewService (renderer <-> WPF bitmap bridge)
├── design/                     # Design reference: style spec + mockup exported from Claude Design
├── tests/
│   └── SpriteForge.Tests/      # Unit tests
└── build.ps1                   # Publishes spriteforge.exe + pixelart.exe as win-x64 single-file builds
```

---

## License

MIT
