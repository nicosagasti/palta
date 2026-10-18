using Mandarina.Core;
using Xunit;

namespace Mandarina.Tests;

public sealed class FakeHost : IShellHost
{
    public string? Folder;
    public string? Clipboard;
    public string? PickFolder(string title) => Folder;
    public void SetClipboardText(string text) => Clipboard = text;
}

public class CatalogTests
{
    private static Operation Fake(string id, WheelKind wheel, FileKind kind) =>
        Operation.PerFile(id, wheel, id, "\uE8A5", kind, (f, _) => Task.FromResult(f));

    [Fact]
    public void For_FiltersByWheelAndApplies()
    {
        var c = new Catalog([Fake("a", WheelKind.Convert, FileKind.Image), Fake("b", WheelKind.Tools, FileKind.Image), Fake("c", WheelKind.Convert, FileKind.Pdf)]);
        var ops = c.For(WheelKind.Convert, [@"C:\x\a.png"]);
        Assert.Equal(["a"], ops.Select(o => o.Id));
    }

    [Fact]
    public void For_EmptyFiles_ReturnsEmpty()
    {
        var c = new Catalog([Fake("a", WheelKind.Convert, FileKind.Image)]);
        Assert.Empty(c.For(WheelKind.Convert, []));
    }

    [Fact]
    public void PerFile_RequiresAllFilesOfKind()
    {
        var op = Fake("a", WheelKind.Convert, FileKind.Image);
        Assert.False(op.AppliesTo([@"C:\x\a.png", @"C:\x\b.pdf"]));
        Assert.True(op.AppliesTo([@"C:\x\a.png", @"C:\x\b.jpg"]));
    }

    [Fact]
    public void ById_FindsOperation()
    {
        var c = new Catalog([Fake("a", WheelKind.Convert, FileKind.Image)]);
        Assert.Equal("a", c.ById("a")?.Id);
        Assert.Null(c.ById("zzz"));
    }

    [Fact]
    public async Task CopyPath_SetsClipboard_AllPaths()
    {
        var host = new FakeHost();
        var c = Catalog.CreateDefault(host);
        var op = c.For(WheelKind.Actions, [@"C:\x\a.txt", @"C:\x\b.txt"]).Single(o => o.Id == "copy-path");
        await op.RunAsync([@"C:\x\a.txt", @"C:\x\b.txt"], new Progress<double>(), default);
        Assert.Equal("C:\\x\\a.txt\r\nC:\\x\\b.txt", host.Clipboard);
    }

    [Fact]
    public async Task CopyTo_CopiesWithoutOverwriting()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        var f = src.File("a.txt", "nuevo");
        File.WriteAllText(Path.Combine(dst.Path, "a.txt"), "viejo");
        var op = Catalog.CreateDefault(new FakeHost { Folder = dst.Path }).ById("copy-to")!;
        var outputs = await op.RunAsync([f], new Progress<double>(), default);
        Assert.Equal(Path.Combine(dst.Path, "a (1).txt"), outputs.Single());
        Assert.Equal("viejo", File.ReadAllText(Path.Combine(dst.Path, "a.txt")));
        Assert.True(File.Exists(f));
    }

    [Fact]
    public async Task MoveTo_MovesFolder()
    {
        using var src = new TempDir();
        using var dst = new TempDir();
        var folder = Path.Combine(src.Path, "carpeta");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "x.txt"), "x");
        var op = Catalog.CreateDefault(new FakeHost { Folder = dst.Path }).ById("move-to")!;
        Assert.True(op.AppliesTo([folder]));
        await op.RunAsync([folder], new Progress<double>(), default);
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(dst.Path, "carpeta", "x.txt")));
    }

    [Fact]
    public async Task CopyTo_PickerCanceled_DoesNothing()
    {
        using var src = new TempDir();
        var f = src.File("a.txt");
        var op = Catalog.CreateDefault(new FakeHost { Folder = null }).ById("copy-to")!;
        Assert.Empty(await op.RunAsync([f], new Progress<double>(), default));
    }
}
