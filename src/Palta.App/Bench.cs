using System.IO;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Palta.App.Native;
using Palta.App.Wheel;
using Palta.Core;

namespace Palta.App;

/// <summary>
/// <c>Palta.exe --bench</c>: mide cuánto tarda la rueda en aparecer (hasta quedar dibujada)
/// y cuánta memoria usa la app en reposo. Escribe el resultado en la consola que la lanzó.
/// </summary>
internal static class Bench
{
    public static async void Run(WheelWindow wheel, Dispatcher dispatcher, Action exit)
    {
        Win32.AttachConsole(-1);
        try
        {
            Win32.GetCursorPos(out var p);
            var times = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var sw = Stopwatch.StartNew();
                wheel.ShowAt(WheelKind.Convert, p.X, p.Y);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                wheel.HideNow();
                await Task.Delay(30);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(2000);
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var first = times[0];
            var rest = times.Skip(1).Average();
            Console.WriteLine();
            Console.WriteLine($"Rueda: primera {first:F1} ms, promedio {rest:F1} ms (19 aperturas)");
            Console.WriteLine($"Memoria en reposo: working set {process.WorkingSet64 / 1048576.0:F1} MB, privada {process.PrivateMemorySize64 / 1048576.0:F1} MB");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Bench falló: {e}");
        }
        exit();
    }

    /// <summary>
    /// <c>Palta.exe --snapshot carpeta</c>: abre cada rueda en el centro de la pantalla con archivos
    /// de ejemplo y guarda cómo se ve (con la captura del fondo real) en PNG, para revisar el diseño.
    /// </summary>
    public static async void Snapshot(WheelWindow wheel, Dispatcher dispatcher, string folder, Action exit)
    {
        Win32.AttachConsole(-1);
        try
        {
            Directory.CreateDirectory(folder);
            var area = System.Windows.SystemParameters.WorkArea;
            var scale = Win32.ScaleAt(0, 0);
            var (x, y) = ((int)(area.Width * scale / 2), (int)(area.Height * scale / 2));
            // Archivos reales de muestra: las acciones solo aparecen para archivos que existen.
            var samples = Path.Combine(Path.GetTempPath(), "palta-snapshot-muestras");
            Directory.CreateDirectory(Path.Combine(samples, "Fotos del viaje"));
            var photo = Path.Combine(samples, "vacaciones.png");
            using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/palta.png"))!.Stream)
            using (var f = File.Create(photo))
                s.CopyTo(f);
            var report = Path.Combine(samples, "informe.pdf");
            File.WriteAllText(report, "%PDF-1.4");
            var cases = new (string Name, WheelKind Wheel, string[] Files, int Hover)[]
            {
                ("convertir-png", WheelKind.Convert, [@"C:\fotos\vacaciones.png"], 0),
                ("herramientas-png", WheelKind.Tools, [photo], 2),
                ("convertir-pdf", WheelKind.Convert, [@"C:\docs\informe.pdf"], -1),
                ("acciones", WheelKind.Actions, [report], 1),
            };
            foreach (var c in cases)
            {
                var (sx, sy, size) = wheel.ScreenRect(x, y);
                var under = Wheel.GlassCapture.Capture(sx, sy, size, size, 1);
                wheel.ShowPreview(c.Wheel, c.Files, x, y);
                wheel.SnapshotHover(c.Hover);
                await Task.Delay(450);
                var path = Path.Combine(folder, c.Name + ".png");
                wheel.SaveSnapshot(path, under);
                Console.WriteLine(path);
                wheel.HideNow();
                await Task.Delay(100);
            }
            await SnapshotClipboard(folder, x, y);
            await SnapshotToasts(folder);
            await SnapshotShelf(folder, x, y, [photo, report, Path.Combine(samples, "Fotos del viaje")]);
            await SnapshotSettings(folder);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Snapshot falló: {e}");
        }
        exit();
    }

    private static async Task SnapshotClipboard(string folder, int x, int y)
    {
        var dir = Path.Combine(Path.GetTempPath(), "palta-snapshot-clip", Guid.NewGuid().ToString("N"));
        var history = new Core.Clipboard.ClipboardHistory(dir);
        var icon = Path.Combine(AppContext.BaseDirectory, "palta-snapshot.png");
        using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/palta.png"))!.Stream)
        using (var f = File.Create(icon))
            s.CopyTo(f);
        history.AddText("Receta de guacamole: 2 paltas maduras, medio limón, cebolla morada, tomate y una pizca de sal");
        history.AddFiles([@"C:\Users\nicol\Documents\Presupuesto 2026.xlsx"]);
        history.AddImage(File.ReadAllBytes(icon));
        history.AddText("https://github.com/nicosagasti/palta");
        var pinned = history.AddText("nicolas@ejemplo.com")!;
        history.Pin(pinned.Id, true);
        File.Delete(icon);

        using var watcher = new Clips.ClipboardWatcher(history, System.Windows.Threading.Dispatcher.CurrentDispatcher) { Enabled = false };
        var window = new Clips.ClipboardWindow(history, watcher);
        var (left, top, w, h, m) = Clips.ClipboardWindow.PanelRect(x, y);
        var under = Wheel.GlassCapture.Capture(left - m, top - m, w + 2 * m, h + 2 * m, 1);
        window.ShowNear(x, y);
        await Task.Delay(500);
        var path = Path.Combine(folder, "portapapeles.png");
        window.SaveSnapshot(path, under);
        Console.WriteLine(path);
        window.Close();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// <c>Palta.exe --toast-test</c>: manda dos notificaciones de Windows de prueba (una lista con
    /// miniatura y una con error) y dice cuántas registró Windows en el centro de notificaciones.
    /// </summary>
    public static async void ToastTest(Jobs.WindowsToasts toasts, Action exit)
    {
        Win32.AttachConsole(-1);
        try
        {
            var result = Path.Combine(Path.GetTempPath(), "palta-toast-prueba.png");
            using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/palta.png"))!.Stream)
            using (var f = File.Create(result))
                s.CopyTo(f);
            var queue = new JobQueue(2);
            queue.JobAdded += toasts.Add;
            queue.Enqueue(new Operation("test-done", WheelKind.Convert, "PNG", "", _ => true, async (_, p, _) =>
            {
                for (var i = 1; i <= 10; i++) { p.Report(i / 10.0); await Task.Delay(150); }
                return [result];
            }), [@"C:\fotos\prueba.jpg"]);
            queue.Enqueue(new Operation("test-fail", WheelKind.Tools, "Comprimir", "", _ => true, (_, _, _) =>
                throw new Core.Backends.ConversionException("Esto es una prueba de error.")), [@"C:\fotos\otra.jpg"]);
            await queue.WhenIdle();
            await Task.Delay(1500);
            var history = Windows.UI.Notifications.ToastNotificationManager.History.GetHistory("Palta.App");
            Console.WriteLine($"Notificaciones de Palta en el centro de notificaciones: {history.Count}");
            foreach (var t in history)
                Console.WriteLine($"  {t.Tag}: {t.Content.GetElementsByTagName("text").FirstOrDefault()?.InnerText}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Falló: {e}");
        }
        exit();
    }

    /// <summary>La ventana de Configuración, abierta fuera de la pantalla y dibujada a PNG.</summary>
    private static async Task SnapshotSettings(string folder)
    {
        var dir = Path.Combine(Path.GetTempPath(), "palta-snapshot-clip", Guid.NewGuid().ToString("N"));
        var history = new Core.Clipboard.ClipboardHistory(dir);
        history.AddText("uno");
        history.AddText("dos");
        var window = new Settings.SettingsWindow(new Core.Settings(), null, history)
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            ShowActivated = false,
        };
        window.Show();
        await Task.Delay(500);
        var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * 2), (int)(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        // El fondo de la ventana (Mica) lo pinta Windows, no WPF: se pone uno liso del tema para que se lea.
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Theme.Palette.Current().Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new System.Windows.Rect(0, 0, window.ActualWidth, window.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(folder, "configuracion.png");
        using (var file = File.Create(path))
            encoder.Save(file);
        Console.WriteLine(path);
        window.Close();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static async Task SnapshotShelf(string folder, int x, int y, string[] files)
    {
        var shelf = new Shelf.ShelfWindow();
        var scale = Win32.ScaleAt(x, y);
        shelf.PlaceAt(x / scale - 168, y / scale - 160);
        shelf.Add(files);
        await Task.Delay(500);
        shelf.Opacity = 0;
        await Task.Delay(80);
        var under = Wheel.GlassCapture.Capture((int)(shelf.Left * scale), (int)(shelf.Top * scale), (int)(shelf.ActualWidth * scale), (int)(shelf.ActualHeight * scale), 1);
        shelf.Opacity = 1;
        var path = Path.Combine(folder, "estante.png");
        shelf.SaveSnapshot(path, under);
        Console.WriteLine(path);
        shelf.Close();
    }

    private static async Task SnapshotToasts(string folder)
    {
        var queue = new JobQueue(2);
        var stack = new Jobs.ToastStack();
        var gate = new TaskCompletionSource();
        var running = new Operation("snap-run", WheelKind.Convert, "JPG", "", _ => true, async (_, progress, _) =>
        {
            progress.Report(0.62);
            await gate.Task;
            return [];
        });
        // Un resultado de imagen real, para que se vea la miniatura.
        var result = Path.Combine(Path.GetTempPath(), "palta-snapshot-resultado.png");
        using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/palta.png"))!.Stream)
        using (var f = File.Create(result))
            s.CopyTo(f);
        var done = new Operation("snap-done", WheelKind.Convert, "PNG", "", _ => true, (_, _, _) =>
            Task.FromResult<IReadOnlyList<string>>([result]));
        var done2 = new Operation("snap-done2", WheelKind.Tools, "Comprimir", "", _ => true, (_, _, _) =>
            Task.FromResult<IReadOnlyList<string>>([@"C:\docs\informe (comprimido).pdf"]));
        var failed = new Operation("snap-fail", WheelKind.Convert, "WebP", "", _ => true, (_, _, _) =>
            throw new Core.Backends.ConversionException("La imagen está dañada o no es lo que dice ser."));
        var toasts = new List<Jobs.JobToast>();
        queue.JobAdded += job => toasts.Add(stack.Add(job));
        queue.Enqueue(failed, [@"C:\fotos\captura.heic"]);
        await Task.Delay(250);
        queue.Enqueue(done2, [@"C:\docs\informe.pdf"]);
        await Task.Delay(250);
        queue.Enqueue(done, [@"C:\fotos\vacaciones.heic"]);
        await Task.Delay(250);
        queue.Enqueue(running, [@"C:\fotos\atardecer.png", @"C:\fotos\playa.png"]);
        await Task.Delay(800);
        toasts[1].RevealButtons();
        for (var i = 0; i < toasts.Count; i++)
        {
            var t = toasts[i];
            var scale = Win32.ScaleAt((int)t.Left, (int)t.Top);
            // Lo de atrás sin la cápsula misma.
            t.Opacity = 0;
            await Task.Delay(80);
            var under = Wheel.GlassCapture.Capture((int)(t.Left * scale), (int)(t.Top * scale), (int)(t.Width * scale), (int)(t.Height * scale), 1);
            t.Opacity = 1;
            var path = Path.Combine(folder, $"aviso-{i + 1}.png");
            t.SaveSnapshot(path, under);
            Win32.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(t).Handle, out var rect);
            Console.WriteLine($"{path}  ventana en ({rect.Left}, {rect.Top})-({rect.Right}, {rect.Bottom})");
        }
        gate.SetResult();
        foreach (var t in toasts)
            t.Close();
        File.Delete(result);
    }
}
