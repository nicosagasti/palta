using Mandarina.Core.Backends;

namespace Mandarina.Core.Operations;

public static class PdfOperations
{
    public const int RenderDpi = 200;

    public static IEnumerable<IOperation> All()
    {
        yield return new Operation("pdf-to-png", WheelKind.Convert, "PNG", "", AllPdf, Render(".png"));
        yield return new Operation("pdf-to-jpg", WheelKind.Convert, "JPG", "", AllPdf, Render(".jpg"));

        yield return Operation.Combine("pdf-merge", WheelKind.Tools, "Unir PDFs", "", FileKind.Pdf, 2,
            (files, ct) => SafeWrite.WriteAsync(OutputPaths.For(files[0], ".pdf", "unido"), tmp => Task.Run(() => PdfBackend.Merge(files, tmp), ct), ct));
        yield return new Operation("pdf-split", WheelKind.Tools, "Separar páginas", "",
            AllPdf,
            async (files, progress, ct) =>
            {
                var outputs = new List<string>();
                foreach (var f in files)
                    outputs.AddRange(await IntoFolder(f, dir => Task.Run(() => PdfBackend.Split(f, dir), ct), ct));
                progress.Report(1);
                return outputs;
            });
        yield return Operation.PerFile("pdf-rotate", WheelKind.Tools, "Rotar", "", FileKind.Pdf,
            (f, ct) => SafeWrite.WriteAsync(OutputPaths.For(f, ".pdf", "rotado"), tmp => Task.Run(() => PdfBackend.Rotate(f, tmp, 90), ct), ct));
    }

    private static bool AllPdf(IReadOnlyList<string> files) => files.All(f => Formats.KindOf(f) == FileKind.Pdf);

    private static Func<IReadOnlyList<string>, IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> Render(string ext) =>
        async (files, progress, ct) =>
        {
            var outputs = new List<string>();
            foreach (var f in files)
            {
                if (await PdfBackend.PageCountAsync(f) == 1)
                    outputs.Add(await SafeWrite.WriteAsync(OutputPaths.For(f, ext),
                        async tmp => await PdfBackend.RenderAsync(f, tmp, ext, RenderDpi, progress, ct), ct));
                else
                    outputs.AddRange(await IntoFolder(f, dir => PdfBackend.RenderAsync(f, dir, ext, RenderDpi, progress, ct), ct));
            }
            return outputs;
        };

    /// <summary>
    /// Genera los archivos en una carpeta temporal oculta y al final la renombra a "nombre (páginas)",
    /// así nunca queda una carpeta a medias.
    /// </summary>
    private static async Task<IReadOnlyList<string>> IntoFolder(string pdf, Func<string, Task<IReadOnlyList<string>>> produce, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(pdf))!;
        var tmp = Path.Combine(parent, $".mandarina-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var produced = await produce(tmp);
            ct.ThrowIfCancellationRequested();
            var final = OutputPaths.UniqueFolder(parent, $"{Path.GetFileNameWithoutExtension(pdf)} (páginas)");
            Directory.Move(tmp, final);
            return produced.Select(p => Path.Combine(final, Path.GetFileName(p))).ToList();
        }
        catch
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
