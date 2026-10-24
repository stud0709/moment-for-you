using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using Forms = System.Windows.Forms;

namespace MomentForYou;

public partial class BreakWindow : Window
{
    private readonly Action finish;
    private readonly Action<Uri> openHomepage;
    private bool allowClose;
    public BreakWindow(Action finish, Action<Uri>? openHomepage = null)
    {
        InitializeComponent();
        this.finish = finish;
        this.openHomepage = openHomepage ?? OpenInBrowser;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        FooterVersion.Text = "v" + version!.ToString(3);
    }

    internal void PositionOn(Forms.Screen screen)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var bounds = screen.Bounds;
        // Screen bounds are physical pixels, including negative monitor coordinates.
        Position(handle, bounds);
    }

    public void ShowOn(Forms.Screen screen)
    {
        PositionOn(screen);
        Show();
        PositionOn(screen);
        ContentRendered += (_, _) => PositionOn(screen);
    }

    private static void Position(IntPtr handle, System.Drawing.Rectangle bounds)
    {
        if (!SetWindowPos(handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dismiss() { allowClose = true; Close(); }
    private void OnProjectHomepage(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        finish();
        openHomepage(e.Uri);
    }
    private static void OpenInBrowser(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception) { System.Windows.Clipboard.SetText(uri.AbsoluteUri); }
    }
    private void OnDone(object sender, RoutedEventArgs e) => finish();
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; finish(); } }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!allowClose) { e.Cancel = true; Dispatcher.BeginInvoke(finish); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
