using Palta.Core.Input;
using Xunit;

namespace Palta.Tests;

public class HookWatchdogTests
{
    [Fact]
    public void HookSawLatestInput_Fine() => Assert.False(HookWatchdog.ShouldReinstall(lastInputTick: 10_000, lastHookTick: 10_000, nowTick: 70_000));

    [Fact]
    public void NoInputSinceHook_Fine() => Assert.False(HookWatchdog.ShouldReinstall(lastInputTick: 5_000, lastHookTick: 9_000, nowTick: 70_000));

    [Fact]
    public void InputAfterHookWentQuiet_Reinstall() => Assert.True(HookWatchdog.ShouldReinstall(lastInputTick: 60_000, lastHookTick: 10_000, nowTick: 70_000));

    [Fact]
    public void InputJustNow_WithinGrace_Fine() => Assert.False(HookWatchdog.ShouldReinstall(lastInputTick: 69_000, lastHookTick: 67_000, nowTick: 70_000));

    [Fact]
    public void TickWraparound_StillDetected()
    {
        // El hook vio algo justo antes de que el contador volviera a cero; la entrada es de después.
        var hook = uint.MaxValue - 5_000;
        Assert.True(HookWatchdog.ShouldReinstall(lastInputTick: 20_000, lastHookTick: hook, nowTick: 30_000));
        Assert.False(HookWatchdog.ShouldReinstall(lastInputTick: hook, lastHookTick: hook, nowTick: 30_000));
    }
}
