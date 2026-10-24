using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            var settings = new SettingsWindow((App)Application.Current);
            try
            {
                settings.Refresh();
                var content = (FrameworkElement)settings.Content;
                var outer = new System.Windows.Controls.Border { Background = settings.Background, Child = null };
                settings.Content = null;
                outer.Child = content;
                RenderElement(outer, Path.Combine(directory, "settings-preview.png"), 490, 538);
            }
            finally { settings.Hide(); }
        }
        finally { window.Dismiss(); }
    }

    private static void RenderElement(FrameworkElement scene, string path, int width, int height)
    {
        VisualTreeHelper.SetRootDpi(scene, new DpiScale(1, 1));
        scene.Measure(new Size(width, height));
        scene.Arrange(new Rect(0, 0, width, height));
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
}
