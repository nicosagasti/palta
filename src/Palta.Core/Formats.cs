namespace Palta.Core;

public enum FileKind { Image, Pdf, Archive, Other }

public static class Formats
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".webp", ".heic", ".heif", ".avif", ".bmp", ".gif", ".tif", ".tiff", ".ico",
    };

    /// <summary>Comprimidos que se pueden extraer (ZIP con .NET; el resto con el tar.exe de Windows).</summary>
    public static readonly IReadOnlySet<string> ArchiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst",
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

    /// <summary>Por la extensión; una carpeta es siempre <see cref="FileKind.Other"/> aunque se llame "fotos.png".</summary>
    public static FileKind KindOf(string path)
    {
        if (Directory.Exists(path))
            return FileKind.Other;
        var ext = Ext(path);
        if (ext == ".pdf")
            return FileKind.Pdf;
        if (ArchiveExtensions.Contains(ext))
            return FileKind.Archive;
        return ImageExtensions.Contains(ext) ? FileKind.Image : FileKind.Other;
    }
}
