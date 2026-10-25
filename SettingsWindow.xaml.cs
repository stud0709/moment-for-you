using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Navigation;

namespace MomentForYou;

public partial class SettingsWindow : Window
{
    private readonly App app;
    private readonly WindowsStartup startup;
    private bool startupEnabled;

    public SettingsWindow(App app) : this(app, new WindowsStartup()) { }

    internal SettingsWindow(App app, WindowsStartup startup)
    {
        InitializeComponent();
        this.app = app;
        this.startup = startup;
        FooterVersion.Text = ProjectInfo.DisplayVersion;
        Interval.Text = app.Schedule.IntervalMinutes.ToString();
        try { Autostart.IsChecked = startupEnabled = startup.IsEnabled; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Autostart.IsEnabled = false;
            Feedback.Text = "The Windows autostart setting is unavailable.";
        }
    }
    public void Refresh()
    {
        var schedule = app.Schedule;
        Status.Text = schedule.InBreak ? "Time for a break" : schedule.Paused ? "Reminders are paused" : schedule.Suspended ? "Reminders are paused while you are away" : "Reminders are on";
        var remaining = schedule.UntilBreak(app.Now);
        NextBreak.Text = schedule.InBreak ? "Look away, move around, and breathe." : schedule.Paused ? "Resume whenever you are ready." : $"Next break in {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}" + (schedule.Suspended ? " (paused)" : "");
        PauseButton.Content = schedule.Paused ? "Resume reminders" : "Pause reminders";
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Interval.Text, out int minutes) || !Settings.IsValidInterval(minutes))
        { Feedback.Text = "Enter a whole number between 1 and 240."; return; }
        try { app.SaveInterval(minutes); Feedback.Text = $"Saved. Reminders every {minutes} minute{(minutes == 1 ? "" : "s")}."; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Feedback.Text = "Could not save settings. Check your folder permissions."; }
    }
    private void OnBreakNow(object sender, RoutedEventArgs e) => app.StartBreak();
    private void OnProjectHomepage(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        ProjectInfo.OpenHomepage(e.Uri);
    }
    private void OnAutostart(object sender, RoutedEventArgs e)
    {
        bool enabled = Autostart.IsChecked == true;
        try
        {
            startup.SetEnabled(enabled);
            startupEnabled = enabled;
            Feedback.Text = enabled ? "Autostart enabled. Starts quietly when you sign in." : "Autostart disabled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            Autostart.IsChecked = startupEnabled;
            Feedback.Text = ex is InvalidOperationException ? ex.Message : "Could not change the Windows autostart setting.";
        }
    }
    private void OnPause(object sender, RoutedEventArgs e) => app.TogglePause();
    private void OnClosing(object? sender, CancelEventArgs e) { e.Cancel = true; Hide(); }
}
