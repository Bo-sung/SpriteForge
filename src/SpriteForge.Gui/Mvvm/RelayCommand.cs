using System.Windows.Input;

namespace SpriteForge.Gui.Mvvm;

/// <summary>A simple <see cref="ICommand"/> backed by delegates.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>Creates a command from an execute delegate and an optional can-execute predicate.</summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>Re-queries <see cref="CanExecute"/> for bound controls.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
