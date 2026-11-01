namespace Palta.Core;

public enum WheelKind { Convert, Tools, Actions }

/// <summary>El usuario se echó atrás a mitad de camino (por ejemplo, cerró el selector de carpeta): el aviso dice "Cancelado".</summary>
public sealed class CanceledByUserException() : OperationCanceledException("Cancelado por el usuario.");

/// <summary>Una opción de la rueda: convertir, una herramienta o una acción.</summary>
public interface IOperation
{
    string Id { get; }
    WheelKind Wheel { get; }
    string Label { get; }

    /// <summary>Carácter de Segoe Fluent Icons.</summary>
    string Glyph { get; }

    bool AppliesTo(IReadOnlyList<string> files);

    /// <summary>Ejecuta la operación y devuelve las rutas que produjo.</summary>
    Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> files, IProgress<double> progress, CancellationToken ct);

    /// <summary>
    /// La operación muestra su propia interfaz (panel de compartir, estante, terminal):
    /// no hace falta aviso salvo que falle.
    /// </summary>
    bool Quiet => false;

    /// <summary>Trabaja sobre un texto arrastrado, no sobre archivos: la lista trae un solo elemento, el texto.</summary>
    bool ForText => false;
}

/// <summary>Lo que una operación le quiere decir al usuario en el aviso (el resultado de una cuenta, cómo quedó el texto).</summary>
public interface IJobNotes
{
    void Note(string text);
}

public static class JobNote
{
    /// <summary>Si quien sigue el progreso es un trabajo de la cola, el aviso muestra <paramref name="text"/>.</summary>
    public static void Set(IProgress<double> progress, string text) => (progress as IJobNotes)?.Note(text);
}

/// <summary>Lo que las acciones necesitan de la interfaz (selector de carpeta, portapapeles, compartir…).</summary>
public interface IShellHost
{
    /// <summary>Null si el usuario cancela.</summary>
    string? PickFolder(string title);

    void SetClipboardText(string text);

    /// <summary>Abre el panel de compartir de Windows con estos archivos (el equivalente a AirDrop).</summary>
    void Share(IReadOnlyList<string> files);

    /// <summary>Deja los archivos en el estante flotante, desde donde se los puede volver a arrastrar.</summary>
    void Float(IReadOnlyList<string> files);

    /// <summary>Abre una terminal en esa carpeta.</summary>
    void OpenTerminal(string folder);

    /// <summary>Abre un link, un mailto:, una carpeta o un programa, como si se hiciera doble clic.</summary>
    void Launch(string target, string? arguments = null);

    /// <summary>Lee el texto en voz alta con las voces de Windows (si se vuelve a pedir, corta lo anterior).</summary>
    void Speak(string text);
}

public sealed class Operation(
    string id,
    WheelKind wheel,
    string label,
    string glyph,
    Func<IReadOnlyList<string>, bool> appliesTo,
    Func<IReadOnlyList<string>, IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> run) : IOperation
{
    public string Id => id;
    public WheelKind Wheel => wheel;
    public string Label => label;
    public string Glyph => glyph;
    public bool Quiet { get; init; }
    public bool ForText { get; init; }

    public bool AppliesTo(IReadOnlyList<string> files) => files.Count > 0 && appliesTo(files);

    public Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> files, IProgress<double> progress, CancellationToken ct) =>
        run(files, progress, ct);

    /// <summary>
    /// Cuántos archivos de un mismo trabajo se procesan a la vez: la mitad de los núcleos, entre 1 y 4.
    /// Más no rinde (las imágenes grandes comen memoria) y deja la PC usable mientras convierte.
    /// </summary>
    public static int Parallelism { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>
    /// Operación que procesa cada archivo por separado (varios a la vez, ver <see cref="Parallelism"/>);
    /// aplica si todos son de <paramref name="kind"/> y pasan <paramref name="accepts"/>.
    /// Los resultados salen en el mismo orden que los archivos, y el progreso es el total del trabajo.
    /// </summary>
    public static Operation PerFile(
        string id, WheelKind wheel, string label, string glyph, FileKind kind,
        Func<string, CancellationToken, Task<string>> runOne,
        Func<string, bool>? accepts = null) =>
        new(id, wheel, label, glyph,
            files => files.All(f => Formats.KindOf(f) == kind && (accepts?.Invoke(f) ?? true)),
            async (files, progress, ct) =>
            {
                var outputs = new string[files.Count];
                var done = 0;
                // Si uno falla, Parallel deja de arrancar los que faltan y el error llega tal cual.
                // Dos resultados con el mismo nombre no se pisan: SafeWrite le busca otro al segundo.
                await Parallel.ForEachAsync(Enumerable.Range(0, files.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
                    async (i, token) =>
                    {
                        outputs[i] = await runOne(files[i], token);
                        progress.Report(Interlocked.Increment(ref done) / (double)files.Count);
                    });
                return outputs;
            });

    /// <summary>Operación que combina todos los archivos en un solo resultado (unir); requiere al menos <paramref name="minFiles"/>.</summary>
    public static Operation Combine(
        string id, WheelKind wheel, string label, string glyph, FileKind kind, int minFiles,
        Func<IReadOnlyList<string>, CancellationToken, Task<string>> runAll) =>
        new(id, wheel, label, glyph,
            files => files.Count >= minFiles && files.All(f => Formats.KindOf(f) == kind),
            async (files, progress, ct) =>
            {
                var output = await runAll(files, ct);
                progress.Report(1);
                return [output];
            });
}
