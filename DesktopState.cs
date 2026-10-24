using System.Runtime.InteropServices;

namespace MomentForYou;

internal static class DesktopState
{
    internal static bool TryGetScreenSaverRunning(out bool running) =>
        SystemParametersInfo(0x0072 /* SPI_GETSCREENSAVERRUNNING */, 0, out running, 0);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool result, uint flags);
}
