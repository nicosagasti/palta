namespace Mandarina.Core.Input;

public enum Mod { Shift, Ctrl, Alt, Other }

/// <summary>
/// Decide cuándo abrir la rueda a partir de la entrada cruda del mouse y el teclado:
/// un "toque" de tecla (apretar y soltar sin otra tecla en el medio) mientras se arrastra.
/// No sabe nada de Windows, así que se puede probar sola. No es segura entre hilos:
/// se llama siempre desde el mismo hilo (el de los hooks).
/// </summary>
public sealed class DragSession(int dragDx, int dragDy, Func<Mod, WheelKind?> wheelFor)
{
    private enum State { Idle, Pressed, Dragging }

    private State _state;
    private int _startX, _startY, _x, _y;
    private Mod? _pending;
    private readonly HashSet<Mod> _held = [];
    private WheelKind? _openWheel;

    /// <summary>(rueda, x, y) en coordenadas de pantalla.</summary>
    public event Action<WheelKind, int, int>? WheelRequested;

    /// <summary>Se tocó la misma tecla con la rueda abierta: repetir la última acción.</summary>
    public event Action? RepeatRequested;

    /// <summary>Se soltó el botón mientras la rueda estaba abierta.</summary>
    public event Action? DragEnded;

    public bool WheelOpen => _openWheel is not null;

    public bool Paused { get; set; }

    private bool Dragging => _state == State.Dragging;

    public void MouseDown(int x, int y)
    {
        _state = State.Pressed;
        (_startX, _startY, _x, _y) = (x, y, x, y);
        _pending = null;
    }

    public void MouseMove(int x, int y)
    {
        (_x, _y) = (x, y);
        if (_state == State.Pressed && (Math.Abs(x - _startX) > dragDx || Math.Abs(y - _startY) > dragDy))
            _state = State.Dragging;
    }

    public void MouseUp()
    {
        var wasOpen = WheelOpen;
        _state = State.Idle;
        _pending = null;
        _openWheel = null;
        if (wasOpen)
            DragEnded?.Invoke();
    }

    public void KeyDown(Mod m)
    {
        if (m == Mod.Other || (_pending is { } p && p != m))
        {
            _pending = null;
            return;
        }
        // Las repeticiones automáticas de una tecla mantenida llegan como más KeyDown: solo cuenta
        // el paso de suelta a apretada, y solo si pasa durante el arrastre (Shift+arrastrar no es un toque).
        if (!_held.Add(m))
            return;
        if (_pending is null && Dragging && !Paused)
            _pending = m;
    }

    public void KeyUp(Mod m)
    {
        _held.Remove(m);
        if (_pending != m)
            return;
        _pending = null;
        if (!Dragging || Paused || wheelFor(m) is not { } wheel)
            return;

        if (_openWheel == wheel)
        {
            RepeatRequested?.Invoke();
            return;
        }
        _openWheel = wheel;
        WheelRequested?.Invoke(wheel, _x, _y);
    }

    /// <summary>La rueda se cerró por su cuenta (soltaron el archivo sobre ella, o Esc).</summary>
    public void WheelClosed() => _openWheel = null;
}
