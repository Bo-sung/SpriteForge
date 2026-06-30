using SkiaSharp;

namespace SpriteForge.Core.PixelArt;

/// <summary>
/// A self-contained port of Xiaolin Wu's greedy orthogonal bipartition color quantizer
/// (Graphics Gems vol. II, 1991). Operates directly on <see cref="SKColor"/> values so the
/// pixel-art layer never needs System.Drawing or any other imaging dependency.
/// </summary>
/// <remarks>
/// Alpha is ignored — feed only opaque pixels. The quantizer builds a 33³ color-moment histogram,
/// recursively splits it into up to <c>maxColors</c> boxes minimizing weighted variance, then maps
/// any color to its box's average via a precomputed lookup table.
/// </remarks>
internal sealed class WuColorQuantizer
{
    private const int IndexBits = 5;          // 32 levels per axis
    private const int SideSize = 33;          // one extra slot (index 0) for the moving lower bound
    private const int TableLength = SideSize * SideSize * SideSize;

    private static int Ind(int r, int g, int b) => (r * SideSize * SideSize) + (g * SideSize) + b;

    private readonly SKColor[] _palette;
    private readonly byte[] _tag = new byte[TableLength];

    /// <summary>The computed palette (at most <c>maxColors</c> entries).</summary>
    public SKColor[] Palette => _palette;

    /// <summary>Builds the quantizer from a set of opaque pixels.</summary>
    /// <param name="opaquePixels">Pixels to quantize; alpha is ignored.</param>
    /// <param name="maxColors">Maximum palette size, clamped to 1-256.</param>
    public WuColorQuantizer(IEnumerable<SKColor> opaquePixels, int maxColors)
    {
        maxColors = Math.Clamp(maxColors, 1, 256);

        // Color moments: weight (count), sum of r/g/b, and sum of squared magnitudes.
        var wt = new long[TableLength];
        var mr = new long[TableLength];
        var mg = new long[TableLength];
        var mb = new long[TableLength];
        var m2 = new double[TableLength];

        BuildHistogram(opaquePixels, wt, mr, mg, mb, m2);
        ComputeCumulativeMoments(wt, mr, mg, mb, m2);

        var cubes = new Box[maxColors];
        for (int i = 0; i < maxColors; i++)
        {
            cubes[i] = new Box();
        }

        cubes[0].R0 = cubes[0].G0 = cubes[0].B0 = 0;
        cubes[0].R1 = cubes[0].G1 = cubes[0].B1 = 32;

        var variance = new double[maxColors];
        int next = 0;
        int colorCount = maxColors;

        for (int i = 1; i < maxColors; i++)
        {
            if (Cut(cubes[next], cubes[i], wt, mr, mg, mb))
            {
                variance[next] = cubes[next].Volume > 1 ? Var(cubes[next], wt, mr, mg, mb, m2) : 0.0;
                variance[i] = cubes[i].Volume > 1 ? Var(cubes[i], wt, mr, mg, mb, m2) : 0.0;
            }
            else
            {
                // Cannot split the chosen box further; retire it and retry this slot.
                variance[next] = 0.0;
                i--;
            }

            next = 0;
            double temp = variance[0];
            for (int k = 1; k <= i; k++)
            {
                if (variance[k] > temp)
                {
                    temp = variance[k];
                    next = k;
                }
            }

            if (temp <= 0.0)
            {
                colorCount = i + 1;
                break;
            }
        }

        // Derive the palette and the color -> palette-index lookup table.
        _palette = new SKColor[colorCount];
        for (int k = 0; k < colorCount; k++)
        {
            Box box = cubes[k];
            MarkTag(box, (byte)k);

            long weight = Vol(box, wt);
            if (weight > 0)
            {
                _palette[k] = new SKColor(
                    (byte)(Vol(box, mr) / weight),
                    (byte)(Vol(box, mg) / weight),
                    (byte)(Vol(box, mb) / weight),
                    255);
            }
            else
            {
                _palette[k] = new SKColor(0, 0, 0, 255);
            }
        }
    }

    /// <summary>Maps an arbitrary color to the nearest palette entry (alpha forced to 255).</summary>
    public SKColor MapToPalette(SKColor c)
    {
        int r = (c.Red >> (8 - IndexBits)) + 1;
        int g = (c.Green >> (8 - IndexBits)) + 1;
        int b = (c.Blue >> (8 - IndexBits)) + 1;
        return _palette[_tag[Ind(r, g, b)]];
    }

    private static void BuildHistogram(
        IEnumerable<SKColor> pixels, long[] wt, long[] mr, long[] mg, long[] mb, double[] m2)
    {
        foreach (SKColor c in pixels)
        {
            int r = c.Red, g = c.Green, b = c.Blue;
            int inr = (r >> (8 - IndexBits)) + 1;
            int ing = (g >> (8 - IndexBits)) + 1;
            int inb = (b >> (8 - IndexBits)) + 1;
            int ind = Ind(inr, ing, inb);

            wt[ind]++;
            mr[ind] += r;
            mg[ind] += g;
            mb[ind] += b;
            m2[ind] += (double)((r * r) + (g * g) + (b * b));
        }
    }

    /// <summary>Converts the histogram into cumulative (summed-area) moments.</summary>
    private static void ComputeCumulativeMoments(long[] wt, long[] mr, long[] mg, long[] mb, double[] m2)
    {
        for (int r = 1; r <= 32; r++)
        {
            var areaW = new long[SideSize];
            var areaR = new long[SideSize];
            var areaG = new long[SideSize];
            var areaB = new long[SideSize];
            var area2 = new double[SideSize];

            for (int g = 1; g <= 32; g++)
            {
                long lineW = 0, lineR = 0, lineG = 0, lineB = 0;
                double line2 = 0.0;

                for (int b = 1; b <= 32; b++)
                {
                    int ind = Ind(r, g, b);
                    lineW += wt[ind];
                    lineR += mr[ind];
                    lineG += mg[ind];
                    lineB += mb[ind];
                    line2 += m2[ind];

                    areaW[b] += lineW;
                    areaR[b] += lineR;
                    areaG[b] += lineG;
                    areaB[b] += lineB;
                    area2[b] += line2;

                    int prev = Ind(r - 1, g, b);
                    wt[ind] = wt[prev] + areaW[b];
                    mr[ind] = mr[prev] + areaR[b];
                    mg[ind] = mg[prev] + areaG[b];
                    mb[ind] = mb[prev] + areaB[b];
                    m2[ind] = m2[prev] + area2[b];
                }
            }
        }
    }

    /// <summary>Sum of a moment over the box, via inclusion-exclusion of its 8 corners.</summary>
    private static long Vol(Box c, long[] m) =>
        m[Ind(c.R1, c.G1, c.B1)] - m[Ind(c.R1, c.G1, c.B0)]
        - m[Ind(c.R1, c.G0, c.B1)] + m[Ind(c.R1, c.G0, c.B0)]
        - m[Ind(c.R0, c.G1, c.B1)] + m[Ind(c.R0, c.G1, c.B0)]
        + m[Ind(c.R0, c.G0, c.B1)] - m[Ind(c.R0, c.G0, c.B0)];

    private static double Vol(Box c, double[] m) =>
        m[Ind(c.R1, c.G1, c.B1)] - m[Ind(c.R1, c.G1, c.B0)]
        - m[Ind(c.R1, c.G0, c.B1)] + m[Ind(c.R1, c.G0, c.B0)]
        - m[Ind(c.R0, c.G1, c.B1)] + m[Ind(c.R0, c.G1, c.B0)]
        + m[Ind(c.R0, c.G0, c.B1)] - m[Ind(c.R0, c.G0, c.B0)];

    /// <summary>The part of a moment's volume independent of the cut position, for one direction.</summary>
    private static long Bottom(Box c, int dir, long[] m) => dir switch
    {
        Red => -m[Ind(c.R0, c.G1, c.B1)] + m[Ind(c.R0, c.G1, c.B0)]
               + m[Ind(c.R0, c.G0, c.B1)] - m[Ind(c.R0, c.G0, c.B0)],
        Green => -m[Ind(c.R1, c.G0, c.B1)] + m[Ind(c.R1, c.G0, c.B0)]
                 + m[Ind(c.R0, c.G0, c.B1)] - m[Ind(c.R0, c.G0, c.B0)],
        _ => -m[Ind(c.R1, c.G1, c.B0)] + m[Ind(c.R1, c.G0, c.B0)]
             + m[Ind(c.R0, c.G1, c.B0)] - m[Ind(c.R0, c.G0, c.B0)],
    };

    /// <summary>The part of a moment's volume that depends on the cut position <paramref name="pos"/>.</summary>
    private static long Top(Box c, int dir, int pos, long[] m) => dir switch
    {
        Red => m[Ind(pos, c.G1, c.B1)] - m[Ind(pos, c.G1, c.B0)]
               - m[Ind(pos, c.G0, c.B1)] + m[Ind(pos, c.G0, c.B0)],
        Green => m[Ind(c.R1, pos, c.B1)] - m[Ind(c.R1, pos, c.B0)]
                 - m[Ind(c.R0, pos, c.B1)] + m[Ind(c.R0, pos, c.B0)],
        _ => m[Ind(c.R1, c.G1, pos)] - m[Ind(c.R1, c.G0, pos)]
             - m[Ind(c.R0, c.G1, pos)] + m[Ind(c.R0, c.G0, pos)],
    };

    /// <summary>Weighted variance of the box (the quantity each split tries to reduce).</summary>
    private static double Var(Box c, long[] wt, long[] mr, long[] mg, long[] mb, double[] m2)
    {
        double dr = Vol(c, mr);
        double dg = Vol(c, mg);
        double db = Vol(c, mb);
        double xx = Vol(c, m2);
        long w = Vol(c, wt);
        if (w == 0)
        {
            return 0.0;
        }

        return xx - (((dr * dr) + (dg * dg) + (db * db)) / w);
    }

    /// <summary>Finds the cut position along one direction that maximizes the between-box sum of squares.</summary>
    private static double Maximize(
        Box c, int dir, int first, int last, out int cut,
        long wholeR, long wholeG, long wholeB, long wholeW,
        long[] wt, long[] mr, long[] mg, long[] mb)
    {
        long baseR = Bottom(c, dir, mr);
        long baseG = Bottom(c, dir, mg);
        long baseB = Bottom(c, dir, mb);
        long baseW = Bottom(c, dir, wt);

        double max = 0.0;
        cut = -1;

        for (int i = first; i < last; i++)
        {
            long halfR = baseR + Top(c, dir, i, mr);
            long halfG = baseG + Top(c, dir, i, mg);
            long halfB = baseB + Top(c, dir, i, mb);
            long halfW = baseW + Top(c, dir, i, wt);

            if (halfW == 0)
            {
                continue; // box has no pixels on this side; cut would be meaningless
            }

            double temp = (((double)halfR * halfR) + ((double)halfG * halfG) + ((double)halfB * halfB)) / halfW;

            long half2R = wholeR - halfR;
            long half2G = wholeG - halfG;
            long half2B = wholeB - halfB;
            long half2W = wholeW - halfW;

            if (half2W == 0)
            {
                continue;
            }

            temp += (((double)half2R * half2R) + ((double)half2G * half2G) + ((double)half2B * half2B)) / half2W;

            if (temp > max)
            {
                max = temp;
                cut = i;
            }
        }

        return max;
    }

    /// <summary>Splits <paramref name="set1"/> into itself and <paramref name="set2"/>; returns false if it cannot be cut.</summary>
    private static bool Cut(Box set1, Box set2, long[] wt, long[] mr, long[] mg, long[] mb)
    {
        long wholeR = Vol(set1, mr);
        long wholeG = Vol(set1, mg);
        long wholeB = Vol(set1, mb);
        long wholeW = Vol(set1, wt);

        double maxR = Maximize(set1, Red, set1.R0 + 1, set1.R1, out int cutR, wholeR, wholeG, wholeB, wholeW, wt, mr, mg, mb);
        double maxG = Maximize(set1, Green, set1.G0 + 1, set1.G1, out int cutG, wholeR, wholeG, wholeB, wholeW, wt, mr, mg, mb);
        double maxB = Maximize(set1, Blue, set1.B0 + 1, set1.B1, out int cutB, wholeR, wholeG, wholeB, wholeW, wt, mr, mg, mb);

        int dir;
        if (maxR >= maxG && maxR >= maxB)
        {
            dir = Red;
            if (cutR < 0)
            {
                return false; // best direction cannot actually be split
            }
        }
        else if (maxG >= maxR && maxG >= maxB)
        {
            dir = Green;
        }
        else
        {
            dir = Blue;
        }

        set2.R1 = set1.R1;
        set2.G1 = set1.G1;
        set2.B1 = set1.B1;

        switch (dir)
        {
            case Red:
                set2.R0 = set1.R1 = cutR;
                set2.G0 = set1.G0;
                set2.B0 = set1.B0;
                break;
            case Green:
                set2.G0 = set1.G1 = cutG;
                set2.R0 = set1.R0;
                set2.B0 = set1.B0;
                break;
            default:
                set2.B0 = set1.B1 = cutB;
                set2.R0 = set1.R0;
                set2.G0 = set1.G0;
                break;
        }

        set1.Volume = (set1.R1 - set1.R0) * (set1.G1 - set1.G0) * (set1.B1 - set1.B0);
        set2.Volume = (set2.R1 - set2.R0) * (set2.G1 - set2.G0) * (set2.B1 - set2.B0);
        return true;
    }

    /// <summary>Tags every histogram cell inside the box with its palette index.</summary>
    private void MarkTag(Box c, byte label)
    {
        for (int r = c.R0 + 1; r <= c.R1; r++)
        {
            for (int g = c.G0 + 1; g <= c.G1; g++)
            {
                for (int b = c.B0 + 1; b <= c.B1; b++)
                {
                    _tag[Ind(r, g, b)] = label;
                }
            }
        }
    }

    private const int Blue = 0;
    private const int Green = 1;
    private const int Red = 2;

    /// <summary>An axis-aligned box in the 33³ color-moment space.</summary>
    private sealed class Box
    {
        public int R0, R1, G0, G1, B0, B1;
        public int Volume;
    }
}
