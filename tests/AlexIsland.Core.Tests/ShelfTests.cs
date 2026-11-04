using AlexIsland.Core.Shelf;

namespace AlexIsland.Core.Tests;

public class ShelfRulesTests
{
    [Theory]
    [InlineData(@"C:\Users\alex\Desktop\informe.pdf", true)]
    [InlineData(@"D:/proyectos/app", true)]
    [InlineData(@"\\servidor\compartido\manual.docx", true)]
    [InlineData(@"informe.pdf", false)]
    [InlineData(@"..\secreto.txt", false)]
    [InlineData(@"\\?\C:\Windows\System32\cmd.exe", false)]
    [InlineData(@"\\.\PhysicalDrive0", false)]
    [InlineData(@"\\\triple", false)]
    [InlineData("C:\\a\u0001b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Accepts_only_normal_absolute_paths(string? path, bool expected)
        => Assert.Equal(expected, ShelfRules.IsAcceptablePath(path));

    [Fact]
    public void Adds_paths_newest_first_and_skips_duplicates()
    {
        var items = new List<ShelfItem>();
        var added = ShelfRules.AddPaths(items,
        [
            (@"C:\a.txt", false),
            (@"C:\carpeta", true),
            (@"c:\A.TXT", false),
            ("relativo.txt", false),
        ]);

        Assert.Equal(2, added);
        Assert.Equal(@"C:\carpeta", items[0].Path);
        Assert.Equal(ShelfItemKind.Folder, items[0].Kind);
        Assert.Equal(ShelfItemKind.File, items[1].Kind);
    }

    [Fact]
    public void Respects_maximum_items()
    {
        var items = new List<ShelfItem>();
        var paths = Enumerable.Range(0, ShelfRules.MaxItems + 10).Select(i => ($@"C:\f{i}.txt", false));
        Assert.Equal(ShelfRules.MaxItems, ShelfRules.AddPaths(items, paths));
        Assert.False(ShelfRules.AddText(items, "uno más"));
    }

    [Fact]
    public void Adds_text_cards_trimmed_and_without_duplicates()
    {
        var items = new List<ShelfItem>();
        Assert.True(ShelfRules.AddText(items, "SELECT * FROM usuarios;"));
        Assert.False(ShelfRules.AddText(items, "SELECT * FROM usuarios;"));
        Assert.False(ShelfRules.AddText(items, "   "));
        Assert.True(ShelfRules.AddText(items, new string('x', ShelfRules.MaxTextLength + 500)));
        Assert.Equal(ShelfRules.MaxTextLength, items[0].Text!.Length);
    }

    [Fact]
    public void Sanitize_drops_invalid_entries_from_disk()
    {
        var loaded = new List<ShelfItem>
        {
            new() { Kind = ShelfItemKind.File, Path = @"C:\ok.txt" },
            new() { Kind = ShelfItemKind.File, Path = @"\\?\C:\malo.exe" },
            new() { Kind = ShelfItemKind.Text, Text = "  " },
            new() { Kind = (ShelfItemKind)99, Path = @"C:\raro" },
            new() { Kind = ShelfItemKind.Text, Text = "nota", Id = "" },
            null!,
        };

        var result = ShelfRules.Sanitize(loaded);
        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.False(string.IsNullOrEmpty(i.Id)));
    }

    [Theory]
    [InlineData(@"C:\docs\informe final.pdf", ShelfItemKind.File, "informe final.pdf")]
    [InlineData(@"C:\docs\proyecto\", ShelfItemKind.Folder, "proyecto")]
    [InlineData(@"\\srv\share\a.log", ShelfItemKind.File, "a.log")]
    public void Display_name_uses_file_or_folder_name(string path, ShelfItemKind kind, string expected)
        => Assert.Equal(expected, new ShelfItem { Kind = kind, Path = path }.DisplayName);

    [Fact]
    public void Text_preview_uses_first_line()
    {
        Assert.Equal("Primera línea", ShelfRules.Preview("  Primera línea\nSegunda"));
        Assert.Equal(10, ShelfRules.Preview(new string('a', 50), maxLength: 10).Length);
    }
}
