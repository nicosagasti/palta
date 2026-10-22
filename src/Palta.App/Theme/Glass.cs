using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Palta.App.Theme;

/// <summary>
/// El material "Liquid Glass" que comparten la rueda, el portapapeles y los avisos:
/// fondo con vibrancia, tinte translúcido, brillo especular arriba, filo de luz y sombra difusa.
/// </summary>
internal static class Glass
{
    /// <summary>
    /// Vibrancia como en macOS: el fondo capturado (ya reducido) se satura y se aclara u oscurece
    /// un poco según el tema. Se hace una sola vez por apertura sobre una imagen chica: casi gratis.
    /// </summary>
    public static BitmapSource Vibrant(BitmapSource source, bool dark)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        const double saturation = 1.7;
        var (mixTo, mix) = dark ? (0.0, 0.18) : (255.0, 0.4);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            var l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            r = l + (r - l) * saturation;
            g = l + (g - l) * saturation;
            b = l + (b - l) * saturation;
            pixels[i] = Clamp(b + (mixTo - b) * mix);
            pixels[i + 1] = Clamp(g + (mixTo - g) * mix);
            pixels[i + 2] = Clamp(r + (mixTo - r) * mix);
            pixels[i + 3] = 255;
        }
        var result = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);

    // En el tema Windows (Look.Translucent == false) las mismas piezas dan el material de Windows 11:
    // superficie opaca como los menús y paneles flotantes, borde finito y nada de brillo.

    /// <summary>Tinte del vidrio: claro y lechoso en tema claro, ahumado en tema oscuro. En Windows, la superficie opaca.</summary>
    public static Color Tint(bool dark) => !Look.Translucent
        ? (dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9))
        : dark ? Color.FromArgb(0x8C, 0x1E, 0x1E, 0x23) : Color.FromArgb(0xC4, 0xF5, 0xF5, 0xF7);

    /// <summary>Tinte un poco más denso para chips y cápsulas que van encima del vidrio (en Windows, el relleno de los controles).</summary>
    public static Color Chip(bool dark) => !Look.Translucent
        ? (dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0, 0, 0))
        : dark ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF);

    /// <summary>Filo de luz: brillante arriba, casi nada a los costados, un rebote suave abajo. En Windows, un borde parejo y tenue.</summary>
    public static Brush Rim(bool dark)
    {
        if (!Look.Translucent)
            return Frozen(new SolidColorBrush(dark ? Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0, 0, 0)));
        var top = (byte)(dark ? 0x8C : 0xF0);
        var side = (byte)(dark ? 0x1A : 0x55);
        var bottom = (byte)(dark ? 0x38 : 0x90);
        return Frozen(new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb(top, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(side, 0xFF, 0xFF, 0xFF), 0.45),
                new GradientStop(Color.FromArgb(side, 0xFF, 0xFF, 0xFF), 0.7),
                new GradientStop(Color.FromArgb(bottom, 0xFF, 0xFF, 0xFF), 1),
            ], new Point(0.5, 0), new Point(0.5, 1)));
    }

    /// <summary>Línea finísima oscura por fuera del filo: le da definición sobre fondos claros.</summary>
    public static Brush Outline(bool dark) => Frozen(new SolidColorBrush(dark ? Color.FromArgb(0x66, 0, 0, 0) : Color.FromArgb(0x1C, 0, 0, 0)));

    /// <summary>Brillo especular: una luz que entra desde arriba y se apaga antes de la mitad. En Windows no hay.</summary>
    public static Brush Specular(bool dark) => !Look.Translucent ? Brushes.Transparent : Frozen(new LinearGradientBrush(
        [
            new GradientStop(Color.FromArgb((byte)(dark ? 0x26 : 0x70), 0xFF, 0xFF, 0xFF), 0),
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.38),
        ], new Point(0.5, 0), new Point(0.5, 1)));

    /// <summary>
    /// Sombra difusa debajo de un disco. <paramref name="edge"/> es dónde está el borde del disco
    /// (radio del disco / radio de la sombra). Desde ahí cae con una curva suave (smoothstep) que
    /// llega a cero sin pendiente, para que el final no se vea como otra circunferencia.
    /// </summary>
    public static Brush DiskShadow(bool dark, double edge)
    {
        var alpha = dark ? 0x80 : 0x38;
        var stops = new GradientStopCollection { new(Color.FromArgb((byte)alpha, 0, 0, 0), Math.Max(0, edge - 0.04)) };
        const int steps = 10;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var fade = 1 - t * t * (3 - 2 * t);
            stops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(alpha * fade), 0, 0, 0), edge - 0.04 + (1 - edge + 0.04) * t));
        }
        return Frozen(new RadialGradientBrush(stops));
    }

    /// <summary>
    /// Sombra suave para un rectángulo redondeado, hecha con capas de borde muy tenues
    /// (más barato que un DropShadowEffect, que se recalcula en cada cuadro de la animación).
    /// </summary>
    public static Grid RectShadow(double radius, double spread, bool dark)
    {
        var grid = new Grid { IsHitTestVisible = false };
        const int layers = 10;
        for (var i = layers; i >= 1; i--)
        {
            var t = i / (double)layers;
            var inset = spread * (1 - t);
            var alpha = (byte)((dark ? 0x16 : 0x09) * (1 - t * 0.85));
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(radius + spread * t),
                Margin = new Thickness(inset, inset + spread * 0.35, inset, inset - spread * 0.35),
                Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)),
            });
        }
        return grid;
    }

    /// <summary>Texto principal y secundario: casi negro / gris en claro, blanco / gris claro en oscuro.</summary>
    public static Color Text(bool dark) => dark ? Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE8, 0x1D, 0x1D, 0x1F);

    public static Color Secondary(bool dark) => dark ? Color.FromArgb(0x99, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x99, 0x3C, 0x3C, 0x43);

    public static Color Tertiary(bool dark) => dark ? Color.FromArgb(0x5C, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x5C, 0x3C, 0x3C, 0x43);

    /// <summary>Velo de una fila bajo el mouse.</summary>
    public static Color RowHover(bool dark) => dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0);

    /// <summary>Línea divisoria finita.</summary>
    public static Color Hairline(bool dark) => dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0);

    public static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}
