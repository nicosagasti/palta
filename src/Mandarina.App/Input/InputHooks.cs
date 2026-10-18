using System.Runtime.InteropServices;
using Mandarina.App.Native;
using Mandarina.Core.Input;

namespace Mandarina.App.Input;

/// <summary>
/// Hooks globales de mouse y teclado en un hilo propio. Los callbacks solo alimentan a
/// <see cref="DragSession"/> y vuelven enseguida: Windows quita los hooks que tardan.
/// Nunca se come un evento.
/// </summary>
internal sealed class InputHooks(DragSession session) : IDisposable
{
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook, _keyboardHook;
    // Referencias fuertes: si el GC junta los delegados, el hook revienta.
    private Win32.HookProc? _mouseProc, _keyboardProc;

    public void Start()
    {
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Mandarina hooks" };
        _thread.Start();
        ready.Wait();
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = Win32.GetCurrentThreadId();
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        var module = Win32.GetModuleHandleW(null);
        _mouseHook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            Core.Log.Error($"No se pudieron instalar los hooks (error {Marshal.GetLastWin32Error()})");
        ready.Set();

        while (Win32.GetMessageW(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        Win32.UnhookWindowsHookEx(_mouseHook);
        Win32.UnhookWindowsHookEx(_keyboardHook);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                lock (session)
                switch ((int)wParam)
                {
                    case Win32.WM_MOUSEMOVE:
                        session.MouseMove(data.pt.X, data.pt.Y);
                        break;
                    case Win32.WM_LBUTTONDOWN:
                        session.MouseDown(data.pt.X, data.pt.Y);
                        break;
                    case Win32.WM_LBUTTONUP:
                        session.MouseUp();
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log.Error("Hook de mouse", e);
            }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var mod = ToMod(data.vkCode);
                lock (session)
                switch ((int)wParam)
                {
                    case Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN:
                        session.KeyDown(mod);
                        break;
                    case Win32.WM_KEYUP or Win32.WM_SYSKEYUP:
                        session.KeyUp(mod);
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log.Error("Hook de teclado", e);
            }
        }
        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static Mod ToMod(uint vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => Mod.Shift,
        0x11 or 0xA2 or 0xA3 => Mod.Ctrl,
        0x12 or 0xA4 or 0xA5 => Mod.Alt,
        _ => Mod.Other,
    };

    public void Dispose()
    {
        if (_thread is null)
            return;
        Win32.PostThreadMessageW(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }
}
