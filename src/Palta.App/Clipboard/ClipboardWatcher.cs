using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Palta.App.Native;
using Palta.Core.Clipboard;
using WpfClipboard = System.Windows.Clipboard;

namespace Palta.App.Clips;

/// <summary>
/// Windows avisa cada vez que cambia el portapapeles (AddClipboardFormatListener): sin sondeo.
/// Respeta las marcas con las que los gestores de contraseñas piden no ser guardados.
/// </summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private const int MaxImageSide = 4096;
    private static readonly string[] ExcludeFormats = ["ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore"];

    private readonly ClipboardHistory _history;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;
    private long _suppressUntil;

    public bool Enabled { get; set; } = true;

    public ClipboardWatcher(ClipboardHistory history, Dispatcher dispatcher)
    {
        _history = history;
        _source = new HwndSource(new HwndSourceParameters("PaltaClipboard") { ParentWindow = new IntPtr(-3) });
        _source.AddHook(WndProc);
        Win32.AddClipboardFormatListener(_source.Handle);
        // La app que copia a veces escribe el portapapeles en varios pasos: esperamos un instante.
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => Capture(), dispatcher) { IsEnabled = false };
    }

    /// <summary>Lo que copie Palta misma en el próximo medio segundo no se registra.</summary>
    public void Suppress() => _suppressUntil = Environment.TickCount64 + 500;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        return IntPtr.Zero;
    }

    private void Capture()
    {
        _debounce.Stop();
        if (!Enabled || Environment.TickCount64 < _suppressUntil)
            return;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                CaptureOnce();
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Otra app tiene el portapapeles abierto.
                Thread.Sleep(50);
            }
            catch (Exception e)
            {
                Core.Log.Error("Leer el portapapeles", e);
                return;
            }
        }
    }

    private void CaptureOnce()
    {
        var data = WpfClipboard.GetDataObject();
        if (data is null || IsExcluded(data))
            return;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _history.AddFiles(files);
            return;
        }
        if (WpfClipboard.ContainsImage() && WpfClipboard.GetImage() is { } image)
        {
            var frozen = Shrink(image);
            frozen.Freeze();
            Task.Run(() =>
            {
                try
                {
                    _history.AddImage(EncodePng(frozen));
                }
                catch (Exception e)
                {
                    Core.Log.Error("Guardar imagen del portapapeles", e);
                }
            });
            return;
        }
        if (WpfClipboard.ContainsText())
            _history.AddText(WpfClipboard.GetText());
    }

    private static bool IsExcluded(IDataObject data)
    {
        if (ExcludeFormats.Any(data.GetDataPresent))
            return true;
        // "CanIncludeInClipboardHistory" = 0: el que copió pidió que no quede en ningún historial.
        if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms)
        {
            var bytes = ms.ToArray();
            return bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == 0;
        }
        return false;
    }

    private static BitmapSource Shrink(BitmapSource image)
    {
        var longest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longest <= MaxImageSide)
            return image;
        var s = MaxImageSide / (double)longest;
        return new TransformedBitmap(image, new ScaleTransform(s, s));
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Pone un elemento del historial en el portapapeles.</summary>
    public void Put(ClipItem item)
    {
        Suppress();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                switch (item.Kind)
                {
                    case ClipKind.Text:
                        WpfClipboard.SetText(item.Text ?? "");
                        break;
                    case ClipKind.Image:
                        WpfClipboard.SetImage(LoadImage(item.ImagePath!, 0));
                        break;
                    case ClipKind.Files:
                        var list = new System.Collections.Specialized.StringCollection();
                        list.AddRange(item.Files!.ToArray());
                        WpfClipboard.SetFileDropList(list);
                        break;
                }
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
        }
    }

    /// <summary>Carga una imagen sin dejar el archivo bloqueado; <paramref name="decodeWidth"/> 0 = tamaño real.</summary>
    public static BitmapImage LoadImage(string path, int decodeWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        if (decodeWidth > 0)
            image.DecodePixelWidth = decodeWidth;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void Dispose()
    {
        Win32.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }
}
