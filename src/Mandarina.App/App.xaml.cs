using System.Windows;
using System.Windows.Threading;
using Mandarina.App.Input;
using Mandarina.App.Jobs;
using Mandarina.App.Native;
using Mandarina.App.Settings;
using Mandarina.App.Shell;
using Mandarina.App.Wheel;
using Mandarina.Core;
using Mandarina.Core.Input;
using AppSettings = Mandarina.Core.Settings;

namespace Mandarina.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppSettings _settings = new();
    private Catalog _catalog = null!;
    private JobQueue _queue = null!;
    private ToastStack _toasts = null!;
    private WheelWindow _wheel = null!;
    private DragSession _session = null!;
    private InputHooks? _hooks;
    private HotkeyManager? _hotkeys;
    private Tray? _tray;
    private SettingsWindow? _settingsWindow;
    private string? _hotkeyProblem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--gpu"))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Excepción no capturada (UI)", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Excepción no capturada", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Tarea no observada", args.Exception); args.SetObserved(); };

        var bench = e.Args.Contains("--bench");
        var tool = bench || e.Args.Contains("--snapshot");
        _singleInstance = new Mutex(true, tool ? "Mandarina.Tool" : "Mandarina.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        var firstRun = !System.IO.File.Exists(AppSettings.DefaultPath);
        _settings = AppSettings.Load(AppSettings.DefaultPath);
        _catalog = Catalog.CreateDefault(new ShellHost(Dispatcher));
        _queue = new JobQueue(JobQueue.DefaultConcurrency);
        _toasts = new ToastStack();
        _queue.JobAdded += job => Dispatcher.BeginInvoke(() => _toasts.Add(job));

        _wheel = new WheelWindow(_catalog, () => _settings.LastOperationId);
        _wheel.OperationChosen += OnOperationChosen;
        _wheel.WheelHidden += () => { if (_session is { } s) lock (s) s.WheelClosed(); };
        _wheel.WarmUp();

        if (Array.IndexOf(e.Args, "--snapshot") is var snap and >= 0)
        {
            var folder = snap + 1 < e.Args.Length ? e.Args[snap + 1] : "snapshots";
            Bench.Snapshot(_wheel, Dispatcher, folder, () => Shutdown());
            return;
        }
        if (bench)
        {
            Bench.Run(_wheel, Dispatcher, () => Shutdown());
            return;
        }

        _session = new DragSession(Win32.GetSystemMetrics(Win32.SM_CXDRAG), Win32.GetSystemMetrics(Win32.SM_CYDRAG), WheelFor);
        _session.WheelRequested += (wheel, x, y) => Dispatcher.BeginInvoke(() => _wheel.ShowAt(wheel, x, y));
        _session.RepeatRequested += () => Dispatcher.BeginInvoke(_wheel.ShowRepeat);
        _session.DragEnded += () => Dispatcher.BeginInvoke(_wheel.EndDrag);
        _hooks = new InputHooks(_session);
        _hooks.Start();

        _hotkeys = new HotkeyManager();
        RegisterHotkey();

        _tray = new Tray(OpenSettings, paused => { lock (_session) _session.Paused = paused; }, () => Shutdown());
        if (firstRun)
        {
            _settings.Save(AppSettings.DefaultPath);
            _tray.ShowTip("Mandarina está lista", $"Arrastrá un archivo y tocá {_settings.ConvertKey} para convertirlo.");
            // Primera vez: mostramos la configuración para elegir arranque con Windows o acceso directo.
            OpenSettings();
        }
        Log.Info("Mandarina iniciada");
    }

    private WheelKind? WheelFor(Mod mod)
    {
        var name = mod switch { Mod.Shift => "Shift", Mod.Ctrl => "Ctrl", Mod.Alt => "Alt", _ => null };
        var s = _settings;
        if (name is null)
            return null;
        if (name == s.ConvertKey) return WheelKind.Convert;
        if (name == s.ToolsKey) return WheelKind.Tools;
        if (name == s.ActionsKey) return WheelKind.Actions;
        return null;
    }

    private void OnOperationChosen(IOperation op, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return;
        _settings.LastOperationId = op.Id;
        SaveSettings();
        _queue.Enqueue(op, files);
    }

    private void RegisterHotkey()
    {
        _hotkeyProblem = _hotkeys!.Register(_settings.Hotkey, OnHotkey)
            ? null
            : $"No se pudo registrar {_settings.Hotkey}: otra aplicación ya lo usa.";
        if (_hotkeyProblem is not null)
            Log.Info(_hotkeyProblem);
    }

    private void OnHotkey()
    {
        lock (_session)
        {
            if (_session.Paused)
                return;
        }
        Win32.GetCursorPos(out var cursor);
        var files = ExplorerSelection.Get();
        if (files.Count == 0)
        {
            _tray?.ShowTip("Nada seleccionado", "Seleccioná archivos en el Explorador o copialos, y volvé a apretar el atajo.");
            return;
        }
        _wheel.ShowForFiles(WheelKind.Convert, files, cursor.X, cursor.Y);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _hotkeyProblem);
        _settingsWindow.Saving = updated =>
        {
            updated.LastOperationId = _settings.LastOperationId;
            var previous = _settings;
            _settings = updated;
            try
            {
                Shell.Startup.Set(updated.StartWithWindows);
            }
            catch (Exception e)
            {
                Log.Error("Arranque con Windows", e);
            }
            try
            {
                if (updated.DesktopShortcut != DesktopShortcut.Exists)
                    DesktopShortcut.Set(updated.DesktopShortcut);
            }
            catch (Exception e)
            {
                Log.Error("Acceso directo", e);
            }
            RegisterHotkey();
            if (_hotkeyProblem is not null && previous.Hotkey != updated.Hotkey)
            {
                var problem = _hotkeyProblem;
                _settings.Hotkey = previous.Hotkey;
                RegisterHotkey();
                SaveSettings();
                return problem;
            }
            SaveSettings();
            return null;
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception e)
        {
            Log.Error("Guardar configuración", e);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hooks?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
