using FluentAssertions;
using SkiaSharp;
using SpriteForge.Core.Models;
using SpriteForge.Core.PixelArt;

namespace SpriteForge.Tests;

public class DitherPassTests
{
    private static readonly SKColor Black = new(0, 0, 0, 255);
    private static readonly SKColor White = new(255, 255, 255, 255);
    private static readonly SKColor[] BlackWhite = { Black, White };

    private static SKBitmap Filled(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.Transparent);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bmp.SetPixel(x, y, color);
            }
        }

        return bmp;
    }

    private static HashSet<SKColor> DistinctColors(SKBitmap bmp)
    {
        var set = new HashSet<SKColor>();
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                set.Add(bmp.GetPixel(x, y));
            }
        }

        return set;
    }

    [Fact]
    public void None_IsPassthrough()
    {
        var gray = new SKColor(128, 128, 128, 255);
        using SKBitmap src = Filled(4, 4, gray);

        using SKBitmap result = DitherPass.Apply(src, DitherMode.None, BlackWhite);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                result.GetPixel(x, y).Should().Be(gray);
            }
        }
    }

    [Fact]
    public void Bayer_FlatGray_ProducesBothPaletteColors()
    {
        using SKBitmap src = Filled(4, 4, new SKColor(128, 128, 128, 255));

        using SKBitmap result = DitherPass.Apply(src, DitherMode.Bayer, BlackWhite);

        HashSet<SKColor> colors = DistinctColors(result);
        colors.Should().Contain(Black);
        colors.Should().Contain(White);
        // Every output pixel must be a palette color.
        colors.Should().OnlyContain(c => c == Black || c == White);
    }

    [Fact]
    public void Floyd_FlatGray_ProducesBothPaletteColors()
    {
        using SKBitmap src = Filled(8, 8, new SKColor(128, 128, 128, 255));

        using SKBitmap result = DitherPass.Apply(src, DitherMode.Floyd, BlackWhite);

        HashSet<SKColor> colors = DistinctColors(result);
        colors.Should().Contain(Black);
        colors.Should().Contain(White);
        colors.Should().OnlyContain(c => c == Black || c == White);
    }

    [Theory]
    [InlineData(DitherMode.Bayer)]
    [InlineData(DitherMode.Floyd)]
    public void TransparentPixelsArePreserved(DitherMode mode)
    {
        using SKBitmap src = Filled(3, 3, new SKColor(128, 128, 128, 255));
        src.SetPixel(1, 1, new SKColor(9, 8, 7, 0)); // transparent, carries RGB

        using SKBitmap result = DitherPass.Apply(src, mode, BlackWhite);

        SKColor center = result.GetPixel(1, 1);
        center.Alpha.Should().Be(0);
        center.Red.Should().Be(9);
        center.Green.Should().Be(8);
        center.Blue.Should().Be(7);
    }

    [Fact]
    public void ResolvePalette_ReturnsFixedPaletteWhenProvided()
    {
        using SKBitmap src = Filled(2, 2, new SKColor(100, 150, 200, 255));

        SKColor[] resolved = DitherPass.ResolvePalette(src, 32, BlackWhite);

        resolved.Should().BeSameAs(BlackWhite);
    }

    [Fact]
    public void ResolvePalette_BuildsWuPaletteWhenNoFixedPalette()
    {
        using var src = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        src.SetPixel(0, 0, new SKColor(200, 0, 0, 255));
        src.SetPixel(1, 0, new SKColor(0, 200, 0, 255));

        SKColor[] resolved = DitherPass.ResolvePalette(src, 4, fixedPalette: null);

        resolved.Should().NotBeEmpty();
        resolved.Length.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void ResolvePalette_EmptyForFullyTransparentImage()
    {
        using var src = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        src.Erase(SKColors.Transparent);

        SKColor[] resolved = DitherPass.ResolvePalette(src, 8, fixedPalette: null);

        resolved.Should().BeEmpty();
    }
}
