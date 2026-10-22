namespace Palta.Core.Operations;

/// <summary>
/// Rueda Acciones, las mismas de Tangerine en el mismo orden: Compartir (AirDrop), Flotar, Copiar,
/// Mover, Copiar ruta y Terminal. Funcionan con cualquier archivo o carpeta.
/// </summary>
public static class ActionOperations
{
    public static IEnumerable<IOperation> All(IShellHost host)
    {
        yield return new Operation("share", WheelKind.Actions, "Compartir", "", Exists, (files, progress, _) => Done(progress, () => host.Share(files)))
        {
            Quiet = true,
        };
        yield return new Operation("float", WheelKind.Actions, "Flotar", "", Exists, (files, progress, _) => Done(progress, () => host.Float(files)))
        {
            Quiet = true,
        };
        yield return new Operation("copy-to", WheelKind.Actions, "Copiar a…", "", Exists,
            (files, progress, ct) => Transfer(host, "Copiar a…", files, move: false, progress, ct));
        yield return new Operation("move-to", WheelKind.Actions, "Mover a…", "", Exists,
            (files, progress, ct) => Transfer(host, "Mover a…", files, move: true, progress, ct));
        yield return new Operation("copy-path", WheelKind.Actions, "Copiar ruta", "", _ => true,
            (files, progress, _) =>
            {
                host.SetClipboardText(string.Join("\r\n", files));
                progress.Report(1);
                return Task.FromResult<IReadOnlyList<string>>([]);
            });
        yield return new Operation("terminal", WheelKind.Actions, "Terminal", "", Exists, (files, progress, _) => Done(progress, () => host.OpenTerminal(FolderOf(files[0]))))
        {
            Quiet = true,
        };
    }

    private static Task<IReadOnlyList<string>> Done(IProgress<double> progress, Action action)
    {
        action();
        progress.Report(1);
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>Una carpeta es su propia carpeta; un archivo, la que lo contiene.</summary>
    private static string FolderOf(string path) =>
        Directory.Exists(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : Path.GetDirectoryName(Path.GetFullPath(path))!;

    private static bool Exists(IReadOnlyList<string> files) => files.All(f => File.Exists(f) || Directory.Exists(f));

    private static Task<IReadOnlyList<string>> Transfer(IShellHost host, string title, IReadOnlyList<string> files, bool move, IProgress<double> progress, CancellationToken ct)
    {
        var target = host.PickFolder(title) ?? throw new CanceledByUserException();

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var outputs = new List<string>();
            for (var i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var src = files[i];
                var isDir = Directory.Exists(src);
                var name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
                var dst = isDir
                    ? OutputPaths.UniqueFolder(target, name)
                    : OutputPaths.For(Path.Combine(target, name), Path.GetExtension(name));
                if (isDir)
                {
                    if (IsInside(target, src))
                        throw new Backends.ConversionException($"No se puede {(move ? "mover" : "copiar")} «{name}» dentro de sí misma.");
                    if (move && SameVolume(src, target))
                        Directory.Move(src, dst);
                    else
                    {
                        CopyDirectory(src, dst, ct);
                        if (move)
                            Directory.Delete(src, true);
                    }
                }
                else if (move)
                    File.Move(src, dst);
                else
                    File.Copy(src, dst);
                outputs.Add(dst);
                progress.Report((i + 1) / (double)files.Count);
            }
            return outputs;
        }, ct);
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>¿<paramref name="path"/> es <paramref name="folder"/> o está adentro?</summary>
    private static bool IsInside(string path, string folder)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return p.Equals(f, StringComparison.OrdinalIgnoreCase) || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string src, string dst, CancellationToken ct)
    {
        // Se lista todo antes de crear nada, y no se siguen accesos directos de carpeta
        // (junctions/symlinks): un bucle de enlaces copiaría sin fin.
        var files = Directory.GetFiles(src);
        var dirs = Directory.GetDirectories(src).Where(d => !File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)).ToArray();
        Directory.CreateDirectory(dst);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        }
        foreach (var dir in dirs)
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)), ct);
    }
}
