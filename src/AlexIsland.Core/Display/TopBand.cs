namespace AlexIsland.Core.Display;

/// <summary>
/// Franja superior que la isla reserva en cada monitor para que las ventanas
/// maximizadas empiecen debajo de ella y no queden tapadas.
/// </summary>
public static class TopBand
{
    /// <summary>Alto de la franja en píxeles físicos: el del notch cerrado, con su escala.</summary>
    public static int HeightFor(int notchHeight, double notchScale, double dpiScale) =>
        Math.Max(1, (int)Math.Ceiling(notchHeight * notchScale * dpiScale));

    /// <summary>
    /// Color predominante de una fila de píxeles (0xRRGGBB) leída justo debajo de la franja,
    /// para pintarla igual que la barra de la ventana que tiene debajo. Se ignora la mitad
    /// central, donde puede estar la isla abierta. Devuelve null si no hay píxeles.
    /// </summary>
    public static int? DominantColor(ReadOnlySpan<int> row)
    {
        if (row.IsEmpty) return null;

        var quarter = row.Length / 4;
        var counts = new Dictionary<int, (int Count, long R, long G, long B, int Order)>();
        var step = Math.Max(1, row.Length / 400);

        for (var x = 0; x < row.Length; x += step)
        {
            // Con filas muy cortas se usa todo; si no, solo los extremos.
            if (row.Length >= 8 && x >= quarter && x < row.Length - quarter) continue;

            var rgb = row[x] & 0xFFFFFF;
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            // Tonos casi iguales (antialiasing, degradados suaves) cuentan como uno.
            var key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);

            counts[key] = counts.TryGetValue(key, out var c)
                ? (c.Count + 1, c.R + r, c.G + g, c.B + b, c.Order)
                : (1, r, g, b, counts.Count);
        }

        if (counts.Count == 0) return null;

        var best = counts.Values.OrderByDescending(c => c.Count).ThenBy(c => c.Order).First();
        return (int)((best.R / best.Count) << 16 | (best.G / best.Count) << 8 | (best.B / best.Count));
    }
}
