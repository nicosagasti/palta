using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using Mandarina.App.Native;
using Mandarina.App.Wheel;
using Mandarina.Core;

namespace Mandarina.App;

/// <summary>
/// <c>Mandarina.exe --bench</c>: mide cuÃ¡nto tarda la rueda en aparecer (hasta quedar dibujada)
/// y cuÃ¡nta memoria usa la app en reposo. Escribe el resultado en la consola que la lanzÃ³.
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
            Console.WriteLine($"Bench fallÃ³: {e}");
        }
        exit();
    }

    /// <summary>
    /// <c>Mandarina.exe --snapshot carpeta</c>: abre cada rueda en el centro de la pantalla con archivos
    /// de ejemplo y guarda cÃ³mo se ve (con la captura del fondo real) en PNG, para revisar el diseÃ±o.
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
            var cases = new (string Name, WheelKind Wheel, string[] Files, int Hover)[]
            {
                ("convertir-png", WheelKind.Convert, [@"C:\fotos\vacaciones.png"], 0),
                ("herramientas-jpg", WheelKind.Tools, [@"C:\fotos\vacaciones.jpg"], 2),
                ("convertir-pdf", WheelKind.Convert, [@"C:\docs\informe.pdf"], -1),
                ("acciones", WheelKind.Actions, [@"C:\docs\informe.pdf"], 1),
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
        }
        catch (Exception e)
        {
            Console.WriteLine($"Snapshot fallÃ³: {e}");
        }
        exit();
    }

    private static async Task SnapshotClipboard(string folder, int x, int y)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mandarina-snapshot-clip", Guid.NewGuid().ToString("N"));
        var history = new Core.Clipboard.ClipboardHistory(dir);
        var icon = Path.Combine(AppContext.BaseDirectory, "mandarina-snapshot.png");
        using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mandarina.png"))!.Stream)
        using (var f = File.Create(icon))
            s.CopyTo(f);
        history.AddText("Receta de torta de mandarina: 3 huevos, 200 g de azÃºcar y la ralladura de dos mandarinas");
        history.AddFiles([@"C:\Users\nicol\Documents\Presupuesto 2026.xlsx"]);
        history.AddImage(File.ReadAllBytes(icon));
        history.AddText("https://github.com/nicosagasti/tangerine");
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
        var result = Path.Combine(Path.GetTempPath(), "mandarina-snapshot-resultado.png");
        using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mandarina.png"))!.Stream)
        using (var f = File.Create(result))
            s.CopyTo(f);
        var done = new Operation("snap-done", WheelKind.Convert, "PNG", "", _ => true, (_, _, _) =>
            Task.FromResult<IReadOnlyList<string>>([result]));
        var done2 = new Operation("snap-done2", WheelKind.Tools, "Comprimir", "", _ => true, (_, _, _) =>
            Task.FromResult<IReadOnlyList<string>>([@"C:\docs\informe (comprimido).pdf"]));
        var failed = new Operation("snap-fail", WheelKind.Convert, "WebP", "", _ => true, (_, _, _) =>
            throw new Core.Backends.ConversionException("La imagen estÃ¡ daÃ±ada o no es lo que dice ser."));
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
            // Lo de atrÃ¡s sin la cÃ¡psula misma.
            t.Opacity = 0;
            await Task.Delay(80);
            var under = Wheel.GlassCapture.Capture((int)(t.Left * scale), (int)(t.Top * scale), (int)(t.Width * scale), (int)(t.Height * scale), 1);
            t.Opacity = 1;
            var path = Path.Combine(folder, $"aviso-{i + 1}.png");
            t.SaveSnapshot(path, under);
            Console.WriteLine(path);
        }
        gate.SetResult();
        foreach (var t in toasts)
            t.Close();
        File.Delete(result);
    }
}
