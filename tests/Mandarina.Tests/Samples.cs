using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Mandarina.Tests;

/// <summary>Archivos de muestra generados en el momento.</summary>
public static class Samples
{
    /// <summary>PNG 400×300 con transparencia y un rectángulo rojo.</summary>
    public static string Png(string dir, string name = "muestra.png")
    {
        var path = Path.Combine(dir, name);
        using var img = new MagickImage(MagickColors.Transparent, 400, 300);
        new Drawables().FillColor(MagickColors.Red).Rectangle(50, 50, 200, 150).Draw(img);
        img.Write(path, MagickFormat.Png);
        return path;
    }

    /// <summary>JPG 3000×2000 con metadatos EXIF.</summary>
    public static string Jpg(string dir, string name = "grande.jpg")
    {
        var path = Path.Combine(dir, name);
        using var img = new MagickImage(MagickColors.SkyBlue, 3000, 2000);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Mandarina Test");
        img.SetProfile(exif);
        img.Write(path, MagickFormat.Jpeg);
        return path;
    }

    public static string Pdf(string dir, int pages = 3, string name = "muestra.pdf")
    {
        var path = Path.Combine(dir, name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            g.DrawRectangle(XBrushes.SteelBlue, 50 + i * 20, 50, 200, 100);
        }
        doc.Save(path);
        return path;
    }

    public static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
