using System.Windows;
using SpriteForge.Gui.Services;
using SpriteForge.Gui.ViewModels;

namespace SpriteForge.Gui;

/// <summary>Main application window: hosts the 3-column layout and wires feature view models.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var main = new MainViewModel(new PreviewService());
        DataContext = main;

        // Feature panels own separate view models; DataContexts are set here so XAML can use typed bindings.
        var animation = new AnimationViewModel(main);
        var equipment = new EquipmentViewModel(main);   // self-wires EquipmentProvider on load
        var output    = new OutputViewModel(main);
        var player    = new SheetPlayerViewModel(main); // animates the generated sheet in the 2D result window

        AnimationPanelView.DataContext = animation;
        EquipmentPanelView.DataContext = equipment;
        OutputPanelView.DataContext    = output;
        ResultPanelView.DataContext    = player;

        // Route a freshly generated sheet to both the root state (HasSheet / SheetPreviewImage) and the
        // 2D animation player.
        main.SheetSink = (sheet, w, h, dirs, frames, fps) =>
        {
            main.FeedGeneratedSheet(sheet, w, h, dirs, frames, fps);
            player.LoadSheet(sheet, w, h, dirs, frames, fps);
        };

        Closed += (_, _) =>
        {
            animation.Dispose();
            output.Dispose();
            player.Dispose();
            main.Dispose();
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
