using System.Net;
using System.Net.Http.Json;
using AlexIsland.Core.Lyrics;

namespace AlexIsland.App.Services;

/// <summary>
/// Obtiene letras sincronizadas desde LRCLIB. Solo envía título, artista y
/// duración. Los resultados se guardan en memoria durante la sesión (nunca
/// en disco), incluido "no hay letra", para no repetir consultas.
/// </summary>
internal sealed class LyricsService : IDisposable
{
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxCacheEntries = 64;

    private readonly HttpClient _http;
    private readonly Dictionary<string, SyncedLyrics?> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private readonly Lock _cacheLock = new();

    public LyricsService()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = DecompressionMethods.All,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = LrclibRequests.BaseUri,
            Timeout = TimeSpan.FromSeconds(8),
            MaxResponseContentBufferSize = MaxResponseBytes,
        };

        // LRCLIB pide identificar la aplicación cliente.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AlexIsland/1.0 (+https://github.com/DiceaDev/Alex-Island-Notch)");
    }

    public async Task<SyncedLyrics?> GetAsync(string title, string artist, TimeSpan duration, CancellationToken cancellationToken)
    {
        var key = $"{LrclibRequests.NormalizeTitle(title)}|{artist.Trim().ToLowerInvariant()}|{(int)Math.Round(duration.TotalSeconds)}";

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        var lyrics = await FetchAsync(title, artist, duration, cancellationToken);

        // Un error de red o una cancelación no se recuerdan: se reintentará en la próxima canción.
        if (lyrics.Completed)
        {
            lock (_cacheLock)
            {
                if (_cache.TryAdd(key, lyrics.Value))
                {
                    _cacheOrder.Enqueue(key);
                    while (_cacheOrder.Count > MaxCacheEntries)
                    {
                        _cache.Remove(_cacheOrder.Dequeue());
                    }
                }
            }
        }

        return lyrics.Value;
    }

    private async Task<(bool Completed, SyncedLyrics? Value)> FetchAsync(
        string title, string artist, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            // 1) Coincidencia exacta por título, artista y duración.
            if (LrclibRequests.BuildGetUri(title, artist, duration) is { } getUri)
            {
                using var response = await _http.GetAsync(getUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var record = await response.Content.ReadFromJsonAsync<LrclibRecord>(cancellationToken);
                    var match = LrclibRequests.PickBest(record is null ? null : new[] { record }, title, duration);
                    if (match is not null) return (true, ToLyrics(match));
                }
                else if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    return (false, null);
                }
            }

            // 2) Búsqueda por título y artista, filtrando por duración.
            if (LrclibRequests.BuildSearchUri(title, artist) is { } searchUri)
            {
                using var response = await _http.GetAsync(searchUri, cancellationToken);
                if (!response.IsSuccessStatusCode) return (false, null);

                var records = await response.Content.ReadFromJsonAsync<List<LrclibRecord>>(cancellationToken);
                var match = LrclibRequests.PickBest(records, title, duration);
                if (match is not null) return (true, ToLyrics(match));
            }

            // 3) Videos de YouTube: "Artista - Canción (Official Video)" publicado por
            //    "ArtistaVEVO". Se busca con el título y el artista deducidos.
            if (VideoTrackInfo.TryParse(title, artist) is { } video
                && LrclibRequests.BuildSearchUri(video.Title, video.Artist) is { } videoUri)
            {
                using var response = await _http.GetAsync(videoUri, cancellationToken);
                if (!response.IsSuccessStatusCode) return (false, null);

                var records = await response.Content.ReadFromJsonAsync<List<LrclibRecord>>(cancellationToken);
                var match = LrclibRequests.PickBestForVideo(records, video.Title, duration);
                return (true, match is null ? null : ToLyrics(match));
            }

            return (true, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private static SyncedLyrics? ToLyrics(LrclibRecord record)
    {
        var lyrics = LrcParser.Parse(record.SyncedLyrics);
        return lyrics.IsEmpty ? null : lyrics;
    }

    public void Dispose() => _http.Dispose();
}
