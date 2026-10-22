using System.Text.Json;

namespace Palta.Core;

public sealed class Settings
{
    public string ConvertKey { get; set; } = "Shift";
    public string ToolsKey { get; set; } = "Ctrl";
    public string ActionsKey { get; set; } = "Alt";
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public string ClipboardHotkey { get; set; } = "Ctrl+Alt+V";
    public bool ClipboardEnabled { get; set; } = true;

    /// <summary>Límites del historial del portapapeles; 0 = sin ese límite. Ver <see cref="Clipboard.ClipboardLimits"/>.</summary>
    public int ClipboardKeepDays { get; set; } = Clipboard.ClipboardLimits.Default.KeepDays;
    public int ClipboardMaxItems { get; set; } = Clipboard.ClipboardLimits.Default.MaxItems;
    public int ClipboardMaxImageMegabytes { get; set; } = Clipboard.ClipboardLimits.Default.MaxImageMegabytes;

    /// <summary>El portapapeles quedó flotando (picture in picture) y dónde, en píxeles de pantalla.</summary>
    public bool ClipboardFloating { get; set; }
    public int? ClipboardFloatX { get; set; }
    public int? ClipboardFloatY { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Clipboard.ClipboardLimits ClipboardLimits =>
        new(Math.Max(0, ClipboardKeepDays), Math.Max(0, ClipboardMaxItems), Math.Max(0, ClipboardMaxImageMegabytes));
    public bool StartWithWindows { get; set; }

    /// <summary>"windows" (por defecto: oscuro, opaco y con las notificaciones del sistema) o "glass" (Liquid glass).</summary>
    public string Theme { get; set; } = "windows";

    /// <summary>Solo para la ventana de configuración: el estado real es si existe el .lnk.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DesktopShortcut { get; set; }
    public string? LastOperationId { get; set; }

    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Palta");
    public static string DefaultPath => Path.Combine(AppDataFolder, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Si el archivo no existe o está dañado, devuelve los valores por defecto.</summary>
    public static Settings Load(string path)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
            // Un archivo editado a mano puede traer nulls: vuelven a los valores por defecto.
            var d = new Settings();
            s.ConvertKey ??= d.ConvertKey;
            s.ToolsKey ??= d.ToolsKey;
            s.ActionsKey ??= d.ActionsKey;
            s.Hotkey ??= d.Hotkey;
            s.ClipboardHotkey ??= d.ClipboardHotkey;
            s.Theme ??= d.Theme;
            return s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
