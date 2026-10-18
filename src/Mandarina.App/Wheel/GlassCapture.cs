using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mandarina.App.Native;

namespace Mandarina.App.Wheel;

/// <summary>
/// Foto de lo que hay detrás de la rueda, tomada una sola vez al abrirla y reducida a 1/4:
/// después se muestra desenfocada. Mucho más barato que desenfocar en vivo.
/// </summary>
internal static class GlassCapture
{
    public const double Downscale = 0.25;

    public static BitmapSource? Capture(int x, int y, int width, int height)
    {
        var screen = Win32.GetDC(IntPtr.Zero);
        var mem = Win32.CreateCompatibleDC(screen);
        var bitmap = Win32.CreateCompatibleBitmap(screen, width, height);
        var old = Win32.SelectObject(mem, bitmap);
        try
        {
            if (!Win32.BitBlt(mem, 0, 0, width, height, screen, x, y, Win32.SRCCOPY))
                return null;
            Win32.SelectObject(mem, old);
            var full = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var small = new TransformedBitmap(full, new ScaleTransform(Downscale, Downscale));
            small.Freeze();
            return small;
        }
        catch (Exception e)
        {
            Core.Log.Error("Captura del fondo", e);
            return null;
        }
        finally
        {
            Win32.DeleteObject(bitmap);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
