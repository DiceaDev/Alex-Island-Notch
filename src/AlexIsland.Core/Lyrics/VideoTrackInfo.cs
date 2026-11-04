using System.Text.RegularExpressions;

namespace AlexIsland.Core.Lyrics;

/// <summary>Título y artista listos para buscar la letra.</summary>
/// <param name="FromVideo">Se dedujeron de los datos de un video (YouTube): la duración puede no coincidir exacta.</param>
public sealed record LyricsQuery(string Title, string Artist, bool FromVideo);

/// <summary>
/// En YouTube (en el navegador) Windows recibe los datos del video, no de la
/// canción: el título suele ser "Artista - Canción (Official Video)" y el
/// "artista" es el canal ("ArtistaVEVO", "Artista - Topic"). Esto deduce el
/// título y el artista reales para una segunda búsqueda de la letra.
/// </summary>
public static partial class VideoTrackInfo
{
    /// <summary>
    /// Devuelve la consulta deducida, o null si no hay nada que limpiar (los datos
    /// ya parecen de una canción, como los de Spotify o YouTube Music).
    /// </summary>
    public static LyricsQuery? TryParse(string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var originalTitle = title.Trim();
        var originalArtist = (artist ?? string.Empty).Trim();

        // "Canción (Official Video) [4K]" → "Canción"; "Canción | Letra" → "Canción".
        var cleanTitle = BracketsRegex().Replace(originalTitle, " ");
        var bar = cleanTitle.IndexOf('|');
        if (bar > 0) cleanTitle = cleanTitle[..bar];

        string songArtist;
        string songTitle;
        var dash = DashRegex().Match(cleanTitle);
        if (dash.Success && dash.Index > 0)
        {
            // Convención de YouTube: "Artista - Canción".
            songArtist = cleanTitle[..dash.Index];
            songTitle = cleanTitle[(dash.Index + dash.Length)..];
        }
        else
        {
            songArtist = CleanChannel(originalArtist);
            songTitle = cleanTitle;
        }

        songTitle = Tidy(FeaturingRegex().Replace(songTitle, string.Empty));
        songArtist = Tidy(FeaturingRegex().Replace(songArtist, string.Empty));
        if (songTitle.Length == 0 || songArtist.Length == 0) return null;

        var unchanged = string.Equals(songTitle, originalTitle, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(songArtist, originalArtist, StringComparison.OrdinalIgnoreCase);
        return unchanged ? null : new LyricsQuery(songTitle, songArtist, FromVideo: true);
    }

    /// <summary>"ThreeDaysGraceVEVO" → "Three Days Grace"; "Shakira - Topic" → "Shakira".</summary>
    public static string CleanChannel(string? channel)
    {
        var text = (channel ?? string.Empty).Trim();
        text = TopicSuffixRegex().Replace(text, string.Empty);
        text = ChannelSuffixRegex().Replace(text, string.Empty).Trim();

        // Nombres de canal pegados: se separan las palabras por las mayúsculas.
        if (!text.Contains(' ') && text.Length > 3) text = CamelCaseRegex().Replace(text, " ");
        return Tidy(text);
    }

    private static string Tidy(string text) => SpacesRegex().Replace(text, " ").Trim(' ', '-', '–', '—', '~', ':', '"', '\'');

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketsRegex();

    // Guion con espacios a los lados (- – — ~); no corta nombres como "Jay-Z".
    [GeneratedRegex(@"\s+[-–—~]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex DashRegex();

    [GeneratedRegex(@"\s+(ft\.?|feat\.?|featuring)\s+.*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingRegex();

    [GeneratedRegex(@"\s*-\s*topic$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffixRegex();

    [GeneratedRegex(@"(\s*(vevo|official|oficial|tv|music))+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffixRegex();

    [GeneratedRegex(@"(?<=\p{Ll})(?=\p{Lu})", RegexOptions.CultureInvariant)]
    private static partial Regex CamelCaseRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();
}
