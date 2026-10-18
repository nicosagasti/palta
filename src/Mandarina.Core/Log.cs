namespace Mandarina.Core;

/// <summary>Registro mínimo en %APPDATA%\Mandarina\log.txt; nunca lanza.</summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Path { get; set; } = System.IO.Path.Combine(Settings.AppDataFolder, "log.txt");

    public static void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}: {e}");

    public static void Info(string message) => Write("INFO", message);

    /// <summary>Solo con la variable de entorno MANDARINA_DEBUG=1.</summary>
    public static void Debug(string message)
    {
        if (DebugEnabled)
            Write("DEBUG", message);
    }

    private static readonly bool DebugEnabled = Environment.GetEnvironmentVariable("MANDARINA_DEBUG") == "1";

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                    File.Move(Path, Path + ".old", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }
}
