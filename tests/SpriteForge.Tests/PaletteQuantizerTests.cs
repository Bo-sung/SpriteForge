using System.Collections.Generic;
using FluentAssertions;
using SpriteForge.Core.PixelArt;
using SkiaSharp;
using Xunit;

namespace PixelSprite.Tests;

public sealed class PaletteQuantizerTests
{
    private static SKBitmap NewBitmap(int w, int h)
        => new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));

    [Fact]
    public void TransparentPixel_RemainsTransparentAfterQuantize()
    {
        using var src = NewBitmap(2, 1);
        src.SetPixel(0, 0, new SKColor(255, 0, 0, 255));   // opaque, gives Wu something to do
        src.SetPixel(1, 0, new SKColor(0, 0, 0, 0));       // transparent

        using SKBitmap result = PaletteQuantizer.Quantize(src, maxColors: 8);

        result.GetPixel(1, 0).Alpha.Should().Be(0);
    }

    [Fact]
    public void MaxColorsTwo_YieldsAtMostTwoDistinctOpaqueColors()
    {
        // Four distinct opaque colors quantized down to 2.
        using var src = NewBitmap(2, 2);
        src.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        src.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
        src.SetPixel(0, 1, new SKColor(0, 0, 255, 255));
        src.SetPixel(1, 1, new SKColor(255, 255, 0, 255));

        using SKBitmap result = PaletteQuantizer.Quantize(src, maxColors: 2);

        var distinct = new HashSet<uint>();
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                SKColor c = result.GetPixel(x, y);
                if (c.Alpha != 0)
                {
                    distinct.Add((uint)c);
                }
            }
        }

        distinct.Count.Should().BeLessThanOrEqualTo(2);
    }
}
