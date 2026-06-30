using System.Numerics;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SpriteForge.Core.Models;
using SpriteForge.Core.Rendering;
using SpriteForge.Gui.Mvvm;
using SpriteForge.Gui.Services;

namespace SpriteForge.Gui.ViewModels;

/// <summary>
/// Root view model: owns the model/camera/preview state and the coalesced render loop. Feature panels
/// (animation playback, equipment, output) are <b>separate view models</b> that take a reference to this
/// one and extend it through its public surface — the <see cref="ModelLoaded"/> event, the
/// <see cref="EquipmentProvider"/> / <see cref="RetargetProvider"/> delegates, <see cref="Reload"/>,
/// <see cref="RequestRender"/>, <see cref="Preview"/>, and <see cref="BuildRenderOptions"/> — so they can
/// be built independently without editing this file.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly PreviewService _preview;

    // Render coalescing: while a render is in flight, further requests just mark the state dirty; when
    // the in-flight render finishes it re-renders once more with the latest params. Bursts (slider
    // drags) collapse to "latest wins" without timers.
    private bool _rendering;
    private bool _renderDirty;

    /// <summary>Creates the view model around a preview service.</summary>
    public MainViewModel(PreviewService preview)
    {
        _preview = preview;
        BrowseMeshCommand = new RelayCommand(BrowseMesh);
        BrowseAnimCommand = new RelayCommand(BrowseAnim);
        LoadCommand = new RelayCommand(Load, () => !string.IsNullOrWhiteSpace(InputPath));
        ToggleSheetModeCommand = new RelayCommand(() => IsSheetMode = !IsSheetMode);
    }

    /// <summary>The preview service, exposed so feature panels can render / generate sheets.</summary>
    public PreviewService Preview => _preview;

    /// <summary>Raised after a successful load so panels can refresh from the clip info.</summary>
    public event Action<PreviewInfo>? ModelLoaded;

    /// <summary>Optional supplier of an equipment manifest, consulted on every <see cref="Load"/>.</summary>
    public Func<EquipmentManifest?>? EquipmentProvider { get; set; }

    /// <summary>Optional supplier of a retarget map, consulted on every <see cref="Load"/>.</summary>
    public Func<RetargetMap?>? RetargetProvider { get; set; }

    /// <summary>
    /// Optional sink invoked when a sprite sheet has just been packed, so the host can feed it to the
    /// 2D result panel. Carries the sheet bitmap plus the grid geometry the packer used.
    /// </summary>
    public Action<BitmapSource, int, int, int, int, int>? SheetSink { get; set; }

    // --- Viewport mode (3D render preview ↔ 2D sheet playback) ---
    private bool _isSheetMode;
    /// <summary>True when the main viewport shows the sheet player instead of the 3D preview.</summary>
    public bool IsSheetMode
    {
        get => _isSheetMode;
        set { if (SetField(ref _isSheetMode, value)) ToggleSheetModeCommand.RaiseCanExecuteChanged(); }
    }

    // --- 3-column layout state ---
    private bool _hasSheet;
    /// <summary>True after a sprite sheet has been generated for the current model.</summary>
    public bool HasSheet { get => _hasSheet; private set => SetField(ref _hasSheet, value); }

    private BitmapSource? _sheetPreviewImage;
    /// <summary>The most recently generated sprite sheet, displayed in the 2D result panel.</summary>
    public BitmapSource? SheetPreviewImage { get => _sheetPreviewImage; private set => SetField(ref _sheetPreviewImage, value); }

    /// <summary>True when a model is successfully loaded and ready to render.</summary>
    public bool IsLoaded => _preview.IsLoaded;

    /// <summary>Filename (no directory) of the currently loaded mesh, or empty when none.</summary>
    public string DocumentName => string.IsNullOrEmpty(_inputPath)
        ? string.Empty
        : System.IO.Path.GetFileName(_inputPath) ?? string.Empty;

    /// <summary>Routes a freshly packed sheet into the 2D result panel.</summary>
    public void FeedGeneratedSheet(BitmapSource sheet, int spriteW, int spriteH, int directions, int frames, int fps)
    {
        SheetPreviewImage = sheet;
        HasSheet = true;
    }

    // --- Model ---
    private string? _inputPath;
    public string? InputPath
    {
        get => _inputPath;
        set
        {
            if (SetField(ref _inputPath, value))
            {
                LoadCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(DocumentName));
            }
        }
    }

    private string? _animPath;
    public string? AnimPath { get => _animPath; set => SetField(ref _animPath, value); }

    // --- Camera (live; re-renders on change) ---
    // Engine-style transform rows: POS = look-at target offset (pan, world units), ROT = pitch/facing/roll
    // (degrees), ZOOM = framing multiplier. ROT.Y is the model facing so the live preview matches the
    // sprite sheet's per-direction turntable exactly (no sign flip); pitch/zoom auto-fit the subject.
    private double _posX;
    public double PosX { get => _posX; set { if (SetField(ref _posX, value)) RequestRender(); } }

    private double _posY;
    public double PosY { get => _posY; set { if (SetField(ref _posY, value)) RequestRender(); } }

    private double _posZ;
    public double PosZ { get => _posZ; set { if (SetField(ref _posZ, value)) RequestRender(); } }

    private double _rotX = 26.5;
    public double RotX { get => _rotX; set { if (SetField(ref _rotX, value)) RequestRender(); } }

    private double _rotY;
    public double RotY { get => _rotY; set { if (SetField(ref _rotY, value)) RequestRender(); } }

    private double _rotZ;
    public double RotZ { get => _rotZ; set { if (SetField(ref _rotZ, value)) RequestRender(); } }

    private double _zoom = 1.0;
    public double Zoom { get => _zoom; set { if (SetField(ref _zoom, value)) RequestRender(); } }

    private double _camDistance;
    public double CamDistance { get => _camDistance; set { if (SetField(ref _camDistance, value)) RequestRender(); } }

    private bool _ortho;
    public bool Ortho { get => _ortho; set { if (SetField(ref _ortho, value)) RequestRender(); } }

    private bool _upAxisZ;
    public bool UpAxisZ { get => _upAxisZ; set { if (SetField(ref _upAxisZ, value)) RequestRender(); } }

    private bool _inPlace = true;
    public bool InPlace { get => _inPlace; set { if (SetField(ref _inPlace, value)) RequestRender(); } }

    // --- Load-time settings (applied on Load) ---
    private int _renderSize = 256;
    public int RenderSize { get => _renderSize; set => SetField(ref _renderSize, value); }

    private int _fps = 12;
    public int Fps { get => _fps; set => SetField(ref _fps, value); }

    private int _directions = 8;
    public int Directions { get => _directions; set => SetField(ref _directions, value); }

    // --- Animation timeline (frame index; playback is driven by the animation panel) ---
    private int _time;
    public int Time { get => _time; set { if (SetField(ref _time, value)) RequestRender(); } }

    private int _maxFrameIndex;
    public int MaxFrameIndex { get => _maxFrameIndex; set => SetField(ref _maxFrameIndex, value); }

    /// <summary>Current animation timestamp in seconds, derived from the frame index and fps.</summary>
    public float CurrentTimeSeconds => Fps > 0 ? (float)Time / Fps : 0f;

    // --- Status / output ---
    private BitmapSource? _previewImage;
    public BitmapSource? PreviewImage { get => _previewImage; private set => SetField(ref _previewImage, value); }

    private string _status = "Load a model to begin.";
    public string Status { get => _status; set => SetField(ref _status, value); }

    private string _animName = "Default";
    public string AnimName { get => _animName; private set => SetField(ref _animName, value); }

    public RelayCommand BrowseMeshCommand { get; }
    public RelayCommand BrowseAnimCommand { get; }
    public RelayCommand LoadCommand { get; }

    /// <summary>Toggles the main viewport between the 3D render preview and the 2D sheet player.</summary>
    public RelayCommand ToggleSheetModeCommand { get; }

    /// <summary>Builds the core render options from the current UI state.</summary>
    public RenderOptions BuildRenderOptions() => new()
    {
        Input = InputPath ?? string.Empty,
        Anim = string.IsNullOrWhiteSpace(AnimPath) ? null : AnimPath,
        RenderSize = RenderSize,
        Fps = Fps,
        Directions = Directions,
        CamPitch = (float)RotX,
        CamZoom = (float)Zoom,
        CamRoll = (float)RotZ,
        CamTarget = new Vector3((float)PosX, (float)PosY, (float)PosZ),
        CamDistance = (float)CamDistance,
        Ortho = Ortho,
        UpAxis = UpAxisZ ? UpAxis.Z : UpAxis.Y,
        InPlace = InPlace,
    };

    /// <summary>Loads (or reloads) the model with the current options, equipment, and retarget map.</summary>
    public void Load()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            return;
        }

        try
        {
            EquipmentManifest? equipment = EquipmentProvider?.Invoke();
            RetargetMap? retarget = RetargetProvider?.Invoke();

            _preview.Load(BuildRenderOptions(), equipment, retarget);

            PreviewInfoLoaded();
            RequestRender();
        }
        catch (Exception ex)
        {
            Status = "Load failed: " + ex.Message;
            OnPropertyChanged(nameof(IsLoaded));
        }
    }

    /// <summary>Reloads the model (e.g. after equipment or render-size changes). Alias for <see cref="Load"/>.</summary>
    public void Reload() => Load();

    private void PreviewInfoLoaded()
    {
        PreviewInfo? info = _preview.Info;
        if (info is null)
        {
            return;
        }

        AnimName = info.AnimationName;
        MaxFrameIndex = Math.Max(0, info.FrameCount - 1);
        Time = 0;
        HasSheet = false;
        SheetPreviewImage = null;
        Status = info.HasAnimation
            ? $"Loaded '{info.AnimationName}' — {info.FrameCount} frames @ {info.Fps} fps."
            : "Loaded static mesh.";
        if (info.HasRootMotion && !InPlace)
        {
            Status += $" Root motion ~{info.RootMotionTravel:F0}u — enable In-Place to center.";
        }

        OnPropertyChanged(nameof(IsLoaded));
        ModelLoaded?.Invoke(info);
    }

    /// <summary>Triggers a coalesced re-render at the current camera / time.</summary>
    public async void RequestRender()
    {
        if (!_preview.IsLoaded)
        {
            return;
        }

        _renderDirty = true;
        if (_rendering)
        {
            return;
        }

        _rendering = true;
        try
        {
            while (_renderDirty)
            {
                _renderDirty = false;
                RenderOptions opts = BuildRenderOptions();
                float yaw = (float)RotY;
                float time = CurrentTimeSeconds;
                try
                {
                    BitmapSource? image = await _preview.RenderAsync(opts, yaw, time);
                    if (image is not null)
                    {
                        PreviewImage = image;
                    }
                }
                catch (Exception ex)
                {
                    Status = "Render error: " + ex.Message;
                }
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void BrowseMesh()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select character mesh",
            Filter = "Models (*.fbx;*.glb;*.gltf;*.obj)|*.fbx;*.glb;*.gltf;*.obj|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            InputPath = dialog.FileName;
        }
    }

    private void BrowseAnim()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select animation (optional)",
            Filter = "Animations (*.fbx;*.glb;*.gltf)|*.fbx;*.glb;*.gltf|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            AnimPath = dialog.FileName;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _preview.Dispose();
}
