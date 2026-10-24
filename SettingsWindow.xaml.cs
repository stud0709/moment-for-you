using System;
using System.ComponentModel;
using System.Windows;

namespace MomentForYou;

public partial class SettingsWindow : Window
{
    private readonly App app;
    public SettingsWindow(App app)
    {
        InitializeComponent();
        this.app = app;
        Interval.Text = app.Schedule.IntervalMinutes.ToString();
    }
    public void Refresh()
    {
        var schedule = app.Schedule;
        Status.Text = schedule.InBreak ? "Time for a break" : schedule.Paused ? "Reminders are paused" : "Reminders are on";
        var remaining = schedule.UntilBreak(app.Now);
        NextBreak.Text = schedule.InBreak ? "Look away, move around, and breathe." : schedule.Paused ? "Resume whenever you are ready." : $"Next break in {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
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
    private void OnPause(object sender, RoutedEventArgs e) => app.TogglePause();
    private void OnClosing(object? sender, CancelEventArgs e) { e.Cancel = true; Hide(); }
}
