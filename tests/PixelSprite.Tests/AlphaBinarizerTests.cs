using FluentAssertions;
using PixelSprite.Core.PixelArt;
using SkiaSharp;
using Xunit;

namespace PixelSprite.Tests;

public sealed class AlphaBinarizerTests
{
    private static SKBitmap NewBitmap(int w, int h)
        => new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));

    [Fact]
    public void AlphaAboveThreshold_BecomesFullyOpaque()
    {
        using var src = NewBitmap(1, 1);
        src.SetPixel(0, 0, new SKColor(40, 80, 120, 200));

        using SKBitmap result = AlphaBinarizer.Binarize(src, threshold: 128, edgeDilate: false);

        result.GetPixel(0, 0).Alpha.Should().Be(255);
    }

    [Fact]
    public void AlphaBelowThreshold_BecomesFullyTransparent()
    {
        using var src = NewBitmap(1, 1);
        src.SetPixel(0, 0, new SKColor(40, 80, 120, 100));

        using SKBitmap result = AlphaBinarizer.Binarize(src, threshold: 128, edgeDilate: false);

        result.GetPixel(0, 0).Alpha.Should().Be(0);
    }

    [Fact]
    public void EdgeDilate_TransparentBorderPixel_ReceivesNeighborRgb()
    {
        // 2x1: left opaque, right transparent. With dilation the right pixel
        // copies the left neighbor's RGB.
        using var src = NewBitmap(2, 1);
        src.SetPixel(0, 0, new SKColor(11, 22, 33, 255));
        src.SetPixel(1, 0, new SKColor(0, 0, 0, 0));

        using SKBitmap result = AlphaBinarizer.Binarize(src, threshold: 128, edgeDilate: true);

        SKColor dilated = result.GetPixel(1, 0);
        dilated.Red.Should().Be(11);
        dilated.Green.Should().Be(22);
        dilated.Blue.Should().Be(33);
    }

    [Fact]
    public void EdgeDilate_FilledPixel_StaysTransparent()
    {
        using var src = NewBitmap(2, 1);
        src.SetPixel(0, 0, new SKColor(11, 22, 33, 255));
        src.SetPixel(1, 0, new SKColor(0, 0, 0, 0));

        using SKBitmap result = AlphaBinarizer.Binarize(src, threshold: 128, edgeDilate: true);

        result.GetPixel(1, 0).Alpha.Should().Be(0);
    }
}
