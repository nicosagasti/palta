using ImageMagick;
using Palta.Core.Backends;

namespace Palta.Core.Operations;

public static class ImageOperations
{
    private const string PictureGlyph = "";

    public static IEnumerable<IOperation> All()
    {
        foreach (var (ext, label) in new[]
                 {
                     (".jpg", "JPG"), (".png", "PNG"), (".webp", "WebP"), (".avif", "AVIF"),
                     (".bmp", "BMP"), (".tiff", "TIFF"), (".ico", "ICO"),
                 })
        {
            yield return Operation.PerFile($"img-to-{ext[1..]}", WheelKind.Convert, label, PictureGlyph, FileKind.Image,
                (f, ct) => ImageBackend.SaveAsync(f, OutputPaths.For(f, ext), ct),
                accepts: f => Formats.Ext(f) != ext);
        }
        yield return Operation.PerFile("img-to-pdf", WheelKind.Convert, "PDF", "", FileKind.Image,
            (f, ct) => SafeWrite.WriteAsync(OutputPaths.For(f, ".pdf"), tmp => Task.Run(() => PdfBackend.ImagesToPdf([f], tmp), ct), ct));

        yield return Operation.PerFile("img-compress", WheelKind.Tools, "Comprimir", "", FileKind.Image, Compress);
        yield return Transform("img-half", "50 %", "", "50%", img => img.Resize(new Percentage(50)));
        yield return Transform("img-max1920", "1920 px", "", "1920px", img => img.Resize(new MagickGeometry(1920, 1920) { Greater = true }));
        yield return Transform("img-rot-right", "Girar der.", "", "rotada", img => img.Rotate(90));
        yield return Transform("img-rot-left", "Girar izq.", "", "rotada", img => img.Rotate(-90));
        yield return Transform("img-strip", "Sin metadatos", "", "sin metadatos", img => img.Strip());
        yield return Operation.Combine("img-merge-pdf", WheelKind.Tools, "Unir en PDF", "", FileKind.Image, 2,
            (files, ct) => SafeWrite.WriteAsync(OutputPaths.For(files[0], ".pdf", "unidas"), tmp => Task.Run(() => PdfBackend.ImagesToPdf(files, tmp), ct), ct));
    }

    private static Operation Transform(string id, string label, string glyph, string suffix, Action<MagickImage> transform) =>
        Operation.PerFile(id, WheelKind.Tools, label, glyph, FileKind.Image,
            (f, ct) => ImageBackend.SaveAsync(f, OutputPaths.For(f, SameFormatExt(f), suffix), ct, transform));

    /// <summary>JPG/WebP con calidad 75, PNG optimizado sin pérdida; el resto pasa a JPG.</summary>
    private static Task<string> Compress(string f, CancellationToken ct)
    {
        var ext = Formats.Ext(f) switch
        {
            ".png" => ".png",
            ".webp" => ".webp",
            _ => ".jpg",
        };
        return ImageBackend.SaveAsync(f, OutputPaths.For(f, ext, "comprimida"), ct,
            quality: ext == ".png" ? null : ImageBackend.CompressQuality, optimizePng: true);
    }

    /// <summary>Formato de salida para herramientas: el mismo, salvo los que Magick no escribe bien (HEIC → JPG).</summary>
    private static string SameFormatExt(string f) => Formats.Ext(f) switch
    {
        ".heic" or ".heif" => ".jpg",
        var e => e,
    };
}
