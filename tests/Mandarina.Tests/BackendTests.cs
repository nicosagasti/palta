using ImageMagick;
using Mandarina.Core;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Mandarina.Tests;

public class BackendTests
{
    private static readonly Catalog Cat = Catalog.CreateDefault(new FakeHost());

    private static async Task<IReadOnlyList<string>> Run(string id, params string[] files)
    {
        var op = Cat.ById(id) ?? throw new InvalidOperationException($"falta {id}");
        Assert.True(op.AppliesTo(files), $"{id} debería aplicar");
        return await op.RunAsync(files, new Progress<double>(), default);
    }

    [Theory]
    [InlineData("img-to-jpg", ".jpg", MagickFormat.Jpeg)]
    [InlineData("img-to-webp", ".webp", MagickFormat.WebP)]
    [InlineData("img-to-avif", ".avif", MagickFormat.Avif)]
    [InlineData("img-to-bmp", ".bmp", MagickFormat.Bmp)]
    [InlineData("img-to-tiff", ".tiff", MagickFormat.Tiff)]
    [InlineData("img-to-ico", ".ico", MagickFormat.Ico)]
    public async Task ImageConvert_ProducesFormat_OriginalUntouched(string id, string ext, MagickFormat format)
    {
        using var d = new TempDir();
        var src = Samples.Png(d.Path);
        var before = Samples.Hash(src);
        var output = (await Run(id, src)).Single();
        Assert.Equal(Path.Combine(d.Path, "muestra" + ext), output);
        using var img = new MagickImage(output);
        Assert.Equal(format, img.Format);
        Assert.Equal(before, Samples.Hash(src));
    }

    [Fact]
    public async Task JpgToPng()
    {
        using var d = new TempDir();
        var output = (await Run("img-to-png", Samples.Jpg(d.Path))).Single();
        using var img = new MagickImage(output);
        Assert.Equal(MagickFormat.Png, img.Format);
    }

    [Fact]
    public async Task PngWithAlpha_ToJpg_WhiteBackground()
    {
        using var d = new TempDir();
        var output = (await Run("img-to-jpg", Samples.Png(d.Path))).Single();
        using var img = new MagickImage(output);
        var corner = img.GetPixels().GetPixel(0, 0).ToColor()!;
        Assert.True(corner.R > 250 && corner.G > 250 && corner.B > 250, $"esquina {corner}");
    }

    [Fact]
    public void Convert_DoesNotOfferCurrentFormat()
    {
        var ids = Cat.For(WheelKind.Convert, [@"C:\x\a.png"]).Select(o => o.Id).ToList();
        Assert.DoesNotContain("img-to-png", ids);
        Assert.Contains("img-to-jpg", ids);
        Assert.DoesNotContain("img-to-jpg", Cat.For(WheelKind.Convert, [@"C:\x\a.JPEG"]).Select(o => o.Id));
    }

    [Fact]
    public void For_MixedImageAndPdf_OnlyCommonOps()
    {
        string[] mixed = [@"C:\x\a.png", @"C:\x\b.pdf"];
        Assert.Empty(Cat.For(WheelKind.Tools, mixed));
        Assert.Empty(Cat.For(WheelKind.Convert, mixed));
        Assert.Contains("copy-path", Cat.For(WheelKind.Actions, mixed).Select(o => o.Id));
    }

    [Fact]
    public void For_TextFile_OnlyActions()
    {
        Assert.Empty(Cat.For(WheelKind.Convert, [@"C:\x\a.txt"]));
        Assert.Empty(Cat.For(WheelKind.Tools, [@"C:\x\a.txt"]));
    }

    [Fact]
    public async Task Half()
    {
        using var d = new TempDir();
        var output = (await Run("img-half", Samples.Png(d.Path))).Single();
        Assert.Equal(Path.Combine(d.Path, "muestra (50%).png"), output);
        using var img = new MagickImage(output);
        Assert.Equal((200u, 150u), (img.Width, img.Height));
    }

    [Fact]
    public async Task Max1920()
    {
        using var d = new TempDir();
        var output = (await Run("img-max1920", Samples.Jpg(d.Path))).Single();
        Assert.Equal(Path.Combine(d.Path, "grande (1920px).jpg"), output);
        using var img = new MagickImage(output);
        Assert.Equal((1920u, 1280u), (img.Width, img.Height));
    }

    [Fact]
    public async Task RotateRight()
    {
        using var d = new TempDir();
        var output = (await Run("img-rot-right", Samples.Png(d.Path))).Single();
        Assert.Equal(Path.Combine(d.Path, "muestra (rotada).png"), output);
        using var img = new MagickImage(output);
        Assert.Equal((300u, 400u), (img.Width, img.Height));
    }

    [Fact]
    public async Task Strip_RemovesExif()
    {
        using var d = new TempDir();
        var src = Samples.Jpg(d.Path);
        using (var original = new MagickImage(src))
            Assert.NotNull(original.GetExifProfile());
        var output = (await Run("img-strip", src)).Single();
        Assert.Equal(Path.Combine(d.Path, "grande (sin metadatos).jpg"), output);
        using var img = new MagickImage(output);
        Assert.Null(img.GetExifProfile());
    }

    [Fact]
    public async Task Compress_Jpg_IsSmaller()
    {
        using var d = new TempDir();
        var src = Samples.Jpg(d.Path);
        using (var noisy = new MagickImage(src))
        {
            noisy.AddNoise(NoiseType.Gaussian);
            noisy.Quality = 98;
            noisy.Write(src);
        }
        var output = (await Run("img-compress", src)).Single();
        Assert.Equal(Path.Combine(d.Path, "grande (comprimida).jpg"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(src).Length);
    }

    [Fact]
    public async Task MergeImagesToPdf()
    {
        using var d = new TempDir();
        var a = Samples.Png(d.Path, "a.png");
        var b = Samples.Jpg(d.Path, "b.jpg");
        var output = (await Run("img-merge-pdf", a, b)).Single();
        Assert.Equal(2, PdfReader.Open(output, PdfDocumentOpenMode.Import).PageCount);
    }

    [Fact]
    public void MergeImagesToPdf_NeedsTwoFiles() =>
        Assert.False(Cat.ById("img-merge-pdf")!.AppliesTo([@"C:\x\a.png"]));

    [Fact]
    public async Task ImageToPdf()
    {
        using var d = new TempDir();
        var output = (await Run("img-to-pdf", Samples.Png(d.Path))).Single();
        Assert.Equal(Path.Combine(d.Path, "muestra.pdf"), output);
        Assert.Equal(1, PdfReader.Open(output, PdfDocumentOpenMode.Import).PageCount);
    }

    [Fact]
    public async Task PdfMerge()
    {
        using var d = new TempDir();
        var a = Samples.Pdf(d.Path, 3, "a.pdf");
        var b = Samples.Pdf(d.Path, 3, "b.pdf");
        var output = (await Run("pdf-merge", a, b)).Single();
        Assert.Equal(Path.Combine(d.Path, "a (unido).pdf"), output);
        Assert.Equal(6, PdfReader.Open(output, PdfDocumentOpenMode.Import).PageCount);
    }

    [Fact]
    public async Task PdfSplit()
    {
        using var d = new TempDir();
        var outputs = await Run("pdf-split", Samples.Pdf(d.Path));
        Assert.Equal(3, outputs.Count);
        Assert.All(outputs, o => Assert.Equal(Path.Combine(d.Path, "muestra (páginas)"), Path.GetDirectoryName(o)));
        Assert.Equal("página 1.pdf", Path.GetFileName(outputs[0]));
    }

    [Fact]
    public async Task PdfRotate()
    {
        using var d = new TempDir();
        var output = (await Run("pdf-rotate", Samples.Pdf(d.Path))).Single();
        Assert.Equal(Path.Combine(d.Path, "muestra (rotado).pdf"), output);
        var doc = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.All(doc.Pages.Cast<PdfSharp.Pdf.PdfPage>(), p => Assert.Equal(90, p.Rotate));
    }

    [Fact]
    public async Task PdfToPng_MultiPage_GoesToFolder()
    {
        using var d = new TempDir();
        var outputs = await Run("pdf-to-png", Samples.Pdf(d.Path));
        Assert.Equal(3, outputs.Count);
        Assert.All(outputs, o => Assert.Equal(Path.Combine(d.Path, "muestra (páginas)"), Path.GetDirectoryName(o)));
        using var img = new MagickImage(outputs[0]);
        Assert.Equal(MagickFormat.Png, img.Format);
        // A4/Letter a 200 DPI: unos 1650–1700 px de ancho.
        Assert.InRange(img.Width, 1600u, 1750u);
    }

    [Fact]
    public async Task PdfToPng_OwnerPasswordPdf_Renders()
    {
        // Facturas y resúmenes bancarios: contraseña de propietario, sin contraseña para abrir.
        using var d = new TempDir();
        var path = Path.Combine(d.Path, "protegido.pdf");
        using (var doc = new PdfSharp.Pdf.PdfDocument())
        {
            doc.AddPage();
            doc.AddPage();
            doc.SecuritySettings.OwnerPassword = "duenio";
            doc.SecuritySettings.PermitModifyDocument = false;
            doc.SecuritySettings.PermitExtractContent = false;
            doc.Save(path);
        }
        var outputs = await Run("pdf-to-png", path);
        Assert.Equal(2, outputs.Count);
        using var img = new MagickImage(outputs[0]);
        Assert.InRange(img.Width, 1600u, 1750u);
    }

    [Fact]
    public async Task PdfToJpg_SinglePage_NextToOriginal()
    {
        using var d = new TempDir();
        var outputs = await Run("pdf-to-jpg", Samples.Pdf(d.Path, 1));
        Assert.Equal(Path.Combine(d.Path, "muestra.jpg"), outputs.Single());
    }

    [Fact]
    public async Task BrokenImage_Throws_NoTempLeft()
    {
        using var d = new TempDir();
        var src = d.File("roto.png", "esto no es una imagen");
        await Assert.ThrowsAnyAsync<Exception>(() => Run("img-to-jpg", src));
        Assert.Equal(["roto.png"], d.Entries());
    }
}
