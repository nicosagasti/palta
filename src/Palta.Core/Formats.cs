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

    /// <summary>
    /// Lo que dice la cápsula de la rueda: con un archivo, su nombre; con varios del mismo tipo,
    /// "12 imágenes"; mezclados, "15 archivos · 12 imágenes, 3 PDF" (de lo que más hay a lo que menos).
    /// </summary>
    public static string Summary(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return "";
        if (paths.Count == 1)
            return Path.GetFileName(paths[0].TrimEnd('\\', '/'));
        var groups = paths
            .GroupBy(p => Directory.Exists(p) ? "carpeta" : KindOf(p) switch
            {
                FileKind.Image => "imagen",
                FileKind.Pdf => "pdf",
                FileKind.Archive => "comprimido",
                _ => "archivo",
            })
            .Select(g => (Kind: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();
        static string Name(string kind, int n) => kind switch
        {
            "imagen" => n == 1 ? "1 imagen" : $"{n} imágenes",
            "pdf" => $"{n} PDF",
            "comprimido" => n == 1 ? "1 comprimido" : $"{n} comprimidos",
            "carpeta" => n == 1 ? "1 carpeta" : $"{n} carpetas",
            _ => n == 1 ? "1 archivo" : $"{n} archivos",
        };
        if (groups.Count == 1)
            return Name(groups[0].Kind, groups[0].Count);
        var total = groups.Any(g => g.Kind == "carpeta") ? $"{paths.Count} elementos" : $"{paths.Count} archivos";
        return $"{total} · {string.Join(", ", groups.Select(g => Name(g.Kind, g.Count)))}";
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
