using System.IO;
using System.Media;
using Windows.Media.SpeechSynthesis;

namespace Palta.App.Shell;

/// <summary>
/// "Leer en voz alta" con las voces que trae Windows (andan sin internet). Prefiere una voz en castellano;
/// el WAV que arma el sintetizador lo reproduce SoundPlayer, así que cortar es un Stop.
/// </summary>
internal static class Speech
{
    private static SoundPlayer? _player;
    private static int _generation;

    public static async void Say(string text)
    {
        var generation = ++_generation;
        Stop();
        try
        {
            using var synth = new SpeechSynthesizer();
            if (SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase)) is { } voice)
                synth.Voice = voice;
            using var stream = await synth.SynthesizeTextToStreamAsync(text);
            // Mientras se armaba el audio pidieron otro: este ya no va.
            if (generation != _generation)
                return;
            var wav = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(wav);
            wav.Position = 0;
            _player = new SoundPlayer(wav);
            _player.Play();
        }
        catch (Exception e)
        {
            Core.Log.Error("Leer en voz alta", e);
        }
    }

    public static void Stop()
    {
        _player?.Stop();
        _player?.Stream?.Dispose();
        _player?.Dispose();
        _player = null;
    }
}
