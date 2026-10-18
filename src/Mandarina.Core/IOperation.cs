namespace Mandarina.Core;

public enum WheelKind { Convert, Tools, Actions }

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
}

/// <summary>Lo que las acciones necesitan de la interfaz (selector de carpeta, portapapeles).</summary>
public interface IShellHost
{
    /// <summary>Null si el usuario cancela.</summary>
    string? PickFolder(string title);

    void SetClipboardText(string text);
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

    public bool AppliesTo(IReadOnlyList<string> files) => files.Count > 0 && appliesTo(files);

    public Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> files, IProgress<double> progress, CancellationToken ct) =>
        run(files, progress, ct);

    /// <summary>Operación que procesa cada archivo por separado; aplica si todos son de <paramref name="kind"/> y pasan <paramref name="accepts"/>.</summary>
    public static Operation PerFile(
        string id, WheelKind wheel, string label, string glyph, FileKind kind,
        Func<string, CancellationToken, Task<string>> runOne,
        Func<string, bool>? accepts = null) =>
        new(id, wheel, label, glyph,
            files => files.All(f => Formats.KindOf(f) == kind && (accepts?.Invoke(f) ?? true)),
            async (files, progress, ct) =>
            {
                var outputs = new List<string>(files.Count);
                for (var i = 0; i < files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    outputs.Add(await runOne(files[i], ct));
                    progress.Report((i + 1) / (double)files.Count);
                }
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
