using AlexIsland.Core.Lyrics;

namespace AlexIsland.Core.Tests;

public class VideoTrackInfoTests
{
    [Theory]
    [InlineData("Three Days Grace - I Hate Everything About You (Official Video)", "ThreeDaysGraceVEVO", "I Hate Everything About You", "Three Days Grace")]
    [InlineData("Bad Bunny - Tití Me Preguntó (Video Oficial) | Un Verano Sin Ti", "Bad Bunny", "Tití Me Preguntó", "Bad Bunny")]
    [InlineData("Shakira – Hips Don't Lie ft. Wyclef Jean [4K]", "shakiraVEVO", "Hips Don't Lie", "Shakira")]
    [InlineData("Daft Punk ~ Get Lucky (Lyrics)", "Some Lyrics Channel", "Get Lucky", "Daft Punk")]
    public void Splits_artist_and_song_from_video_title(string title, string channel, string expectedTitle, string expectedArtist)
    {
        var query = VideoTrackInfo.TryParse(title, channel);

        Assert.NotNull(query);
        Assert.Equal(expectedTitle, query!.Title);
        Assert.Equal(expectedArtist, query.Artist);
        Assert.True(query.FromVideo);
    }

    [Fact]
    public void Uses_cleaned_channel_when_title_has_no_artist()
    {
        var query = VideoTrackInfo.TryParse("Numb (Official Music Video) [4K UPGRADE]", "Linkin Park - Topic");

        Assert.Equal(new LyricsQuery("Numb", "Linkin Park", true), query);
    }

    [Theory]
    [InlineData("ThreeDaysGraceVEVO", "Three Days Grace")]
    [InlineData("Shakira - Topic", "Shakira")]
    [InlineData("Coldplay", "Coldplay")]
    [InlineData("Bad Bunny Official", "Bad Bunny")]
    [InlineData("", "")]
    public void Cleans_channel_names(string channel, string expected)
        => Assert.Equal(expected, VideoTrackInfo.CleanChannel(channel));

    [Theory]
    [InlineData("Yellow", "Coldplay")]                    // Spotify / YouTube Music: ya es una canción
    [InlineData("Jay-Z", "Jay-Z")]                        // guion sin espacios no es separador
    [InlineData("   ", "Coldplay")]
    public void Returns_null_when_there_is_nothing_to_clean(string title, string artist)
        => Assert.Null(VideoTrackInfo.TryParse(title, artist));

    [Fact]
    public void Works_without_channel_when_title_has_artist()
    {
        var query = VideoTrackInfo.TryParse("Queen - Bohemian Rhapsody (Official Video Remastered)", null);

        Assert.Equal(new LyricsQuery("Bohemian Rhapsody", "Queen", true), query);
    }

    [Fact]
    public void Video_pick_accepts_song_shorter_than_video_and_prefers_closest()
    {
        var records = new[]
        {
            new LrclibRecord { TrackName = "I Hate Everything About You", Duration = 200, SyncedLyrics = "[00:01.00]a" },
            new LrclibRecord { TrackName = "I Hate Everything About You", Duration = 231, SyncedLyrics = "[00:01.00]b" },
            new LrclibRecord { TrackName = "I Hate Everything About You", Duration = 260, SyncedLyrics = "[00:01.00]c" },
        };

        // Video de 4:00 (240 s): 231 s entra (9 s de más en el video); 260 s es más larga que el video.
        var pick = LrclibRequests.PickBestForVideo(records, "I Hate Everything About You", TimeSpan.FromSeconds(240));

        Assert.Equal(231, pick!.Duration);
    }

    [Fact]
    public void Video_pick_rejects_songs_far_shorter_than_video()
    {
        var records = new[] { new LrclibRecord { TrackName = "Numb", Duration = 150, SyncedLyrics = "[00:01.00]a" } };

        Assert.Null(LrclibRequests.PickBestForVideo(records, "Numb", TimeSpan.FromSeconds(200)));
    }
}
