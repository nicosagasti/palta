using System.Diagnostics;
using System.IO.Compression;
using Palta.Core.Backends;

namespace Palta.Core.Operations;

/// <summary>
/// Comprimir en ZIP (el formato que se abre en cualquier sistema sin instalar nada) y descomprimir
/// ZIP, RAR, 7Z, TAR y compañía. ZIP lo hace .NET; el resto, el tar.exe que trae Windows (libarchive).
/// Nada se pisa: el resultado va al lado, con nombre único, y se arma en una carpeta oculta.
/// </summary>
public static class ArchiveOperations
{
    /// <summary>Formatos que ya vienen comprimidos: recomprimirlos tarda y no ahorra casi nada.</summary>
    private static readonly HashSet<string> AlreadyCompressed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".avif",
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac",
        ".zip", ".rar", ".7z", ".gz", ".tgz", ".bz2", ".xz", ".zst",
        ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".epub", ".jar", ".apk",
    };

    public static IEnumerable<IOperation> All()
    {
        yield return new Operation("unzip", WheelKind.Tools, "Descomprimir", "",
            files => files.All(f => Formats.KindOf(f) == FileKind.Archive), ExtractAll);
        yield return new Operation("zip", WheelKind.Tools, "ZIP", "",
            files => files.All(f => File.Exists(f) || Directory.Exists(f)),
            async (files, progress, ct) => [await Zip(files, progress, ct)]);
    }

    // ---------- Comprimir ----------

    private static Task<string> Zip(IReadOnlyList<string> items, IProgress<double> progress, CancellationToken ct)
    {
        var first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(items[0]));
        var parent = Path.GetDirectoryName(first)!;
        // Una carpeta o un archivo: "Fotos.zip" / "informe.zip". Varios: "Archivos.zip".
        var name = items.Count == 1 ? Path.GetFileName(first) : "Archivos";
        var final = OutputPaths.For(Path.Combine(parent, name + ".zip"), ".zip");
        if (items.Count == 1 && File.Exists(first))
            final = OutputPaths.For(first, ".zip");

        return SafeWrite.WriteAsync(final, tmp => Task.Run(() =>
        {
            var entries = new List<(string Path, string Name, bool IsDir)>();
            foreach (var item in items)
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item));
                if (Directory.Exists(full))
                    Collect(full, Path.GetFileName(full), entries);
                else
                    entries.Add((full, Path.GetFileName(full), false));
            }
            var total = Math.Max(1, entries.Where(e => !e.IsDir).Sum(e => new FileInfo(e.Path).Length));
            long done = 0;
            var buffer = new byte[1 << 16];
            using var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var (path, entryName, isDir) in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (isDir)
                {
                    zip.CreateEntry(entryName + "/");
                    continue;
                }
                var level = AlreadyCompressed.Contains(Path.GetExtension(path)) ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
                var entry = zip.CreateEntry(entryName, level);
                entry.LastWriteTime = File.GetLastWriteTime(path);
                using var input = File.OpenRead(path);
                using var output = entry.Open();
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    done += read;
                    progress.Report(Math.Min(0.99, done / (double)total));
                }
            }
        }, ct), ct);
    }

    /// <summary>Todo lo de adentro de la carpeta, con rutas relativas; las carpetas vacías también. No sigue enlaces.</summary>
    private static void Collect(string folder, string prefix, List<(string, string, bool)> entries)
    {
        var files = Directory.GetFiles(folder);
        var dirs = Directory.GetDirectories(folder).Where(d => !File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)).ToArray();
        if (files.Length == 0 && dirs.Length == 0)
            entries.Add((folder, prefix, true));
        foreach (var file in files)
            entries.Add((file, $"{prefix}/{Path.GetFileName(file)}", false));
        foreach (var dir in dirs)
            Collect(dir, $"{prefix}/{Path.GetFileName(dir)}", entries);
    }

    // ---------- Descomprimir ----------

    private static async Task<IReadOnlyList<string>> ExtractAll(IReadOnlyList<string> archives, IProgress<double> progress, CancellationToken ct)
    {
        var outputs = new List<string>();
        for (var i = 0; i < archives.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var slice = new SliceProgress(progress, i, archives.Count);
            outputs.Add(await Task.Run(() => Extract(archives[i], slice, ct), ct));
        }
        return outputs;
    }

    /// <summary>
    /// Extrae en una carpeta oculta y recién al final la deja a la vista. Si adentro hay una sola carpeta
    /// o un solo archivo, sale eso directamente (sin "Fotos\Fotos"); si no, una carpeta con el nombre del comprimido.
    /// </summary>
    internal static string Extract(string archive, IProgress<double> progress, CancellationToken ct)
    {
        var full = Path.GetFullPath(archive);
        var parent = Path.GetDirectoryName(full)!;
        var tmp = Path.Combine(parent, $".palta-{Guid.NewGuid():N}");
        OutputPaths.CreateHiddenFolder(tmp);
        try
        {
            if (Formats.Ext(full) == ".zip")
                ExtractZip(full, tmp, progress, ct);
            else
                ExtractWithTar(full, tmp, ct);
            ct.ThrowIfCancellationRequested();

            var top = Directory.GetFileSystemEntries(tmp);
            if (top.Length == 1 && Directory.Exists(top[0]))
            {
                var final = OutputPaths.UniqueFolder(parent, Path.GetFileName(top[0]));
                Directory.Move(top[0], final);
                return final;
            }
            if (top.Length == 1)
            {
                var final = OutputPaths.For(Path.Combine(parent, Path.GetFileName(top[0])), Path.GetExtension(top[0]));
                File.Move(top[0], final);
                return final;
            }
            var folder = OutputPaths.UniqueFolder(parent, StemOf(full));
            Directory.Move(tmp, folder);
            var info = new DirectoryInfo(folder);
            info.Attributes &= ~FileAttributes.Hidden;
            return folder;
        }
        finally
        {
            if (Directory.Exists(tmp))
                SafeWrite.TryDeleteFolder(tmp);
        }
    }

    /// <summary>"fotos.zip" → "fotos"; "copia.tar.gz" → "copia".</summary>
    public static string StemOf(string archive)
    {
        var stem = Path.GetFileNameWithoutExtension(archive);
        return stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? stem[..^4] : stem;
    }

    private static void ExtractZip(string zipPath, string dest, IProgress<double> progress, CancellationToken ct)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dest)) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        var count = Math.Max(1, zip.Entries.Count);
        for (var i = 0; i < zip.Entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = zip.Entries[i];
            var target = Path.GetFullPath(Path.Combine(dest, entry.FullName));
            // Un ZIP armado con "..\" podría escribir fuera de la carpeta.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ConversionException("El ZIP intenta escribir fuera de su carpeta; no se extrajo nada.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
            }
            progress.Report((i + 1) / (double)count);
        }
    }

    /// <summary>
    /// RAR, 7Z, TAR, GZ…: el tar.exe de Windows es bsdtar (libarchive), que los lee todos y por defecto
    /// rechaza rutas absolutas y con "..".
    /// </summary>
    private static void ExtractWithTar(string archive, string dest, CancellationToken ct)
    {
        var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tar))
            throw new ConversionException($"Para abrir archivos {Formats.Ext(archive).TrimStart('.').ToUpperInvariant()} hace falta el tar.exe de Windows 10 o 11.");
        var info = new ProcessStartInfo(tar)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "-xf", archive, "-C", dest })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new ConversionException("No se pudo abrir tar.exe.");
        var error = process.StandardError.ReadToEndAsync(ct);
        _ = process.StandardOutput.ReadToEndAsync(ct);
        try
        {
            process.WaitForExitAsync(ct).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        if (process.ExitCode != 0)
        {
            var detail = error.IsCompletedSuccessfully ? error.Result.Split('\n', 2)[0].Trim() : "";
            throw new ConversionException(detail.Length > 0
                ? $"No se pudo descomprimir: {detail.Replace("tar.exe: ", "")}"
                : "No se pudo descomprimir: el archivo está dañado o no es un comprimido.");
        }
    }

    /// <summary>Reparte el progreso total entre varios comprimidos.</summary>
    private sealed class SliceProgress(IProgress<double> total, int index, int count) : IProgress<double>
    {
        public void Report(double value) => total.Report((index + Math.Clamp(value, 0, 1)) / count);
    }
}
