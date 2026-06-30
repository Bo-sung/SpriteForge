using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpriteForge.Core.Models;
using SpriteForge.Core.Rendering;
using SkiaSharp;
// Disambiguate from System.Windows.Media.RenderOptions.
using CoreRenderOptions = SpriteForge.Core.Models.RenderOptions;

namespace SpriteForge.Gui.Services;

/// <summary>
/// UI-side wrapper around <see cref="PreviewSession"/>: owns the loaded session and converts the
/// renderer's <see cref="SKBitmap"/> output into frozen WPF <see cref="BitmapSource"/>s that can be
/// bound on the UI thread.
/// </summary>
public sealed class PreviewService : IDisposable
{
    private PreviewSession? _session;

    /// <summary>Facts about the currently loaded clip, or null when nothing is loaded.</summary>
    public PreviewInfo? Info => _session?.Info;

    /// <summary>True once a model has been loaded.</summary>
    public bool IsLoaded => _session is not null;

    /// <summary>Loads (or reloads) a model, disposing any previous session.</summary>
    public void Load(CoreRenderOptions opts, EquipmentManifest? equipment = null, RetargetMap? retarget = null)
    {
        Unload();
        _session = PreviewSession.Load(opts, equipment, retarget);
    }

    /// <summary>Disposes the current session, if any.</summary>
    public void Unload()
    {
        _session?.Dispose();
        _session = null;
    }

    /// <summary>
    /// Renders one preview frame and returns it as a frozen <see cref="BitmapSource"/> (safe to assign
    /// to a bound property on the UI thread). Returns null if nothing is loaded.
    /// </summary>
    public async Task<BitmapSource?> RenderAsync(CoreRenderOptions cameraOpts, float yaw, float time)
    {
        PreviewSession? session = _session;
        if (session is null)
        {
            return null;
        }

        using SKBitmap bmp = await session.RenderAsync(cameraOpts, yaw, time).ConfigureAwait(false);
        return ToFrozenBitmap(bmp);
    }

    /// <summary>Generates the full pixel-art sprite sheet for the loaded model.</summary>
    public Task<List<SpriteFrame>> GenerateSheetAsync(
        CoreRenderOptions opts, PixelArtOptions pixelOpts, Action<string>? progress = null)
    {
        PreviewSession session = _session
            ?? throw new InvalidOperationException("No model is loaded.");
        return session.GenerateSheetAsync(opts, pixelOpts, progress);
    }

    /// <summary>Converts an unpremultiplied RGBA <see cref="SKBitmap"/> into a frozen BGRA32 BitmapSource.</summary>
    private static BitmapSource ToFrozenBitmap(SKBitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int srcStride = bmp.RowBytes;
        ReadOnlySpan<byte> src = bmp.GetPixelSpan();
        var dst = new byte[w * h * 4];

        // SKBitmap is Rgba8888 (R,G,B,A); WPF Bgra32 wants (B,G,R,A) with straight alpha (Unpremul matches).
        for (int y = 0; y < h; y++)
        {
            int srcRow = y * srcStride;
            int dstRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int s = srcRow + (x * 4);
                int d = dstRow + (x * 4);
                dst[d + 0] = src[s + 2]; // B
                dst[d + 1] = src[s + 1]; // G
                dst[d + 2] = src[s + 0]; // R
                dst[d + 3] = src[s + 3]; // A
            }
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dst, w * 4);
        bitmap.Freeze(); // cross-thread bindable
        return bitmap;
    }

    /// <inheritdoc />
    public void Dispose() => Unload();
}
