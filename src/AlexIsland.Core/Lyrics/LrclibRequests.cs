using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AlexIsland.Core.Lyrics;

/// <summary>Respuesta de LRCLIB (solo los campos que se usan).</summary>
public sealed class LrclibRecord
{
    [JsonPropertyName("trackName")] public string? TrackName { get; set; }
    [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
    [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
}

/// <summary>
/// Arma las consultas a LRCLIB y elige el resultado correcto. Solo se envían
/// título, artista y duración: nunca datos del usuario ni del equipo.
/// </summary>
public static partial class LrclibRequests
{
    public static readonly Uri BaseUri = new("https://lrclib.net/");

    /// <summary>Diferencia máxima aceptada entre la duración real y la del registro.</summary>
    public static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);

    private const int MaxFieldLength = 200;

    public static Uri? BuildGetUri(string? title, string? artist, TimeSpan duration)
    {
        if (!TryClean(title, out var t) || !TryClean(artist, out var a) || duration <= TimeSpan.Zero) return null;

        var seconds = ((int)Math.Round(duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return new Uri(BaseUri, $"api/get?track_name={Encode(t)}&artist_name={Encode(a)}&duration={seconds}");
    }

    public static Uri? BuildSearchUri(string? title, string? artist)
    {
        if (!TryClean(title, out var t) || !TryClean(artist, out var a)) return null;
        return new Uri(BaseUri, $"api/search?track_name={Encode(t)}&artist_name={Encode(a)}");
    }

    /// <summary>
    /// Elige el primer registro con letra sincronizada cuyo título coincida y,
    /// si se conoce la duración, que no difiera en más de 2 segundos.
    /// </summary>
    public static LrclibRecord? PickBest(IEnumerable<LrclibRecord>? records, string title, TimeSpan duration)
    {
        if (records is null) return null;
        var wanted = NormalizeTitle(title);

        return records.FirstOrDefault(r =>
            !r.Instrumental
            && !string.IsNullOrWhiteSpace(r.SyncedLyrics)
            && NormalizeTitle(r.TrackName) == wanted
            && (duration <= TimeSpan.Zero || IsDurationMatch(r.Duration, duration)));
    }

    /// <summary>Lo que un video musical puede durar de más respecto de la canción (intro, final, créditos).</summary>
    public static readonly TimeSpan VideoExtraAllowance = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Como <see cref="PickBest"/>, pero para datos deducidos de un video: acepta
    /// canciones hasta 25 s más cortas que el video (nunca más largas) y se queda
    /// con la de duración más parecida.
    /// </summary>
    public static LrclibRecord? PickBestForVideo(IEnumerable<LrclibRecord>? records, string title, TimeSpan videoDuration)
    {
        if (records is null) return null;
        var wanted = NormalizeTitle(title);
        var video = videoDuration.TotalSeconds;

        return records
            .Where(r => !r.Instrumental
                        && !string.IsNullOrWhiteSpace(r.SyncedLyrics)
                        && NormalizeTitle(r.TrackName) == wanted
                        && (videoDuration <= TimeSpan.Zero
                            || (r.Duration > 0
                                && r.Duration <= video + DurationTolerance.TotalSeconds
                                && r.Duration >= video - VideoExtraAllowance.TotalSeconds)))
            .OrderBy(r => videoDuration <= TimeSpan.Zero ? 0 : Math.Abs(r.Duration - video))
            .FirstOrDefault();
    }

    public static bool IsDurationMatch(double recordSeconds, TimeSpan duration)
        => recordSeconds > 0 && Math.Abs(recordSeconds - duration.TotalSeconds) <= DurationTolerance.TotalSeconds;

    /// <summary>
    /// Normaliza un título para compararlo: sin mayúsculas, tildes, textos entre
    /// paréntesis/corchetes ni sufijos como " - Remastered 2011".
    /// </summary>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var text = BracketsRegex().Replace(title, " ");
        var dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) text = text[..dash];

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return SpacesRegex().Replace(sb.ToString(), " ").Trim();
    }

    private static bool TryClean(string? value, out string cleaned)
    {
        cleaned = (value ?? string.Empty).Trim();
        if (cleaned.Length == 0) return false;
        if (cleaned.Length > MaxFieldLength) cleaned = cleaned[..MaxFieldLength];
        return true;
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketsRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();
}
