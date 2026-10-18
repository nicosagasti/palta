using System.IO;
using Microsoft.Win32;

namespace Mandarina.App.Shell;

/// <summary>Arrancar con Windows: una entrada en HKCU\...\Run.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mandarina";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Acceso directo "Mandarina.lnk" en el escritorio del usuario.</summary>
internal static class DesktopShortcut
{
    private static string PathOnDesktop =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mandarina.lnk");

    public static bool Exists => File.Exists(PathOnDesktop);

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PathOnDesktop);
            return;
        }
        // WScript.Shell es la forma más corta de escribir un .lnk desde .NET.
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell no está disponible.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(PathOnDesktop);
        link.TargetPath = Environment.ProcessPath;
        link.WorkingDirectory = AppContext.BaseDirectory;
        link.IconLocation = $"{Environment.ProcessPath},0";
        link.Description = "Mandarina: convertí archivos arrastrando";
        link.Save();
    }
}
