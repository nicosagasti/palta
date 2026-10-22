namespace Palta.Core;

/// <summary>Elige dónde se guarda un resultado: al lado del original y sin pisar nada.</summary>
public static class OutputPaths
{
    /// <summary>foto.png + ".jpg" → foto.jpg; con sufijo → "foto (comprimida).jpg"; si existe → "foto (1).jpg".</summary>
    public static string For(string source, string newExt, string? suffix = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
        var stem = Path.GetFileNameWithoutExtension(source);
        if (suffix is not null)
            stem += $" ({suffix})";
        return Unique(n => Path.Combine(dir, n + newExt), stem, p => File.Exists(p) || Directory.Exists(p));
    }

    public static string UniqueFolder(string parentDir, string name) =>
        Unique(n => Path.Combine(parentDir, n), name, p => File.Exists(p) || Directory.Exists(p));

    /// <summary>
    /// Archivo temporal dentro de una carpeta <c>.palta-…</c> en la misma carpeta (así se renombra al final
    /// sin copiar). La carpeta la crea <see cref="CreateHiddenFolder"/>: un archivo oculto no siempre se
    /// puede sobrescribir, una carpeta oculta sí se puede llenar.
    /// </summary>
    public static string TempFor(string finalPath) =>
        Path.Combine(Path.GetDirectoryName(finalPath)!, $".palta-{Guid.NewGuid():N}", $"temp{Path.GetExtension(finalPath)}");

    /// <summary>Crea la carpeta con el atributo oculto, para que no aparezca en el Explorador mientras se trabaja.</summary>
    public static void CreateHiddenFolder(string folder)
    {
        var info = Directory.CreateDirectory(folder);
        info.Attributes |= FileAttributes.Hidden;
    }

    private static string Unique(Func<string, string> build, string stem, Func<string, bool> taken)
    {
        var candidate = build(stem);
        for (var i = 1; taken(candidate); i++)
            candidate = build($"{stem} ({i})");
        return candidate;
    }
}
