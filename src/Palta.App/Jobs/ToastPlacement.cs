using System.Windows;
using Microsoft.Win32;
using Palta.App.Native;

namespace Palta.App.Jobs;

/// <summary>
/// Dónde y cómo aparecen los avisos. Si está activo el mod de Windhawk "notifications-placement",
/// se copia su configuración (monitor, esquina, distancia al borde y dirección de entrada) para que
/// los avisos de Palta se comporten igual que las notificaciones de Windows. Si no, arriba a la
/// derecha del monitor principal.
/// </summary>
internal sealed record ToastPlacement(
    int Monitor, string Horizontal, string Vertical, double HorizontalDistance, double VerticalDistance, string Animation)
{
    private const string ModKey = @"SOFTWARE\Windhawk\Engine\Mods\notifications-placement";

    /// <summary>Sin el mod: arriba a la derecha del monitor principal (-1).</summary>
    public static readonly ToastPlacement Default = new(-1, "right", "top", 0, 0, "automatic");

    /// <summary>Se lee en cada aviso nuevo: es barato y así sigue los cambios del mod sin reiniciar.</summary>
    public static ToastPlacement Current()
    {
        try
        {
            using var mod = Registry.LocalMachine.OpenSubKey(ModKey);
            if (mod is null || mod.GetValue("Disabled") is int and not 0)
                return Default;
            using var s = mod.OpenSubKey("Settings");
            if (s is null)
                return Default;
            return new ToastPlacement(
                s.GetValue("monitor") is int m ? m : Default.Monitor,
                s.GetValue("horizontalPlacement") as string ?? Default.Horizontal,
                s.GetValue("verticalPlacement") as string ?? Default.Vertical,
                s.GetValue("horizontalDistanceFromScreenEdge") is int h ? h : 0,
                s.GetValue("verticalDistanceFromScreenEdge") is int v ? v : 0,
                s.GetValue("animationDirection") as string ?? Default.Animation);
        }
        catch (Exception e)
        {
            Core.Log.Error("Leer la ubicación de las notificaciones de Windhawk", e);
            return Default;
        }
    }

    /// <summary>
    /// Área de trabajo del monitor elegido, en píxeles físicos, y su escala. Igual que el mod:
    /// el número es el orden de <c>EnumDisplayMonitors</c> (que no siempre coincide con el de
    /// Configuración de Windows) y 0 es el monitor donde está el mouse.
    /// </summary>
    public (System.Drawing.Rectangle Area, double Scale) Screen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = Monitor switch
        {
            0 => System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position),
            > 0 when Monitor <= screens.Length => screens[Monitor - 1],
            _ => System.Windows.Forms.Screen.PrimaryScreen!,
        };
        var area = screen.WorkingArea;
        return (area, Win32.ScaleAt(area.Left + area.Width / 2, area.Top + area.Height / 2));
    }

    /// <summary>Desde dónde entra el aviso, como vector unitario (x, y). "automatic" entra desde el borde más cercano.</summary>
    public Vector EnterFrom() => Animation switch
    {
        "fromLeft" => new Vector(-1, 0),
        "fromRight" => new Vector(1, 0),
        "fromTop" => new Vector(0, -1),
        "fromBottom" => new Vector(0, 1),
        _ => Horizontal switch
        {
            "left" => new Vector(-1, 0),
            "right" => new Vector(1, 0),
            _ => new Vector(0, Vertical == "bottom" ? 1 : -1),
        },
    };

    /// <summary>Se va hacia el borde de la pantalla más cercano, como al descartar una notificación de Windows.</summary>
    public Vector LeaveTo() => Horizontal switch
    {
        "left" => new Vector(-1, 0),
        "right" => new Vector(1, 0),
        _ => new Vector(0, Vertical == "bottom" ? 1 : -1),
    };
}
