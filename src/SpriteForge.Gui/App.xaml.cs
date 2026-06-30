using System.Diagnostics;
using System.Windows;

namespace SpriteForge.Gui;

/// <summary>WPF application entry point.</summary>
public partial class App : Application
{
    // TEMP DIAGNOSTIC: log WPF binding errors to a file so we can verify the viewer wiring.
    // Remove after the sheet-player integration is confirmed working.
    protected override void OnStartup(StartupEventArgs e)
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spriteforge_binding_errors.log");
        var listener = new TextWriterTraceListener(logPath) { Name = "bindingLog" };
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        System.AppDomain.CurrentDomain.FirstChanceException += (s, args) =>
        {
            if (args.Exception.Message.Contains("BindingExpression", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.AppendAllText(logPath, "[BINDING] " + args.Exception + System.Environment.NewLine);
            }
        };
        base.OnStartup(e);
    }
}
