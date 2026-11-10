using AlexIsland.Core.Display;

namespace AlexIsland.Core.Tests;

public class TopBandTests
{
    [Theory]
    [InlineData(36, 1.0, 1.0, 36)]
    [InlineData(36, 1.1, 1.25, 50)] // se redondea hacia arriba: nunca queda más baja que el notch
    [InlineData(36, 1.0, 1.5, 54)]
    [InlineData(0, 1.0, 1.0, 1)]
    public void Height_matches_the_closed_notch(int notch, double scale, double dpi, int expected)
        => Assert.Equal(expected, TopBand.HeightFor(notch, scale, dpi));

    [Fact]
    public void Takes_the_color_of_the_window_bar_below()
    {
        // Barra de Chrome oscura con una pestaña activa más clara y los botones de la ventana.
        var row = Enumerable.Repeat(0x202124, 1920).ToArray();
        for (var x = 100; x < 340; x++) row[x] = 0x35363A;
        for (var x = 1780; x < 1920; x++) row[x] = 0x2B2C30;

        Assert.Equal(0x202124, TopBand.DominantColor(row));
    }

    [Fact]
    public void Ignores_the_center_where_the_open_island_can_be()
    {
        var row = Enumerable.Repeat(0xDEE1E6, 1000).ToArray(); // barra clara
        for (var x = 260; x < 740; x++) row[x] = 0x032539;    // isla abierta en el centro

        Assert.Equal(0xDEE1E6, TopBand.DominantColor(row));
    }

    [Fact]
    public void Nearly_equal_shades_count_as_one_color()
    {
        var row = new int[800];
        for (var x = 0; x < row.Length; x++) row[x] = x % 4 < 2 ? 0x202020 : 0x222222;
        for (var x = 0; x < 150; x++) row[x] = 0xFFFFFF;

        Assert.Equal(0x212121, TopBand.DominantColor(row));
    }

    [Fact]
    public void Ignores_the_alpha_channel_of_the_screen_pixels()
        => Assert.Equal(0x112233, TopBand.DominantColor(Enumerable.Repeat(unchecked((int)0xFF112233), 50).ToArray()));

    [Fact]
    public void An_empty_row_has_no_color()
        => Assert.Null(TopBand.DominantColor([]));
}
