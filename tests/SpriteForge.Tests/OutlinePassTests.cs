using FluentAssertions;
using SkiaSharp;
using SpriteForge.Core.Models;
using SpriteForge.Core.PixelArt;

namespace SpriteForge.Tests;

public class OutlinePassTests
{
    private static SKBitmap NewBitmap(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    [Fact]
    public void Outer_PaintsOutlineIntoTransparentNeighbors()
    {
        using SKBitmap src = NewBitmap(3, 3);
        var fill = new SKColor(10, 20, 30, 255);
        src.SetPixel(1, 1, fill);

        using SKBitmap outlined = OutlinePass.Apply(src, SKColors.Red, OutlineType.Outer);

        // The opaque pixel keeps its color; the 4 cardinal neighbors become opaque outline.
        outlined.GetPixel(1, 1).Should().Be(fill);
        outlined.GetPixel(1, 0).Should().Be(SKColors.Red);
        outlined.GetPixel(1, 2).Should().Be(SKColors.Red);
        outlined.GetPixel(0, 1).Should().Be(SKColors.Red);
        outlined.GetPixel(2, 1).Should().Be(SKColors.Red);

        // Diagonal corners are not 4-neighbors, so they stay transparent.
        outlined.GetPixel(0, 0).Alpha.Should().Be(0);
        outlined.GetPixel(2, 2).Alpha.Should().Be(0);
    }

    [Fact]
    public void Outer_DoesNotOverwriteExistingOpaquePixels()
    {
        using SKBitmap src = NewBitmap(4, 1);
        var a = new SKColor(10, 20, 30, 255);
        var b = new SKColor(40, 50, 60, 255);
        src.SetPixel(1, 0, a);
        src.SetPixel(2, 0, b);

        using SKBitmap outlined = OutlinePass.Apply(src, SKColors.Red, OutlineType.Outer);

        // The two adjacent opaque pixels are untouched; only the transparent ends get outlined.
        outlined.GetPixel(1, 0).Should().Be(a);
        outlined.GetPixel(2, 0).Should().Be(b);
        outlined.GetPixel(0, 0).Should().Be(SKColors.Red);
        outlined.GetPixel(3, 0).Should().Be(SKColors.Red);
    }

    [Fact]
    public void Inner_RecolorsBoundaryPixelsButNotInterior()
    {
        using SKBitmap src = NewBitmap(5, 5);
        var fill = new SKColor(10, 20, 30, 255);
        for (int y = 1; y <= 3; y++)
        {
            for (int x = 1; x <= 3; x++)
            {
                src.SetPixel(x, y, fill);
            }
        }

        using SKBitmap outlined = OutlinePass.Apply(src, SKColors.Blue, OutlineType.Inner);

        // The center has only opaque neighbors -> kept; the surrounding ring borders transparency -> recolored.
        outlined.GetPixel(2, 2).Should().Be(fill);
        outlined.GetPixel(1, 1).Should().Be(SKColors.Blue);
        outlined.GetPixel(3, 3).Should().Be(SKColors.Blue);
        outlined.GetPixel(2, 1).Should().Be(SKColors.Blue);
    }

    [Fact]
    public void TransparentPixelsKeepTheirRgb()
    {
        using SKBitmap src = NewBitmap(5, 1);
        src.SetPixel(0, 0, new SKColor(10, 20, 30, 255)); // lone opaque pixel
        src.SetPixel(4, 0, new SKColor(50, 60, 70, 0));   // far transparent pixel carrying RGB

        using SKBitmap outlined = OutlinePass.Apply(src, SKColors.Red, OutlineType.Outer);

        SKColor far = outlined.GetPixel(4, 0);
        far.Alpha.Should().Be(0);
        far.Red.Should().Be(50);
        far.Green.Should().Be(60);
        far.Blue.Should().Be(70);
    }

    [Fact]
    public void AllTransparentInput_ProducesNoOutline()
    {
        using SKBitmap src = NewBitmap(3, 3); // nothing opaque to seed an outline

        using SKBitmap outlined = OutlinePass.Apply(src, SKColors.Red, OutlineType.Outer);

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                outlined.GetPixel(x, y).Alpha.Should().Be(0);
            }
        }
    }

    [Fact]
    public void Apply_DoesNotMutateSource()
    {
        using SKBitmap src = NewBitmap(3, 3);
        var fill = new SKColor(10, 20, 30, 255);
        src.SetPixel(1, 1, fill);

        using SKBitmap _ = OutlinePass.Apply(src, SKColors.Red, OutlineType.Outer);

        // Source is unchanged: still one opaque pixel, transparent neighbors.
        src.GetPixel(1, 1).Should().Be(fill);
        src.GetPixel(1, 0).Alpha.Should().Be(0);
    }
}
