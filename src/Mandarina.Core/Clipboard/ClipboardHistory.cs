using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mandarina.Core.Clipboard;

public enum ClipKind { Text, Image, Files }

public sealed class ClipItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ClipKind Kind { get; init; }
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
    public string? Text { get; init; }
    public string? ImagePath { get; init; }
    public IReadOnlyList<string>? Files { get; init; }

    /// <summary>Huella del contenido, para no guardar dos veces lo mismo.</summary>
    public string Hash { get; init; } = "";

    public string Preview => Kind switch
    {
        ClipKind.Text => ClipboardHistory.MakePreview(Text ?? ""),
        ClipKind.Image => "Imagen",
        _ => Files is { Count: 1 } f ? Path.GetFileName(f[0].TrimEnd('\\')) : $"{Files?.Count ?? 0} archivos",
    };
}

/// <summary>
/// Lo último que se copió (texto, imágenes y archivos), guardado en disco: un índice JSON y las imágenes en PNG.
/// Los fijados no se borran solos ni con "vaciar".
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxTextLength = 200_000;
    private const string IndexName = "historial.json";

    private readonly string _folder;
    private readonly int _max;
    private readonly object _gate = new();
    private List<ClipItem> _items = [];

    public event Action? Changed;

    public ClipboardHistory(string folder, int max = 50)
    {
        _folder = folder;
        _max = max;
        Directory.CreateDirectory(Path.Combine(folder, "imagenes"));
        Load();
    }

    /// <summary>MANDARINA_CLIPBOARD_DIR la cambia (para pruebas).</summary>
    public static string DefaultFolder =>
        Environment.GetEnvironmentVariable("MANDARINA_CLIPBOARD_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mandarina", "portapapeles");

    /// <summary>Fijados primero; dentro de cada grupo, lo más nuevo arriba.</summary>
    public IReadOnlyList<ClipItem> Items
    {
        get
        {
            lock (_gate)
                return _items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.CopiedAt).ToList();
        }
    }

    public IReadOnlyList<ClipItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Items;
        return Items.Where(i =>
            (i.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            (i.Files?.Any(f => Path.GetFileName(f).Contains(query, StringComparison.CurrentCultureIgnoreCase)) ?? false) ||
            (i.Kind == ClipKind.Image && "imagen".Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    public ClipItem? AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];
        return Add(Hash("t:" + text), h => new ClipItem { Kind = ClipKind.Text, Text = text, Hash = h });
    }

    public ClipItem? AddImage(byte[] png)
    {
        if (png.Length == 0)
            return null;
        var hash = Convert.ToHexString(SHA256.HashData(png))[..32];
        return Add("i:" + hash, h =>
        {
            var path = Path.Combine(_folder, "imagenes", $"{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, png);
            return new ClipItem { Kind = ClipKind.Image, ImagePath = path, Hash = h };
        });
    }

    public ClipItem? AddFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return null;
        return Add(Hash("f:" + string.Join('|', files)), h => new ClipItem { Kind = ClipKind.Files, Files = files.ToList(), Hash = h });
    }

    /// <summary>Lo vuelve a poner arriba (se acaba de pegar).</summary>
    public void Touch(Guid id) => Mutate(items =>
    {
        if (items.FirstOrDefault(i => i.Id == id) is { } item)
            item.CopiedAt = DateTime.Now;
    });

    public void Pin(Guid id, bool pinned) => Mutate(items =>
    {
        if (items.FirstOrDefault(i => i.Id == id) is { } item)
            item.Pinned = pinned;
    });

    public void Remove(Guid id) => Mutate(items =>
    {
        if (items.FirstOrDefault(i => i.Id == id) is { } item)
        {
            items.Remove(item);
            DeleteImage(item);
        }
    });

    /// <summary>Borra todo lo que no está fijado.</summary>
    public void Clear() => Mutate(items =>
    {
        foreach (var item in items.Where(i => !i.Pinned).ToList())
        {
            items.Remove(item);
            DeleteImage(item);
        }
    });

    public static string MakePreview(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > 200 ? collapsed[..200] + "…" : collapsed;
    }

    private ClipItem Add(string hash, Func<string, ClipItem> create)
    {
        ClipItem result = null!;
        Mutate(items =>
        {
            if (items.FirstOrDefault(i => i.Hash == hash) is { } existing)
            {
                existing.CopiedAt = DateTime.Now;
                result = existing;
                return;
            }
            result = create(hash);
            items.Add(result);
            // Se pasa del límite: afuera lo más viejo que no esté fijado.
            foreach (var old in items.Where(i => !i.Pinned).OrderByDescending(i => i.CopiedAt).Skip(Math.Max(0, _max - items.Count(i => i.Pinned))).ToList())
            {
                items.Remove(old);
                DeleteImage(old);
            }
        });
        return result;
    }

    private void Mutate(Action<List<ClipItem>> change)
    {
        lock (_gate)
        {
            change(_items);
            Save();
        }
        Changed?.Invoke();
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..32];

    private static void DeleteImage(ClipItem item)
    {
        if (item.ImagePath is { } p)
            SafeWrite.TryDelete(p);
    }

    private void Load()
    {
        try
        {
            var path = Path.Combine(_folder, IndexName);
            if (!File.Exists(path))
                return;
            var loaded = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(path)) ?? [];
            _items = loaded.Where(i => i.Kind != ClipKind.Image || (i.ImagePath is { } p && File.Exists(p))).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Historial del portapapeles dañado; empiezo de cero", e);
            _items = [];
        }
    }

    private void Save()
    {
        try
        {
            var path = Path.Combine(_folder, IndexName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Guardar historial del portapapeles", e);
        }
    }
}
