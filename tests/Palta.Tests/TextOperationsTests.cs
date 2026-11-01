using Palta.Core;
using Palta.Core.Operations;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Palta.Tests;

public class TextOperationsTests
{
    private static string[] Ids(Catalog catalog, WheelKind wheel, string text) => catalog.For(wheel, [text]).Concat(catalog.ForText(wheel, text)).Select(o => o.Id).ToArray();

    [Fact]
    public void TextOps_NeverMixWithFileOps()
    {
        var catalog = Catalog.CreateDefault(new FakeHost());
        // "Copiar ruta" acepta cualquier cosa: con un texto no tiene que aparecer.
        Assert.DoesNotContain("copy-path", catalog.ForText(WheelKind.Actions, "hola").Select(o => o.Id));
        Assert.All(catalog.For(WheelKind.Convert, [@"C:\x\a.png"]), o => Assert.False(o.ForText));
        Assert.All(catalog.ForText(WheelKind.Convert, "hola"), o => Assert.True(o.ForText));
        Assert.Empty(catalog.ForText(WheelKind.Tools, "   "));
    }

    [Fact]
    public void Wheels_FitTheWheel()
    {
        // Lo más que puede mostrar una rueda: un texto que dispara todo lo contextual a la vez no existe,
        // pero ninguna rueda con un texto común se pasa de 9 (los atajos 1–9).
        var catalog = Catalog.CreateDefault(new FakeHost());
        foreach (var text in new[] { "hola mundo", "Línea uno\nlínea dos\nlínea uno", "https://example.com", "15*3+2" })
            foreach (var wheel in Enum.GetValues<WheelKind>())
                Assert.InRange(catalog.ForText(wheel, text).Count, 1, 9);
    }

    [Fact]
    public void Contextual_OnlyWhenItFits()
    {
        var catalog = Catalog.CreateDefault(new FakeHost());
        Assert.Contains("text-open-link", Ids(catalog, WheelKind.Actions, "www.uns.edu.ar"));
        Assert.DoesNotContain("text-open-link", Ids(catalog, WheelKind.Actions, "hola mundo"));
        Assert.Contains("text-mail", Ids(catalog, WheelKind.Actions, "alguien@gmail.com"));
        Assert.Contains("text-calc", Ids(catalog, WheelKind.Tools, "15*3+2"));
        Assert.DoesNotContain("text-calc", Ids(catalog, WheelKind.Tools, "26/10/2026"));
        Assert.Contains("text-color", Ids(catalog, WheelKind.Convert, "#7CB342"));
        Assert.Contains("text-json", Ids(catalog, WheelKind.Convert, """{"a":1}"""));
        Assert.DoesNotContain("text-join-lines", Ids(catalog, WheelKind.Convert, "una sola línea"));
        Assert.DoesNotContain("text-no-accents", Ids(catalog, WheelKind.Convert, "sin acentos"));
    }

    [Theory]
    [InlineData("hola MUNDO cruel", "Hola Mundo Cruel")]
    [InlineData("ÁRBOL de navidad", "Árbol De Navidad")]
    public void TitleCase(string input, string expected) => Assert.Equal(expected, TextOperations.TitleCase(input));

    [Fact]
    public void RemoveAccents_Simple() => Assert.Equal("cancion pinguino año", TextOperations.RemoveAccents("canción pingüino año"));

    [Fact]
    public void JoinLines_FromPdf()
    {
        var pdf = "Esta es una línea cor-\ntada y otra que sigue\nen la próxima.\n\nOtro párrafo\nde dos líneas.";
        Assert.Equal("Esta es una línea cortada y otra que sigue en la próxima.\r\n\r\nOtro párrafo de dos líneas.", TextOperations.JoinLines(pdf));
    }

    [Fact]
    public void CleanSpaces() =>
        Assert.Equal("uno dos\r\n\r\ntres", TextOperations.CleanSpaces("  uno    dos  \n\n\n\n\ttres  "));

    [Fact]
    public void FormatJson()
    {
        Assert.Equal("{\r\n  \"nombre\": \"Ñandú\",\r\n  \"n\": [\r\n    1,\r\n    2\r\n  ]\r\n}", TextOperations.FormatJson("""{"nombre":"Ñandú","n":[1,2]}"""));
        Assert.Null(TextOperations.FormatJson("{no es json"));
        Assert.Null(TextOperations.FormatJson("hola"));
    }

    [Theory]
    [InlineData("#7CB342", "rgb(124, 179, 66)")]
    [InlineData("#fff", "rgb(255, 255, 255)")]
    [InlineData("7cb342", "rgb(124, 179, 66)")]
    [InlineData("rgb(124, 179, 66)", "#7CB342")]
    [InlineData("rgba(0,0,0,0.5)", "#000000")]
    public void ConvertColor(string input, string expected) => Assert.Equal(expected, TextOperations.ConvertColor(input));

    [Theory]
    [InlineData("rgb(300, 0, 0)")]
    [InlineData("#12345")]
    [InlineData("fff")]
    [InlineData("decade")] // palabras que también son hexadecimal
    public void ConvertColor_NotAColor(string input) => Assert.Null(TextOperations.ConvertColor(input));

    [Theory]
    [InlineData("15*3+2", "47")]
    [InlineData("10 / 4", "2,5")]
    [InlineData("2,5 x 2", "5")]
    [InlineData("1.000,5 + 1", "1.001,5")]
    [InlineData("(1+2)*3 =", "9")]
    [InlineData("7 % 3", "1")]
    [InlineData("1/3", "0,3333333333")]
    public void Calculate(string input, string expected) => Assert.Equal(expected, TextOperations.Calculate(input));

    [Theory]
    [InlineData("47")]
    [InlineData("-5")]
    [InlineData("hola + chau")]
    [InlineData("2026-10-26")]
    [InlineData("26/10/2026")]
    [InlineData("1/0")]
    [InlineData("50%")]
    [InlineData("((1+2)")]
    public void Calculate_NotACalculation(string input) => Assert.Null(TextOperations.Calculate(input));

    [Fact]
    public void SortAndDedupe()
    {
        Assert.Equal("árbol\r\nBanana\r\ncasa", TextOperations.SortLines("casa\nBanana\n\nárbol"));
        Assert.Equal("uno\r\ndos", TextOperations.Dedupe("uno\ndos\nUNO \ndos"));
    }

    [Fact]
    public void Count() =>
        Assert.Equal("6 palabras · 32 caracteres · 2 líneas", TextOperations.Count("Hola, ¿cómo andás?\nMuy bien, che"));

    [Theory]
    [InlineData("https://www.uns.edu.ar/carreras", "https://www.uns.edu.ar/carreras")]
    [InlineData("uns.edu.ar", "https://uns.edu.ar/")]
    [InlineData("  www.google.com  ", "https://www.google.com/")]
    public void Url(string input, string expected) => Assert.Equal(expected, TextOperations.Url(input));

    [Theory]
    [InlineData("hola mundo")]
    [InlineData("archivo.txt")]
    [InlineData("ftp://x.com")]
    [InlineData("Hola. Chau")]
    public void Url_NotALink(string input) => Assert.Null(TextOperations.Url(input));

    [Fact]
    public void Email()
    {
        Assert.Equal("nico@uns.edu.ar", TextOperations.Email(" <nico@uns.edu.ar> "));
        Assert.Null(TextOperations.Email("nico arroba uns"));
    }

    [Fact]
    public void ExistingPath()
    {
        using var d = new TempDir();
        var file = d.File("a.txt");
        Assert.Equal(d.Path, TextOperations.ExistingPath($"\"{d.Path}\""));
        Assert.Equal(file, TextOperations.ExistingPath(file));
        Assert.Null(TextOperations.ExistingPath(@"C:\no\existe\nada"));
        Assert.Null(TextOperations.ExistingPath("hola"));
    }

    [Fact]
    public void Snippet_AndSummary()
    {
        Assert.Equal("«Hola mundo»", TextOperations.Snippet("  Hola\n  mundo "));
        Assert.Equal("«Una frase bastante larga que…»", TextOperations.Snippet("Una frase bastante larga que no entra entera", 30));
        Assert.Equal("«Hola mundo» · 2 palabras", TextOperations.Summary("Hola mundo"));
    }

    [Theory]
    [InlineData("Hola: ¿qué tal?", "Hola ¿qué tal")]
    [InlineData("   ", "texto")]
    [InlineData("Una frase muy larga que se pasa de los cuarenta caracteres seguro", "Una frase muy larga que se pasa de los")]
    public void FileNameFrom(string input, string expected) => Assert.Equal(expected, TextOperations.FileNameFrom(input));

    [Fact]
    public void Wrap_SplitsByWidth()
    {
        // Cada letra mide 1: renglones de hasta 10.
        var rows = TextOperations.Wrap("uno dos tres cuatro cincuentaycinco", s => s.Length, 10).ToList();
        Assert.Equal(["uno dos", "tres", "cuatro", "cincuentay", "cinco"], rows);
    }

    [Fact]
    public async Task Transform_CopiesResult_AndNotes()
    {
        var host = new FakeHost();
        var op = Catalog.CreateDefault(host).ById("text-upper")!;
        var notes = new NoteSink();
        Assert.Empty(await op.RunAsync(["hola che"], notes, default));
        Assert.Equal("HOLA CHE", host.Clipboard);
        Assert.Equal("«HOLA CHE»", notes.Last);
    }

    [Fact]
    public async Task Calc_CopiesResult()
    {
        var host = new FakeHost();
        var notes = new NoteSink();
        await Catalog.CreateDefault(host).ById("text-calc")!.RunAsync(["15*3+2"], notes, default);
        Assert.Equal("47", host.Clipboard);
        Assert.Equal("15*3+2 = 47", notes.Last);
    }

    [Fact]
    public async Task Actions_Launch()
    {
        var host = new FakeHost();
        var catalog = Catalog.CreateDefault(host);
        await catalog.ById("text-open-link")!.RunAsync(["uns.edu.ar"], new Progress<double>(), default);
        await catalog.ById("text-search")!.RunAsync(["sistemas operativos"], new Progress<double>(), default);
        await catalog.ById("text-speak")!.RunAsync(["hola"], new Progress<double>(), default);
        Assert.Equal(["https://uns.edu.ar/", "https://www.google.com/search?q=sistemas%20operativos"], host.Launched);
        Assert.Equal("hola", host.Spoken);
    }

    [Fact]
    public void QrPng_IsAPng()
    {
        var png = TextOperations.QrPng("https://github.com/nicosagasti/palta");
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Fact]
    public void TextToPdf_PaginatesLongText()
    {
        using var d = new TempDir();
        var path = Path.Combine(d.Path, "texto.pdf");
        var text = string.Join("\n", Enumerable.Range(1, 120).Select(i => $"Renglón {i}: año, acción, pingüino."));
        TextOperations.TextToPdf(text, path);
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 2, 4);
    }

    private sealed class NoteSink : IProgress<double>, IJobNotes
    {
        public string? Last;
        public void Report(double value) { }
        public void Note(string text) => Last = text;
    }
}
