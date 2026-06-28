using System.CommandLine;
using System.CommandLine.Invocation;
using PixelSprite.Core.Models;
using PixelSprite.Core.Packing;
using PixelSprite.Core.Rendering;
using SkiaSharp;

namespace PixelSprite.Cli;

/// <summary>
/// Command-line entry point. Parses the full CLI contract, assembles the option objects, runs the
/// render pipeline, and writes output as a sprite sheet, individual frames, or both.
/// </summary>
internal static class Program
{
    /// <summary>Parses arguments and dispatches to the render/save pipeline.</summary>
    /// <param name="args">Raw process arguments.</param>
    /// <returns>0 on success; 1 if any error occurred.</returns>
    private static async Task<int> Main(string[] args)
    {
        // --- Options. Defaults mirror the CLI contract in CLAUDE.md exactly. ---
        var inputOption = new Option<string>("--input", "FBX or GLB file (skinned mesh + animation, or mesh only).")
        {
            IsRequired = true,
        };

        var animOption = new Option<string?>(
            "--anim", "Separate animation-only FBX (retargeted by bone name).");
        var directionsOption = new Option<int>(
            "--directions", () => 8, "Number of rendered directions: 2, 4, or 8.");
        var renderSizeOption = new Option<int>(
            "--render-size", () => 256, "Offscreen render resolution (square).");
        var spriteSizeOption = new Option<int>(
            "--sprite-size", () => 48, "Final pixel art resolution (square).");
        var fpsOption = new Option<int>(
            "--fps", () => 12, "Frame sampling rate.");
        var framesOption = new Option<int>(
            "--frames", () => 0, "Force exact frame count; 0 = whole clip.");
        var maxColorsOption = new Option<int>(
            "--max-colors", () => 32, "Palette color limit.");
        var paletteOption = new Option<string?>(
            "--palette", "Fixed palette PNG (skip Wu quantization).");
        var alphaThresholdOption = new Option<int>(
            "--alpha-threshold", () => 128, "Binarization cutoff 0-255.");
        var noEdgeDilateOption = new Option<bool>(
            "--no-edge-dilate", "Disable edge dilation.");
        var cleanupOption = new Option<string>(
            "--cleanup", () => "morph,jaggy", "Comma-separated cleanup passes: morph,jaggy.");
        var outputModeOption = new Option<string>(
            "--output-mode", () => "sheet", "Output mode: sheet | frames | both.");
        var outOption = new Option<string>(
            "--out", () => "./output", "Output directory.");
        var camPitchOption = new Option<float>(
            "--cam-pitch", () => 26.5f, "Camera vertical angle in degrees.");
        var camZoomOption = new Option<float>(
            "--cam-zoom", () => 1.0f, "Zoom factor.");
        var camYawOption = new Option<float>(
            "--cam-yaw", () => 0f, "Base azimuth in degrees for direction 0 (rotates all directions about the up axis).");
        var camDistanceOption = new Option<float>(
            "--cam-distance", () => 0f, "Explicit camera distance in model units (0 = automatic).");
        var camTargetOption = new Option<string?>(
            "--cam-target", "Look-at pan offset from the model centre as 'x,y,z' (Y-up frame).");
        var orthoOption = new Option<bool>(
            "--ortho", "Orthographic projection.");
        var upAxisOption = new Option<string>(
            "--up-axis", () => "y", "Up axis: y | z.");
        var verboseOption = new Option<bool>(
            "--verbose", "Print per-frame progress.");
        var inPlaceOption = new Option<bool>(
            "--in-place", "Remove root motion: hold the root/hips horizontal translation so the character stays centered.");
        var checkRootMotionOption = new Option<bool>(
            "--check-root-motion", "Report whether the animation has root motion, then exit without rendering.");
        var listBonesOption = new Option<bool>(
            "--list-bones", "Dump the skeleton/node tree with equipment-relevant bones flagged, then exit without rendering.");
        var equipOption = new Option<string?>(
            "--equip", "Equipment manifest JSON (Unreal socket / master-pose attachments: weapons, armor).");
        var retargetOption = new Option<string?>(
            "--retarget", "Retarget map JSON (joint mapping) for playing an animation authored for a different skeleton (e.g. Mixamo -> Unreal rig).");

        var rootCommand = new RootCommand("PixelSprite CLI - render a rigged model to pixel-art sprite sheets.");

        // ~19 options exceed the arity of the generic SetHandler<T..> overloads, so all options are
        // added to the command and read individually from the InvocationContext's parse result below.
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(animOption);
        rootCommand.AddOption(directionsOption);
        rootCommand.AddOption(renderSizeOption);
        rootCommand.AddOption(spriteSizeOption);
        rootCommand.AddOption(fpsOption);
        rootCommand.AddOption(framesOption);
        rootCommand.AddOption(maxColorsOption);
        rootCommand.AddOption(paletteOption);
        rootCommand.AddOption(alphaThresholdOption);
        rootCommand.AddOption(noEdgeDilateOption);
        rootCommand.AddOption(cleanupOption);
        rootCommand.AddOption(outputModeOption);
        rootCommand.AddOption(outOption);
        rootCommand.AddOption(camPitchOption);
        rootCommand.AddOption(camZoomOption);
        rootCommand.AddOption(camYawOption);
        rootCommand.AddOption(camDistanceOption);
        rootCommand.AddOption(camTargetOption);
        rootCommand.AddOption(orthoOption);
        rootCommand.AddOption(upAxisOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(inPlaceOption);
        rootCommand.AddOption(checkRootMotionOption);
        rootCommand.AddOption(listBonesOption);
        rootCommand.AddOption(equipOption);
        rootCommand.AddOption(retargetOption);

        rootCommand.SetHandler((InvocationContext ctx) =>
        {
            try
            {
                var parsed = ctx.ParseResult;

                bool verbose = parsed.GetValueForOption(verboseOption);
                int alphaThreshold = parsed.GetValueForOption(alphaThresholdOption);

                var renderOpts = new RenderOptions
                {
                    Input = parsed.GetValueForOption(inputOption)!,
                    Anim = parsed.GetValueForOption(animOption),
                    Directions = parsed.GetValueForOption(directionsOption),
                    RenderSize = parsed.GetValueForOption(renderSizeOption),
                    Fps = parsed.GetValueForOption(fpsOption),
                    Frames = parsed.GetValueForOption(framesOption),
                    CamPitch = parsed.GetValueForOption(camPitchOption),
                    CamZoom = parsed.GetValueForOption(camZoomOption),
                    CamYaw = parsed.GetValueForOption(camYawOption),
                    CamDistance = parsed.GetValueForOption(camDistanceOption),
                    CamTarget = ParseVec3(parsed.GetValueForOption(camTargetOption)),
                    Ortho = parsed.GetValueForOption(orthoOption),
                    UpAxis = ParseUpAxis(parsed.GetValueForOption(upAxisOption)!),
                    InPlace = parsed.GetValueForOption(inPlaceOption),
                };

                // --check-root-motion: report and exit without rendering.
                if (parsed.GetValueForOption(checkRootMotionOption))
                {
                    Console.WriteLine(new RenderJob().CheckRootMotion(renderOpts));
                    ctx.ExitCode = 0;
                    return;
                }

                // --list-bones: dump the skeleton and exit without rendering.
                if (parsed.GetValueForOption(listBonesOption))
                {
                    Console.WriteLine(new RenderJob().ListBones(renderOpts));
                    ctx.ExitCode = 0;
                    return;
                }

                // --equip: load the equipment manifest (validates all attachment files up front).
                EquipmentManifest? equipment = null;
                string? equipPath = parsed.GetValueForOption(equipOption);
                if (!string.IsNullOrEmpty(equipPath))
                {
                    equipment = EquipmentManifestLoader.Load(equipPath);
                }

                // --retarget: load the joint-mapping retarget map (rotation transfer + optional length rescale).
                RetargetMap? retargetMap = null;
                string? retargetPath = parsed.GetValueForOption(retargetOption);
                if (!string.IsNullOrEmpty(retargetPath))
                {
                    retargetMap = RetargetMapLoader.Load(retargetPath);
                }

                (bool morph, bool jaggy) = ParseCleanup(parsed.GetValueForOption(cleanupOption)!);

                var pixelOpts = new PixelArtOptions
                {
                    SpriteSize = parsed.GetValueForOption(spriteSizeOption),
                    MaxColors = parsed.GetValueForOption(maxColorsOption),
                    PalettePath = parsed.GetValueForOption(paletteOption),
                    AlphaThreshold = ToByte(alphaThreshold),
                    EdgeDilate = !parsed.GetValueForOption(noEdgeDilateOption),
                    Cleanup = new CleanupOptions { Morph = morph, Jaggy = jaggy },
                };

                var outputOpts = new OutputOptions
                {
                    Mode = ParseOutputMode(parsed.GetValueForOption(outputModeOption)!),
                    OutDir = parsed.GetValueForOption(outOption)!,
                    Verbose = verbose,
                };

                Action<string>? progress = verbose ? Console.WriteLine : null;

                List<SpriteFrame> frames = new RenderJob().Execute(renderOpts, pixelOpts, equipment, retargetMap, progress).ToList();
                try
                {
                    Save(frames, renderOpts, pixelOpts, outputOpts);
                }
                finally
                {
                    foreach (SpriteFrame frame in frames)
                    {
                        frame.Dispose();
                    }
                }

                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
            }
        });

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>Writes output according to <see cref="OutputOptions.Mode"/>.</summary>
    private static void Save(
        IReadOnlyList<SpriteFrame> frames,
        RenderOptions renderOpts,
        PixelArtOptions pixelOpts,
        OutputOptions outputOpts)
    {
        Directory.CreateDirectory(outputOpts.OutDir);

        bool writeFrames = outputOpts.Mode is OutputMode.Frames or OutputMode.Both;
        bool writeSheet = outputOpts.Mode is OutputMode.Sheet or OutputMode.Both;

        if (writeFrames)
        {
            SaveFrames(frames, outputOpts.OutDir);
        }

        if (writeSheet)
        {
            // Normally a single animation; group defensively in case more than one slips through.
            foreach (IGrouping<string, SpriteFrame> group in frames.GroupBy(f => f.AnimName))
            {
                SaveSheet(group.ToList(), group.Key, renderOpts, pixelOpts, outputOpts.OutDir);
            }
        }
    }

    /// <summary>Writes each frame as <c>{outDir}/{animName}_dir{DD}_f{FFFF}.png</c>.</summary>
    private static void SaveFrames(IReadOnlyList<SpriteFrame> frames, string outDir)
    {
        foreach (SpriteFrame frame in frames)
        {
            string fileName = $"{frame.AnimName}_dir{frame.DirectionIndex:D2}_f{frame.FrameIndex:D4}.png";
            string path = Path.Combine(outDir, fileName);
            using SKData data = frame.Bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }
    }

    /// <summary>Packs frames for one animation into a sheet PNG plus its metadata JSON.</summary>
    private static void SaveSheet(
        IReadOnlyList<SpriteFrame> frames,
        string animName,
        RenderOptions renderOpts,
        PixelArtOptions pixelOpts,
        string outDir)
    {
        int frameCount = frames.Max(f => f.FrameIndex) + 1;
        int spriteSize = pixelOpts.SpriteSize;
        int directions = renderOpts.Directions;

        using SKBitmap sheet = SpriteSheetPacker.Pack(frames, spriteSize, spriteSize, directions, frameCount);

        string sheetPath = Path.Combine(outDir, $"{animName}_sheet.png");
        using (SKData data = sheet.Encode(SKEncodedImageFormat.Png, 100))
        using (FileStream stream = File.Create(sheetPath))
        {
            data.SaveTo(stream);
        }

        var metadata = new OutputMetadata
        {
            SpriteWidth = spriteSize,
            SpriteHeight = spriteSize,
            Directions = directions,
            Animations = new List<AnimationMetadata>
            {
                new()
                {
                    Name = animName,
                    FrameCount = frameCount,
                    Fps = renderOpts.Fps,
                    SheetRow = 0,
                },
            },
            // (0.5, 0.5): the root/hips bone is pinned to the frame centre (see OffscreenRenderer),
            // so the sprite pivots at the body centre, not bottom-center.
            Pivot = new PivotMetadata { X = 0.5f, Y = 0.5f },
        };

        MetadataWriter.Write(Path.Combine(outDir, $"{animName}_metadata.json"), metadata);
    }

    /// <summary>Parses the <c>--cleanup</c> CSV (e.g. "morph,jaggy") into individual pass flags.</summary>
    private static (bool Morph, bool Jaggy) ParseCleanup(string cleanup)
    {
        bool morph = false;
        bool jaggy = false;

        foreach (string token in cleanup.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "morph":
                    morph = true;
                    break;
                case "jaggy":
                    jaggy = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown cleanup pass: '{token}'. Valid values: morph, jaggy.");
            }
        }

        return (morph, jaggy);
    }

    /// <summary>Maps the <c>--output-mode</c> string to <see cref="OutputMode"/>.</summary>
    private static OutputMode ParseOutputMode(string mode) => mode.ToLowerInvariant() switch
    {
        "sheet" => OutputMode.Sheet,
        "frames" => OutputMode.Frames,
        "both" => OutputMode.Both,
        _ => throw new ArgumentException($"Invalid --output-mode '{mode}'. Valid values: sheet, frames, both."),
    };

    /// <summary>Maps the <c>--up-axis</c> string to <see cref="UpAxis"/>.</summary>
    private static UpAxis ParseUpAxis(string upAxis) => upAxis.ToLowerInvariant() switch
    {
        "y" => UpAxis.Y,
        "z" => UpAxis.Z,
        _ => throw new ArgumentException($"Invalid --up-axis '{upAxis}'. Valid values: y, z."),
    };

    /// <summary>Clamps an alpha-threshold integer into the 0-255 byte range.</summary>
    private static byte ToByte(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>Parses a <c>--cam-target</c> "x,y,z" string into a vector; empty means no offset.</summary>
    private static System.Numerics.Vector3 ParseVec3(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return System.Numerics.Vector3.Zero;
        }

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (parts.Length != 3
            || !float.TryParse(parts[0], ci, out float x)
            || !float.TryParse(parts[1], ci, out float y)
            || !float.TryParse(parts[2], ci, out float z))
        {
            throw new ArgumentException($"Invalid --cam-target '{value}'. Expected three numbers 'x,y,z'.");
        }

        return new System.Numerics.Vector3(x, y, z);
    }
}
