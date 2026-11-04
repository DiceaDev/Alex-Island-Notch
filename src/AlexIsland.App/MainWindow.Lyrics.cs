using AlexIsland.App.Services;
using AlexIsland.Core.Lyrics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN LETRAS SINCRONIZADAS (LRCLIB)
    // ==============================================================

    private const int ExpandedMediaLyricsHeight = 200;

    private readonly LyricsService _lyricsService = new();
    private DispatcherQueueTimer? _lyricsTimer;
    private DispatcherQueueTimer? _lyricsRequestTimer;
    private CancellationTokenSource? _lyricsCts;
    private SyncedLyrics? _lyrics;
    private string? _lyricsTrackKey;
    private string _currentTrackTitle = string.Empty;
    private string _currentTrackArtist = string.Empty;
    private int _lyricsIndex = -2;
    private bool _lyricShownInCompact;

    private bool HasLyrics => _lyrics is not null && _settings.ShowLyrics;

    private void InitializeLyrics()
    {
        // 250 ms basta para que la línea cambie a tiempo sin gastar CPU.
        _lyricsTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _lyricsTimer.Interval = TimeSpan.FromMilliseconds(250);
        _lyricsTimer.IsRepeating = true;
        _lyricsTimer.Tick += (_, _) => UpdateLyricsLine();

        // La duración de la canción a veces llega un poco después del título;
        // se espera un momento antes de pedir la letra.
        _lyricsRequestTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _lyricsRequestTimer.Interval = TimeSpan.FromMilliseconds(700);
        _lyricsRequestTimer.IsRepeating = false;
        _lyricsRequestTimer.Tick += (_, _) =>
        {
            _lyricsRequestTimer.Stop();
            _ = LoadLyricsAsync();
        };
    }

    private void StopLyrics()
    {
        _lyricsTimer?.Stop();
        _lyricsRequestTimer?.Stop();
        _lyricsCts?.Cancel();
        _lyricsService.Dispose();
    }

    /// <summary>Se llama cada vez que se actualizan los datos de la canción.</summary>
    private void OnTrackInfoChanged(string? title, string? artist)
    {
        _currentTrackTitle = title?.Trim() ?? string.Empty;
        _currentTrackArtist = artist?.Trim() ?? string.Empty;

        var key = $"{_currentTrackTitle}\n{_currentTrackArtist}";
        if (key == _lyricsTrackKey)
        {
            // Misma canción (por ejemplo, cambió solo la carátula): se repinta la línea.
            _lyricsIndex = -2;
            UpdateLyricsLine();
            return;
        }

        _lyricsTrackKey = key;
        ClearLyrics();

        // Sin artista solo se intenta si el título trae "Artista - Canción" (videos de YouTube).
        var hasArtist = _currentTrackArtist.Length > 0
            || AlexIsland.Core.Lyrics.VideoTrackInfo.TryParse(_currentTrackTitle, null) is not null;
        if (!_settings.ShowLyrics || _currentTrackTitle.Length == 0 || !hasArtist) return;

        _lyricsRequestTimer?.Stop();
        _lyricsRequestTimer?.Start();
    }

    private async Task LoadLyricsAsync()
    {
        _lyricsCts?.Cancel();
        _lyricsCts = new CancellationTokenSource();
        var token = _lyricsCts.Token;
        var requestedKey = _lyricsTrackKey;

        SyncTimelineSnapshot();
        var lyrics = await _lyricsService.GetAsync(_currentTrackTitle, _currentTrackArtist, _duration, token);

        // Si cambió la canción mientras se esperaba la respuesta, se descarta.
        if (token.IsCancellationRequested || _isDisposed || requestedKey != _lyricsTrackKey) return;

        _lyrics = lyrics;
        _lyricsIndex = -2;
        ApplyLyricsState();
    }

    private void ClearLyrics()
    {
        _lyricsCts?.Cancel();
        _lyrics = null;
        _lyricsIndex = -2;
        ApplyLyricsState();
    }

    /// <summary>Muestra u oculta el panel de letras y arranca el temporizador solo si hace falta.</summary>
    private void ApplyLyricsState()
    {
        LyricsPanel.Visibility = HasLyrics ? Visibility.Visible : Visibility.Collapsed;

        var visibleSomewhere = _currentDisplayMode == NotchDisplayMode.Closed
            || (_isExpanded && _activeTab == IslandTab.Media);

        if (HasLyrics && _isPlaying && visibleSomewhere)
        {
            if (_lyricsTimer is { IsRunning: false }) _lyricsTimer.Start();
            UpdateLyricsLine();
        }
        else
        {
            _lyricsTimer?.Stop();
            RestoreCompactArtist();
        }

        // La altura de la pestaña Música depende de si hay letra.
        if (_isExpanded && _activeTab == IslandTab.Media)
        {
            var expected = HasLyrics ? ExpandedMediaLyricsHeight : ExpandedHeight;
            if ((int)IslandContainer.Height != expected) ApplyIslandState();
        }
    }

    private void UpdateLyricsLine()
    {
        if (_lyrics is null || !_settings.ShowLyrics) return;

        var index = _lyrics.IndexAt(GetCurrentPosition());
        if (index == _lyricsIndex) return;
        _lyricsIndex = index;

        var current = index >= 0 ? _lyrics.Lines[index].Text : string.Empty;
        var next = index + 1 < _lyrics.Lines.Count ? _lyrics.Lines[index + 1].Text : string.Empty;

        // Las líneas vacías en LRC marcan pausas instrumentales.
        LyricsCurrentLine.Text = string.IsNullOrWhiteSpace(current) ? "♪" : current;
        LyricsCurrentLine.Tag = index >= 0 ? _lyrics.Lines[index].Time : null;
        LyricsNextLine.Text = string.IsNullOrWhiteSpace(next) ? " " : next;
        LyricsNextLine.Tag = index + 1 < _lyrics.Lines.Count ? _lyrics.Lines[index + 1].Time : null;

        // En el notch cerrado, la línea actual reemplaza al artista (como en Nusa).
        if (string.IsNullOrWhiteSpace(current))
        {
            RestoreCompactArtist();
        }
        else
        {
            CompactTrackArtist.Text = current;
            _lyricShownInCompact = true;
        }
    }

    /// <summary>
    /// Devuelve el subtítulo del notch cerrado al artista (o a la app que reproduce,
    /// tal como lo dejó la sección multimedia), solo si antes se mostraba una letra.
    /// </summary>
    private void RestoreCompactArtist()
    {
        if (!_lyricShownInCompact) return;
        _lyricShownInCompact = false;
        CompactTrackArtist.Text = TrackArtist.Text;
    }

    private async void LyricsLine_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBlock { Tag: TimeSpan time } || _mediaSession is null) return;

        _basePosition = time;
        _lastPositionSyncTime = DateTimeOffset.UtcNow;
        _lyricsIndex = -2;
        UpdateTimeline();
        UpdateLyricsLine();

        try
        {
            await _mediaSession.TryChangePlaybackPositionAsync(time.Ticks);
        }
        catch
        {
            // Algunas apps no permiten adelantar; no pasa nada.
        }
    }
}
