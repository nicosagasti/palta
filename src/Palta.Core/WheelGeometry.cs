namespace Palta.Core;

/// <summary>Sectores del anillo: el 0 centrado arriba y en sentido horario. Y crece hacia abajo, como en pantalla.</summary>
public static class WheelGeometry
{
    /// <returns>-1 en el centro, null fuera del anillo, o el índice del sector.</returns>
    public static int? SectorAt(double dx, double dy, int count, double inner, double outer)
    {
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < inner)
            return -1;
        if (r > outer || count <= 0)
            return null;
        var width = 360.0 / count;
        var angle = Degrees(dx, dy);
        return (int)Math.Floor((angle + width / 2) / width) % count;
    }

    public static (double X, double Y) SectorCenter(int index, int count, double radius)
    {
        var a = index * (2 * Math.PI / count);
        return (radius * Math.Sin(a), -radius * Math.Cos(a));
    }

    /// <summary>Ángulo en grados [0, 360) medido desde arriba en sentido horario.</summary>
    public static double Degrees(double dx, double dy)
    {
        var a = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }
}
