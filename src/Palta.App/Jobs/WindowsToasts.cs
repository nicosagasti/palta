using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows.Threading;
using Microsoft.Win32;
using Palta.Core;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Palta.App.Jobs;

/// <summary>
/// Avisos del tema Windows: notificaciones del sistema (las mismas del centro de notificaciones),
/// con barra de progreso mientras trabaja, "Cancelar", y al terminar "Abrir" y "Mostrar en carpeta".
/// Una app sin paquete necesita un AppUserModelID registrado en HKCU con nombre e ícono.
/// </summary>
internal sealed class WindowsToasts
{
    private const string Aumid = "Palta.App";
    private const string Group = "trabajos";
    /// <summary>Si termina antes de esto, no se muestra el aviso de progreso: directo el resultado.</summary>
    private static readonly TimeSpan ProgressDelay = TimeSpan.FromMilliseconds(600);

    private readonly Dispatcher _dispatcher;
    private ToastNotifier? _notifier;

    public WindowsToasts(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Add(Job job)
    {
        var tag = job.Id.ToString("N");
        // Las acciones silenciosas (Compartir, Flotar, Terminal) llegan acá recién cuando ya fallaron.
        if (job.State == JobState.Failed)
        {
            Show(FailedXml(job), job, tag, expires: TimeSpan.FromHours(12));
            return;
        }
        uint sequence = 0;
        var progressShown = false;
        var lastUpdate = 0L;
        var delay = new DispatcherTimer(ProgressDelay, DispatcherPriority.Normal, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            if (job.State is JobState.Queued or JobState.Running)
            {
                progressShown = true;
                Show(ProgressXml(job), job, tag, data: Progress(job, ++sequence));
            }
        }, _dispatcher);
        delay.Start();

        job.Changed += j => _dispatcher.BeginInvoke(() =>
        {
            switch (j.State)
            {
                case JobState.Running when progressShown:
                    // Sin inundar a Windows: como mucho cuatro actualizaciones por segundo.
                    var now = Environment.TickCount64;
                    if (now - lastUpdate < 250)
                        return;
                    lastUpdate = now;
                    try { Notifier()?.Update(Progress(j, ++sequence), tag, Group); }
                    catch (Exception e) { Core.Log.Error("Actualizar notificación", e); }
                    break;
                case JobState.Done:
                    delay.Stop();
                    Show(DoneXml(j), j, tag, expires: TimeSpan.FromHours(12));
                    break;
                case JobState.Failed:
                    delay.Stop();
                    Show(FailedXml(j), j, tag, expires: TimeSpan.FromHours(12));
                    break;
                case JobState.Canceled:
                    delay.Stop();
                    try { ToastNotificationManager.History.Remove(tag, Group, Aumid); }
                    catch (Exception e) { Core.Log.Error("Quitar notificación", e); }
                    break;
            }
        });
    }

    // ---------- Contenido ----------

    private static string Esc(string s) => SecurityElement.Escape(s) ?? "";

    private static string ProgressXml(Job job) => $"""
        <toast launch="open">
          <visual><binding template="ToastGeneric">
            <text>{Esc(JobToast.Doing(job.Operation))}</text>
            <text>{Esc(JobToast.FileLine(job))}</text>
            <progress value="{"{progressValue}"}" status="{"{progressStatus}"}"/>
          </binding></visual>
          <actions><action content="Cancelar" arguments="cancel"/></actions>
        </toast>
        """;

    private static NotificationData Progress(Job job, uint sequence)
    {
        var p = Math.Clamp(job.Progress, 0, 1);
        var data = new NotificationData { SequenceNumber = sequence };
        data.Values["progressValue"] = p.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        data.Values["progressStatus"] = job.State == JobState.Queued ? "En espera" : $"{p:0 %}".Replace(" ", "");
        return data;
    }

    private static string DoneXml(Job job)
    {
        var body = job.Outputs.Count switch
        {
            0 => JobToast.FileLine(job),
            1 => Path.GetFileName(job.Outputs[0]),
            var n => $"{n} archivos · {Path.GetFileName(Path.GetDirectoryName(job.Outputs[0]))}",
        };
        // Si el resultado es una imagen, se ve chiquita a la izquierda, como en las notificaciones de fotos.
        var image = job.Outputs.FirstOrDefault(o => Formats.KindOf(o) == FileKind.Image && File.Exists(o)) is { } img
            ? $"""<image placement="appLogoOverride" src="{Esc(new Uri(img).AbsoluteUri)}"/>"""
            : "";
        var actions = job.Outputs.Count > 0
            ? """<actions><action content="Abrir" arguments="open"/><action content="Mostrar en carpeta" arguments="reveal"/></actions>"""
            : "";
        return $"""
            <toast launch="open">
              <visual><binding template="ToastGeneric">
                <text>{Esc(JobToast.DoneTitle(job.Operation))}</text>
                <text>{Esc(body)}</text>
                {image}
              </binding></visual>
              {actions}
            </toast>
            """;
    }

    private static string FailedXml(Job job)
    {
        var title = job.Operation.Wheel == WheelKind.Convert ? $"No se pudo convertir a {job.Operation.Label}" : $"No se pudo: {job.Operation.Label}";
        return $"""
            <toast>
              <visual><binding template="ToastGeneric">
                <text>{Esc(title)}</text>
                <text>{Esc(job.Error ?? "Error desconocido")}</text>
                <text>{Esc(JobToast.FileLine(job))}</text>
              </binding></visual>
            </toast>
            """;
    }

    // ---------- Mostrar y responder ----------

    private void Show(string xml, Job job, string tag, NotificationData? data = null, TimeSpan? expires = null)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var toast = new ToastNotification(doc) { Tag = tag, Group = Group };
            if (data is not null)
                toast.Data = data;
            if (expires is { } e)
                toast.ExpirationTime = DateTimeOffset.Now + e;
            toast.Activated += (_, args) =>
            {
                var what = (args as ToastActivatedEventArgs)?.Arguments ?? "open";
                _dispatcher.BeginInvoke(() => OnActivated(job, what));
            };
            Notifier()?.Show(toast);
        }
        catch (Exception e)
        {
            Core.Log.Error("Mostrar notificación de Windows", e);
        }
    }

    private static void OnActivated(Job job, string what)
    {
        switch (what)
        {
            case "cancel":
                job.Cancel();
                break;
            case "reveal" when job.Outputs.Count > 0:
                JobToast.Reveal(job.Outputs[0]);
                break;
            case "open" when job.State == JobState.Done && job.Outputs.Count > 0:
                var target = job.Outputs.Count == 1 ? job.Outputs[0] : Path.GetDirectoryName(job.Outputs[0])!;
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                catch (Exception e) { Core.Log.Error($"Abrir {target}", e); }
                break;
        }
    }

    private ToastNotifier? Notifier()
    {
        if (_notifier is not null)
            return _notifier;
        try
        {
            Register();
            return _notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
        }
        catch (Exception e)
        {
            Core.Log.Error("Notificaciones de Windows", e);
            return null;
        }
    }

    /// <summary>Nombre e ícono con los que Windows muestra las notificaciones de Palta.</summary>
    private static void Register()
    {
        var icon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Palta", "palta.png");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/palta.png"))!.Stream)
        using (var f = File.Create(icon))
            s.CopyTo(f);
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}");
        key.SetValue("DisplayName", "Palta");
        key.SetValue("IconUri", icon);
        key.SetValue("IconBackgroundColor", "00000000");
    }
}
