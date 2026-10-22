using System.IO;
using Microsoft.Win32;

namespace Palta.App.Shell;

/// <summary>
/// La app antes se llamaba Mandarina. Al primer arranque como Palta se mudan la configuración y el
/// historial del portapapeles, y se reemplazan la entrada de arranque con Windows y el acceso directo
/// del escritorio, que apuntaban a Mandarina.exe. Si ya está todo migrado, no hace nada.
/// </summary>
internal static class Migration
{
    private const string OldName = "Mandarina";

    public static void FromMandarina()
    {
        MoveFolder(Environment.SpecialFolder.ApplicationData);
        MoveFolder(Environment.SpecialFolder.LocalApplicationData);
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run?.GetValue(OldName) is not null)
            {
                run.DeleteValue(OldName);
                Startup.Set(true);
            }
        }
        catch (Exception e)
        {
            Core.Log.Error("Migrar el arranque con Windows", e);
        }
        try
        {
            var oldLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), OldName + ".lnk");
            if (File.Exists(oldLink))
            {
                File.Delete(oldLink);
                DesktopShortcut.Set(true);
            }
        }
        catch (Exception e)
        {
            Core.Log.Error("Migrar el acceso directo", e);
        }
    }

    private static void MoveFolder(Environment.SpecialFolder root)
    {
        var parent = Environment.GetFolderPath(root);
        var from = Path.Combine(parent, OldName);
        var to = Path.Combine(parent, "Palta");
        try
        {
            if (!Directory.Exists(from))
                return;
            if (!Directory.Exists(to))
            {
                Directory.Move(from, to);
                return;
            }
            // Ya existe (por ejemplo, un log escrito antes): se pasa lo que falta y lo nuevo gana.
            foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            {
                var target = Path.Combine(to, Path.GetFileName(entry));
                if (File.Exists(target) || Directory.Exists(target))
                    continue;
                if (Directory.Exists(entry))
                    Directory.Move(entry, target);
                else
                    File.Move(entry, target);
            }
            if (!Directory.EnumerateFileSystemEntries(from).Any())
                Directory.Delete(from);
        }
        catch (Exception e)
        {
            Core.Log.Error($"Migrar {from}", e);
        }
    }
}
