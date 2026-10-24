using System;
using System.IO;
using System.Text.Json;

namespace MomentForYou;

public sealed class Settings
{
    public int IntervalMinutes { get; set; } = 20;
    public static bool IsValidInterval(int value) => value >= 1 && value <= 240;
    // Retain the settings location so rebranding preserves existing intervals.
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeProtect", "settings.json");

    public static Settings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? FilePath));
            return settings != null && IsValidInterval(settings.IntervalMinutes) ? settings : new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new Settings(); }
    }

    public void Save(string? path = null)
    {
        if (!IsValidInterval(IntervalMinutes)) throw new ArgumentOutOfRangeException(nameof(IntervalMinutes));
        var destination = path ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, destination, true);
    }
}

[Flags]
public enum AwayReason { None = 0, SessionLocked = 1, ScreenSaver = 2, Sleep = 4 }

// All deadlines use monotonic time, so wall-clock adjustments cannot trigger a break.
public sealed class BreakSchedule
{
    public int IntervalMinutes { get; private set; }
    public bool Paused { get; private set; }
    public bool InBreak { get; private set; }
    public AwayReason AwayReasons { get; private set; }
    public bool Suspended => AwayReasons != AwayReason.None;
    private TimeSpan nextBreak;
    private TimeSpan lastTick;
    private TimeSpan remainingWhileAway;

    public BreakSchedule(int intervalMinutes, TimeSpan now) { SetInterval(intervalMinutes, now); }
    public void SetInterval(int minutes, TimeSpan now)
    {
        if (!Settings.IsValidInterval(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes));
        IntervalMinutes = minutes;
        Reset(now);
    }
    public void Reset(TimeSpan now)
    {
        remainingWhileAway = TimeSpan.FromMinutes(IntervalMinutes);
        nextBreak = now + remainingWhileAway;
        lastTick = now;
    }
    public void SetPaused(bool paused, TimeSpan now) { Paused = paused; Reset(now); }
    public void SetAway(AwayReason reason, bool active, TimeSpan now)
    {
        bool wasSuspended = Suspended;
        AwayReasons = active ? AwayReasons | reason : AwayReasons & ~reason;
        if (wasSuspended == Suspended) return;
        if (Suspended) remainingWhileAway = nextBreak > now ? nextBreak - now : TimeSpan.Zero;
        else Reset(now);
        lastTick = now;
    }
    public bool Tick(TimeSpan now)
    {
        if (Suspended) { lastTick = now; return false; }
        // A suspended PC or stalled session gets a fresh interval on return.
        if (now - lastTick > TimeSpan.FromSeconds(30) && !InBreak) Reset(now);
        lastTick = now;
        return !Paused && !InBreak && now >= nextBreak;
    }
    public void StartBreak() { InBreak = true; }
    public bool FinishBreak(TimeSpan now)
    {
        if (!InBreak) return false;
        InBreak = false;
        Reset(now);
        return true;
    }
    public TimeSpan UntilBreak(TimeSpan now) => Suspended ? remainingWhileAway : nextBreak > now ? nextBreak - now : TimeSpan.Zero;
}
