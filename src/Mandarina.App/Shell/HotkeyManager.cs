using System.Windows.Input;
using System.Windows.Interop;
using Mandarina.App.Native;

namespace Mandarina.App.Shell;

/// <summary>Atajos globales (RegisterHotKey) sobre una ventana de mensajes oculta; cada uno con su id.</summary>
internal sealed class HotkeyManager : IDisposable
{
    public const int WheelId = 1;
    public const int ClipboardId = 2;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("MandarinaHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _source.AddHook(WndProc);
    }

    /// <summary>Registra "Ctrl+Alt+Space" y similares; false si el formato es inválido o el atajo ya lo usa otra app.</summary>
    public bool Register(int id, string? combo, Action action)
    {
        Unregister(id);
        if (!TryParse(combo, out var mods, out var vk))
            return false;
        if (!Win32.RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, vk))
            return false;
        _actions[id] = action;
        return true;
    }

    public static bool TryParse(string? combo, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(combo))
            return false;
        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default:
                    if (!Enum.TryParse<Key>(raw, true, out var key))
                        return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }

    public void Unregister(int id)
    {
        if (_actions.Remove(id))
            Win32.UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys.ToList())
            Unregister(id);
        _source.Dispose();
    }
}
