namespace Palta.App.Theme;

/// <summary>Los temas que se eligen en Configuración o en el menú de la bandeja.</summary>
internal enum AppTheme
{
    /// <summary>Como Windows 11: superficies oscuras y opacas, y las notificaciones del sistema. Por defecto.</summary>
    Windows,

    /// <summary>Liquid glass: vidrio con el fondo desenfocado, filo de luz y avisos propios estilo macOS.</summary>
    Glass,
}

/// <summary>
/// El tema activo. Cada ventana lo consulta al abrirse (a través de <see cref="Glass"/> y <see cref="Palette"/>),
/// así un cambio se ve en la próxima apertura sin reiniciar.
/// </summary>
internal static class Look
{
    private static AppTheme _current = AppTheme.Windows;

    public static event Action? Changed;

    public static AppTheme Current
    {
        get => Override ?? _current;
        set
        {
            if (_current == value)
                return;
            _current = value;
            Changed?.Invoke();
        }
    }

    /// <summary>¿Se usa el vidrio (captura del fondo, desenfoque, brillo)?</summary>
    public static bool Translucent => Current == AppTheme.Glass;

    /// <summary>Para las capturas de diseño: PALTA_LOOK=windows|glass fuerza un tema.</summary>
    private static AppTheme? Override => Environment.GetEnvironmentVariable("PALTA_LOOK") switch
    {
        "windows" => AppTheme.Windows,
        "glass" => AppTheme.Glass,
        _ => null,
    };

    public static AppTheme Parse(string? value) =>
        string.Equals(value, "glass", StringComparison.OrdinalIgnoreCase) ? AppTheme.Glass : AppTheme.Windows;

    public static string ToSetting(AppTheme theme) => theme == AppTheme.Glass ? "glass" : "windows";

    public static string Name(AppTheme theme) => theme == AppTheme.Glass ? "Liquid glass" : "Windows";
}
