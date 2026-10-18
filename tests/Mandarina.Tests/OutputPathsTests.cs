using Mandarina.Core;
using Xunit;

namespace Mandarina.Tests;

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
    public void TempFor_SameFolderHiddenName()
    {
        var t = OutputPaths.TempFor(@"C:\x\foto.jpg");
        Assert.Equal(@"C:\x", Path.GetDirectoryName(t));
        Assert.StartsWith(".mandarina-", Path.GetFileName(t));
        Assert.EndsWith(".jpg", t);
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
