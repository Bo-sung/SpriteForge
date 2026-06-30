using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using SpriteForge.Core.Models;
using SpriteForge.Core.Packing;
using SpriteForge.Core.Rendering;
using SpriteForge.Gui.Mvvm;

namespace SpriteForge.Gui.ViewModels;

/// <summary>
/// Output / sprite-sheet panel: renders the full pixel-art sprite sheet for the loaded model, previews
/// it inline, and saves the sheet PNG plus a sibling <c>{anim}_metadata.json</c>. Built entirely from
/// <see cref="MainViewModel"/>'s public surface — no edits to the root view model.
/// </summary>
public sealed class OutputViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;

    private int _spriteSize = 48;
    private int _maxColors = 32;
    private string _progress = string.Empty;
    private bool _isGenerating;
    private BitmapSource? _sheetImage;

    // The generated frames are disposed right after Pack (see finally), so the packed sheet bitmap is
    // kept alive here to re-encode at save time. Metadata describing that exact sheet is cached too.
    private SKBitmap? _lastSheet;
    private SheetSaveInfo? _lastInfo;

    /// <summary>Creates the output panel bound to the given root view model.</summary>
    /// <param name="main">The root view model whose render options / preview service drive generation.</param>
    public OutputViewModel(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;

        GenerateCommand = new RelayCommand(
            async () => await GenerateAsync(),
            () => !IsGenerating && _main.Preview.IsLoaded);
        SaveCommand = new RelayCommand(Save, () => SheetImage is not null);

        _main.ModelLoaded += OnModelLoaded;
    }

    /// <summary>Exposes the root view model so OutputPanel can bind to MainViewModel properties.</summary>
    public MainViewModel Main => _main;

    private static readonly string[] DirNames = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    private static readonly int[] DirAngles = [0, 45, 90, 135, 180, 225, 270, 315];

    private int _currentDirection;

    /// <summary>Index of the direction currently highlighted in the OutputPanel compass (0..7).</summary>
    public int CurrentDirection
    {
        get => _currentDirection;
        set
        {
            int clamped = value % 8;
            if (clamped < 0) clamped += 8;
            if (SetField(ref _currentDirection, clamped))
            {
                OnPropertyChanged(nameof(ActiveDirLabel));
                OnPropertyChanged(nameof(ActiveDirAngle));
            }
        }
    }

    /// <summary>Cardinal name (N/NE/E/…) of the current direction.</summary>
    public string ActiveDirLabel => DirNames[CurrentDirection % 8];

    /// <summary>Yaw angle in degrees of the current direction.</summary>
    public int ActiveDirAngle => DirAngles[CurrentDirection % 8];

    /// <summary>Final pixel-art cell size in pixels (square). Default 48.</summary>
    public int SpriteSize
    {
        get => _spriteSize;
        set => SetField(ref _spriteSize, value);
    }

    /// <summary>Maximum palette colors for Wu quantization. Default 32.</summary>
    public int MaxColors
    {
        get => _maxColors;
        set => SetField(ref _maxColors, value);
    }

    /// <summary>Per-frame progress text reported by the sheet generator.</summary>
    public string Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    /// <summary>True while a sheet is being generated (disables <see cref="GenerateCommand"/>).</summary>
    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetField(ref _isGenerating, value))
            {
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The last generated sheet as a frozen, cross-thread-bindable image; null until generated.</summary>
    public BitmapSource? SheetImage
    {
        get => _sheetImage;
        private set
        {
            if (SetField(ref _sheetImage, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Generates the full sprite sheet for the loaded model and previews it.</summary>
    public RelayCommand GenerateCommand { get; }

    /// <summary>Saves the last generated sheet PNG and a sibling metadata.json via a file dialog.</summary>
    public RelayCommand SaveCommand { get; }

    private async Task GenerateAsync()
    {
        if (_isGenerating)
        {
            return;
        }

        IsGenerating = true;
        Progress = "Starting…";

        List<SpriteFrame> frames = new();
        try
        {
            var opts = _main.BuildRenderOptions();
            frames = await _main.Preview.GenerateSheetAsync(
                opts,
                new PixelArtOptions { SpriteSize = SpriteSize, MaxColors = MaxColors },
                m => Progress = m);

            int frameCount = frames.Count == 0 ? 0 : frames.Max(f => f.FrameIndex) + 1;
            if (frameCount == 0)
            {
                Progress = "No frames generated.";
                DiscardCachedSheet();
                SheetImage = null;
                return;
            }

            // Pack, preview, and keep the packed sheet alive for Save (frames are disposed below).
            SKBitmap sheet = SpriteSheetPacker.Pack(frames, SpriteSize, SpriteSize, opts.Directions, frameCount);
            SheetImage = ToFrozenBitmap(sheet);
            DiscardCachedSheet();
            _lastSheet = sheet;
            _lastInfo = new SheetSaveInfo(_main.AnimName, SpriteSize, opts.Directions, frameCount, opts.Fps);

            // Hand the freshly packed sheet to the player (if a sink is wired) and bring it on screen.
            // The sheet bitmap is frozen, so it is safe to share between the output cache and the player.
            _main.SheetSink?.Invoke(SheetImage, SpriteSize, SpriteSize, opts.Directions, frameCount, opts.Fps);
            Progress = $"Done — {frameCount} frame(s) × {opts.Directions} direction(s).";
        }
        catch (Exception ex)
        {
            Progress = "Generate failed: " + ex.Message;
        }
        finally
        {
            foreach (SpriteFrame frame in frames)
            {
                frame.Dispose();
            }
            IsGenerating = false;
        }
    }

    private void Save()
    {
        SKBitmap? sheet = _lastSheet;
        SheetSaveInfo? info = _lastInfo;
        if (sheet is null || info is null)
        {
            return;
        }

        string anim = info.AnimName;
        string file = SanitizeFileName(anim);
        var dialog = new SaveFileDialog
        {
            Title = "Save sprite sheet",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{file}_sheet.png",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string sheetPath = dialog.FileName;
        string dir = Path.GetDirectoryName(sheetPath) ?? string.Empty;
        try
        {
            using (SKData data = sheet.Encode(SKEncodedImageFormat.Png, 100))
            using (Stream fs = File.Create(sheetPath))
            {
                data.SaveTo(fs);
            }

            OutputMetadata metadata = new()
            {
                SpriteWidth = info.SpriteSize,
                SpriteHeight = info.SpriteSize,
                Directions = info.Directions,
                Animations = new List<AnimationMetadata>
                {
                    new() { Name = anim, FrameCount = info.FrameCount, Fps = info.Fps, SheetRow = 0 },
                },
                Pivot = new PivotMetadata { X = 0.5f, Y = 0.5f },
            };
            MetadataWriter.Write(Path.Combine(dir, $"{file}_metadata.json"), metadata);
            Progress = $"Saved '{sheetPath}'.";
        }
        catch (Exception ex)
        {
            Progress = "Save failed: " + ex.Message;
        }
    }

    private void OnModelLoaded(PreviewInfo _)
    {
        // A new model invalidates the previously cached sheet (it belongs to the old clip).
        DiscardCachedSheet();
        SheetImage = null;
        Progress = string.Empty;
        GenerateCommand.RaiseCanExecuteChanged();
    }

    private void DiscardCachedSheet()
    {
        _lastSheet?.Dispose();
        _lastSheet = null;
        _lastInfo = null;
    }

    /// <summary>Replaces characters illegal in file names with underscores; falls back to "sprite".</summary>
    private static string SanitizeFileName(string name)
    {
        HashSet<char> invalid = new(Path.GetInvalidFileNameChars());
        string cleaned = string.Concat(name.Trim().Select(c => invalid.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(cleaned) ? "sprite" : cleaned;
    }

    /// <summary>
    /// Converts an unpremultiplied RGBA <see cref="SKBitmap"/> (the packer's output) into a frozen BGRA32
    /// <see cref="BitmapSource"/> safe to bind on the UI thread. Mirrors PreviewService's conversion.
    /// </summary>
    private static BitmapSource ToFrozenBitmap(SKBitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int srcStride = bmp.RowBytes;
        ReadOnlySpan<byte> src = bmp.GetPixelSpan();
        var dst = new byte[w * h * 4];

        // SKBitmap is Rgba8888 (R,G,B,A); WPF Bgra32 wants (B,G,R,A).
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

        BitmapSource bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dst, w * 4);
        bitmap.Freeze(); // cross-thread bindable
        return bitmap;
    }

    /// <summary>Releases the cached sheet bitmap and unsubscribes from the root view model.</summary>
    public void Dispose()
    {
        _main.ModelLoaded -= OnModelLoaded;
        DiscardCachedSheet();
    }

    /// <summary>Snapshot of the metadata needed to save a previously generated sheet.</summary>
    private sealed record SheetSaveInfo(string AnimName, int SpriteSize, int Directions, int FrameCount, int Fps);
}
