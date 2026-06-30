using System.ComponentModel;
using System.Windows.Threading;
using SpriteForge.Core.Rendering;
using SpriteForge.Gui.Mvvm;

namespace SpriteForge.Gui.ViewModels;

/// <summary>
/// Animation playback transport for the preview: a UI-thread <see cref="DispatcherTimer"/> that
/// advances <see cref="MainViewModel.Time"/> one frame per tick, with play/pause, stop, single-frame
/// stepping, and optional looping. It subscribes to <see cref="MainViewModel.ModelLoaded"/> to reset
/// on reload and is <see cref="IDisposable"/> so the host can tear the timer down. Setting
/// <see cref="MainViewModel.Time"/> already triggers a re-render, so this view model never calls
/// <see cref="MainViewModel.RequestRender"/> itself.
/// </summary>
public sealed class AnimationViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;
    private bool _isPlaying;
    private bool _loop = true;

    /// <summary>Creates the animation panel bound to the given root view model.</summary>
    /// <param name="main">The root view model whose timeline this panel drives.</param>
    public AnimationViewModel(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;

        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _main.Fps)),
        };
        _timer.Tick += OnTick;

        PlayPauseCommand = new RelayCommand(TogglePlayPause, () => _main.MaxFrameIndex > 0);
        StopCommand = new RelayCommand(Stop, () => _isPlaying);
        StepBackCommand = new RelayCommand(StepBack, CanStepBack);
        StepForwardCommand = new RelayCommand(StepForward, CanStepForward);

        _main.PropertyChanged += OnMainPropertyChanged;
        _main.ModelLoaded += OnModelLoaded;
    }

    /// <summary>The root view model whose <c>Time</c>/<c>MaxFrameIndex</c>/<c>AnimName</c>/<c>Fps</c> drive playback.</summary>
    public MainViewModel Main => _main;

    /// <summary>True while the playback timer is advancing frames.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetField(ref _isPlaying, value))
            {
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>When true, playback wraps to frame 0 after the last frame; otherwise it stops on the last frame.</summary>
    public bool Loop
    {
        get => _loop;
        set => SetField(ref _loop, value);
    }

    /// <summary>Toggles between playing and paused.</summary>
    public RelayCommand PlayPauseCommand { get; }

    /// <summary>Stops playback and returns to the first frame.</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>Pauses and steps one frame backward, clamped to frame 0.</summary>
    public RelayCommand StepBackCommand { get; }

    /// <summary>Pauses and steps one frame forward, clamped to the last frame.</summary>
    public RelayCommand StepForwardCommand { get; }

    private void TogglePlayPause()
    {
        if (_isPlaying)
        {
            Pause();
            return;
        }

        StartPlaying();
    }

    private void StartPlaying()
    {
        // Static mesh (no animation): playback is a no-op.
        if (_main.MaxFrameIndex <= 0)
        {
            return;
        }

        // Re-derive the interval each play so an edited FPS takes effect immediately.
        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _main.Fps));
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
        _main.Time = 0;
    }

    private bool CanStepBack() => _main.MaxFrameIndex > 0 && _main.Time > 0;

    private void StepBack()
    {
        Pause();
        if (_main.MaxFrameIndex <= 0)
        {
            return;
        }

        _main.Time = Math.Max(0, _main.Time - 1);
    }

    private bool CanStepForward() => _main.MaxFrameIndex > 0 && _main.Time < _main.MaxFrameIndex;

    private void StepForward()
    {
        Pause();
        if (_main.MaxFrameIndex <= 0)
        {
            return;
        }

        _main.Time = Math.Min(_main.MaxFrameIndex, _main.Time + 1);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_main.MaxFrameIndex <= 0)
        {
            Pause();
            return;
        }

        int next = _main.Time + 1;
        if (next > _main.MaxFrameIndex)
        {
            if (_loop)
            {
                _main.Time = 0;
                return;
            }

            // No loop: hold on the last frame and stop.
            _main.Time = _main.MaxFrameIndex;
            Pause();
            return;
        }

        _main.Time = next;
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Time) or nameof(MainViewModel.MaxFrameIndex))
        {
            PlayPauseCommand.RaiseCanExecuteChanged();
            StepBackCommand.RaiseCanExecuteChanged();
            StepForwardCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnModelLoaded(PreviewInfo _)
    {
        Pause();
        _main.Time = 0;
    }

    /// <summary>Stops and detaches the timer and unsubscribes from the root view model.</summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _main.ModelLoaded -= OnModelLoaded;
        _main.PropertyChanged -= OnMainPropertyChanged;
    }
}
