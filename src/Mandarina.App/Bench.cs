using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using Mandarina.App.Native;
using Mandarina.App.Wheel;
using Mandarina.Core;

namespace Mandarina.App;

/// <summary>
/// <c>Mandarina.exe --bench</c>: mide cuánto tarda la rueda en aparecer (hasta quedar dibujada)
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
    /// <c>Mandarina.exe --snapshot carpeta</c>: abre cada rueda en el centro de la pantalla con archivos
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
            var cases = new (string Name, WheelKind Wheel, string[] Files, int Hover)[]
            {
                ("convertir-png", WheelKind.Convert, [@"C:\fotos\vacaciones.png"], 0),
                ("herramientas-jpg", WheelKind.Tools, [@"C:\fotos\vacaciones.jpg"], 2),
                ("convertir-pdf", WheelKind.Convert, [@"C:\docs\informe.pdf"], -1),
                ("acciones", WheelKind.Actions, [@"C:\docs\informe.pdf"], 1),
            };
            foreach (var c in cases)
            {
                wheel.ShowForFiles(c.Wheel, c.Files, x, y);
                wheel.SnapshotHover(c.Hover);
                await Task.Delay(450);
                var path = Path.Combine(folder, c.Name + ".png");
                wheel.SaveSnapshot(path);
                Console.WriteLine(path);
                wheel.HideNow();
                await Task.Delay(100);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Snapshot falló: {e}");
        }
        exit();
    }
}
