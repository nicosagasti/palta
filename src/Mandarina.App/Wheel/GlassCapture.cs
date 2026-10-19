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

    /// <summary>
    /// Captura lo que hay detrás de una ventana que ya está visible (p. ej. después de moverla):
    /// la excluye de la captura solo durante ese instante, así no aparece en su propio fondo
    /// y no hace falta esconderla (sin parpadeo).
    /// </summary>
    public static BitmapSource? CaptureBehind(IntPtr hwnd, int x, int y, int width, int height)
    {
        var excluded = Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
        try
        {
            return excluded ? Capture(x, y, width, height) : null;
        }
        finally
        {
            if (excluded)
                Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_NONE);
        }
    }

    public static BitmapSource? Capture(int x, int y, int width, int height, double downscale = Downscale)
    {
        var screen = Win32.GetDC(IntPtr.Zero);
        var mem = Win32.CreateCompatibleDC(screen);
        var bitmap = Win32.CreateCompatibleBitmap(screen, width, height);
        var old = Win32.SelectObject(mem, bitmap);
        try
        {
            if (bitmap == IntPtr.Zero || !Win32.BitBlt(mem, 0, 0, width, height, screen, x, y, Win32.SRCCOPY))
                return null;
            Win32.SelectObject(mem, old);
            old = IntPtr.Zero;
            var full = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            if (downscale >= 1)
            {
                full.Freeze();
                return full;
            }
            var small = new TransformedBitmap(full, new ScaleTransform(downscale, downscale));
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
            // Un bitmap seleccionado en un DC no se puede borrar: primero se lo saca.
            if (old != IntPtr.Zero)
                Win32.SelectObject(mem, old);
            Win32.DeleteObject(bitmap);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
