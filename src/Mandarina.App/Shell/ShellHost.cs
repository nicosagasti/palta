using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Mandarina.Core;

namespace Mandarina.App.Shell;

/// <summary>Lo que las acciones piden a la interfaz; las operaciones corren en segundo plano, así que todo pasa al hilo de UI.</summary>
internal sealed class ShellHost(Dispatcher dispatcher, Action<IReadOnlyList<string>> floatFiles) : IShellHost
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

    public void Share(IReadOnlyList<string> files) => dispatcher.Invoke(() => ShareSheet.Show(files));

    public void Float(IReadOnlyList<string> files) => dispatcher.Invoke(() => floatFiles(files));

    /// <summary>Windows Terminal si está (viene con Windows 11); si no, PowerShell.</summary>
    public void OpenTerminal(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", ["-d", folder]) { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit") { WorkingDirectory = folder, UseShellExecute = true });
        }
    }
}
