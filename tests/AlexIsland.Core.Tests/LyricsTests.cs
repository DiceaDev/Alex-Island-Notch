using AlexIsland.Core.Lyrics;

namespace AlexIsland.Core.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parses_timestamps_and_sorts_lines()
    {
        var lyrics = LrcParser.Parse("""
            [ar:Artista]
            [ti:Canción]
            [00:12.50] Segunda
            [00:05.00] Primera
            [01:02.123]Tercera
            sin marca de tiempo
            """);

        Assert.Equal(3, lyrics.Lines.Count);
        Assert.Equal(new LyricLine(TimeSpan.FromSeconds(5), "Primera"), lyrics.Lines[0]);
        Assert.Equal(new LyricLine(TimeSpan.FromMilliseconds(12_500), "Segunda"), lyrics.Lines[1]);
        Assert.Equal(new LyricLine(TimeSpan.FromMilliseconds(62_123), "Tercera"), lyrics.Lines[2]);
    }

    [Fact]
    public void Supports_multiple_timestamps_per_line()
    {
        var lyrics = LrcParser.Parse("[00:10.00][00:40.00]Coro\n[00:20.00]Estrofa");
        Assert.Equal(new[] { "Coro", "Estrofa", "Coro" }, lyrics.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("[00:01.5]x", 1500)]
    [InlineData("[00:01.50]x", 1500)]
    [InlineData("[00:01.500]x", 1500)]
    [InlineData("[00:01:25]x", 1250)]
    [InlineData("[00:01]x", 1000)]
    [InlineData("[100:00.00]x", 6_000_000)]
    public void Parses_fraction_formats(string lrc, int expectedMs)
        => Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), LrcParser.Parse(lrc).Lines.Single().Time);

    [Fact]
    public void Applies_offset_tag()
    {
        var lyrics = LrcParser.Parse("[offset:+500]\n[00:02.00]A\n[00:00.20]B");
        Assert.Equal(TimeSpan.FromSeconds(1.5), lyrics.Lines.Single(l => l.Text == "A").Time);
        Assert.Equal(TimeSpan.Zero, lyrics.Lines.Single(l => l.Text == "B").Time);
    }

    [Fact]
    public void Keeps_empty_lines_as_instrumental_breaks()
    {
        var lyrics = LrcParser.Parse("[00:01.00]Hola\n[00:05.00]\n[00:09.00]Chao");
        Assert.Equal("", lyrics.Lines[1].Text);
    }

    [Fact]
    public void Strips_control_characters_and_limits_length()
    {
        var lyrics = LrcParser.Parse("[00:01.00]Ho\u0007la\u001b[31m " + new string('x', 1000));
        var text = lyrics.Lines.Single().Text;
        Assert.DoesNotContain('\u0007', text);
        Assert.DoesNotContain('\u001b', text);
        Assert.True(text.Length <= 300);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin letras sincronizadas")]
    public void Returns_empty_for_missing_or_plain_text(string? lrc)
        => Assert.True(LrcParser.Parse(lrc).IsEmpty);

    [Fact]
    public void Rejects_oversized_input()
        => Assert.True(LrcParser.Parse("[00:01.00]" + new string('a', LrcParser.MaxInputLength)).IsEmpty);

    [Fact]
    public void Finds_current_line_by_position()
    {
        var lyrics = LrcParser.Parse("[00:05.00]A\n[00:10.00]B\n[00:15.00]C");
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(9.99)));
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, lyrics.IndexAt(TimeSpan.FromMinutes(5)));
        Assert.Equal(-1, new SyncedLyrics([]).IndexAt(TimeSpan.FromSeconds(3)));
    }
}

public class LrclibRequestsTests
{
    [Fact]
    public void Builds_get_uri_with_escaped_fields_and_rounded_duration()
    {
        var uri = LrclibRequests.BuildGetUri("Canción & Amor", "Los Bunkers", TimeSpan.FromSeconds(214.6));
        Assert.NotNull(uri);
        Assert.Equal("lrclib.net", uri!.Host);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("/api/get", uri.AbsolutePath);
        Assert.Contains("track_name=Canci%C3%B3n%20%26%20Amor", uri.Query);
        Assert.Contains("artist_name=Los%20Bunkers", uri.Query);
        Assert.Contains("duration=215", uri.Query);
    }

    [Fact]
    public void Does_not_build_requests_without_required_data()
    {
        Assert.Null(LrclibRequests.BuildGetUri("Título", "Artista", TimeSpan.Zero));
        Assert.Null(LrclibRequests.BuildGetUri("", "Artista", TimeSpan.FromMinutes(3)));
        Assert.Null(LrclibRequests.BuildSearchUri("Título", "   "));
    }

    [Fact]
    public void Query_cannot_escape_to_another_host_or_path()
    {
        var uri = LrclibRequests.BuildSearchUri("../../admin?x=1#", "@evil.example/");
        Assert.Equal("lrclib.net", uri!.Host);
        Assert.Equal("/api/search", uri.AbsolutePath);
    }

    [Theory]
    [InlineData("Bohemian Rhapsody - Remastered 2011", "bohemian rhapsody")]
    [InlineData("Despacito (feat. Daddy Yankee)", "despacito")]
    [InlineData("  Él Mató a un Policía Motorizado  ", "el mato a un policia motorizado")]
    [InlineData("Song [Live]", "song")]
    public void Normalizes_titles(string input, string expected)
        => Assert.Equal(expected, LrclibRequests.NormalizeTitle(input));

    [Fact]
    public void Picks_record_with_matching_title_duration_and_synced_lyrics()
    {
        var records = new[]
        {
            new LrclibRecord { TrackName = "Otra", Duration = 200, SyncedLyrics = "[00:01.00]x" },
            new LrclibRecord { TrackName = "Canción", Duration = 200, SyncedLyrics = null },
            new LrclibRecord { TrackName = "Canción", Duration = 230, SyncedLyrics = "[00:01.00]larga" },
            new LrclibRecord { TrackName = "Canción", Duration = 200, Instrumental = true, SyncedLyrics = "[00:01.00]x" },
            new LrclibRecord { TrackName = "Canción (Remastered)", Duration = 201.5, SyncedLyrics = "[00:01.00]buena" },
        };

        var best = LrclibRequests.PickBest(records, "Canción", TimeSpan.FromSeconds(200));
        Assert.Equal("[00:01.00]buena", best?.SyncedLyrics);
        Assert.Null(LrclibRequests.PickBest(records, "Canción", TimeSpan.FromSeconds(300)));
        Assert.Null(LrclibRequests.PickBest(null, "Canción", TimeSpan.FromSeconds(200)));
    }
}
