using Palta.Core;
using Palta.Core.Backends;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Palta.Tests;

public class ManyFilesTests
{
    private static readonly Catalog Catalog = Catalog.CreateDefault(new FakeHost());

    [Fact]
    public void Summary_OneFile_IsItsName() => Assert.Equal("foto.png", Formats.Summary([@"C:\x\foto.png"]));

    [Fact]
    public void Summary_SameKind() => Assert.Equal("3 imágenes", Formats.Summary([@"C:\x\a.png", @"C:\x\b.jpg", @"C:\x\c.webp"]));

    [Fact]
    public void Summary_Mixed_MostFirst() =>
        Assert.Equal("4 archivos · 2 PDF, 1 imagen, 1 archivo",
            Formats.Summary([@"C:\x\a.pdf", @"C:\x\b.pdf", @"C:\x\c.png", @"C:\x\d.txt"]));

    [Fact]
    public void Summary_WithFolders_SaysElementos()
    {
        using var d = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(d.Path, "Fotos")).FullName;
        Assert.Equal("2 elementos · 1 carpeta, 1 imagen", Formats.Summary([folder, d.File("a.png")]));
    }

    [Fact]
    public void MixedMerge_OnlyWhenImagesAndPdfTogether()
    {
        using var d = new TempDir();
        var png = Samples.Png(d.Path);
        var pdf = Samples.Pdf(d.Path);
        var png2 = Samples.Png(d.Path, "otra.png");
        var pdf2 = Samples.Pdf(d.Path, name: "otro.pdf");
        string[] Tools(params string[] f) => Catalog.For(WheelKind.Tools, f).Select(o => o.Id).ToArray();
        Assert.Contains("mixed-merge-pdf", Tools(png, pdf));
        Assert.DoesNotContain("mixed-merge-pdf", Tools(png, png2));
        Assert.DoesNotContain("mixed-merge-pdf", Tools(pdf, pdf2));
        Assert.DoesNotContain("mixed-merge-pdf", Tools(png, d.File("notas.txt")));
    }

    [Fact]
    public async Task MixedMerge_KeepsSelectionOrder_AndAllPages()
    {
        using var d = new TempDir();
        var pdf = Samples.Pdf(d.Path, pages: 3);
        var png = Samples.Png(d.Path);
        var output = Assert.Single(await Catalog.ById("mixed-merge-pdf")!.RunAsync([pdf, png], new Progress<double>(), default));
        using var merged = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        // Las 3 páginas del PDF y después la imagen.
        Assert.Equal(4, merged.PageCount);
        Assert.Equal(Path.Combine(d.Path, "muestra (unido).pdf"), output);
    }

    [Fact]
    public async Task PerFile_RunsInParallel_KeepsOrder_ReportsTotal()
    {
        var running = 0;
        var peak = 0;
        var reports = new List<double>();
        var op = Operation.PerFile("test", WheelKind.Tools, "Prueba", "", FileKind.Other, async (f, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            lock (reports)
                peak = Math.Max(peak, now);
            await Task.Delay(int.Parse(Path.GetFileNameWithoutExtension(f)), ct);
            Interlocked.Decrement(ref running);
            return f + ".out";
        });
        var previous = Operation.Parallelism;
        Operation.Parallelism = 3;
        try
        {
            // El primero tarda más: igual tiene que salir primero en los resultados.
            string[] files = ["300.x", "50.x", "50.x", "50.x", "50.x"];
            var outputs = await op.RunAsync(files, new SyncProgressFor(reports), default);
            Assert.Equal(files.Select(f => f + ".out"), outputs);
            Assert.InRange(peak, 2, 3);
            Assert.Equal(1.0, reports.Max());
            Assert.Equal(5, reports.Count);
        }
        finally
        {
            Operation.Parallelism = previous;
        }
    }

    [Fact]
    public async Task PerFile_OneFails_ErrorComesThrough()
    {
        var op = Operation.PerFile("test", WheelKind.Tools, "Prueba", "", FileKind.Other, async (f, ct) =>
        {
            await Task.Yield();
            return f == "malo" ? throw new ConversionException("se rompió") : f;
        });
        var e = await Assert.ThrowsAsync<ConversionException>(() => op.RunAsync(["bueno", "malo", "otro"], new Progress<double>(), default));
        Assert.Equal("se rompió", e.Message);
    }

    /// <summary>Progress&lt;T&gt; publica en otro hilo; para contar los avisos hace falta uno inmediato.</summary>
    private sealed class SyncProgressFor(List<double> sink) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (sink)
                sink.Add(value);
        }
    }
}
