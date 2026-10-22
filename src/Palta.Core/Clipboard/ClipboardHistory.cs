using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Palta.Core.Clipboard;

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
/// Cuánto se guarda del portapapeles. 0 en cualquiera de los tres = sin ese límite.
/// Lo que se cumpla primero manda; los fijados nunca se borran solos.
/// </summary>
/// <param name="KeepDays">Se borra lo que tenga más de estos días desde que se copió (o se pegó) por última vez.</param>
/// <param name="MaxItems">Cantidad máxima de elementos sin fijar.</param>
/// <param name="MaxImageMegabytes">Espacio máximo para las imágenes sin fijar; si se pasa, se van las más viejas.</param>
public sealed record ClipboardLimits(int KeepDays, int MaxItems, int MaxImageMegabytes)
{
    public static readonly ClipboardLimits Default = new(30, 500, 300);
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
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private List<ClipItem> _items = [];
    private ClipboardLimits _limits;

    public event Action? Changed;

    /// <param name="now">Reloj (para pruebas); por defecto, <see cref="DateTime.Now"/>.</param>
    public ClipboardHistory(string folder, ClipboardLimits? limits = null, Func<DateTime>? now = null)
    {
        _folder = folder;
        _limits = limits ?? ClipboardLimits.Default;
        _now = now ?? (() => DateTime.Now);
        Directory.CreateDirectory(Path.Combine(folder, "imagenes"));
        Load();
        Prune();
    }

    /// <summary>Cambiarlos recorta en el momento lo que ya no entra.</summary>
    public ClipboardLimits Limits
    {
        get => _limits;
        set
        {
            _limits = value;
            Prune();
        }
    }

    /// <summary>Lo que ocupan en disco las imágenes guardadas (fijadas incluidas).</summary>
    public long ImageBytes
    {
        get
        {
            lock (_gate)
                return _items.Sum(SizeOf);
        }
    }

    /// <summary>Borra lo que ya no entra en los límites (por ejemplo, lo que venció desde ayer). Lo llama el panel al abrirse.</summary>
    public void Prune()
    {
        var removed = false;
        lock (_gate)
        {
            removed = PruneLocked(_items);
            if (removed)
                Save();
        }
        if (removed)
            Changed?.Invoke();
    }

    /// <summary>PALTA_CLIPBOARD_DIR la cambia (para pruebas).</summary>
    public static string DefaultFolder =>
        Environment.GetEnvironmentVariable("PALTA_CLIPBOARD_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Palta", "portapapeles");

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
            item.CopiedAt = _now();
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
                existing.CopiedAt = _now();
                result = existing;
                return;
            }
            result = create(hash);
            result.CopiedAt = _now();
            items.Add(result);
            PruneLocked(items);
        });
        return result;
    }

    /// <summary>
    /// Afuera, en este orden, lo que no esté fijado: lo vencido, lo que se pasa de la cantidad
    /// (lo más viejo primero) y las imágenes más viejas hasta entrar en el espacio.
    /// Lo recién copiado se queda aunque una sola imagen supere el espacio.
    /// </summary>
    private bool PruneLocked(List<ClipItem> items)
    {
        var drop = new HashSet<ClipItem>();
        var loose = items.Where(i => !i.Pinned).OrderByDescending(i => i.CopiedAt).ToList();
        if (_limits.KeepDays > 0)
        {
            var cutoff = _now().AddDays(-_limits.KeepDays);
            drop.UnionWith(loose.Where(i => i.CopiedAt < cutoff));
        }
        if (_limits.MaxItems > 0)
            drop.UnionWith(loose.Where(i => !drop.Contains(i)).Skip(_limits.MaxItems));
        if (_limits.MaxImageMegabytes > 0)
        {
            var budget = _limits.MaxImageMegabytes * 1024L * 1024L;
            long used = 0;
            var newest = true;
            foreach (var image in loose.Where(i => i.Kind == ClipKind.Image && !drop.Contains(i)))
            {
                used += SizeOf(image);
                if (used > budget && !newest)
                    drop.Add(image);
                newest = false;
            }
        }
        foreach (var item in drop)
        {
            items.Remove(item);
            DeleteImage(item);
        }
        return drop.Count > 0;
    }

    private static long SizeOf(ClipItem item)
    {
        if (item.ImagePath is not { } p)
            return 0;
        try { return new FileInfo(p).Length; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
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
