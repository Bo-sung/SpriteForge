using System.Windows.Controls;

namespace SpriteForge.Gui.Views;

/// <summary>
/// Animation playback transport panel (play/pause, stop, frame stepping, loop). Code-behind is limited
/// to <c>InitializeComponent</c>; the integrator wires the <see cref="ViewModels.AnimationViewModel"/>
/// as <c>DataContext</c>.
/// </summary>
public partial class AnimationPanel : UserControl
{
    /// <summary>Creates the animation playback panel.</summary>
    public AnimationPanel()
    {
        InitializeComponent();
    }
}
