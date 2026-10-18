using System.Windows;
using System.Windows.Threading;
using Mandarina.Core;

namespace Mandarina.App.Shell;

/// <summary>Lo que las acciones piden a la interfaz; las operaciones corren en segundo plano, así que todo pasa al hilo de UI.</summary>
internal sealed class ShellHost(Dispatcher dispatcher) : IShellHost
{
    public string? PickFolder(string title) => dispatcher.Invoke(() =>
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    });

    public void SetClipboardText(string text) => dispatcher.Invoke(() =>
    {
        // El portapapeles puede estar tomado por otra app un instante.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
        }
    });
}
