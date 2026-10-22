using Palta.Core;
using Palta.Core.Input;
using Xunit;

namespace Palta.Tests;

public class DragSessionTests
{
    private readonly List<(WheelKind Wheel, int X, int Y)> _requests = [];
    private int _repeats;
    private int _ended;

    private DragSession New()
    {
        var s = new DragSession(4, 4, m => m switch
        {
            Mod.Shift => WheelKind.Convert,
            Mod.Ctrl => WheelKind.Tools,
            Mod.Alt => WheelKind.Actions,
            _ => null,
        });
        s.WheelRequested += (w, x, y) => _requests.Add((w, x, y));
        s.RepeatRequested += () => _repeats++;
        s.DragEnded += () => _ended++;
        return s;
    }

    private static void Drag(DragSession s)
    {
        s.MouseDown(100, 100);
        s.MouseMove(103, 100);
        s.MouseMove(110, 105);
    }

    private static void Tap(DragSession s, Mod m)
    {
        s.KeyDown(m);
        s.KeyUp(m);
    }

    [Fact]
    public void Tap_WhileDragging_RequestsWheel()
    {
        var s = New();
        Drag(s);
        Tap(s, Mod.Shift);
        Assert.Equal([(WheelKind.Convert, 110, 105)], _requests);
        Assert.True(s.WheelOpen);
    }

    [Fact]
    public void Tap_WithoutDrag_Nothing()
    {
        var s = New();
        Tap(s, Mod.Shift);
        s.MouseDown(0, 0);
        Tap(s, Mod.Shift);
        Assert.Empty(_requests);
    }

    [Fact]
    public void ShortDrag_BelowThreshold_Nothing()
    {
        var s = New();
        s.MouseDown(100, 100);
        s.MouseMove(103, 102);
        Tap(s, Mod.Shift);
        Assert.Empty(_requests);
    }

    [Fact]
    public void ChordWithOtherKey_NotATap()
    {
        var s = New();
        Drag(s);
        s.KeyDown(Mod.Shift);
        s.KeyDown(Mod.Other);
        s.KeyUp(Mod.Other);
        s.KeyUp(Mod.Shift);
        Assert.Empty(_requests);
    }

    [Fact]
    public void KeyHeldBeforeDrag_NotATap()
    {
        var s = New();
        s.KeyDown(Mod.Shift);
        Drag(s);
        s.KeyUp(Mod.Shift);
        Assert.Empty(_requests);
    }

    [Fact]
    public void HeldBeforeDrag_WithAutoRepeat_NotATap()
    {
        // Shift+arrastrar (mover en el Explorador): Windows repite KeyDown mientras se mantiene.
        var s = New();
        s.KeyDown(Mod.Shift);
        Drag(s);
        s.KeyDown(Mod.Shift);
        s.KeyDown(Mod.Shift);
        s.KeyUp(Mod.Shift);
        Assert.Empty(_requests);
    }

    [Fact]
    public void AutoRepeatKeyDown_StillATap()
    {
        var s = New();
        Drag(s);
        s.KeyDown(Mod.Ctrl);
        s.KeyDown(Mod.Ctrl);
        s.KeyDown(Mod.Ctrl);
        s.KeyUp(Mod.Ctrl);
        Assert.Equal(WheelKind.Tools, _requests.Single().Wheel);
    }

    [Fact]
    public void SameTapWhileOpen_Repeat()
    {
        var s = New();
        Drag(s);
        Tap(s, Mod.Shift);
        Tap(s, Mod.Shift);
        Assert.Single(_requests);
        Assert.Equal(1, _repeats);
    }

    [Fact]
    public void OtherTapWhileOpen_SwitchesWheel()
    {
        var s = New();
        Drag(s);
        Tap(s, Mod.Shift);
        s.MouseMove(120, 120);
        Tap(s, Mod.Alt);
        Assert.Equal([(WheelKind.Convert, 110, 105), (WheelKind.Actions, 120, 120)], _requests);
    }

    [Fact]
    public void MouseUp_Resets()
    {
        var s = New();
        Drag(s);
        Tap(s, Mod.Shift);
        s.MouseUp();
        Assert.False(s.WheelOpen);
        Assert.Equal(1, _ended);
        Tap(s, Mod.Shift);
        Assert.Single(_requests);
    }

    [Fact]
    public void Paused_IgnoresEverything()
    {
        var s = New();
        s.Paused = true;
        Drag(s);
        Tap(s, Mod.Shift);
        Assert.Empty(_requests);
    }
}

public class WheelGeometryTests
{
    [Theory]
    [InlineData(0, -100, 4, 0)]   // arriba
    [InlineData(100, 0, 4, 1)]    // derecha
    [InlineData(0, 100, 4, 2)]    // abajo
    [InlineData(-100, 0, 4, 3)]   // izquierda
    [InlineData(-20, -100, 4, 0)] // un poco a la izquierda de arriba sigue siendo 0
    [InlineData(70, -70, 8, 1)]   // diagonal arriba-derecha con 8 sectores
    public void SectorAt_Angles(double dx, double dy, int count, int expected) =>
        Assert.Equal(expected, WheelGeometry.SectorAt(dx, dy, count, 48, 150));

    [Fact]
    public void SectorAt_Center() => Assert.Equal(-1, WheelGeometry.SectorAt(10, 10, 4, 48, 150));

    [Fact]
    public void SectorAt_Outside() => Assert.Null(WheelGeometry.SectorAt(200, 0, 4, 48, 150));

    [Fact]
    public void SectorAt_NoSectors_RingIsNothing() => Assert.Null(WheelGeometry.SectorAt(100, 0, 0, 48, 150));

    [Fact]
    public void SectorCenter_RoundTrips()
    {
        for (var i = 0; i < 7; i++)
        {
            var (x, y) = WheelGeometry.SectorCenter(i, 7, 100);
            Assert.Equal(i, WheelGeometry.SectorAt(x, y, 7, 48, 150));
        }
    }
}
