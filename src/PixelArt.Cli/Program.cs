using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using SpriteForge.Core.Models;
using SpriteForge.Core.PixelArt;
using SkiaSharp;

namespace PixelArt.Cli;

/// <summary>
/// Standalone CLI that runs the SpriteForge pixel-art post-processing pipeline on existing images
/// (single file or a folder batch), independent of the 3D render path. Useful for re-processing
/// already-rendered frames or hand-made art with palette reduction, dithering, and outlining.
/// </summary>
internal static class Program
{
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>Parses arguments and dispatches to the image-processing pipeline.</summary>
    /// <param name="args">Raw process arguments.</param>
    /// <returns>0 on success; 1 if any error occurred.</returns>
    private static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<string>("--input", "Image file or folder to convert.")
        {
            IsRequired = true,
        };
        var outputOption = new Option<string>(
            "--output", () => "./output", "Output file (single input) or folder (batch input).");
        var paletteOption = new Option<string?>(
            "--palette", "Fixed palette PNG (skip Wu quantization).");
        var maxColorsOption = new Option<int>(
            "--max-colors", () => 32, "Palette color limit for Wu quantization.");
        var spriteSizeOption = new Option<int>(
            "--sprite-size", () => 0, "Downscale target resolution (square). 0 = skip downscaling.");
        var alphaThresholdOption = new Option<int>(
            "--alpha-threshold", () => 128, "Alpha binarization cutoff 0-255.");
        var noEdgeDilateOption = new Option<bool>(
            "--no-edge-dilate", "Disable edge dilation.");
        var cleanupOption = new Option<string>(
            "--cleanup", () => "morph,jaggy", "Comma-separated cleanup passes: morph,jaggy.");
        var outlineOption = new Option<bool>(
            "--outline", "Draw an outline around the silhouette.");
        var outlineColorOption = new Option<string>(
            "--outline-color", () => "#000000", "Outline color hex: #RRGGBB or #AARRGGBB.");
        var outlineTypeOption = new Option<string>(
            "--outline-type", () => "outer", "Outline type: outer | inner.");
        var ditherOption = new Option<string>(
            "--dither", () => "none", "Dither mode: none | bayer | floyd.");
        var verboseOption = new Option<bool>(
            "--verbose", "Print per-file progress.");

        var rootCommand = new RootCommand(
            "PixelArt CLI - convert images to pixel art (downscale, palette, dither, outline).");
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(paletteOption);
        rootCommand.AddOption(maxColorsOption);
        rootCommand.AddOption(spriteSizeOption);
        rootCommand.AddOption(alphaThresholdOption);
        rootCommand.AddOption(noEdgeDilateOption);
        rootCommand.AddOption(cleanupOption);
        rootCommand.AddOption(outlineOption);
        rootCommand.AddOption(outlineColorOption);
        rootCommand.AddOption(outlineTypeOption);
        rootCommand.AddOption(ditherOption);
        rootCommand.AddOption(verboseOption);

        rootCommand.SetHandler((InvocationContext ctx) =>
        {
            try
            {
                var parsed = ctx.ParseResult;
                bool verbose = parsed.GetValueForOption(verboseOption);

                (bool morph, bool jaggy) = ParseCleanup(parsed.GetValueForOption(cleanupOption)!);

                var opts = new PixelArtOptions
                {
                    SpriteSize = parsed.GetValueForOption(spriteSizeOption),
                    MaxColors = parsed.GetValueForOption(maxColorsOption),
                    PalettePath = parsed.GetValueForOption(paletteOption),
                    AlphaThreshold = ToByte(parsed.GetValueForOption(alphaThresholdOption)),
                    EdgeDilate = !parsed.GetValueForOption(noEdgeDilateOption),
                    Cleanup = new CleanupOptions { Morph = morph, Jaggy = jaggy },
                    Outline = parsed.GetValueForOption(outlineOption),
                    OutlineColor = ParseHexColor(parsed.GetValueForOption(outlineColorOption)!),
                    OutlineType = ParseOutlineType(parsed.GetValueForOption(outlineTypeOption)!),
                    Dither = ParseDither(parsed.GetValueForOption(ditherOption)!),
                };

                string input = parsed.GetValueForOption(inputOption)!;
                string output = parsed.GetValueForOption(outputOption)!;

                int processed = Run(input, output, opts, verbose);
                if (verbose)
                {
                    Console.WriteLine($"done: {processed} file(s).");
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

    /// <summary>Processes a single file or a whole folder; returns the number of files written.</summary>
    private static int Run(string input, string output, PixelArtOptions opts, bool verbose)
    {
        var processor = new PixelArtProcessor();

        if (Directory.Exists(input))
        {
            // Batch: output is treated as a directory, one PNG per input image.
            Directory.CreateDirectory(output);
            List<string> files = Directory.EnumerateFiles(input)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No supported images (*.png, *.jpg, *.jpeg, *.bmp) found in folder: {input}");
            }

            foreach (string file in files)
            {
                string outPath = Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".png");
                ProcessFile(processor, file, outPath, opts, verbose);
            }

            return files.Count;
        }

        if (File.Exists(input))
        {
            string outPath = ResolveSingleOutputPath(input, output);
            string? dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            ProcessFile(processor, input, outPath, opts, verbose);
            return 1;
        }

        throw new FileNotFoundException($"Input not found: {input}", input);
    }

    /// <summary>
    /// Decides the output path for a single-file input: if <paramref name="output"/> names a directory
    /// (exists as one, ends with a separator, or has no extension), the image is written inside it as
    /// <c>{inputName}.png</c>; otherwise <paramref name="output"/> is used verbatim as the file path.
    /// </summary>
    private static string ResolveSingleOutputPath(string inputFile, string output)
    {
        bool looksLikeDirectory = Directory.Exists(output)
            || output.EndsWith('/')
            || output.EndsWith('\\')
            || string.IsNullOrEmpty(Path.GetExtension(output));

        return looksLikeDirectory
            ? Path.Combine(output, Path.GetFileNameWithoutExtension(inputFile) + ".png")
            : output;
    }

    /// <summary>Loads, processes, and writes one image as a PNG with a preserved alpha channel.</summary>
    private static void ProcessFile(
        PixelArtProcessor processor, string inputFile, string outputFile, PixelArtOptions opts, bool verbose)
    {
        using SKBitmap? src = SKBitmap.Decode(inputFile);
        if (src is null)
        {
            throw new InvalidOperationException($"Could not decode image: {inputFile}");
        }

        using SKBitmap result = processor.Process(src, opts);
        using SKData data = result.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(outputFile);
        data.SaveTo(stream);

        if (verbose)
        {
            Console.WriteLine($"{Path.GetFileName(inputFile)} -> {outputFile} ({result.Width}x{result.Height})");
        }
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

    /// <summary>Maps the <c>--outline-type</c> string to <see cref="OutlineType"/>.</summary>
    private static OutlineType ParseOutlineType(string type) => type.ToLowerInvariant() switch
    {
        "outer" => OutlineType.Outer,
        "inner" => OutlineType.Inner,
        _ => throw new ArgumentException($"Invalid --outline-type '{type}'. Valid values: outer, inner."),
    };

    /// <summary>Maps the <c>--dither</c> string to <see cref="DitherMode"/>.</summary>
    private static DitherMode ParseDither(string mode) => mode.ToLowerInvariant() switch
    {
        "none" => DitherMode.None,
        "bayer" => DitherMode.Bayer,
        "floyd" => DitherMode.Floyd,
        _ => throw new ArgumentException($"Invalid --dither '{mode}'. Valid values: none, bayer, floyd."),
    };

    /// <summary>Parses a <c>#RRGGBB</c> or <c>#AARRGGBB</c> hex color (leading '#' optional).</summary>
    private static SKColor ParseHexColor(string value)
    {
        string s = value.Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if ((s.Length != 6 && s.Length != 8)
            || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
        {
            throw new ArgumentException($"Invalid --outline-color '{value}'. Expected #RRGGBB or #AARRGGBB.");
        }

        byte r = (byte)((v >> 16) & 0xFF);
        byte g = (byte)((v >> 8) & 0xFF);
        byte b = (byte)(v & 0xFF);
        byte a = s.Length == 8 ? (byte)((v >> 24) & 0xFF) : (byte)255;
        return new SKColor(r, g, b, a);
    }

    /// <summary>Clamps an integer into the 0-255 byte range.</summary>
    private static byte ToByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
