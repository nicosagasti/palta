using System.Windows;
using System.Windows.Threading;
using Palta.App.Clips;
using Palta.App.Input;
using Palta.App.Jobs;
using Palta.App.Native;
using Palta.App.Settings;
using Palta.App.Shell;
using Palta.App.Wheel;
using Palta.Core;
using Palta.Core.Clipboard;
using Palta.Core.Input;
using AppSettings = Palta.Core.Settings;

namespace Palta.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppSettings _settings = new();
    private Catalog _catalog = null!;
    private JobQueue _queue = null!;
    private ToastStack _toasts = null!;
    private WindowsToasts _windowsToasts = null!;
    private WheelWindow _wheel = null!;
    private DragSession _session = null!;
    private InputHooks? _hooks;
    private HotkeyManager? _hotkeys;
    private Tray? _tray;
    private SettingsWindow? _settingsWindow;
    private string? _hotkeyProblem;
    private ClipboardHistory? _clipHistory;
    private ClipboardWatcher? _clipWatcher;
    private ClipboardWindow? _clipWindow;
    private Shelf.ShelfWindow? _shelf;

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
        var tool = bench || e.Args.Contains("--snapshot") || e.Args.Contains("--toast-test");
        _singleInstance = new Mutex(true, tool ? "Palta.Tool" : "Palta.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        if (!tool)
            Shell.Migration.FromMandarina();
        var firstRun = !System.IO.File.Exists(AppSettings.DefaultPath);
        _settings = AppSettings.Load(AppSettings.DefaultPath);
        _catalog = Catalog.CreateDefault(new ShellHost(Dispatcher, files => (_shelf ??= new Shelf.ShelfWindow()).Add(files)));
        _queue = new JobQueue(JobQueue.DefaultConcurrency);
        Theme.Look.Current = Theme.Look.Parse(_settings.Theme);
        _toasts = new ToastStack();
        _windowsToasts = new WindowsToasts(Dispatcher);
        _queue.JobAdded += job =>
        {
            if (!job.Operation.Quiet)
            {
                Dispatcher.BeginInvoke(() => Notify(job));
                return;
            }
            // Compartir, Flotar y Terminal muestran su propia interfaz: aviso solo si fallan.
            var shown = false;
            job.Changed += j =>
            {
                if (j.State == JobState.Failed && !shown)
                {
                    shown = true;
                    Dispatcher.BeginInvoke(() => Notify(j));
                }
            };
        };

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
        if (e.Args.Contains("--toast-test"))
        {
            Bench.ToastTest(_windowsToasts, () => Shutdown());
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

        _clipHistory = new ClipboardHistory(ClipboardHistory.DefaultFolder, _settings.ClipboardLimits);
        _clipWatcher = new ClipboardWatcher(_clipHistory, Dispatcher) { Enabled = _settings.ClipboardEnabled };
        _clipWindow = new ClipboardWindow(_clipHistory, _clipWatcher);
        _clipWindow.RestoreFloat(_settings.ClipboardFloating, _settings.ClipboardFloatX, _settings.ClipboardFloatY);
        _clipWindow.FloatChanged += () =>
        {
            _settings.ClipboardFloating = _clipWindow.Floating;
            (_settings.ClipboardFloatX, _settings.ClipboardFloatY) = _clipWindow.FloatPosition is { } p ? (p.X, p.Y) : ((int?)null, (int?)null);
            SaveSettings();
        };
        RegisterClipboardHotkey();

        _tray = new Tray(OpenSettings, OpenClipboard, paused => { lock (_session) _session.Paused = paused; }, () => Shutdown(), Theme.Look.Current, SetTheme);
        if (firstRun)
        {
            _settings.Save(AppSettings.DefaultPath);
            _tray.ShowTip("Palta está lista", $"Arrastrá un archivo y tocá {_settings.ConvertKey} para convertirlo.");
            // Primera vez: mostramos la configuración para elegir arranque con Windows o acceso directo.
            OpenSettings();
        }
        Log.Info("Palta iniciada");
    }

    /// <summary>Tema Windows: notificación del sistema. Liquid glass: el aviso propio, estilo macOS.</summary>
    private void Notify(Job job)
    {
        if (Theme.Look.Translucent)
            _toasts.Add(job);
        else
            _windowsToasts.Add(job);
    }

    /// <summary>Desde Configuración o desde el menú de la bandeja: se aplica al toque y se guarda.</summary>
    private void SetTheme(Theme.AppTheme theme)
    {
        _settings.Theme = Theme.Look.ToSetting(theme);
        Theme.Look.Current = theme;
        _tray?.SetTheme(theme);
        SaveSettings();
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
        _hotkeyProblem = _hotkeys!.Register(HotkeyManager.WheelId, _settings.Hotkey, OnHotkey)
            ? null
            : $"No se pudo registrar {_settings.Hotkey}: otra aplicación ya lo usa.";
        if (_hotkeyProblem is not null)
            Log.Info(_hotkeyProblem);
    }

    private void RegisterClipboardHotkey()
    {
        if (!_hotkeys!.Register(HotkeyManager.ClipboardId, _settings.ClipboardHotkey, OpenClipboard))
        {
            var problem = $"No se pudo registrar {_settings.ClipboardHotkey} para el portapapeles: otra aplicación ya lo usa.";
            _hotkeyProblem = _hotkeyProblem is null ? problem : $"{_hotkeyProblem}\n{problem}";
            Log.Info(problem);
        }
    }

    private void OpenClipboard()
    {
        Win32.GetCursorPos(out var cursor);
        // Lo que venció mientras tanto (por ejemplo, de un día para otro) se va antes de mostrar.
        _clipHistory?.Prune();
        _clipWindow?.ShowNear(cursor.X, cursor.Y);
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
        _settingsWindow = new SettingsWindow(_settings, _hotkeyProblem, _clipHistory);
        _settingsWindow.Saving = updated =>
        {
            // Lo que no está en la ventana de Configuración se conserva.
            updated.LastOperationId = _settings.LastOperationId;
            updated.ClipboardFloating = _settings.ClipboardFloating;
            updated.ClipboardFloatX = _settings.ClipboardFloatX;
            updated.ClipboardFloatY = _settings.ClipboardFloatY;
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
            if (_clipWatcher is not null)
                _clipWatcher.Enabled = updated.ClipboardEnabled;
            if (_clipHistory is not null)
                _clipHistory.Limits = updated.ClipboardLimits;
            Theme.Look.Current = Theme.Look.Parse(updated.Theme);
            _tray?.SetTheme(Theme.Look.Current);
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
            RegisterClipboardHotkey();
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
        _clipWatcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
