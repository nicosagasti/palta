using Palta.Core;
using Xunit;

namespace Palta.Tests;

public class OutputPathsTests
{
    [Fact]
    public void For_ChangesExtension()
    {
        using var d = new TempDir();
        var src = d.File("foto.png");
        Assert.Equal(Path.Combine(d.Path, "foto.jpg"), OutputPaths.For(src, ".jpg"));
    }

    [Fact]
    public void For_WithSuffix()
    {
        using var d = new TempDir();
        var src = d.File("foto.jpg");
        Assert.Equal(Path.Combine(d.Path, "foto (comprimida).jpg"), OutputPaths.For(src, ".jpg", "comprimida"));
    }

    [Fact]
    public void For_Collision_AddsCounter()
    {
        using var d = new TempDir();
        var src = d.File("foto.png");
        d.File("foto.jpg");
        d.File("foto (1).jpg");
        Assert.Equal(Path.Combine(d.Path, "foto (2).jpg"), OutputPaths.For(src, ".jpg"));
    }

    [Fact]
    public void For_SameExtensionNoSuffix_DoesNotReturnSource()
    {
        using var d = new TempDir();
        var src = d.File("foto.jpg");
        Assert.Equal(Path.Combine(d.Path, "foto (1).jpg"), OutputPaths.For(src, ".jpg"));
    }

    [Fact]
    public void UniqueFolder_Collision()
    {
        using var d = new TempDir();
        Directory.CreateDirectory(Path.Combine(d.Path, "doc (páginas)"));
        Assert.Equal(Path.Combine(d.Path, "doc (páginas) (1)"), OutputPaths.UniqueFolder(d.Path, "doc (páginas)"));
    }

    [Fact]
    public void TempFor_InPaltaFolderNextToTarget()
    {
        var t = OutputPaths.TempFor(@"C:\x\foto.jpg");
        var folder = Path.GetDirectoryName(t)!;
        Assert.Equal(@"C:\x", Path.GetDirectoryName(folder));
        Assert.StartsWith(".palta-", Path.GetFileName(folder));
        Assert.EndsWith(".jpg", t);
    }

    [Fact]
    public async Task SafeWrite_TempIsHidden_AndNothingLeftBehind()
    {
        using var d = new TempDir();
        var hidden = false;
        var final = await SafeWrite.WriteAsync(Path.Combine(d.Path, "out.txt"), tmp =>
        {
            hidden = new DirectoryInfo(Path.GetDirectoryName(tmp)!).Attributes.HasFlag(FileAttributes.Hidden);
            return File.WriteAllTextAsync(tmp, "hola");
        }, default);
        Assert.True(hidden);
        Assert.Equal([final], Directory.GetFileSystemEntries(d.Path));
        Assert.False(File.GetAttributes(final).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public async Task SafeWrite_Fails_NothingLeftBehind()
    {
        using var d = new TempDir();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SafeWrite.WriteAsync(Path.Combine(d.Path, "out.txt"), async tmp =>
        {
            await File.WriteAllTextAsync(tmp, "a medias");
            throw new InvalidOperationException("falló");
        }, default));
        Assert.Empty(Directory.GetFileSystemEntries(d.Path));
    }

    [Fact]
    public void KindOf_FolderWithImageName_IsOther()
    {
        using var d = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(d.Path, "fotos.png")).FullName;
        Assert.Equal(FileKind.Other, Formats.KindOf(folder));
    }

    [Theory]
    [InlineData("a.JPEG", FileKind.Image)]
    [InlineData("a.heic", FileKind.Image)]
    [InlineData("a.webp", FileKind.Image)]
    [InlineData("a.PDF", FileKind.Pdf)]
    [InlineData("a.txt", FileKind.Other)]
    [InlineData("sin-extension", FileKind.Other)]
    public void KindOf(string name, FileKind expected) => Assert.Equal(expected, Formats.KindOf(name));

    [Fact]
    public void Ext_NormalizesJpeg() => Assert.Equal(".jpg", Formats.Ext("X.JPEG"));
}
