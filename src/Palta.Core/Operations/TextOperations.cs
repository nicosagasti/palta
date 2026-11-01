using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QRCoder;

namespace Palta.Core.Operations;

/// <summary>
/// Opciones para un texto arrastrado (no un archivo). La "lista de archivos" que reciben trae un solo
/// elemento: el texto. Las que lo cambian lo dejan copiado; las que crean algo lo guardan en el Escritorio.
/// </summary>
public static partial class TextOperations
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    public static IEnumerable<IOperation> All(IShellHost host)
    {
        // ---------- Convertir: devuelven el texto cambiado, copiado ----------
        yield return Transform(host, "text-upper", "MAYÚSCULAS", "", _ => true, t => t.ToUpper(Es));
        yield return Transform(host, "text-lower", "minúsculas", "", _ => true, t => t.ToLower(Es));
        yield return Transform(host, "text-title", "Tipo Título", "", _ => true, TitleCase);
        yield return Transform(host, "text-no-accents", "Sin tildes", "", t => RemoveAccents(t) != t, RemoveAccents);
        yield return Transform(host, "text-join-lines", "Unir líneas", "", t => JoinLines(t) != t, JoinLines);
        yield return Transform(host, "text-clean", "Limpiar espacios", "", t => CleanSpaces(t) != t, CleanSpaces);
        yield return Transform(host, "text-json", "JSON ordenado", "", t => FormatJson(t) is not null, t => FormatJson(t)!);
        yield return Transform(host, "text-color", "RGB / HEX", "", t => ConvertColor(t) is not null, t => ConvertColor(t)!);

        // ---------- Herramientas ----------
        yield return Text("text-count", WheelKind.Tools, "Contar", "", _ => true, (text, progress) =>
        {
            JobNote.Set(progress, Count(text));
            return [];
        });
        yield return Transform(host, "text-sort", "Ordenar líneas", "", t => Lines(t).Length > 1 && SortLines(t) != t, SortLines, WheelKind.Tools);
        yield return Transform(host, "text-dedupe", "Quitar repetidas", "", t => Dedupe(t) != t, Dedupe, WheelKind.Tools);
        yield return Text("text-calc", WheelKind.Tools, "Calcular", "", t => Calculate(t) is not null, (text, progress) =>
        {
            var result = Calculate(text)!;
            host.SetClipboardText(result);
            JobNote.Set(progress, $"{text.Trim()} = {result}");
            return [];
        });
        yield return Text("text-qr", WheelKind.Tools, "Código QR", "", t => t.Trim().Length is > 0 and <= MaxQrChars, (text, _) =>
            [SaveToDesktop(text, "QR", ".png", path => File.WriteAllBytes(path, QrPng(text.Trim())))]);
        yield return Text("text-save", WheelKind.Tools, "Guardar .txt", "", _ => true, (text, _) =>
            [SaveToDesktop(text, null, ".txt", path => File.WriteAllText(path, text, new UTF8Encoding(true)))]);
        yield return Text("text-pdf", WheelKind.Tools, "Hacer PDF", "", _ => true, (text, _) =>
            [SaveToDesktop(text, null, ".pdf", path => TextToPdf(text, path))]);

        // ---------- Acciones ----------
        yield return Text("text-copy", WheelKind.Actions, "Copiar", "", _ => true, (text, progress) =>
        {
            // Sin formato: solo el texto, como "pegar sin formato".
            host.SetClipboardText(text);
            JobNote.Set(progress, Snippet(text));
            return [];
        });
        yield return Quiet("text-open-link", "Abrir link", "", t => Url(t) is not null, t => host.Launch(Url(t)!));
        yield return Quiet("text-mail", "Escribir mail", "", t => Email(t) is not null, t => host.Launch("mailto:" + Email(t)));
        yield return Quiet("text-open-folder", "Abrir carpeta", "", t => ExistingPath(t) is not null, t =>
        {
            var path = ExistingPath(t)!;
            if (Directory.Exists(path))
                host.Launch(path);
            else
                host.Launch("explorer.exe", $"/select,\"{path}\"");
        });
        yield return Quiet("text-search", "Buscar en la web", "", t => t.Trim().Length is > 0 and <= MaxSearchChars,
            t => host.Launch("https://www.google.com/search?q=" + Uri.EscapeDataString(Squash(t))));
        yield return Quiet("text-notepad", "Bloc de notas", "", _ => true, t =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "Palta");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"texto {DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt");
            File.WriteAllText(path, t, new UTF8Encoding(true));
            host.Launch("notepad.exe", $"\"{path}\"");
        });
        yield return Quiet("text-speak", "Leer en voz alta", "", t => !string.IsNullOrWhiteSpace(t), host.Speak);
    }

    public const int MaxQrChars = 1000;
    public const int MaxSearchChars = 300;

    // ---------- Fábricas ----------

    private static Operation Text(string id, WheelKind wheel, string label, string glyph, Func<string, bool> applies,
        Func<string, IProgress<double>, IReadOnlyList<string>> run) =>
        new(id, wheel, label, glyph, files => files.Count == 1 && applies(files[0]),
            (files, progress, ct) => Task.Run(() =>
            {
                var outputs = run(files[0], progress);
                progress.Report(1);
                return outputs;
            }, ct))
        {
            ForText = true,
        };

    /// <summary>Cambia el texto y lo deja copiado; el aviso muestra cómo quedó.</summary>
    private static Operation Transform(IShellHost host, string id, string label, string glyph, Func<string, bool> applies,
        Func<string, string> change, WheelKind wheel = WheelKind.Convert) =>
        Text(id, wheel, label, glyph, applies, (text, progress) =>
        {
            var result = change(text);
            host.SetClipboardText(result);
            JobNote.Set(progress, Snippet(result));
            return [];
        });

    /// <summary>Abre algo (navegador, mail, carpeta): no hace falta aviso salvo que falle.</summary>
    private static Operation Quiet(string id, string label, string glyph, Func<string, bool> applies, Action<string> run) =>
        new(id, WheelKind.Actions, label, glyph, files => files.Count == 1 && applies(files[0]),
            (files, progress, _) =>
            {
                run(files[0]);
                progress.Report(1);
                return Task.FromResult<IReadOnlyList<string>>([]);
            })
        {
            ForText = true,
            Quiet = true,
        };

    // ---------- Lo que se muestra ----------

    /// <summary>Las primeras palabras en una línea: «Hola, ¿cómo andás?…».</summary>
    public static string Snippet(string text, int max = 40)
    {
        var flat = Squash(text);
        if (flat.Length <= max)
            return $"«{flat}»";
        // Se corta en un espacio para no dejar media palabra, salvo que la primera sea larguísima.
        var cut = flat.LastIndexOf(' ', max);
        return $"«{flat[..(cut > max / 2 ? cut : max)].TrimEnd(' ', ',', '.', ';', ':')}…»";
    }

    /// <summary>Lo que dice la cápsula de la rueda: «Hola, ¿cómo…» · 12 palabras.</summary>
    public static string Summary(string text)
    {
        // Un link o una cuenta no son "palabras": alcanza con mostrarlos.
        if (!text.Trim().Any(char.IsWhiteSpace))
            return Snippet(text, 30);
        var words = WordCount(text);
        return $"{Snippet(text, 24)} · {(words == 1 ? "1 palabra" : $"{words} palabras")}";
    }

    public static string Count(string text)
    {
        var words = WordCount(text);
        var chars = text.Length;
        var lines = Lines(text).Length;
        var parts = new List<string>
        {
            words == 1 ? "1 palabra" : $"{words.ToString("N0", Es)} palabras",
            chars == 1 ? "1 carácter" : $"{chars.ToString("N0", Es)} caracteres",
        };
        if (lines > 1)
            parts.Add($"{lines.ToString("N0", Es)} líneas");
        return string.Join(" · ", parts);
    }

    private static int WordCount(string text) => WordRegex().Count(text);

    // ---------- Convertir ----------

    /// <summary>Cada palabra con mayúscula inicial. ToTitleCase deja las siglas en mayúscula, por eso se baja todo antes.</summary>
    public static string TitleCase(string text) => Es.TextInfo.ToTitleCase(text.ToLower(Es));

    /// <summary>Saca tildes y diéresis pero conserva la ñ, que es otra letra.</summary>
    public static string RemoveAccents(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is 'ñ' or 'Ñ')
            {
                sb.Append(c);
                continue;
            }
            foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark)
                    sb.Append(d);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Texto copiado de un PDF: las líneas cortadas se unen en párrafos (y "pala-\nbra" vuelve a ser "palabra").
    /// Las líneas en blanco separan párrafos y se respetan.
    /// </summary>
    public static string JoinLines(string text)
    {
        var paragraphs = ParagraphBreak().Split(text.Replace("\r\n", "\n").Trim('\n'));
        return string.Join("\r\n\r\n", paragraphs.Select(p =>
        {
            var joined = HyphenBreak().Replace(p.Trim(), "$1$2");
            return SpacesAroundBreak().Replace(joined, " ");
        }));
    }

    /// <summary>Espacios de más, tabulaciones, espacios al final de las líneas y más de una línea en blanco seguida.</summary>
    public static string CleanSpaces(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => ManySpaces().Replace(l, " ").Trim());
        var joined = string.Join("\n", lines);
        return ManyBlankLines().Replace(joined, "\n\n").Trim('\n').Replace("\n", "\r\n");
    }

    /// <summary>Null si no es JSON.</summary>
    public static string? FormatJson(string text)
    {
        var t = text.Trim();
        if (t.Length < 2 || !(t[0] is '{' or '[') || t.Length > 5_000_000)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(t);
            var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            return pretty == t ? null : pretty.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>#7CB342 → rgb(124, 179, 66), y al revés. Null si no es un color.</summary>
    public static string? ConvertColor(string text)
    {
        var t = text.Trim();
        // Sin "#" solo cuenta si son 6 y hay algún número: "decade" o "facade" también son hexadecimal.
        if (HexColor().Match(t) is { Success: true } hex && (t[0] == '#' || hex.Groups[1].Length == 6 && t.Any(char.IsDigit)))
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3)
                h = string.Concat(h.Select(c => $"{c}{c}"));
            var v = Convert.ToInt32(h, 16);
            return $"rgb({v >> 16 & 0xFF}, {v >> 8 & 0xFF}, {v & 0xFF})";
        }
        if (RgbColor().Match(t) is { Success: true } rgb)
        {
            var parts = new[] { rgb.Groups[1], rgb.Groups[2], rgb.Groups[3] }.Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture)).ToArray();
            return parts.All(p => p <= 255) ? $"#{parts[0]:X2}{parts[1]:X2}{parts[2]:X2}" : null;
        }
        return null;
    }

    // ---------- Herramientas ----------

    public static string SortLines(string text) =>
        string.Join("\r\n", Lines(text).Order(StringComparer.Create(Es, CompareOptions.IgnoreCase)));

    /// <summary>Saca las líneas repetidas (sin importar mayúsculas ni espacios de los bordes) y deja la primera.</summary>
    public static string Dedupe(string text)
    {
        var seen = new HashSet<string>(StringComparer.Create(Es, CompareOptions.IgnoreCase));
        return string.Join("\r\n", Lines(text).Where(l => seen.Add(l.Trim())));
    }

    /// <summary>
    /// "15*3+2" → "47"; "10 / 4" → "2,5". Solo números, + − × ÷ %, paréntesis y coma o punto decimal.
    /// Null si no es una cuenta (tiene que haber al menos una operación).
    /// </summary>
    public static string? Calculate(string text)
    {
        var t = text.Trim().TrimEnd('=').Trim()
            .Replace('×', '*').Replace('x', '*').Replace('X', '*').Replace('÷', '/').Replace('−', '-');
        if (t.Length is 0 or > 200 || !MathExpression().IsMatch(t) || !OperatorBetweenNumbers().IsMatch(t) || LooksLikeDate().IsMatch(t))
            return null;
        // En castellano la coma es el decimal: "2,5". Si además hay puntos, son de miles ("1.000,5").
        t = t.Contains(',') ? t.Replace(".", "").Replace(',', '.') : t;
        // DataTable entiende "a % b" como resto; "50%" suelto no tiene sentido para él.
        if (TrailingPercent().IsMatch(t))
            return null;
        try
        {
            var value = Convert.ToDouble(new System.Data.DataTable().Compute(t, null), CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;
            return Math.Round(value, 10).ToString("#,0.##########", Es);
        }
        catch (Exception e) when (e is System.Data.EvaluateException or System.Data.SyntaxErrorException or InvalidCastException or OverflowException or DivideByZeroException)
        {
            return null;
        }
    }

    public static byte[] QrPng(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(12);
    }

    /// <summary>Texto en A4 con Segoe UI 11, márgenes de 2 cm y saltos de página; respeta los párrafos.</summary>
    public static void TextToPdf(string text, string dst)
    {
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
        const double margin = 56.7; // 2 cm
        var font = new XFont("Segoe UI", 11);
        var lineHeight = 11 * 1.45;
        using var doc = new PdfDocument();
        PdfPage? page = null;
        XGraphics? g = null;
        var y = 0.0;
        double width = 0, bottom = 0;

        void NewPage()
        {
            g?.Dispose();
            page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            g = XGraphics.FromPdfPage(page);
            width = page.Width.Point - margin * 2;
            bottom = page.Height.Point - margin;
            y = margin;
        }

        NewPage();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            foreach (var row in Wrap(line, w => g!.MeasureString(w, font).Width, width))
            {
                if (y + lineHeight > bottom)
                    NewPage();
                g!.DrawString(row, font, XBrushes.Black, new XPoint(margin, y + 11));
                y += lineHeight;
            }
        }
        g?.Dispose();
        doc.Save(dst);
    }

    /// <summary>
    /// PDFsharp no sabe buscar fuentes por su cuenta: le damos el archivo de Segoe UI de Windows
    /// (y Arial si no está). Las fuentes quedan incrustadas en el PDF.
    /// </summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? "bold" : "regular");

        public byte[]? GetFont(string faceName)
        {
            var (main, fallback) = faceName == "bold" ? ("segoeuib.ttf", "arialbd.ttf") : ("segoeui.ttf", "arial.ttf");
            var path = Path.Combine(Fonts, main);
            return File.ReadAllBytes(File.Exists(path) ? path : Path.Combine(Fonts, fallback));
        }
    }

    /// <summary>Corta una línea en renglones que entran en <paramref name="width"/>; una palabra larguísima se corta por letras.</summary>
    public static IEnumerable<string> Wrap(string line, Func<string, double> measure, double width)
    {
        if (line.Length == 0)
        {
            yield return "";
            yield break;
        }
        var current = "";
        foreach (var word in line.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (measure(candidate) <= width)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
                yield return current;
            current = word;
            while (current.Length > 1 && measure(current) > width)
            {
                var cut = current.Length - 1;
                while (cut > 1 && measure(current[..cut]) > width)
                    cut--;
                yield return current[..cut];
                current = current[cut..];
            }
        }
        yield return current;
    }

    // ---------- Acciones ----------

    /// <summary>El link, si el texto es solo eso (con o sin http).</summary>
    public static string? Url(string text)
    {
        var t = text.Trim();
        if (t.Length > 2000 || t.Contains(' ') || t.Contains('\n'))
            return null;
        if (!t.Contains("://"))
        {
            // Sin "https://" solo si empieza con www. o termina en un dominio conocido: "archivo.txt" no es un link.
            if (WebAddress().Match(t) is not { Success: true } m || !(m.Groups[1].Success || CommonDomains.Contains(m.Groups[3].Value.ToLowerInvariant())))
                return null;
            t = "https://" + t;
        }
        return Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.') ? uri.AbsoluteUri : null;
    }

    public static string? Email(string text)
    {
        var t = text.Trim().TrimStart('<').TrimEnd('>');
        return EmailRegex().IsMatch(t) ? t : null;
    }

    /// <summary>Una ruta escrita que existe: una carpeta se abre; un archivo, la carpeta que lo tiene, con él marcado.</summary>
    public static string? ExistingPath(string text)
    {
        var t = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        if (t.Length is < 3 or > 400 || t.Contains('\n') || !(PathStart().IsMatch(t)))
            return null;
        try
        {
            return Directory.Exists(t) || File.Exists(t) ? Path.GetFullPath(t) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // ---------- Ayudas ----------

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToArray();

    /// <summary>Todo en una línea, con un espacio entre palabras.</summary>
    private static string Squash(string text) => ManyWhitespace().Replace(text, " ").Trim();

    /// <summary>
    /// Guarda en el Escritorio con un nombre sacado de las primeras palabras ("QR - Hola mundo.png"), sin pisar nada.
    /// </summary>
    private static string SaveToDesktop(string text, string? prefix, string ext, Action<string> write)
    {
        var name = FileNameFrom(text);
        if (prefix is not null)
            name = $"{prefix} - {name}";
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dst = OutputPaths.For(Path.Combine(desktop, name + ext), ext);
        return SafeWrite.WriteAsync(dst, tmp => { write(tmp); return Task.CompletedTask; }, default).GetAwaiter().GetResult();
    }

    /// <summary>Las primeras palabras, sin los caracteres que Windows no acepta en un nombre.</summary>
    public static string FileNameFrom(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(Squash(text).Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        clean = ManySpaces().Replace(clean, " ").Trim().TrimEnd('.');
        if (clean.Length > 40)
        {
            var cut = clean.LastIndexOf(' ', 40);
            clean = clean[..(cut > 15 ? cut : 40)].TrimEnd('.', ' ');
        }
        return clean.Length == 0 ? "texto" : clean;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(\p{Ll})-[ \t]*\n[ \t]*(\p{Ll})")]
    private static partial Regex HyphenBreak();

    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex SpacesAroundBreak();

    [GeneratedRegex(@"[ \t ]{2,}|\t")]
    private static partial Regex ManySpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex ManyWhitespace();

    [GeneratedRegex(@"^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*[\d.]+\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbColor();

    [GeneratedRegex(@"^[\d\s+\-*/().,%]+$")]
    private static partial Regex MathExpression();

    [GeneratedRegex(@"[\d)]\s*[+\-*/%]\s*[\d(.,]")]
    private static partial Regex OperatorBetweenNumbers();

    /// <summary>"2026-10-26" o "26/10/2026" son fechas, no restas ni divisiones.</summary>
    [GeneratedRegex(@"^\s*(\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\s*$")]
    private static partial Regex LooksLikeDate();

    [GeneratedRegex(@"%\s*($|\))")]
    private static partial Regex TrailingPercent();

    private static readonly HashSet<string> CommonDomains =
        ["com", "ar", "org", "net", "edu", "gov", "io", "dev", "app", "ai", "co", "uy", "cl", "es", "mx", "me", "info", "tv", "gg", "xyz", "site", "online"];

    [GeneratedRegex(@"^(www\.)?[a-z0-9\-]+(\.[a-z0-9\-]+)*\.([a-z]{2,})(/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();

    [GeneratedRegex(@"^[^@\s<>()]+@[^@\s<>()]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^([a-zA-Z]:\\|\\\\|%)")]
    private static partial Regex PathStart();
}
