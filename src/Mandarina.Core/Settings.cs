using System.Text.Json;

namespace Mandarina.Core;

public sealed class Settings
{
    public string ConvertKey { get; set; } = "Shift";
    public string ToolsKey { get; set; } = "Ctrl";
    public string ActionsKey { get; set; } = "Alt";
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public string ClipboardHotkey { get; set; } = "Ctrl+Alt+V";
    public bool ClipboardEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }

    /// <summary>Solo para la ventana de configuración: el estado real es si existe el .lnk.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DesktopShortcut { get; set; }
    public string? LastOperationId { get; set; }

    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mandarina");
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
