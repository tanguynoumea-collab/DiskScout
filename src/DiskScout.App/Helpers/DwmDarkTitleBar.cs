using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DiskScout.Helpers;

public static partial class DwmDarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        int useDark = 1;
        _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }
}
