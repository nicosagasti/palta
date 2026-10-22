using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Palta.App.Native;

namespace Palta.App.Shell;

/// <summary>
/// El panel de compartir de Windows (Compartir con dispositivos cercanos, Phone Link, mail, apps),
/// que es lo más parecido a AirDrop. Una app de escritorio lo abre por <c>IDataTransferManagerInterop</c>
/// sobre una ventana propia; usamos una invisible junto al cursor para que el panel aparezca ahí.
/// </summary>
internal static class ShareSheet
{
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    private static Window? _anchor;

    public static void Show(IReadOnlyList<string> files)
    {
        _anchor?.Close();
        Win32.GetCursorPos(out var cursor);
        var scale = Win32.ScaleAt(cursor.X, cursor.Y);
        // Ventana mínima y transparente: Windows necesita una ventana dueña para ubicar el panel.
        var anchor = new Window
        {
            Width = 1,
            Height = 1,
            Left = cursor.X / scale,
            Top = cursor.Y / scale,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
        };
        // Ventana de herramienta: no aparece en Alt+Tab ni en la barra de tareas.
        anchor.SourceInitialized += (_, _) => Win32.AddExStyle(new WindowInteropHelper(anchor).Handle, Win32.WS_EX_TOOLWINDOW);
        anchor.Show();
        _anchor = anchor;
        var hwnd = new WindowInteropHelper(anchor).Handle;
        // Se cierra sola: al elegir destino o, si cancelan el panel, en un rato.
        var cleanup = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMinutes(2), System.Windows.Threading.DispatcherPriority.Background, (s, _) =>
        {
            ((System.Windows.Threading.DispatcherTimer)s!).Stop();
            CloseAnchor(anchor);
        }, anchor.Dispatcher);
        cleanup.Start();
        // El panel solo se abre para la ventana que está al frente. Después de un drop la app no tiene
        // el foco y Windows bloquea SetForegroundWindow; un toque de Alt sintético lo habilita.
        const byte VK_MENU = 0x12;
        Win32.keybd_event(VK_MENU, 0, 0, IntPtr.Zero);
        Win32.keybd_event(VK_MENU, 0, 2, IntPtr.Zero);
        Win32.SetForegroundWindow(hwnd);
        anchor.Activate();

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = DataTransferManagerIid;
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(hwnd, ref iid));
        manager.DataRequested += async (_, args) =>
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var path in files)
                {
                    if (System.IO.Directory.Exists(path))
                        items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                    else
                        items.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                args.Request.Data.Properties.Title = files.Count == 1 ? System.IO.Path.GetFileName(files[0]) : $"{files.Count} archivos";
                args.Request.Data.Properties.Description = "Compartido desde Palta";
                args.Request.Data.SetStorageItems(items);
            }
            catch (Exception e)
            {
                Core.Log.Error("Compartir", e);
                args.Request.FailWithDisplayText("No se pudieron preparar los archivos para compartir.");
            }
            finally
            {
                deferral.Complete();
            }
        };
        manager.TargetApplicationChosen += (_, _) => anchor.Dispatcher.BeginInvoke(() => CloseAnchor(anchor));
        interop.ShowShareUIForWindow(hwnd);
    }

    private static void CloseAnchor(Window anchor)
    {
        if (_anchor == anchor)
            _anchor = null;
        if (anchor.IsLoaded)
            anchor.Close();
    }
}
