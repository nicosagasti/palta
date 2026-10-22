using Palta.Core;
using Xunit;

namespace Palta.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults()
    {
        var s = new Settings();
        Assert.Equal(("Shift", "Ctrl", "Alt", "Ctrl+Alt+Space"), (s.ConvertKey, s.ToolsKey, s.ActionsKey, s.Hotkey));
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        using var d = new TempDir();
        var p = Path.Combine(d.Path, "sub", "settings.json");
        new Settings { ToolsKey = "Alt", ActionsKey = "Ctrl", StartWithWindows = true, LastOperationId = "img-to-jpg" }.Save(p);
        var s = Settings.Load(p);
        Assert.Equal(("Alt", "Ctrl", true, "img-to-jpg"), (s.ToolsKey, s.ActionsKey, s.StartWithWindows, s.LastOperationId));
    }

    [Fact]
    public void Theme_DefaultsToWindows_OldFilesToo_AndFloatRoundTrips()
    {
        using var d = new TempDir();
        Assert.Equal("windows", new Settings().Theme);
        // Un settings.json de antes del cambio (sin Theme) o con null queda en el tema Windows.
        Assert.Equal("windows", Settings.Load(d.File("viejo.json", "{ \"ConvertKey\": \"Shift\" }")).Theme);
        Assert.Equal("windows", Settings.Load(d.File("nulo.json", "{ \"Theme\": null }")).Theme);
        var p = Path.Combine(d.Path, "s.json");
        new Settings { Theme = "glass", ClipboardFloating = true, ClipboardFloatX = -1500, ClipboardFloatY = 40 }.Save(p);
        var s = Settings.Load(p);
        Assert.Equal(("glass", true, -1500, 40), (s.Theme, s.ClipboardFloating, s.ClipboardFloatX, s.ClipboardFloatY));
    }

    [Fact]
    public void Settings_CorruptFile_Defaults()
    {
        using var d = new TempDir();
        var p = d.File("settings.json", "{ esto no es json");
        Assert.Equal("Shift", Settings.Load(p).ConvertKey);
        Assert.Equal("Shift", Settings.Load(Path.Combine(d.Path, "no-existe.json")).ConvertKey);
    }

    [Fact]
    public void Settings_NullStrings_BecomeDefaults()
    {
        using var d = new TempDir();
        var p = d.File("settings.json", "{ \"Hotkey\": null, \"ConvertKey\": null, \"ClipboardHotkey\": null }");
        var s = Settings.Load(p);
        Assert.Equal(("Ctrl+Alt+Space", "Shift", "Ctrl+Alt+V"), (s.Hotkey, s.ConvertKey, s.ClipboardHotkey));
    }

    [Fact]
    public async Task SafeWrite_Exception_DeletesTemp()
    {
        using var d = new TempDir();
        var final = Path.Combine(d.Path, "out.jpg");
        await Assert.ThrowsAsync<InvalidOperationException>(() => SafeWrite.WriteAsync(final, async tmp =>
        {
            await File.WriteAllTextAsync(tmp, "a medias");
            throw new InvalidOperationException("falló");
        }, default));
        Assert.Empty(d.Entries());
    }

    [Fact]
    public async Task SafeWrite_Success_MovesToFinal()
    {
        using var d = new TempDir();
        var final = Path.Combine(d.Path, "out.jpg");
        var result = await SafeWrite.WriteAsync(final, tmp => File.WriteAllTextAsync(tmp, "ok"), default);
        Assert.Equal(final, result);
        Assert.Equal(["out.jpg"], d.Entries());
    }

    [Fact]
    public async Task SafeWrite_TargetAppearedMeanwhile_PicksNewName()
    {
        using var d = new TempDir();
        var final = Path.Combine(d.Path, "out.jpg");
        var result = await SafeWrite.WriteAsync(final, async tmp =>
        {
            await File.WriteAllTextAsync(tmp, "nuevo");
            await File.WriteAllTextAsync(final, "otro proceso");
        }, default);
        Assert.Equal(Path.Combine(d.Path, "out (1).jpg"), result);
        Assert.Equal("otro proceso", File.ReadAllText(final));
    }
}
