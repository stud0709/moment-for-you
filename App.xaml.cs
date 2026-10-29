using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MomentForYou;

public partial class App : Application
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<BreakWindow> overlays = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Settings settings = new();
    private SettingsWindow? settingsWindow;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Forms.ToolStripMenuItem? pauseItem;
    private Mutex? instance;
    private bool ownsInstance;
    private DesktopPowerMonitor? powerMonitor;
    private TimeSpan lastUiTick;
    public BreakSchedule Schedule { get; private set; } = new(20, TimeSpan.Zero);
    public TimeSpan Now => clock.Elapsed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test")) { Shutdown(SelfTests.Run()); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--render-preview") { SelfTests.RenderPreview(e.Args[1]); Shutdown(); return; }
        // Keep the original mutex so an older copy cannot run reminders alongside this one.
        instance = new Mutex(true, "Local\\EyeProtect.Reminders", out ownsInstance);
        if (!ownsInstance) { MessageBox.Show("A moment for you is already running, or an older Eye Protect copy is still open. Right-click its tray icon to change the interval or exit.", "A moment for you"); Shutdown(); return; }

        settings = Settings.Load();
        Schedule = new BreakSchedule(settings.IntervalMinutes, Now);
        UpdateDesktopState();
        CreateTray();
        timer.Tick += OnTick;
        lastUiTick = Now;
        timer.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        DispatcherUnhandledException += (_, args) =>
        {
            // Release every screen if an unexpected UI error occurs.
            args.Handled = true;
            CloseOverlays();
            MessageBox.Show("A moment for you encountered an error and will close.\n\n" + args.Exception.Message, "A moment for you");
            Shutdown(1);
        };
        // Queue outside the native window procedure before closing WPF windows.
        powerMonitor = new DesktopPowerMonitor((reason, active) =>
            Dispatcher.InvokeAsync(() => SetAway(reason, active)));
        if (!e.Args.Contains("--tray")) ShowSettings();
    }

    private void CreateTray()
    {
        trayIcon = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("A moment for you").Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Set interval…", null, (_, _) => ShowSettings());
        menu.Items.Add("Take a break now", null, (_, _) => StartBreak());
        pauseItem = new Forms.ToolStripMenuItem("Pause reminders", null, (_, _) => TogglePause());
        menu.Items.Add(pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "A moment for you", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
    }

    internal void VerifyTray()
    {
        // Called only by the short self-test run. Never starts the reminder timer.
        CreateTray();
        if (tray?.Visible != true || tray.Icon == null || tray.ContextMenuStrip?.Items.Count != 7)
            throw new InvalidOperationException("Tray icon or menu was not created.");
        ((Forms.ToolStripMenuItem)tray.ContextMenuStrip.Items[4]).PerformClick();
        if (!Schedule.Paused || pauseItem?.Text != "Resume reminders")
            throw new InvalidOperationException("Tray pause command failed.");
        pauseItem.PerformClick();
        if (Schedule.Paused || pauseItem.Text != "Pause reminders")
            throw new InvalidOperationException("Tray resume command failed.");
        tray.Visible = false;
        tray.ContextMenuStrip.Dispose();
        tray.Dispose();
        tray = null;
        trayIcon?.Dispose();
        trayIcon = null;
    }

    public void ShowSettings()
    {
        if (Schedule.InBreak) return;
        settingsWindow ??= new SettingsWindow(this);
        settingsWindow.Refresh();
        settingsWindow.Show();
        settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }

    public void SaveInterval(int minutes)
    {
        var updated = new Settings { IntervalMinutes = minutes };
        updated.Save();
        settings = updated;
        Schedule.SetInterval(minutes, Now);
        settingsWindow?.Refresh();
    }

    public void TogglePause()
    {
        if (Schedule.InBreak) FinishBreak();
        Schedule.SetPaused(!Schedule.Paused, Now);
        if (pauseItem != null) pauseItem.Text = Schedule.Paused ? "Resume reminders" : "Pause reminders";
        settingsWindow?.Refresh();
    }

    public void StartBreak()
    {
        UpdateDesktopState();
        if (Schedule.InBreak || Schedule.Suspended) return;
        settingsWindow?.Hide();
        Schedule.StartBreak();
        try { OpenOverlays(); }
        catch { CloseOverlays(); Schedule.FinishBreak(Now); throw; }
    }

    private void OpenOverlays()
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new BreakWindow(FinishBreak);
            overlays.Add(overlay);
            overlay.ShowOn(screen);
        }
        // Put keyboard focus on the monitor containing the pointer.
        var cursorScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var screens = Forms.Screen.AllScreens;
        var index = Array.FindIndex(screens, x => x.DeviceName == cursorScreen.DeviceName);
        if (index >= 0 && index < overlays.Count) overlays[index].Activate();
    }

    public void FinishBreak()
    {
        if (!Schedule.FinishBreak(Now)) return;
        CloseOverlays();
        settingsWindow?.Refresh();
    }

    private void CloseOverlays()
    {
        foreach (var overlay in overlays.ToArray()) overlay.Dismiss();
        overlays.Clear();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Now;
        if (now - lastUiTick > TimeSpan.FromSeconds(30))
        {
            // Also recover when firmware omits a sleep/power notification.
            FinishBreak();
            Schedule.Reset(now);
        }
        lastUiTick = now;
        UpdateDesktopState();
        CheckBreakVisibility();
        if (Schedule.Tick(Now)) StartBreak();
        settingsWindow?.Refresh();
        if (tray != null)
        {
            var remaining = Schedule.UntilBreak(Now);
            tray.Text = Schedule.InBreak ? "A moment for you · Take a break" : Schedule.Paused ? "A moment for you · Paused" : Schedule.Suspended ? "A moment for you · Paused while away" : $"A moment for you · Next break in {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        HandleDisplayChange();
    });

    private void HandleDisplayChange()
    {
        // Lid and docking changes can invalidate WPF surfaces or move focus to
        // another app. Release all windows instead of recreating a hidden blocker.
        FinishBreak();
        Schedule.Reset(Now);
    }

    private void CheckBreakVisibility()
    {
        if (!Schedule.InBreak) return;
        var foreground = GetForegroundWindow();
        if (!overlays.Any(window => window.IsVisible && window.WindowState != WindowState.Minimized &&
            new WindowInteropHelper(window).Handle == foreground))
            FinishBreak();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        if (e.Reason == SessionSwitchReason.SessionLock) SetAway(AwayReason.SessionLocked, true);
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            // Refresh the screensaver flag before clearing the lock flag: both
            // conditions must be gone before the countdown resumes.
            UpdateDesktopState();
            SetAway(AwayReason.SessionLocked, false);
        }
    });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        if (e.Mode == PowerModes.Suspend) SetAway(AwayReason.Sleep, true);
        if (e.Mode == PowerModes.Resume)
        {
            UpdateDesktopState();
            SetAway(AwayReason.Sleep, false);
        }
    });

    private void UpdateDesktopState()
    {
        // A failed query leaves the previous state intact instead of resuming
        // reminders by mistake on an inaccessible desktop.
        if (DesktopState.TryGetScreenSaverRunning(out bool running))
            SetAway(AwayReason.ScreenSaver, running);
    }

    private void SetAway(AwayReason reason, bool active)
    {
        if (active && !Schedule.Suspended) FinishBreak();
        Schedule.SetAway(reason, active, Now);
    }

    internal void VerifyDesktopRecovery()
    {
        // Exercise real window cleanup using hidden HWNDs; never cover the desktop.
        var originalSchedule = Schedule;
        var originalTick = lastUiTick;
        void Verify(Action transition, string name)
        {
            Schedule = new BreakSchedule(20, Now);
            for (int i = 0; i < 2; i++)
            {
                var window = new BreakWindow(FinishBreak);
                new WindowInteropHelper(window).EnsureHandle();
                overlays.Add(window);
            }
            Schedule.StartBreak();
            transition();
            if (Schedule.InBreak || overlays.Count != 0)
                throw new InvalidOperationException(name + " left a blocker open.");
            if (Schedule.UntilBreak(Now) < TimeSpan.FromMinutes(19))
                throw new InvalidOperationException(name + " failed to restart the interval.");
        }
        try
        {
            foreach (var reason in new[] { AwayReason.LidClosed, AwayReason.DisplayOff, AwayReason.Sleep, AwayReason.SessionLocked })
                Verify(() =>
                {
                    SetAway(reason, true);
                    if (!Schedule.Suspended) throw new InvalidOperationException(reason + " did not suspend reminders.");
                    SetAway(reason, false);
                }, reason.ToString());
            Verify(HandleDisplayChange, "Monitor layout change");
            Verify(CheckBreakVisibility, "Hidden/unfocused break");
            Verify(() => { lastUiTick = Now - TimeSpan.FromMinutes(2); OnTick(this, EventArgs.Empty); }, "Missed sleep notification");
        }
        finally
        {
            CloseOverlays();
            Schedule = originalSchedule;
            lastUiTick = originalTick;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        timer.Stop();
        powerMonitor?.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        CloseOverlays();
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        trayIcon?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }

    private static System.Drawing.Icon CreateIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(28, 49, 39));
        graphics.FillEllipse(background, 0, 0, 31, 31);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(195, 232, 170), 2);
        graphics.DrawArc(pen, 4, 8, 24, 17, 195, 150);
        graphics.DrawArc(pen, 4, 6, 24, 17, 15, 150);
        graphics.DrawEllipse(pen, 12, 11, 8, 9);
        var handle = bitmap.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
