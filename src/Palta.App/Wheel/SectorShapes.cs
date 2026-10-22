using System.Windows;
using System.Windows.Media;

namespace Palta.App.Wheel;

/// <summary>Geometrías del anillo. Ángulos en grados desde arriba, en sentido horario.</summary>
internal static class SectorShapes
{
    public static Point At(Point center, double radius, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Sin(a), center.Y - radius * Math.Cos(a));
    }

    /// <summary>Porción de anillo entre <paramref name="from"/> y <paramref name="to"/>.</summary>
    public static Geometry Wedge(Point c, double inner, double outer, double from, double to)
    {
        var large = to - from > 180;
        var figure = new PathFigure { StartPoint = At(c, outer, from), IsClosed = true };
        figure.Segments.Add(new ArcSegment(At(c, outer, to), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(c, inner, to), true));
        figure.Segments.Add(new ArcSegment(At(c, inner, from), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    public static Geometry Ring(Point c, double inner, double outer)
    {
        var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, outer, outer), new EllipseGeometry(c, inner, inner));
        geometry.Freeze();
        return geometry;
    }
}
