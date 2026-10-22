namespace Palta.Core;

/// <summary>
/// Escribe a un temporal en una carpeta oculta al lado del destino y lo renombra al final;
/// pase lo que pase, no deja restos.
/// </summary>
public static class SafeWrite
{
    /// <returns>La ruta final, que puede cambiar si mientras tanto apareció un archivo con ese nombre.</returns>
    public static async Task<string> WriteAsync(string finalPath, Func<string, Task> writeTemp, CancellationToken ct)
    {
        var tmp = OutputPaths.TempFor(finalPath);
        var tmpFolder = Path.GetDirectoryName(tmp)!;
        OutputPaths.CreateHiddenFolder(tmpFolder);
        try
        {
            await writeTemp(tmp);
            ct.ThrowIfCancellationRequested();
            while (true)
            {
                try
                {
                    File.Move(tmp, finalPath, overwrite: false);
                    return finalPath;
                }
                catch (IOException) when (File.Exists(finalPath) || Directory.Exists(finalPath))
                {
                    finalPath = OutputPaths.For(finalPath, Path.GetExtension(finalPath));
                }
            }
        }
        finally
        {
            TryDeleteFolder(tmpFolder);
        }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal static void TryDeleteFolder(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
