using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Mandarina.App.Native;

namespace Mandarina.App.Shell;

/// <summary>Material nativo de Windows 11 (Acrylic / Mica) para ventanas rectangulares.</summary>
internal static class Backdrop
{
    public static void Apply(Window window, int backdropType, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = Colors.Transparent;
        var margins = new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Win32.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var darkValue = dark ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
        var corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
    }
}
