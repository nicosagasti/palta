using System.Diagnostics;
using System.IO.Compression;
using Palta.Core;
using Palta.Core.Backends;
using Palta.Core.Operations;
using Xunit;

namespace Palta.Tests;

public class ArchiveTests
{
    private static readonly Catalog Catalog = Catalog.CreateDefault(new FakeHost());

    private static Task<IReadOnlyList<string>> Run(string id, params string[] files) =>
        Catalog.ById(id)!.RunAsync(files, new Progress<double>(), default);

    private static string MakeFolder(TempDir d, string name)
    {
        var folder = Path.Combine(d.Path, name);
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        Directory.CreateDirectory(Path.Combine(folder, "vacía"));
        File.WriteAllText(Path.Combine(folder, "a.txt"), "hola");
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "chau");
        return folder;
    }

    [Fact]
    public void Zip_InToolsForAnything_Unzip_OnlyForArchives()
    {
        using var d = new TempDir();
        var txt = d.File("a.txt");
        var zip = d.File("b.zip");
        Assert.Contains("zip", Catalog.For(WheelKind.Tools, [txt, d.Path]).Select(o => o.Id));
        Assert.DoesNotContain("unzip", Catalog.For(WheelKind.Tools, [txt]).Select(o => o.Id));
        Assert.Equal("unzip", Catalog.For(WheelKind.Tools, [zip]).First().Id);
    }

    [Fact]
    public async Task Zip_Folder_KeepsStructure_AndRoundTrips()
    {
        using var d = new TempDir();
        var folder = MakeFolder(d, "Fotos");
        var zip = Assert.Single(await Run("zip", folder));
        Assert.Equal(Path.Combine(d.Path, "Fotos.zip"), zip);
        using (var archive = ZipFile.OpenRead(zip))
            Assert.Equal(["Fotos/a.txt", "Fotos/sub/b.txt", "Fotos/vacía/"], archive.Entries.Select(e => e.FullName).Order());

        Directory.Delete(folder, true);
        var extracted = Assert.Single(await Run("unzip", zip));
        // Adentro hay una sola carpeta: sale esa, sin "Fotos\Fotos".
        Assert.Equal(folder, extracted);
        Assert.Equal("chau", File.ReadAllText(Path.Combine(folder, "sub", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(folder, "vacía")));
        Assert.False(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public async Task Zip_SeveralFiles_OneArchive_NeverOverwrites()
    {
        using var d = new TempDir();
        var a = d.File("a.txt");
        var b = d.File("b.png");
        File.WriteAllText(Path.Combine(d.Path, "Archivos.zip"), "ya estaba");
        var zip = Assert.Single(await Run("zip", a, b));
        Assert.Equal(Path.Combine(d.Path, "Archivos (1).zip"), zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["a.txt", "b.png"], archive.Entries.Select(e => e.FullName).Order());
        // Un PNG ya viene comprimido: se guarda tal cual.
        var png = archive.Entries.First(e => e.Name == "b.png");
        Assert.Equal(png.Length, png.CompressedLength);
    }

    [Fact]
    public async Task Unzip_SeveralFilesInside_FolderNamedAfterArchive()
    {
        using var d = new TempDir();
        var zip = Path.Combine(d.Path, "copia.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("uno.txt");
            archive.CreateEntry("dos.txt");
        }
        var folder = Assert.Single(await Run("unzip", zip));
        Assert.Equal(Path.Combine(d.Path, "copia"), folder);
        Assert.Equal(["dos.txt", "uno.txt"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Unzip_PathEscapingEntry_Rejected_NothingLeft()
    {
        using var d = new TempDir();
        var inner = Directory.CreateDirectory(Path.Combine(d.Path, "adentro")).FullName;
        var zip = Path.Combine(inner, "malo.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("bien.txt");
            archive.CreateEntry("../escapado.txt");
        }
        await Assert.ThrowsAsync<ConversionException>(() => Run("unzip", zip));
        Assert.False(File.Exists(Path.Combine(d.Path, "escapado.txt")));
        Assert.Equal([zip], Directory.GetFileSystemEntries(inner));
    }

    [Fact]
    public async Task Unzip_TarGz_WithWindowsTar()
    {
        using var d = new TempDir();
        var folder = MakeFolder(d, "datos");
        var tgz = Path.Combine(d.Path, "datos.tar.gz");
        var tar = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"))
        {
            ArgumentList = { "-czf", tgz, "-C", d.Path, "datos" },
            CreateNoWindow = true,
        })!;
        await tar.WaitForExitAsync();
        Assert.Equal(0, tar.ExitCode);
        Directory.Delete(folder, true);

        var extracted = Assert.Single(await Run("unzip", tgz));
        Assert.Equal(folder, extracted);
        Assert.Equal("hola", File.ReadAllText(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public async Task Unzip_Damaged_FailsCleanly()
    {
        using var d = new TempDir();
        var rar = Path.Combine(d.Path, "roto.rar");
        File.WriteAllText(rar, "esto no es un rar");
        await Assert.ThrowsAsync<ConversionException>(() => Run("unzip", rar));
        Assert.Equal([rar], Directory.GetFileSystemEntries(d.Path));
    }

    [Theory]
    [InlineData("fotos.zip", "fotos")]
    [InlineData("copia.tar.gz", "copia")]
    [InlineData("x.7z", "x")]
    public void StemOf(string name, string stem) => Assert.Equal(stem, ArchiveOperations.StemOf(name));
}
