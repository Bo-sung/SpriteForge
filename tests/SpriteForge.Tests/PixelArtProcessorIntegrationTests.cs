using FluentAssertions;
using SkiaSharp;
using SpriteForge.Core.Models;
using SpriteForge.Core.PixelArt;

namespace SpriteForge.Tests;

/// <summary>
/// End-to-end tests of <see cref="PixelArtProcessor"/> covering the new dither/outline stages and
/// the sprite-size==0 skip, exercising the full pipeline rather than a single pass in isolation.
/// </summary>
public class PixelArtProcessorIntegrationTests
{
    /// <summary>A square of <paramref name="color"/> centered on a transparent canvas.</summary>
    private static SKBitmap SquareOnTransparent(int size, int inset, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.Transparent);
        for (int y = inset; y < size - inset; y++)
        {
            for (int x = inset; x < size - inset; x++)
            {
                bmp.SetPixel(x, y, color);
            }
        }

        return bmp;
    }

    private static bool ContainsColor(SKBitmap bmp, SKColor target)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y) == target)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly SKColor Green = new(0, 200, 0, 255);

    private static PixelArtOptions Options(int spriteSize, bool outline, DitherMode dither = DitherMode.None) => new()
    {
        SpriteSize = spriteSize,
        MaxColors = 8,
        Outline = outline,
        OutlineColor = SKColors.Red,
        Dither = dither,
        // Disable cleanup so the test asserts the silhouette without morphological erosion of the small block.
        Cleanup = new CleanupOptions { Morph = false, Jaggy = false },
    };

    [Fact]
    public void Outline_AddsOutlineColorAsTheFinalStep()
    {
        using SKBitmap src = SquareOnTransparent(16, 4, Green);
        using SKBitmap result = new PixelArtProcessor().Process(src, Options(spriteSize: 8, outline: true));

        // The red outline is laid down after quantization/jaggy, so it survives into the output.
        ContainsColor(result, SKColors.Red).Should().BeTrue();
    }

    [Fact]
    public void NoOutline_ProducesNoOutlineColor()
    {
        using SKBitmap src = SquareOnTransparent(16, 4, Green);
        using SKBitmap result = new PixelArtProcessor().Process(src, Options(spriteSize: 8, outline: false));

        ContainsColor(result, SKColors.Red).Should().BeFalse();
    }

    [Fact]
    public void SpriteSizeZero_SkipsDownscale_KeepsInputResolution()
    {
        using SKBitmap src = SquareOnTransparent(10, 3, Green);

        using SKBitmap kept = new PixelArtProcessor().Process(src, Options(spriteSize: 0, outline: false));
        kept.Width.Should().Be(10);
        kept.Height.Should().Be(10);

        using SKBitmap downscaled = new PixelArtProcessor().Process(src, Options(spriteSize: 8, outline: false));
        downscaled.Width.Should().Be(8);
        downscaled.Height.Should().Be(8);
    }

    [Fact]
    public void Dither_RunsThroughTheFullPipeline()
    {
        using SKBitmap src = SquareOnTransparent(16, 2, new SKColor(120, 120, 120, 255));
        using SKBitmap result = new PixelArtProcessor().Process(
            src, Options(spriteSize: 8, outline: false, dither: DitherMode.Bayer));

        result.Width.Should().Be(8);
        result.Height.Should().Be(8);

        // The silhouette survives: at least one fully opaque pixel remains.
        bool anyOpaque = false;
        for (int y = 0; y < result.Height && !anyOpaque; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                if (result.GetPixel(x, y).Alpha == 255)
                {
                    anyOpaque = true;
                    break;
                }
            }
        }

        anyOpaque.Should().BeTrue();
    }

    [Fact]
    public void OutlineAndDither_ComposeWithoutError()
    {
        using SKBitmap src = SquareOnTransparent(16, 3, Green);
        using SKBitmap result = new PixelArtProcessor().Process(
            src, Options(spriteSize: 8, outline: true, dither: DitherMode.Floyd));

        result.Width.Should().Be(8);
        ContainsColor(result, SKColors.Red).Should().BeTrue();
    }
}
