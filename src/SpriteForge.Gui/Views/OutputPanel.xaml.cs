using System.Windows;
using System.Windows.Controls;
using SpriteForge.Gui.ViewModels;

namespace SpriteForge.Gui.Views;

/// <summary>
/// Output / sprite-sheet panel: direction compass, generate, and save controls.
/// Direction button clicks are handled here (Tag = direction index string) and forwarded to the VM.
/// </summary>
public partial class OutputPanel : UserControl
{
    /// <summary>Creates the output panel.</summary>
    public OutputPanel()
    {
        InitializeComponent();
    }

    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int dir)
            && DataContext is OutputViewModel vm)
        {
            vm.CurrentDirection = dir;
        }
    }
}
