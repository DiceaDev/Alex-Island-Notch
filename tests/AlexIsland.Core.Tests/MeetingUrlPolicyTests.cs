using AlexIsland.Core.Models;
using AlexIsland.Core.Security;

namespace AlexIsland.Core.Tests;

public class MeetingUrlPolicyTests
{
    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/19%3ameeting")]
    [InlineData("https://teams.live.com/meet/9876543210")]
    [InlineData("https://zoom.us/j/123456789")]
    [InlineData("https://us02web.zoom.us/j/123456789?pwd=abc")]
    [InlineData("https://empresa.webex.com/meet/alex")]
    [InlineData("  https://MEET.GOOGLE.COM/abc-defg-hij  ")]
    public void Accepts_known_https_meeting_links(string url)
    {
        Assert.True(MeetingUrlPolicy.TryGetSafeUri(url, out var uri));
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Esquemas que podrían ejecutar acciones locales.
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"file://\\servidor\compartido\malware.exe")]
    [InlineData("search-ms:query=factura&crumb=location:\\\\servidor\\x")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("calculator:")]
    // Sin cifrar.
    [InlineData("http://meet.google.com/abc-defg-hij")]
    // Dominios que imitan a los permitidos.
    [InlineData("https://meet.google.com.evil.example/abc")]
    [InlineData("https://evil-zoom.us/j/1")]
    [InlineData("https://zoom.us.evil.example/j/1")]
    [InlineData("https://notwebex.com/meet")]
    [InlineData("https://google.com/meet")]
    // Credenciales embebidas, puertos raros, IPs y espacios.
    [InlineData("https://meet.google.com@evil.example/abc")]
    [InlineData("https://user@meet.google.com/abc")]
    [InlineData("https://meet.google.com:8443/abc")]
    [InlineData("https://142.250.0.1/abc")]
    [InlineData("https://meet.google.com/abc def")]
    public void Rejects_unsafe_or_unknown_links(string? url)
    {
        Assert.False(MeetingUrlPolicy.TryGetSafeUri(url, out _));
        Assert.Null(MeetingUrlPolicy.Sanitize(url));
    }

    [Fact]
    public void Rejects_overly_long_links()
    {
        var url = "https://meet.google.com/" + new string('a', MeetingUrlPolicy.MaxLength);
        Assert.False(MeetingUrlPolicy.TryGetSafeUri(url, out _));
    }

    [Fact]
    public void Extracts_first_safe_link_from_free_text()
    {
        const string text = "Entra por http://meet.google.com/inseguro o mejor https://meet.google.com/abc-defg-hij, gracias.";
        Assert.Equal("https://meet.google.com/abc-defg-hij", CalendarEvent.TryExtractMeetingUrl(text));
    }

    [Fact]
    public void Ignores_unsafe_links_in_free_text()
    {
        Assert.Null(CalendarEvent.TryExtractMeetingUrl("Abre file:///C:/x.exe o https://evil.example/meet"));
    }
}
