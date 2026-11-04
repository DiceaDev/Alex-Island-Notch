using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AlexIsland.App;

/// <summary>Silueta del notch: orejas cóncavas arriba y esquinas redondeadas abajo.</summary>
internal static class NotchGeometry
{
    public const double EarRadius = 12.0;

    public static PathGeometry Create(double width, double height, double bottomRadius)
    {
        // En el notch mínimo la altura es muy baja: los radios se limitan para
        // que la silueta no se cruce consigo misma.
        var earRadius = Math.Min(EarRadius, height / 2.0);
        bottomRadius = Math.Min(bottomRadius, height / 2.0);
        var bodyWidth = width - 2 * earRadius;
        if (bodyWidth <= 0) bodyWidth = width;

        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        // 1. Oreja cóncava izquierda (fillet fluido hacia el marco superior)
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(earRadius * 0.552, 0),
            Point2 = new Point(earRadius, earRadius * 0.448),
            Point3 = new Point(earRadius, earRadius)
        });

        // 2. Borde vertical izquierdo
        figure.Segments.Add(new LineSegment { Point = new Point(earRadius, height - bottomRadius) });

        // 3. Esquina inferior izquierda convexa
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(earRadius, height - bottomRadius * 0.448),
            Point2 = new Point(earRadius + bottomRadius * 0.448, height),
            Point3 = new Point(earRadius + bottomRadius, height)
        });

        // 4. Borde inferior horizontal
        figure.Segments.Add(new LineSegment { Point = new Point(earRadius + bodyWidth - bottomRadius, height) });

        // 5. Esquina inferior derecha convexa
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(earRadius + bodyWidth - bottomRadius * 0.448, height),
            Point2 = new Point(earRadius + bodyWidth, height - bottomRadius * 0.448),
            Point3 = new Point(earRadius + bodyWidth, height - bottomRadius)
        });

        // 6. Borde vertical derecho
        figure.Segments.Add(new LineSegment { Point = new Point(earRadius + bodyWidth, earRadius) });

        // 7. Oreja cóncava derecha (fillet fluido hacia el marco superior)
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(earRadius + bodyWidth, earRadius * 0.448),
            Point2 = new Point(earRadius + bodyWidth + earRadius * 0.448, 0),
            Point3 = new Point(earRadius + bodyWidth + earRadius, 0)
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
