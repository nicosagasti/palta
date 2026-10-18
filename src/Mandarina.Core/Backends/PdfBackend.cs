using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace Mandarina.Core.Backends;

/// <summary>PDF: PDFsharp para unir/separar/rotar y el renderizador de Windows para pasar páginas a imagen.</summary>
public static class PdfBackend
{
    public static void Merge(IReadOnlyList<string> pdfs, string dst)
    {
        using var output = new PdfDocument();
        foreach (var path in pdfs)
        {
            using var input = Open(path);
            foreach (var page in input.Pages)
                output.AddPage(page);
        }
        output.Save(dst);
    }

    /// <summary>Una página por archivo ("página 1.pdf", …) dentro de <paramref name="outDir"/>.</summary>
    public static IReadOnlyList<string> Split(string pdf, string outDir)
    {
        using var input = Open(pdf);
        var outputs = new List<string>();
        for (var i = 0; i < input.PageCount; i++)
        {
            using var single = new PdfDocument();
            single.AddPage(input.Pages[i]);
            var path = Path.Combine(outDir, $"página {i + 1}.pdf");
            single.Save(path);
            outputs.Add(path);
        }
        return outputs;
    }

    public static void Rotate(string pdf, string dst, int degrees)
    {
        using var output = new PdfDocument();
        using var input = Open(pdf);
        foreach (var page in input.Pages)
        {
            var added = output.AddPage(page);
            added.Rotate = ((added.Rotate + degrees) % 360 + 360) % 360;
        }
        output.Save(dst);
    }

    /// <summary>Una página por imagen, del tamaño de la imagen (a 96 DPI).</summary>
    public static void ImagesToPdf(IReadOnlyList<string> images, string dst)
    {
        using var output = new PdfDocument();
        foreach (var path in images)
        {
            var jpeg = ImageBackend.ToJpegBytes(path, out var w, out var h);
            using var stream = new MemoryStream(jpeg);
            using var image = XImage.FromStream(stream);
            var page = output.AddPage();
            var (pw, ph) = (w * 72.0 / 96, h * 72.0 / 96);
            page.Width = XUnit.FromPoint(pw);
            page.Height = XUnit.FromPoint(ph);
            using var g = XGraphics.FromPdfPage(page);
            g.DrawImage(image, 0, 0, pw, ph);
        }
        output.Save(dst);
    }

    public static async Task<int> PageCountAsync(string pdf) => (int)(await LoadForRender(pdf)).PageCount;

    /// <summary>
    /// Renderiza cada página a PNG o JPG a <paramref name="dpi"/>. Con una sola página <paramref name="singleOrDir"/>
    /// es el archivo destino; con varias, la carpeta donde van "página N.ext".
    /// </summary>
    public static async Task<IReadOnlyList<string>> RenderAsync(string pdf, string singleOrDir, string ext, int dpi, IProgress<double>? progress, CancellationToken ct)
    {
        var doc = await LoadForRender(pdf);
        // Windows escala tanto page.Size como DestinationWidth por el DPI de la pantalla (p. ej. ×1,25).
        // Pedimos el ancho calculado desde los puntos de PDFsharp y, si sale escalado, lo corregimos
        // dividiendo por el factor que medimos en la primera página.
        var widthsPt = PageWidthsInPoints(pdf);
        var displayScale = 1.0;
        var outputs = new List<string>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var target = doc.PageCount == 1 ? singleOrDir : Path.Combine(singleOrDir, $"página {i + 1}{ext}");
            using var page = doc.GetPage(i);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new WinPdf.PdfPageRenderOptions
            {
                // Solo el ancho: Windows mantiene la proporción.
                DestinationWidth = (uint)Math.Round(widthsPt[(int)i] * dpi / 72.0 / displayScale),
            }).AsTask(ct);
            using var png = stream.AsStreamForRead();
            using var img = new ImageMagick.MagickImage(png);
            var wanted = (uint)Math.Round(widthsPt[(int)i] * dpi / 72.0);
            if (i == 0)
                displayScale = img.Width / (double)wanted;
            if (Math.Abs((int)img.Width - (int)wanted) > 1)
                img.Resize(wanted, 0);
            if (ext == ".jpg")
            {
                img.BackgroundColor = ImageMagick.MagickColors.White;
                img.Alpha(ImageMagick.AlphaOption.Remove);
                img.Quality = 90;
            }
            img.Write(target, ImageBackend.FormatFor(ext));
            outputs.Add(target);
            progress?.Report((i + 1) / (double)doc.PageCount);
        }
        return outputs;
    }

    /// <summary>Ancho visible de cada página en puntos, con la rotación aplicada.</summary>
    private static double[] PageWidthsInPoints(string pdf)
    {
        using var doc = Open(pdf);
        return doc.Pages.Cast<PdfPage>()
            .Select(p => p.Rotate % 180 == 0 ? p.Width.Point : p.Height.Point)
            .ToArray();
    }

    private static async Task<WinPdf.PdfDocument> LoadForRender(string pdf)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdf));
            return await WinPdf.PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ConversionException($"No se pudo abrir {Path.GetFileName(pdf)}: {e.Message}", e);
        }
    }

    private static PdfDocument Open(string path)
    {
        try
        {
            return PdfReader.Open(path, PdfDocumentOpenMode.Import);
        }
        catch (Exception e)
        {
            throw new ConversionException($"No se pudo abrir {Path.GetFileName(path)} (¿está protegido o dañado?).", e);
        }
    }
}
