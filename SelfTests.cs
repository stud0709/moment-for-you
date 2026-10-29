using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace MomentForYou;

internal static class SelfTests
{
    public static int Run()
    {
        var results = new List<string>();
        int failures = 0;
        void Test(string name, Action action)
        {
            try { action(); results.Add("PASS " + name); }
            catch (Exception ex) { failures++; results.Add("FAIL " + name + ": " + ex); }
        }
        static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);
        static void TickUntil(BreakSchedule schedule, int before)
        { for (int second = 1; second < before; second++) if (schedule.Tick(At(second))) throw new InvalidOperationException("Break started early."); }

        Test("App uses Windows .NET Framework 4.8 without the modern .NET runtime", () =>
        {
            var framework = typeof(App).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert(framework?.FrameworkName == ".NETFramework,Version=v4.8", "Incorrect target framework.");
            Assert(Environment.Version.Major == 4, "The app is not running on the Windows Framework CLR.");
            Assert(Environment.Is64BitProcess, "The app is not running as Windows x64.");
            foreach (var reference in typeof(App).Assembly.GetReferencedAssemblies())
                Assert(reference.Name != "System.Text.Json" && reference.Name != "System.Private.CoreLib", "A modern .NET runtime dependency remains.");
        });
        Test("Default interval is 20 minutes", () => Assert(new Settings().IntervalMinutes == 20, "Incorrect default."));
        Test("Automatic deadline fires at exactly 20 minutes", () =>
        {
            var schedule = new BreakSchedule(20, At(0));
            TickUntil(schedule, 1200);
            Assert(schedule.Tick(At(1200)), "No break at deadline.");
            schedule.StartBreak();
            Assert(!schedule.Tick(At(1201)), "Duplicate break during overlay.");
        });
        Test("One-minute interval and interval changes", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            TickUntil(schedule, 60);
            Assert(schedule.Tick(At(60)), "One-minute deadline missed.");
            schedule.SetInterval(2, At(60));
            for (int second = 61; second < 180; second++) Assert(!schedule.Tick(At(second)), "Changed interval fired early.");
            Assert(schedule.Tick(At(180)), "Changed interval missed.");
        });
        Test("Done works immediately; next interval begins on dismissal", () =>
        {
            var schedule = new BreakSchedule(20, At(0));
            schedule.StartBreak();
            Assert(schedule.FinishBreak(At(0)), "Done did not work immediately.");
            Assert(!schedule.InBreak, "Break did not end.");
            Assert(schedule.UntilBreak(At(0)) == TimeSpan.FromMinutes(20), "Next interval did not reset.");
            Assert(!schedule.FinishBreak(At(1)), "Duplicate dismissal succeeded.");
        });
        Test("Break stays open until dismissed, even after a long absence", () =>
        {
            var schedule = new BreakSchedule(20, At(0));
            schedule.StartBreak();
            Assert(!schedule.Tick(At(3600)) && schedule.InBreak, "Break ended or restarted automatically.");
            Assert(schedule.FinishBreak(At(3600)), "Could not dismiss long break.");
            Assert(schedule.UntilBreak(At(3600)) == TimeSpan.FromMinutes(20), "Next interval did not start on return.");
        });
        Test("Pause suppresses reminders and resume resets interval", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            schedule.SetPaused(true, At(0));
            for (int second = 1; second <= 180; second++) Assert(!schedule.Tick(At(second)), "Paused reminder fired.");
            schedule.SetPaused(false, At(180));
            for (int second = 181; second < 240; second++) Assert(!schedule.Tick(At(second)), "Resume fired early.");
            Assert(schedule.Tick(At(240)), "Resume deadline missed.");
        });
        Test("Resume after long timer gap starts fresh", () =>
        {
            var schedule = new BreakSchedule(20, At(0));
            Assert(!schedule.Tick(At(4000)), "Reminder fired after sleep.");
            Assert(schedule.UntilBreak(At(4000)) == TimeSpan.FromMinutes(20), "Gap failed to reset.");
        });
        Test("Lock, screensaver, sleep, closed lid and display off restart a full interval on return", () =>
        {
            foreach (var reason in new[] { AwayReason.SessionLocked, AwayReason.ScreenSaver, AwayReason.Sleep, AwayReason.LidClosed, AwayReason.DisplayOff })
            {
                var schedule = new BreakSchedule(1, At(0));
                schedule.SetAway(reason, true, At(20));
                Assert(schedule.UntilBreak(At(4000)) == At(40), "Countdown continued while away: " + reason);
                Assert(!schedule.Tick(At(4000)), "Reminder fired while away: " + reason);
                schedule.SetAway(reason, false, At(4000));
                Assert(schedule.UntilBreak(At(4000)) == At(60), "Return did not start a full interval: " + reason);
                for (int second = 4001; second < 4060; second++) Assert(!schedule.Tick(At(second)), "Reminder fired before the new interval elapsed.");
                Assert(schedule.Tick(At(4060)), "Reminder did not fire after the full new interval.");
            }
        });
        Test("Unlock does not resume reminders while the screensaver is still running", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            schedule.SetAway(AwayReason.ScreenSaver, true, At(10));
            schedule.SetAway(AwayReason.SessionLocked, true, At(20));
            schedule.SetAway(AwayReason.SessionLocked, false, At(100));
            Assert(schedule.Suspended && !schedule.Tick(At(1000)), "Unlock cleared the screensaver pause.");
            Assert(schedule.UntilBreak(At(1000)) == At(50), "Overlapping pauses changed the remaining time.");
            schedule.SetAway(AwayReason.ScreenSaver, false, At(1000));
            Assert(!schedule.Suspended && schedule.UntilBreak(At(1000)) == At(60), "Screensaver exit did not start a full new interval.");
        });
        Test("Lid reopening waits for the display and sleep to end before restarting", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            schedule.SetAway(AwayReason.LidClosed, true, At(10));
            schedule.SetAway(AwayReason.DisplayOff, true, At(11));
            schedule.SetAway(AwayReason.Sleep, true, At(12));
            schedule.SetAway(AwayReason.LidClosed, false, At(1000));
            schedule.SetAway(AwayReason.Sleep, false, At(1001));
            Assert(schedule.Suspended && !schedule.Tick(At(1001)), "Resume cleared the display-off state.");
            schedule.SetAway(AwayReason.DisplayOff, false, At(1002));
            schedule.SetAway(AwayReason.DisplayOff, false, At(1003));
            Assert(!schedule.Suspended && schedule.UntilBreak(At(1003)) == At(59), "Return or duplicate notifications failed to preserve the new interval.");
            schedule.SetPaused(true, At(1004));
            schedule.SetAway(AwayReason.LidClosed, true, At(1005));
            schedule.SetAway(AwayReason.LidClosed, false, At(1006));
            Assert(schedule.Paused, "Lid reopening cleared a manual pause.");
        });
        Test("Lid, display, focus and missed sleep events close every real blocker window", () =>
            ((App)Application.Current).VerifyDesktopRecovery());
        Test("Native power notifications decode lid and session display changes", () =>
        {
            var changes = new List<(AwayReason reason, bool active)>();
            IntPtr handle;
            using (var monitor = new DesktopPowerMonitor((reason, active) => changes.Add((reason, active))))
            {
                handle = monitor.Handle;
                Assert(IsWindow(handle) && !IsWindowVisible(handle), "Power event window is missing or visible.");
                void Send(Guid setting, int value, int length = 4)
                {
                    var buffer = Marshal.AllocHGlobal(24);
                    try
                    {
                        Marshal.Copy(setting.ToByteArray(), 0, buffer, 16);
                        Marshal.WriteInt32(buffer, 16, length);
                        Marshal.WriteInt32(buffer, 20, value);
                        SendMessage(handle, 0x0218, new IntPtr(0x8013), buffer);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                foreach (var setting in new[] { DesktopPowerMonitor.LidState, DesktopPowerMonitor.DisplayState })
                {
                    var reason = setting == DesktopPowerMonitor.LidState ? AwayReason.LidClosed : AwayReason.DisplayOff;
                    Send(setting, 0);
                    Assert(changes[changes.Count - 1] == (reason, true), "Off/closed notification failed.");
                    Send(setting, 1);
                    Assert(changes[changes.Count - 1] == (reason, false), "On/open notification failed.");
                }
                Send(DesktopPowerMonitor.DisplayState, 2);
                Assert(changes[changes.Count - 1] == (AwayReason.DisplayOff, false), "Dimming incorrectly suspended reminders.");
                int count = changes.Count;
                Send(Guid.Empty, 0);
                Send(DesktopPowerMonitor.LidState, 2);
                Send(DesktopPowerMonitor.DisplayState, 3);
                Send(DesktopPowerMonitor.LidState, 0, 0);
                Assert(changes.Count == count, "Invalid or unrelated power notification changed the desktop state.");
            }
            Assert(!IsWindow(handle), "Power event window was not disposed.");
        });
        Test("Repeated screensaver polls do not reset the new interval after returning", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            schedule.SetAway(AwayReason.ScreenSaver, true, At(10));
            for (int second = 11; second <= 100; second++) schedule.SetAway(AwayReason.ScreenSaver, true, At(second));
            schedule.SetAway(AwayReason.ScreenSaver, false, At(100));
            for (int second = 101; second < 160; second++)
            {
                schedule.SetAway(AwayReason.ScreenSaver, false, At(second));
                Assert(!schedule.Tick(At(second)), "Screensaver polling fired a reminder early.");
            }
            Assert(schedule.Tick(At(160)), "Screensaver polling pushed the deadline back.");
        });
        Test("Returning from a lock preserves a manual pause", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            schedule.SetPaused(true, At(0));
            schedule.SetAway(AwayReason.SessionLocked, true, At(10));
            schedule.SetAway(AwayReason.SessionLocked, false, At(100));
            Assert(schedule.Paused && !schedule.Suspended, "Unlock changed the manual pause.");
            for (int second = 101; second <= 200; second++) Assert(!schedule.Tick(At(second)), "Unlock resumed a manually paused reminder.");
        });
        Test("Changing the interval while away keeps the new interval frozen", () =>
        {
            var schedule = new BreakSchedule(1, At(0));
            schedule.SetAway(AwayReason.ScreenSaver, true, At(10));
            schedule.SetInterval(2, At(20));
            Assert(schedule.UntilBreak(At(1000)) == At(120), "New interval counted down while away.");
            schedule.SetAway(AwayReason.ScreenSaver, false, At(1000));
            Assert(schedule.UntilBreak(At(1000)) == At(120), "New interval did not restart in full.");
        });
        Test("Invalid interval bounds rejected", () =>
        {
            foreach (int value in new[] { -1, 0, 241, int.MaxValue })
            {
                Assert(!Settings.IsValidInterval(value), "Invalid interval accepted.");
                bool rejected = false;
                try { _ = new BreakSchedule(value, At(0)); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Assert(rejected, "Scheduler accepted invalid interval.");
            }
            Assert(Settings.IsValidInterval(1) && Settings.IsValidInterval(240), "Valid boundaries rejected.");
        });
        Test("Settings persist and recover from corrupt or invalid input", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "MomentForYouTests-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "settings.json");
            try
            {
                Assert(Settings.Load(path).IntervalMinutes == 20, "Missing settings failed.");
                new Settings { IntervalMinutes = 37 }.Save(path);
                Assert(Settings.Load(path).IntervalMinutes == 37, "Settings roundtrip failed.");
                new Settings { IntervalMinutes = 1 }.Save(path);
                Assert(Settings.Load(path).IntervalMinutes == 1, "Settings update failed.");
                File.WriteAllText(path, "{\n  \"IntervalMinutes\": 37\n}");
                Assert(Settings.Load(path).IntervalMinutes == 37, "Settings from the earlier .NET 10 version could not be loaded.");
                File.WriteAllText(path, "{not json");
                Assert(Settings.Load(path).IntervalMinutes == 20, "Corrupt settings failed.");
                File.WriteAllText(path, "{\"IntervalMinutes\":0}");
                Assert(Settings.Load(path).IntervalMinutes == 20, "Invalid saved interval accepted.");
            }
            finally
            {
                // Only remove the two explicitly named test files in our unique directory.
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        });
        Test("Actual WPF overlay offers Done immediately", () =>
        {
            bool dismissed = false;
            var window = new BreakWindow(() => dismissed = true);
            try
            {
                var done = (Button)window.FindName("DoneButton");
                Assert(done.IsEnabled, "Done was not available immediately.");
                done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dismissed, "Done click did not request dismissal.");
                Assert(window.Topmost && !window.ShowInTaskbar && window.WindowStyle == WindowStyle.None, "Overlay window flags incorrect.");
            }
            finally { window.Dismiss(); }
        });
        Test("Clicking Project homepage requests Done before opening the browser", () =>
        {
            var actions = new List<string>();
            Uri? opened = null;
            var window = new BreakWindow(() => actions.Add("Done"), uri => { actions.Add("Open"); opened = uri; });
            try
            {
                var link = (Hyperlink)window.FindName("HomepageLink");
                var request = new RequestNavigateEventArgs(link.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent };
                link.RaiseEvent(request);
                Assert(request.Handled && string.Join(",", actions) == "Done,Open", "Homepage did not finish the break before opening.");
                Assert(opened?.AbsoluteUri == "https://github.com/stud0709/moment-for-you", "Wrong project homepage opened.");
            }
            finally { window.Dismiss(); }
        });
        Test("Connected monitor inventory has positive physical bounds", () =>
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            Assert(screens.Length > 0, "No screens found.");
            foreach (var screen in screens) Assert(screen.Bounds.Width > 0 && screen.Bounds.Height > 0, "Invalid display bounds.");
            results.Add("INFO " + screens.Length + " monitor(s) detected; physical coverage still needs manual confirmation.");
        });
        Test("Native overlay bounds match every monitor without showing windows", () =>
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var window = new BreakWindow(() => { });
                try
                {
                    window.PositionOn(screen);
                    var handle = new WindowInteropHelper(window).Handle;
                    Assert(GetWindowRect(handle, out var rect), "GetWindowRect failed.");
                    var expected = screen.Bounds;
                    Assert(rect.Left == expected.Left && rect.Top == expected.Top && rect.Right == expected.Right && rect.Bottom == expected.Bottom, "Physical bounds do not match " + screen.DeviceName);
                    Assert(!IsWindowVisible(handle), "Test revealed an overlay.");
                    results.Add($"INFO {screen.DeviceName}: {expected.Width}×{expected.Height} at ({expected.X},{expected.Y}), native bounds verified.");
                }
                finally { window.Dismiss(); }
            }
        });
        Test("Native tray icon, menu, pause and resume", () => ((App)Application.Current).VerifyTray());
        Test("Autostart quotes paths, runs quietly, and removes only its own entry", () =>
        {
            var keyPath = @"Software\MomentForYou.AutostartTest-" + Guid.NewGuid().ToString("N");
            var startup = new WindowsStartup(keyPath);
            try
            {
                Assert(!startup.IsEnabled, "Autostart should be off without an entry.");
                Assert(WindowsStartup.CreateCommand(@"C:\Program Files\A moment for you\A moment for you.exe") == "\"C:\\Program Files\\A moment for you\\A moment for you.exe\" --tray", "Autostart command is not quoted or quiet.");
                startup.SetEnabled(true);
                Assert(new WindowsStartup(keyPath).IsEnabled, "Autostart did not persist.");
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
                {
                    using var process = Process.GetCurrentProcess();
                    Assert((string?)key!.GetValue("A moment for you") == WindowsStartup.CreateCommand(process.MainModule!.FileName), "Autostart did not use the running executable.");
                    key.SetValue("Other app", "Keep this value");
                }
                startup.SetEnabled(false);
                Assert(!startup.IsEnabled, "Autostart was not disabled.");
                using var remaining = Registry.CurrentUser.OpenSubKey(keyPath);
                Assert((string?)remaining?.GetValue("Other app") == "Keep this value", "Disabling autostart changed another entry.");
            }
            finally { Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false); }
        });
        Test("Settings checkbox toggles autostart without enabling real Windows startup", () =>
        {
            var keyPath = @"Software\MomentForYou.AutostartTest-" + Guid.NewGuid().ToString("N");
            var startup = new WindowsStartup(keyPath);
            var window = new SettingsWindow((App)Application.Current, startup);
            try
            {
                var checkbox = (CheckBox)window.FindName("Autostart");
                Assert(checkbox.IsChecked == false, "Autostart checkbox should default to off.");
                checkbox.IsChecked = true;
                checkbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(startup.IsEnabled, "Checkbox did not enable autostart.");
                checkbox.IsChecked = false;
                checkbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(!startup.IsEnabled, "Checkbox did not disable autostart.");
            }
            finally { window.Hide(); Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false); }
        });
        Test("Version, author credit and homepage remain in the lower-right corner at common display scales", () =>
        {
            var window = new BreakWindow(() => { });
            try
            {
                var scene = (FrameworkElement)window.Content;
                var footer = (Border)window.FindName("Footer");
                var version = (Run)window.FindName("FooterVersion");
                var link = (Hyperlink)window.FindName("HomepageLink");
                Assert(version.Text == "v" + Assembly.GetExecutingAssembly().GetName().Version!.ToString(3), "Footer version does not match the executable.");
                foreach (var display in new[] { (1920, 1200, 1.25), (1920, 1080, 1.0), (1280, 800, 1.5), (1024, 768, 1.0) })
                {
                    var size = new Size(display.Item1 / display.Item3, display.Item2 / display.Item3);
                    VisualTreeHelper.SetRootDpi(scene, new DpiScale(display.Item3, display.Item3));
                    scene.Measure(size);
                    scene.Arrange(new Rect(new Point(), size));
                    scene.UpdateLayout();
                    var origin = footer.TranslatePoint(new Point(), scene);
                    Assert(footer.Visibility == Visibility.Visible && footer.ActualWidth > 0 && footer.ActualHeight > 0, "Footer has no visible layout.");
                    Assert(origin.X >= 0 && origin.Y >= 0, "Footer is clipped at the top or left.");
                    Assert(Math.Abs(size.Width - origin.X - footer.ActualWidth - 24) < 1, "Footer is not inset from the right edge.");
                    Assert(Math.Abs(size.Height - origin.Y - footer.ActualHeight - 24) < 1, "Footer is not inset from the bottom edge.");
                }
                Assert((link.Inlines.FirstInline as Run)?.Text == "Project homepage", "Project homepage label is incorrect.");
                Assert(link.NavigateUri.AbsoluteUri == "https://github.com/stud0709/moment-for-you", "Project homepage URL is incorrect.");
                Assert((version.NextInline as Run)?.Text == " · © stud0709 · ", "Author credit is missing.");
                Assert(((TextBlock)footer.Child).FontSize == 12, "Footer font size does not match the Take your time text.");
            }
            finally { window.Dismiss(); }
        });

        results.Add($"{results.FindAll(x => x.StartsWith("PASS ")).Count} passed; {failures} failed.");
        Directory.CreateDirectory("artifacts");
        File.WriteAllLines("artifacts/self-test-results.txt", results);
        return failures == 0 ? 0 : 1;
    }

    public static void RenderPreview(string path)
    {
        var window = new BreakWindow(() => { });
        try
        {
            RenderElement((FrameworkElement)window.Content, path, 1280, 800);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            // Match the user's 1920×1200 monitor at 125% scaling as well.
            RenderElement((FrameworkElement)window.Content, Path.Combine(directory, "break-screen-1920x1200.png"), 1920, 1200, 1.25);
            var settings = new SettingsWindow((App)Application.Current);
            try
            {
                settings.Refresh();
                var content = (FrameworkElement)settings.Content;
                var outer = new System.Windows.Controls.Border { Background = settings.Background, Child = null };
                settings.Content = null;
                outer.Child = content;
                RenderElement(outer, Path.Combine(directory, "settings-preview.png"), 490, 588);
            }
            finally { settings.Hide(); }
        }
        finally { window.Dismiss(); }
    }

    private static void RenderElement(FrameworkElement scene, string path, int width, int height, double scale = 1)
    {
        VisualTreeHelper.SetRootDpi(scene, new DpiScale(scale, scale));
        scene.Measure(new Size(width / scale, height / scale));
        scene.Arrange(new Rect(0, 0, width / scale, height / scale));
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
}
