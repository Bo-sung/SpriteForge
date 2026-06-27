# PixelSprite CLI

A Windows-first, standalone **.NET 8** command-line tool that loads a rigged FBX/GLB model, renders it from 2, 4, or 8 directions at high resolution via OpenGL offscreen rendering, and converts the result into transparent-background **pixel-art sprite sheets** (or frame sequences). Downsampling uses dominant-color block voting plus Wu palette quantization, producing clean, low-color sprites ready for engines like Unity. The rendering core is forked and extended from [Bo-sung/ComfyUI-FBX-ControlNet-Converter](https://github.com/Bo-sung/ComfyUI-FBX-ControlNet-Converter).

## Tech stack

- **C# / .NET 8** — self-contained `win-x64` publish
- **Silk.NET.OpenGL + Silk.NET.Assimp** — FBX/GLB load, skinning, animation evaluation, OpenGL offscreen rendering
- **SkiaSharp** — PNG image I/O with full alpha channel
- **Vendored Wu quantizer** (nQuant.Core / Wu quantization) — palette reduction
- **System.CommandLine** — CLI argument parsing

## Build & run

```powershell
# Restore + build (Debug or Release)
dotnet build -c Release

# Run the test suite
dotnet test

# Publish a self-contained single-file executable -> ./bin/pixelsprite.exe
./build.ps1
```

`build.ps1` runs `dotnet publish` for win-x64 with `--self-contained` and `PublishSingleFile`, bundling all managed and native dependencies. No .NET runtime install is required on the target machine. The resulting binary is `./bin/pixelsprite.exe`.

## Quick start

Render the committed sample cube from 4 directions and emit both a sheet and frames:

```powershell
./bin/pixelsprite.exe --input samples/cube.obj --directions 4 --render-size 64 --sprite-size 16 --frames 1 --output-mode both --out output
```

## Options reference

| Option | Default | Description |
| --- | --- | --- |
| `--input <path>` | *(required)* | FBX or GLB file (skinned mesh + animation, or mesh only) |
| `--anim <path>` | — | Separate animation-only FBX, retargeted by bone name |
| `--directions <n>` | `8` | Number of render directions: `2`, `4`, or `8` |
| `--render-size <n>` | `256` | Offscreen render resolution (square) |
| `--sprite-size <n>` | `48` | Final pixel-art resolution (square) |
| `--fps <n>` | `12` | Frame sampling rate |
| `--frames <n>` | `0` | Force exact frame count; `0` = whole clip |
| `--max-colors <n>` | `32` | Palette color limit |
| `--palette <path>` | — | Fixed palette PNG (skips Wu quantization, nearest-color match only) |
| `--alpha-threshold <n>` | `128` | Binarization cutoff, `0`–`255` |
| `--no-edge-dilate` | off | Disable edge dilation |
| `--cleanup <list>` | `morph,jaggy` | Comma-separated cleanup steps: `morph`, `jaggy` |
| `--output-mode <m>` | `sheet` | Output mode: `sheet`, `frames`, or `both` |
| `--out <path>` | `./output` | Output directory |
| `--cam-pitch <f>` | `26.5` | Camera vertical angle, in degrees |
| `--cam-zoom <f>` | `1.0` | Zoom factor |
| `--ortho` | off | Use orthographic projection |
| `--up-axis <s>` | `y` | Model up axis: `y` or `z` |
| `--verbose` | off | Print per-frame progress |

## Output

**Frame sequence mode** (`--output-mode frames` or `both`):

```
{outDir}/{animName}_dir{DD}_f{FFFF}.png
```

Directions are zero-padded to 2 digits (`dir00`), frames to 4 digits (`f0000`).

**Sheet mode** (`--output-mode sheet` or `both`):

```
{outDir}/{animName}_sheet.png
{outDir}/{animName}_metadata.json
```

The sheet is laid out with **rows = directions** and **columns = frames** (width = `spriteWidth × frameCount`, height = `spriteHeight × directionCount`). Backgrounds are fully transparent.

### `metadata.json` schema

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

## Notes / known limitations

- The render path is verified for **static models** (e.g. the bundled `samples/cube.obj`).
- **Skinned / animated models** and separate `--anim` retargeting (by bone name) are not yet fully validated/implemented.
- **Image-texture shading is pending**: the current renderer shades using material diffuse color plus vertex color only — texture maps are not yet sampled.
