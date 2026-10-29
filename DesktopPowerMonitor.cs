using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MomentForYou;

// Independent of settings and break windows, which may all be closed or hidden.
internal sealed class DesktopPowerMonitor : IDisposable
{
    internal static readonly Guid LidState = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    internal static readonly Guid DisplayState = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private readonly HwndSource source;
    private readonly Action<AwayReason, bool> changed;
    private IntPtr lidRegistration;
    private IntPtr displayRegistration;
    internal IntPtr Handle => source.Handle;

    internal DesktopPowerMonitor(Action<AwayReason, bool> changed)
    {
        this.changed = changed;
        source = new HwndSource(new HwndSourceParameters("A moment for you power events")
        {
            WindowStyle = 0, Width = 0, Height = 0
        });
        source.AddHook(OnMessage);
        try
        {
            lidRegistration = Register(LidState);
            displayRegistration = Register(DisplayState);
        }
        catch { Dispose(); throw; }
    }

    private IntPtr Register(Guid setting)
    {
        var registration = RegisterPowerSettingNotification(Handle, ref setting, 0);
        if (registration == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        return registration;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0218 /* WM_POWERBROADCAST */ || wParam.ToInt64() != 0x8013 /* PBT_POWERSETTINGCHANGE */ || lParam == IntPtr.Zero)
            return IntPtr.Zero;
        // POWERBROADCAST_SETTING contains a GUID, DWORD length, then the data.
        if (Marshal.ReadInt32(lParam, 16) < 4) return IntPtr.Zero;
        var setting = (Guid)Marshal.PtrToStructure(lParam, typeof(Guid));
        int value = Marshal.ReadInt32(lParam, 20);
        if (setting == LidState && (value == 0 || value == 1))
            changed(AwayReason.LidClosed, value == 0);
        else if (setting == DisplayState && value >= 0 && value <= 2)
            changed(AwayReason.DisplayOff, value == 0); // Dimmed is still usable.
        else return IntPtr.Zero;
        handled = true;
        return new IntPtr(1);
    }

    public void Dispose()
    {
        if (lidRegistration != IntPtr.Zero) UnregisterPowerSettingNotification(lidRegistration);
        if (displayRegistration != IntPtr.Zero) UnregisterPowerSettingNotification(displayRegistration);
        lidRegistration = displayRegistration = IntPtr.Zero;
        source.RemoveHook(OnMessage);
        source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr registration);
}
