using System.Windows.Input;
using System.Windows.Interop;
using Mandarina.App.Native;

namespace Mandarina.App.Shell;

/// <summary>Un atajo global (RegisterHotKey) sobre una ventana de mensajes oculta.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int Id = 1;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private Action? _action;
    private bool _registered;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("MandarinaHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _source.AddHook(WndProc);
    }

    /// <summary>Registra "Ctrl+Alt+Space" y similares; false si el formato es inválido o el atajo ya lo usa otra app.</summary>
    public bool Register(string combo, Action action)
    {
        Unregister();
        if (!TryParse(combo, out var mods, out var vk))
            return false;
        _action = action;
        _registered = Win32.RegisterHotKey(_source.Handle, Id, mods | MOD_NOREPEAT, vk);
        return _registered;
    }

    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
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

    private void Unregister()
    {
        if (_registered)
            Win32.UnregisterHotKey(_source.Handle, Id);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            _action?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
