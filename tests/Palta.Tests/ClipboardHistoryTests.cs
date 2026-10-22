using Palta.Core.Clipboard;
using Xunit;

namespace Palta.Tests;

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
        // Los fijados no cuentan: 2 sin fijar + el fijado.
        var h = new ClipboardHistory(d.Path, new ClipboardLimits(0, 2, 0));
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
    public void KeepDays_DropsExpired_KeepsPinned_EvenAfterRestart()
    {
        using var d = new TempDir();
        var now = new DateTime(2026, 10, 1, 12, 0, 0);
        var h = new ClipboardHistory(d.Path, new ClipboardLimits(7, 0, 0), () => now);
        var viejo = h.AddImage(FakePng)!;
        h.Pin(h.AddText("fijo viejo")!.Id, true);
        h.AddText("viejo");
        now = now.AddDays(6);
        h.AddText("reciente");
        // Al día 8 lo de hace 8 días vence; se nota al abrir el panel (Prune) o al reiniciar.
        now = now.AddDays(2);
        var restarted = new ClipboardHistory(d.Path, new ClipboardLimits(7, 0, 0), () => now);
        Assert.Equal(["fijo viejo", "reciente"], restarted.Items.Select(i => i.Text));
        Assert.False(File.Exists(viejo.ImagePath));
    }

    [Fact]
    public void Touch_RenewsExpiry()
    {
        using var d = new TempDir();
        var now = new DateTime(2026, 10, 1);
        var h = new ClipboardHistory(d.Path, new ClipboardLimits(7, 0, 0), () => now);
        var item = h.AddText("lo sigo usando")!;
        now = now.AddDays(5);
        h.Touch(item.Id);
        now = now.AddDays(5);
        h.Prune();
        Assert.Single(h.Items);
    }

    [Fact]
    public void ImageSpace_DropsOldestImages_NotTexts_NotPinned()
    {
        using var d = new TempDir();
        var now = new DateTime(2026, 10, 1);
        var h = new ClipboardHistory(d.Path, new ClipboardLimits(0, 0, 1), () => now);
        var mega = new byte[600 * 1024];
        byte[] Png(byte tag) { var b = (byte[])mega.Clone(); b[0] = tag; return b; }
        var pinned = h.AddImage(Png(1))!;
        h.Pin(pinned.Id, true);
        now = now.AddMinutes(1);
        var first = h.AddImage(Png(2))!;
        now = now.AddMinutes(1);
        h.AddText("texto");
        now = now.AddMinutes(1);
        var second = h.AddImage(Png(3))!;
        // 1 MB para las sin fijar: entra una de 600 KB, la más vieja se va.
        Assert.Contains(h.Items, i => i.Id == pinned.Id);
        Assert.Contains(h.Items, i => i.Id == second.Id);
        Assert.Contains(h.Items, i => i.Text == "texto");
        Assert.DoesNotContain(h.Items, i => i.Id == first.Id);
        Assert.False(File.Exists(first.ImagePath));
        Assert.Equal(2 * 600 * 1024, h.ImageBytes);
    }

    [Fact]
    public void SingleHugeImage_StillKept()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path, new ClipboardLimits(0, 0, 1));
        var img = h.AddImage(new byte[3 * 1024 * 1024])!;
        Assert.Equal(img.Id, Assert.Single(h.Items).Id);
    }

    [Fact]
    public void LoweringLimits_PrunesRightAway()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        for (var i = 0; i < 5; i++)
            h.AddText($"t{i}");
        h.Limits = new ClipboardLimits(0, 2, 0);
        Assert.Equal(["t4", "t3"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Settings_Defaults_And_Negatives()
    {
        var s = new Palta.Core.Settings();
        Assert.Equal(ClipboardLimits.Default, s.ClipboardLimits);
        s.ClipboardKeepDays = -3;
        Assert.Equal(0, s.ClipboardLimits.KeepDays);
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
    public void Touch_MovesToTop()
    {
        using var d = new TempDir();
        var h = new ClipboardHistory(d.Path);
        var first = h.AddText("primero")!;
        Thread.Sleep(5);
        h.AddText("segundo");
        h.Touch(first.Id);
        Assert.Equal("primero", h.Items[0].Text);
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
