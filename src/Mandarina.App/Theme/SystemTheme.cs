using System.Windows.Media;
using Microsoft.Win32;

namespace Mandarina.App.Theme;

/// <summary>Colores derivados del modo claro/oscuro y del color de acento de Windows.</summary>
internal sealed record Palette(bool Dark, Color Text, Color SubText, Color Tint, Color CenterTint, Color Accent, Color Danger)
{
    public static Palette Current()
    {
        var dark = IsDark();
        var accent = AccentColor() ?? Color.FromRgb(0xFF, 0x8A, 0x3D);
        return dark
            ? new Palette(true, Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x3C, 0x14, 0x14, 0x1A), Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), accent, Color.FromRgb(0xFF, 0x6B, 0x6B))
            : new Palette(false, Color.FromArgb(0xE6, 0x10, 0x10, 0x14), Color.FromArgb(0x99, 0x10, 0x10, 0x14),
                Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), accent, Color.FromRgb(0xE0, 0x3E, 0x3E));
    }

    /// <summary>Resaltado del sector: luz blanca apenas teñida con el acento, como vidrio iluminado.</summary>
    public Color Hover
    {
        get
        {
            static byte Mix(byte a, byte b) => (byte)(a + (b - a) * 0.3);
            return Color.FromArgb((byte)(Dark ? 0x46 : 0x8C), Mix(0xFF, Accent.R), Mix(0xFF, Accent.G), Mix(0xFF, Accent.B));
        }
    }

    private static bool IsDark()
    {
        // Para las capturas de diseño: MANDARINA_THEME=light|dark fuerza un tema.
        switch (Environment.GetEnvironmentVariable("MANDARINA_THEME"))
        {
            case "light": return false;
            case "dark": return true;
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>HKCU\...\DWM\AccentColor está en formato ABGR.</summary>
    private static Color? AccentColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is not int abgr)
            return null;
        var v = unchecked((uint)abgr);
        return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static readonly FontFamily UiFont = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
}
