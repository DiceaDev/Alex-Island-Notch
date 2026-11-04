using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AlexIsland.Core.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Letra sincronizada lista para mostrarse junto a la reproducción.</summary>
public sealed class SyncedLyrics
{
    public IReadOnlyList<LyricLine> Lines { get; }

    public SyncedLyrics(IReadOnlyList<LyricLine> lines) => Lines = lines;

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// Índice de la línea que corresponde a la posición dada, o -1 si todavía
    /// no empieza la primera. Búsqueda binaria: se llama varias veces por segundo.
    /// </summary>
    public int IndexAt(TimeSpan position)
    {
        int lo = 0, hi = Lines.Count - 1, result = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (Lines[mid].Time <= position)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }
}

public static partial class LrcParser
{
    /// <summary>Límites para que una respuesta anómala no consuma memoria.</summary>
    public const int MaxInputLength = 512 * 1024;
    public const int MaxLines = 5000;
    private const int MaxLineTextLength = 300;

    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex OffsetRegex();

    /// <summary>
    /// Interpreta el formato LRC: "[mm:ss.xx] texto". Admite varias marcas por
    /// línea, centésimas o milésimas y la etiqueta [offset:±ms]. Ignora el resto
    /// de etiquetas ([ar:], [ti:], ...) y las líneas sin marca de tiempo.
    /// </summary>
    public static SyncedLyrics Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc) || lrc.Length > MaxInputLength)
        {
            return new SyncedLyrics([]);
        }

        var lines = new List<LyricLine>();
        var offset = TimeSpan.Zero;

        try
        {
            foreach (var raw in lrc.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                var offsetMatch = OffsetRegex().Match(line);
                if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                {
                    // Un offset positivo adelanta la letra (aparece antes).
                    offset = TimeSpan.FromMilliseconds(-ms);
                    continue;
                }

                var stamps = new List<TimeSpan>();
                var index = 0;
                while (index < line.Length && line[index] == '[')
                {
                    var match = TimestampRegex().Match(line, index);
                    if (!match.Success || match.Index != index) break;

                    stamps.Add(ToTime(match));
                    index += match.Length;
                }

                if (stamps.Count == 0) continue;

                var text = CleanText(line[index..]);
                foreach (var stamp in stamps)
                {
                    lines.Add(new LyricLine(stamp, text));
                    if (lines.Count >= MaxLines) break;
                }

                if (lines.Count >= MaxLines) break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new SyncedLyrics([]);
        }

        if (offset != TimeSpan.Zero)
        {
            lines = lines
                .Select(l => l with { Time = l.Time + offset < TimeSpan.Zero ? TimeSpan.Zero : l.Time + offset })
                .ToList();
        }

        // Orden estable: líneas con la misma marca conservan su orden original.
        var ordered = lines.Select((l, i) => (l, i)).OrderBy(x => x.l.Time).ThenBy(x => x.i).Select(x => x.l).ToList();
        return new SyncedLyrics(ordered);
    }

    private static TimeSpan ToTime(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups[3].Success ? match.Groups[3].Value : "0";

        // "5" = 500 ms, "50" = 500 ms, "500" = 500 ms.
        var millis = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return new TimeSpan(0, 0, minutes, seconds, millis);
    }

    private static string CleanText(string text)
    {
        // Las letras vienen de un servicio externo: se quitan caracteres de control.
        var sb = new StringBuilder(Math.Min(text.Length, MaxLineTextLength));
        foreach (var c in text)
        {
            if (sb.Length >= MaxLineTextLength) break;
            if (!char.IsControl(c)) sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
