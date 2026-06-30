using FluentAssertions;
using SpriteForge.Core.Models;
using SpriteForge.Core.Packing;
using SkiaSharp;
using Xunit;

namespace PixelSprite.Tests;

public sealed class SpriteSheetPackerTests
{
    private static SKBitmap SolidBitmap(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bmp.SetPixel(x, y, color);
            }
        }

        return bmp;
    }

    [Fact]
    public void Pack_TwoDirsTwoFrames_ProducesCorrectSizeAndPlacement()
    {
        const int spriteW = 2;
        const int spriteH = 2;
        const int dirCount = 2;
        const int frameCount = 2;

        // Distinct color per (dir, frame) cell so we can verify placement unambiguously.
        var d0f0 = new SKColor(10, 0, 0, 255);
        var d0f1 = new SKColor(20, 0, 0, 255);
        var d1f0 = new SKColor(30, 0, 0, 255);
        var d1f1 = new SKColor(40, 0, 0, 255);

        var frames = new List<SpriteFrame>
        {
            new() { Bitmap = SolidBitmap(spriteW, spriteH, d0f0), DirectionIndex = 0, FrameIndex = 0, AnimName = "Walk" },
            new() { Bitmap = SolidBitmap(spriteW, spriteH, d0f1), DirectionIndex = 0, FrameIndex = 1, AnimName = "Walk" },
            new() { Bitmap = SolidBitmap(spriteW, spriteH, d1f0), DirectionIndex = 1, FrameIndex = 0, AnimName = "Walk" },
            new() { Bitmap = SolidBitmap(spriteW, spriteH, d1f1), DirectionIndex = 1, FrameIndex = 1, AnimName = "Walk" },
        };

        try
        {
            using SKBitmap sheet = SpriteSheetPacker.Pack(frames, spriteW, spriteH, dirCount, frameCount);

            sheet.Width.Should().Be(4);
            sheet.Height.Should().Be(4);

            // Frame at (DirectionIndex=1, FrameIndex=1) lands at offset (1*2, 1*2).
            sheet.GetPixel(1 * spriteW, 1 * spriteH).Should().Be(d1f1);

            // Spot-check the other three cells too.
            sheet.GetPixel(0, 0).Should().Be(d0f0);
            sheet.GetPixel(1 * spriteW, 0).Should().Be(d0f1);
            sheet.GetPixel(0, 1 * spriteH).Should().Be(d1f0);
        }
        finally
        {
            foreach (SpriteFrame frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public void Pack_EmptyCells_StayTransparent()
    {
        const int spriteW = 2;
        const int spriteH = 2;
        const int dirCount = 2;
        const int frameCount = 2;

        // Only fill cell (dir 0, frame 0); the other three cells must remain transparent.
        var frames = new List<SpriteFrame>
        {
            new() { Bitmap = SolidBitmap(spriteW, spriteH, new SKColor(255, 255, 255, 255)), DirectionIndex = 0, FrameIndex = 0, AnimName = "Walk" },
        };

        try
        {
            using SKBitmap sheet = SpriteSheetPacker.Pack(frames, spriteW, spriteH, dirCount, frameCount);

            // A gap pixel inside an unfilled cell must be fully transparent.
            sheet.GetPixel(1 * spriteW, 1 * spriteH).Alpha.Should().Be(0);
            sheet.GetPixel(1 * spriteW, 0).Alpha.Should().Be(0);
            sheet.GetPixel(0, 1 * spriteH).Alpha.Should().Be(0);
        }
        finally
        {
            foreach (SpriteFrame frame in frames)
            {
                frame.Dispose();
            }
        }
    }
}
