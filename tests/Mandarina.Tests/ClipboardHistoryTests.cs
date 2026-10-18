using Mandarina.Core.Clipboard;
using Xunit;

namespace Mandarina.Tests;

public class ClipboardHistoryTests
{
    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public void AddText_NewestFirst()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        h.AddText("uno");
        h.AddText("dos");
        Assert.Equal(["dos", "uno"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Duplicate_MovesToTop_NoCopy()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        h.AddText("uno");
        h.AddText("dos");
        h.AddText("uno");
        Assert.Equal(["uno", "dos"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void BlankText_Ignored()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        Assert.Null(h.AddText("   "));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Limit_DropsOldestUnpinned_AndItsImage()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path, max: 3);
        var img = h.AddImage(FakePng)!;
        h.Pin(h.AddText("fijo")!.Id, true);
        h.AddText("a");
        h.AddText("b");
        Assert.Equal(3, h.Items.Count);
        Assert.Contains(h.Items, i => i.Text == "fijo");
        Assert.DoesNotContain(h.Items, i => i.Id == img.Id);
        Assert.False(File.Exists(img.ImagePath));
    }

    [Fact]
    public void Pinned_ComeFirst()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        var old = h.AddText("viejo")!;
        h.AddText("nuevo");
        h.Pin(old.Id, true);
        Assert.Equal(["viejo", "nuevo"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Clear_KeepsPinned()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        h.Pin(h.AddText("fijo")!.Id, true);
        h.AddText("suelto");
        h.Clear();
        Assert.Equal(["fijo"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Persists_AcrossInstances()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        h.AddText("texto");
        var img = h.AddImage(FakePng)!;
        h.AddFiles([@"C:\a.txt", @"C:\b.txt"]);
        h.Pin(img.Id, true);

        var again = new ClipboardHistory(d.Path);
        Assert.Equal(3, again.Items.Count);
        var loadedImg = again.Items.Single(i => i.Kind == ClipKind.Image);
        Assert.True(loadedImg.Pinned);
        Assert.Equal(FakePng, File.ReadAllBytes(loadedImg.ImagePath!));
        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], again.Items.Single(i => i.Kind == ClipKind.Files).Files);
    }

    [Fact]
    public void Remove_DeletesImageFile()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        var img = h.AddImage(FakePng)!;
        h.Remove(img.Id);
        Assert.Empty(h.Items);
        Assert.False(File.Exists(img.ImagePath));
    }

    [Fact]
    public void CorruptIndex_StartsEmpty()
    {
        using var d = new TempDir();
        File.WriteAllText(Path.Combine(d.Path, "historial.json"), "{ roto");
        Assert.Empty(new ClipboardHistory(d.Path).Items);
    }

    [Fact]
    public void Preview_TrimsWhitespaceAndLength()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        var item = h.AddText("  hola\n\n   mundo  " + new string('x', 500))!;
        Assert.StartsWith("hola mundo", item.Preview);
        Assert.True(item.Preview.Length <= 201);
        Assert.Equal("2 archivos", h.AddFiles([@"C:\a", @"C:\b"])!.Preview);
    }

    [Fact]
    public void Filter_MatchesTextAndFileNames()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        h.AddText("receta de torta");
        h.AddFiles([@"C:\fotos\Torta.png"]);
        h.AddText("otra cosa");
        Assert.Equal(2, h.Search("torta").Count);
        Assert.Equal(3, h.Search("").Count);
    }

    [Fact]
    public void Changed_Raised()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        var count = 0;
        h.Changed += () => count++;
        h.AddText("a");
        h.Clear();
        Assert.Equal(2, count);
    }
}
