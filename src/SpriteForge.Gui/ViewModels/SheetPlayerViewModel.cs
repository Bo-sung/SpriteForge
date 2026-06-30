using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpriteForge.Core.Rendering;
using SpriteForge.Gui.Mvvm;

namespace SpriteForge.Gui.ViewModels;

/// <summary>
/// Plays a generated sprite sheet as an animation in the 2D result window. The packed sheet is laid out
/// rows = directions, columns = frames (see <c>SpriteSheetPacker</c>), so this crops the
/// <c>(direction, frame)</c> cell out of the sheet and advances frames on a UI-thread timer, with
/// direction selection and looping. Fed by <see cref="MainViewModel.SheetSink"/> after each generate.
/// </summary>
public sealed class SheetPlayerViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;

    private BitmapSource? _sheet;
    private int _spriteW = 1, _spriteH = 1, _directions = 1, _frames = 1, _fps = 12;
    private int _dir, _frame;

    private bool _isPlaying;
    private bool _loop = true;
    private BitmapSource? _currentFrameImage;

    /// <summary>Creates the player bound to the root view model (it clears when a new model loads).</summary>
    public SheetPlayerViewModel(MainViewModel main)
    {
        _main = main;
        _main.ModelLoaded += OnModelLoaded;

        _timer = new DispatcherTimer(DispatcherPriority.Normal);
        _timer.Tick += OnTick;

        PlayPauseCommand = new RelayCommand(TogglePlay, () => HasSheet && _frames > 1);
        StopCommand = new RelayCommand(Stop, () => HasSheet);
        PrevDirCommand = new RelayCommand(() => StepDirection(-1), () => HasSheet && _directions > 1);
        NextDirCommand = new RelayCommand(() => StepDirection(1), () => HasSheet && _directions > 1);
    }

    /// <summary>The current animation frame (a crop of the sheet) for the selected direction.</summary>
    public BitmapSource? CurrentFrameImage { get => _currentFrameImage; private set => SetField(ref _currentFrameImage, value); }

    /// <summary>True once a sheet has been loaded into the player.</summary>
    public bool HasSheet => _sheet is not null;

    /// <summary>True while the playback timer is advancing frames.</summary>
    public bool IsPlaying { get => _isPlaying; private set => SetField(ref _isPlaying, value); }

    /// <summary>Loop playback (default true).</summary>
    public bool Loop { get => _loop; set => SetField(ref _loop, value); }

    /// <summary>Number of directions in the loaded sheet.</summary>
    public int Directions => _directions;

    /// <summary>"{dir+1} / {n} · {angle}°" for the currently selected direction.</summary>
    public string DirectionLabel => HasSheet ? $"{_dir + 1} / {_directions}  ·  {DirAngle}°" : "—";

    /// <summary>"frame {i+1} / {n}" for the current frame.</summary>
    public string FrameLabel => HasSheet ? $"frame {_frame + 1} / {_frames}" : "—";

    private int DirAngle => _directions > 0 ? (int)Math.Round(_dir * 360.0 / _directions) : 0;

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand PrevDirCommand { get; }
    public RelayCommand NextDirCommand { get; }

    /// <summary>Loads a freshly packed sheet and begins playback (frame 0, direction 0).</summary>
    public void LoadSheet(BitmapSource sheet, int spriteW, int spriteH, int directions, int frames, int fps)
    {
        _sheet = sheet;
        _spriteW = Math.Max(1, spriteW);
        _spriteH = Math.Max(1, spriteH);
        _directions = Math.Max(1, directions);
        _frames = Math.Max(1, frames);
        _fps = Math.Max(1, fps);
        _dir = 0;
        _frame = 0;

        UpdateFrame();
        RaiseAll();

        if (_frames > 1)
        {
            Play();
        }
        else
        {
            _timer.Stop();
            IsPlaying = false;
        }
    }

    private void UpdateFrame()
    {
        if (_sheet is null)
        {
            CurrentFrameImage = null;
            return;
        }

        int x = _frame * _spriteW;
        int y = _dir * _spriteH;
        int w = _spriteW;
        int h = _spriteH;

        // Defensive clamp so a geometry mismatch never throws on CroppedBitmap.
        if (x < 0 || y < 0 || x >= _sheet.PixelWidth || y >= _sheet.PixelHeight)
        {
            return;
        }

        if (x + w > _sheet.PixelWidth) w = _sheet.PixelWidth - x;
        if (y + h > _sheet.PixelHeight) h = _sheet.PixelHeight - y;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var crop = new CroppedBitmap(_sheet, new Int32Rect(x, y, w, h));
        if (crop.CanFreeze)
        {
            crop.Freeze();
        }

        CurrentFrameImage = crop;
        OnPropertyChanged(nameof(FrameLabel));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_sheet is null || _frames <= 1)
        {
            _timer.Stop();
            IsPlaying = false;
            return;
        }

        int next = _frame + 1;
        if (next >= _frames)
        {
            if (_loop)
            {
                next = 0;
            }
            else
            {
                next = _frames - 1;
                _timer.Stop();
                IsPlaying = false;
            }
        }

        if (next != _frame)
        {
            _frame = next;
            UpdateFrame();
        }
    }

    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void Play()
    {
        if (!HasSheet || _frames <= 1)
        {
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _fps));
        _timer.Start();
        IsPlaying = true;
    }

    private void Pause()
    {
        _timer.Stop();
        IsPlaying = false;
    }

    private void Stop()
    {
        _timer.Stop();
        IsPlaying = false;
        _frame = 0;
        UpdateFrame();
    }

    private void StepDirection(int delta)
    {
        if (!HasSheet || _directions <= 1)
        {
            return;
        }

        _dir = (((_dir + delta) % _directions) + _directions) % _directions;
        UpdateFrame();
        OnPropertyChanged(nameof(DirectionLabel));
    }

    private void OnModelLoaded(PreviewInfo _) => Clear();

    private void Clear()
    {
        _timer.Stop();
        IsPlaying = false;
        _sheet = null;
        _frame = 0;
        _dir = 0;
        CurrentFrameImage = null;
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasSheet));
        OnPropertyChanged(nameof(Directions));
        OnPropertyChanged(nameof(DirectionLabel));
        OnPropertyChanged(nameof(FrameLabel));
        PlayPauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        PrevDirCommand.RaiseCanExecuteChanged();
        NextDirCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Stops the timer and unsubscribes from the root view model.</summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _main.ModelLoaded -= OnModelLoaded;
    }
}
