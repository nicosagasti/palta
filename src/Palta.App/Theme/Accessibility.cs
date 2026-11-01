using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Palta.App.Theme;

/// <summary>
/// Las tres preferencias de accesibilidad de Windows que Palta respeta:
/// efectos de transparencia, efectos de animación y contraste alto.
/// Se leen una vez y se vuelven a leer cuando cambia algo en Configuración de Windows.
/// </summary>
internal static class Accessibility
{
    private static bool? _transparency;

    static Accessibility()
    {
        SystemEvents.UserPreferenceChanged += (_, _) => _transparency = null;
    }

    /// <summary>Personalización → Colores → Efectos de transparencia.</summary>
    public static bool Transparency => !Forced("notransparency") && (_transparency ??= ReadTransparency());

    /// <summary>Accesibilidad → Efectos visuales → Efectos de animación.</summary>
    public static bool Motion => !Forced("nomotion") && SystemParameters.ClientAreaAnimation;

    /// <summary>Accesibilidad → Temas de contraste.</summary>
    public static bool HighContrast => Forced("contrast") || SystemParameters.HighContrast;

    private static bool ReadTransparency()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("EnableTransparency") is not int value || value != 0;
    }

    /// <summary>Para las capturas de diseño: PALTA_A11Y=notransparency,nomotion,contrast simula esas preferencias.</summary>
    private static bool Forced(string what) =>
        (Environment.GetEnvironmentVariable("PALTA_A11Y") ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(what);
}

/// <summary>
/// Todas las animaciones pasan por acá. Con los efectos de animación apagados, las de opacidad se
/// vuelven un fundido corto (80 ms) y el resto (escalas, deslizamientos, rebotes) salta directo al
/// final. Igual terminan, así sus <c>Completed</c> (cerrar, ocultar) se siguen ejecutando.
/// </summary>
internal static class MotionExtensions
{
    private static readonly TimeSpan ReducedFade = TimeSpan.FromMilliseconds(80);

    public static void Animate(this IAnimatable target, DependencyProperty property, AnimationTimeline? animation)
    {
        if (animation is null || Accessibility.Motion)
        {
            target.BeginAnimation(property, animation);
            return;
        }
        var quick = animation.Clone();
        quick.BeginTime = TimeSpan.Zero;
        var isFade = property == UIElement.OpacityProperty;
        var full = animation.Duration.HasTimeSpan ? animation.Duration.TimeSpan : ReducedFade;
        quick.Duration = isFade ? (full < ReducedFade ? full : ReducedFade) : TimeSpan.Zero;
        if (quick is DoubleAnimation d)
            d.EasingFunction = null;
        target.BeginAnimation(property, quick);
    }
}
