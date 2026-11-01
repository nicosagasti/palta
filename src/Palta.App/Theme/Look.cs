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

    /// <summary>
    /// ¿Se dibuja con el estilo Liquid glass (tinte, brillo, filo de luz, avisos propios)? En contraste
    /// alto nunca: ahí manda el sistema, con sus colores y sus notificaciones.
    /// </summary>
    public static bool IsGlass => Current == AppTheme.Glass && !Accessibility.HighContrast;

    /// <summary>
    /// ¿Se captura y desenfoca el fondo? Solo en Liquid glass y con los efectos de transparencia de
    /// Windows prendidos; si están apagados, el vidrio queda casi opaco.
    /// </summary>
    public static bool Translucent => IsGlass && Accessibility.Transparency;

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
