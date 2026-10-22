using System.Windows;
using Palta.App.Native;

namespace Palta.App.Shell;

/// <summary>Archivos para el atajo sin arrastre: la selección del Explorador en primer plano o, si no hay, los copiados.</summary>
internal static class ExplorerSelection
{
    public static IReadOnlyList<string> Get()
    {
        var fromExplorer = FromForegroundExplorer();
        if (fromExplorer.Count > 0)
            return fromExplorer;
        try
        {
            if (Clipboard.ContainsFileDropList())
                return Clipboard.GetFileDropList().Cast<string>().ToList();
        }
        catch (Exception e)
        {
            Core.Log.Error("Portapapeles", e);
        }
        return [];
    }

    /// <summary>
    /// Shell.Application por COM. Se usa <c>dynamic</c> porque la app no se recorta (trimming),
    /// así que el binder está disponible. Con pestañas varias comparten ventana: gana la primera con selección.
    /// </summary>
    private static List<string> FromForegroundExplorer()
    {
        var result = new List<string>();
        var foreground = Win32.GetForegroundWindow();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
                return result;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (dynamic window in shell.Windows())
            {
                try
                {
                    if (new IntPtr((long)window.HWND) != foreground)
                        continue;
                    foreach (dynamic item in window.Document.SelectedItems())
                        result.Add((string)item.Path);
                    if (result.Count > 0)
                        break;
                }
                catch (Exception)
                {
                    // Ventanas que no son carpetas (Panel de control, etc.).
                }
            }
        }
        catch (Exception e)
        {
            Core.Log.Error("Selección del Explorador", e);
        }
        return result;
    }
}
