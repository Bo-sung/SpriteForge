using FluentAssertions;
using SpriteForge.Core.PixelArt;
using SkiaSharp;
using Xunit;

namespace PixelSprite.Tests;

public sealed class ArtifactCleanerTests
{
    private static SKBitmap NewBitmap(int w, int h)
        => new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));

    [Fact]
    public void MorphClean_IsolatedOpaquePixel_IsErasedByOpen()
    {
        // 3x3 transparent field with one opaque pixel in the center. A morphological
        // open (erode then dilate) removes the isolated speck -> A=0 everywhere.
        using var src = NewBitmap(3, 3);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                src.SetPixel(x, y, new SKColor(0, 0, 0, 0));
            }
        }

        src.SetPixel(1, 1, new SKColor(200, 100, 50, 255));

        using SKBitmap result = ArtifactCleaner.MorphClean(src);

        result.GetPixel(1, 1).Alpha.Should().Be(0);
    }

    [Fact]
    public void JaggyClean_CenterDiffersFromAllCardinals_IsReplacedByNeighborColor()
    {
        // 3x3: the 4 cardinal neighbors of the center share one color; the center
        // differs from all of them and so is replaced by that shared neighbor color.
        var neighbor = new SKColor(10, 20, 30, 255);
        var center = new SKColor(200, 100, 50, 255);

        using var src = NewBitmap(3, 3);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                src.SetPixel(x, y, new SKColor(0, 0, 0, 0));
            }
        }

        // Cardinal neighbors of (1,1): up, down, left, right.
        src.SetPixel(1, 0, neighbor);
        src.SetPixel(1, 2, neighbor);
        src.SetPixel(0, 1, neighbor);
        src.SetPixel(2, 1, neighbor);
        src.SetPixel(1, 1, center);

        using SKBitmap result = ArtifactCleaner.JaggyClean(src);

        result.GetPixel(1, 1).Should().Be(neighbor);
    }
}
