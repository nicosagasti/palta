using Palta.Core;
using Xunit;

namespace Palta.Tests;

public sealed class FakeHost : IShellHost
{
    public string? Folder;
    public string? Clipboard;
    public IReadOnlyList<string>? Shared;
    public IReadOnlyList<string>? Floated;
    public string? TerminalAt;
    public string? PickFolder(string title) => Folder;
    public void SetClipboardText(string text) => Clipboard = text;
    public void Share(IReadOnlyList<string> files) => Shared = files;
    public void Float(IReadOnlyList<string> files) => Floated = files;
    public void OpenTerminal(string folder) => TerminalAt = folder;
}

public class TangerineActionsTests
{
    [Fact]
    public void Actions_SameAsTangerine_InOrder()
    {
        using var d = new TempDir();
        var f = d.File("a.txt");
        var ids = Catalog.CreateDefault(new FakeHost()).For(WheelKind.Actions, [f]).Select(o => o.Id);
        Assert.Equal(["share", "float", "copy-to", "move-to", "copy-path", "terminal"], ids);
    }

    [Fact]
    public async Task Share_And_Float_PassFilesToHost()
    {
        using var d = new TempDir();
        string[] files = [d.File("a.png"), d.File("b.pdf")];
        var host = new FakeHost();
        var c = Catalog.CreateDefault(host);
        await c.ById("share")!.RunAsync(files, new Progress<double>(), default);
        await c.ById("float")!.RunAsync(files, new Progress<double>(), default);
        Assert.Equal(files, host.Shared);
        Assert.Equal(files, host.Floated);
    }

    [Fact]
    public async Task Terminal_OpensInFolderOfFile_OrInFolderItself()
    {
        using var d = new TempDir();
        var file = d.File("a.txt");
        var sub = Path.Combine(d.Path, "sub");
        Directory.CreateDirectory(sub);
        var host = new FakeHost();
        var op = Catalog.CreateDefault(host).ById("terminal")!;
        await op.RunAsync([file], new Progress<double>(), default);
        Assert.Equal(d.Path, host.TerminalAt);
        await op.RunAsync([sub], new Progress<double>(), default);
        Assert.Equal(sub, host.TerminalAt);
    }

    [Fact]
    public void Quiet_OnlyForActionsThatShowTheirOwnUi()
    {
        var c = Catalog.CreateDefault(new FakeHost());
        Assert.True(c.ById("share")!.Quiet);
        Assert.True(c.ById("float")!.Quiet);
        Assert.True(c.ById("terminal")!.Quiet);
        Assert.False(c.ById("copy-path")!.Quiet);
        Assert.False(c.ById("img-to-jpg")!.Quiet);
    }

    [Fact]
    public void Share_NeedsExistingFiles()
    {
        var c = Catalog.CreateDefault(new FakeHost());
        Assert.False(c.ById("share")!.AppliesTo([@"C:\no\existe.txt"]));
    }
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
    public async Task CopyTo_IntoItsOwnSubfolder_Refused()
    {
        using var src = new TempDir();
        var folder = Path.Combine(src.Path, "carpeta");
        var sub = Path.Combine(folder, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(folder, "x.txt"), "x");
        var op = Catalog.CreateDefault(new FakeHost { Folder = sub }).ById("copy-to")!;
        var e = await Assert.ThrowsAsync<Palta.Core.Backends.ConversionException>(() => op.RunAsync([folder], new Progress<double>(), default));
        Assert.Contains("dentro de sí misma", e.Message);
        Assert.Equal(["sub"], Directory.GetDirectories(folder).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFileSystemEntries(sub));
    }

    [Fact]
    public async Task CopyTo_PickerCanceled_DoesNothing()
    {
        using var src = new TempDir();
        var f = src.File("a.txt");
        var op = Catalog.CreateDefault(new FakeHost { Folder = null }).ById("copy-to")!;
        await Assert.ThrowsAsync<CanceledByUserException>(async () => await op.RunAsync([f], new Progress<double>(), default));
        Assert.Equal([f], Directory.GetFiles(src.Path));
    }

    [Fact]
    public async Task CopyTo_PickerCanceled_JobSaysCanceled()
    {
        using var src = new TempDir();
        var op = Catalog.CreateDefault(new FakeHost { Folder = null }).ById("move-to")!;
        var q = new JobQueue(1);
        var job = q.Enqueue(op, [src.File("a.txt")]);
        await q.WhenIdle();
        Assert.Equal(JobState.Canceled, job.State);
    }
}
