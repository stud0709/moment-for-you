using System;

namespace MomentForYou;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Set DPI switches before WPF initializes. Keeping them here makes the
        // executable independent of a companion .exe.config file.
        AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
        AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
