namespace Mandarina.Core;

public enum FileKind { Image, Pdf, Other }

public static class Formats
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".webp", ".heic", ".heif", ".avif", ".bmp", ".gif", ".tif", ".tiff", ".ico",
    };

    /// <summary>Extensión en minúscula con punto; .jpeg → .jpg, .tif → .tiff.</summary>
    public static string Ext(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpeg" or ".jpe" => ".jpg",
            ".tif" => ".tiff",
            _ => ext,
        };
    }

    public static FileKind KindOf(string path)
    {
        var ext = Ext(path);
        if (ext == ".pdf")
            return FileKind.Pdf;
        return ImageExtensions.Contains(ext) ? FileKind.Image : FileKind.Other;
    }
}
