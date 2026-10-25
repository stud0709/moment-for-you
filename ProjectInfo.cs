using System;
using System.ComponentModel;
using System.Diagnostics;

namespace MomentForYou;

internal static class ProjectInfo
{
    internal static string DisplayVersion => "v" + typeof(App).Assembly.GetName().Version!.ToString(3);

    internal static void OpenHomepage(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception) { System.Windows.Clipboard.SetText(uri.AbsoluteUri); }
    }
}
