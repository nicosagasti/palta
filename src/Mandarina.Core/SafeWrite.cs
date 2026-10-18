namespace Mandarina.Core;

/// <summary>Escribe a un temporal al lado del destino y lo renombra al final; si algo falla, no deja restos.</summary>
public static class SafeWrite
{
    /// <returns>La ruta final, que puede cambiar si mientras tanto apareció un archivo con ese nombre.</returns>
    public static async Task<string> WriteAsync(string finalPath, Func<string, Task> writeTemp, CancellationToken ct)
    {
        var tmp = OutputPaths.TempFor(finalPath);
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
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
