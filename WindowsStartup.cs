using System;
using System.IO;
using Microsoft.Win32;

namespace MomentForYou;

internal sealed class WindowsStartup
{
    private const string ValueName = "A moment for you";
    private readonly string keyPath;

    internal WindowsStartup(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
    { this.keyPath = keyPath; }

    internal bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
        }
    }

    internal static string CreateCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            executable.Contains('"'))
            throw new InvalidOperationException("Enable autostart from the published app executable.");
        var command = "\"" + executable + "\" --tray";
        if (command.Length > 260)
            throw new InvalidOperationException("Move the app to a shorter folder path before enabling autostart.");
        return command;
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var command = CreateCommand(Environment.ProcessPath ??
                throw new InvalidOperationException("Cannot find the app executable."));
            using var key = Registry.CurrentUser.CreateSubKey(keyPath) ??
                throw new IOException("Cannot access the Windows startup setting.");
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
