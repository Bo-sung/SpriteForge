using FluentAssertions;
using PixelSprite.Core.PixelArt;
using SkiaSharp;
using Xunit;

namespace PixelSprite.Tests;

public sealed class DominantDownscalerTests
{
    private static SKBitmap NewBitmap(int w, int h)
        => new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));

    [Fact]
    public void SolidOpaqueBlock_DownscalesToThatColor()
    {
        using var src = NewBitmap(4, 4);
        var color = new SKColor(12, 200, 75, 255);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                src.SetPixel(x, y, color);
            }
        }

        using SKBitmap result = DominantDownscaler.Downscale(src, 1, 1);

        result.GetPixel(0, 0).Should().Be(new SKColor(12, 200, 75, 255));
    }

    [Fact]
    public void FullyTransparentBlock_DownscalesToTransparent()
    {
        using var src = NewBitmap(4, 4);
        var transparent = new SKColor(50, 60, 70, 0);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                src.SetPixel(x, y, transparent);
            }
        }

        using SKBitmap result = DominantDownscaler.Downscale(src, 1, 1);

        result.GetPixel(0, 0).Alpha.Should().Be(0);
    }

    [Fact]
    public void FiftyFiftyMix_ResolvesToRoundedMean()
    {
        // 2x1 source: one red, one blue. Ratio 0.5 is NOT > 0.5 (DominanceThreshold),
        // so the result is the rounded mean of the two opaque pixels.
        using var src = NewBitmap(2, 1);
        src.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        src.SetPixel(1, 0, new SKColor(0, 0, 255, 255));

        using SKBitmap result = DominantDownscaler.Downscale(src, 1, 1);

        // (sum + count/2) / count: R=(255+1)/2=128, G=0, B=(255+1)/2=128
        result.GetPixel(0, 0).Should().Be(new SKColor(128, 0, 128, 255));
    }

    [Fact]
    public void SeventyThirtyMix_ResolvesToDominantColor()
    {
        // 10x1 source: 7 pixels color A, 3 pixels color B. 0.7 > 0.5 -> dominant wins.
        using var src = NewBitmap(10, 1);
        var dominant = new SKColor(10, 20, 30, 255);
        var minority = new SKColor(200, 210, 220, 255);
        for (int x = 0; x < 7; x++)
        {
            src.SetPixel(x, 0, dominant);
        }

        for (int x = 7; x < 10; x++)
        {
            src.SetPixel(x, 0, minority);
        }

        using SKBitmap result = DominantDownscaler.Downscale(src, 1, 1);

        result.GetPixel(0, 0).Should().Be(new SKColor(10, 20, 30, 255));
    }
}
