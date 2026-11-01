using Palta.Core.Operations;

namespace Palta.Core;

public sealed class Catalog(IEnumerable<IOperation> operations)
{
    private readonly IReadOnlyList<IOperation> _operations = operations.ToList();

    public IReadOnlyList<IOperation> All => _operations;

    public static Catalog CreateDefault(IShellHost host) =>
        new([.. ImageOperations.All(), .. PdfOperations.All(), .. ArchiveOperations.All(), .. ActionOperations.All(host), .. TextOperations.All(host)]);

    /// <summary>Las opciones de <paramref name="wheel"/> que sirven para todos estos archivos, en orden de catálogo.</summary>
    public IReadOnlyList<IOperation> For(WheelKind wheel, IReadOnlyList<string> files) =>
        files.Count == 0 ? [] : _operations.Where(o => o.Wheel == wheel && !o.ForText && o.AppliesTo(files)).ToList();

    /// <summary>Las opciones de <paramref name="wheel"/> para un texto arrastrado.</summary>
    public IReadOnlyList<IOperation> ForText(WheelKind wheel, string text) =>
        string.IsNullOrWhiteSpace(text) ? [] : _operations.Where(o => o.Wheel == wheel && o.ForText && o.AppliesTo([text])).ToList();

    public IOperation? ById(string id) => _operations.FirstOrDefault(o => o.Id == id);
}
