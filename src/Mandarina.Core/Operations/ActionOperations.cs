namespace Mandarina.Core.Operations;

/// <summary>Rueda Acciones: funcionan con cualquier archivo o carpeta.</summary>
public static class ActionOperations
{
    public static IEnumerable<IOperation> All(IShellHost host)
    {
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
    }

    private static bool Exists(IReadOnlyList<string> files) => files.All(f => File.Exists(f) || Directory.Exists(f));

    private static Task<IReadOnlyList<string>> Transfer(IShellHost host, string title, IReadOnlyList<string> files, bool move, IProgress<double> progress, CancellationToken ct)
    {
        var target = host.PickFolder(title);
        if (target is null)
            return Task.FromResult<IReadOnlyList<string>>([]);

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

    private static void CopyDirectory(string src, string dst, CancellationToken ct)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)), ct);
    }
}
