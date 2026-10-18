using ImageMagick;

namespace Mandarina.Core.Backends;

/// <summary>Imágenes con Magick.NET: lee y escribe todos los formatos (HEIC, WebP y AVIF incluidos).</summary>
public static class ImageBackend
{
    public const uint CompressQuality = 75;

    /// <summary>
    /// Lee <paramref name="src"/>, le aplica <paramref name="transform"/> y lo guarda en <paramref name="dst"/>
    /// con el formato que indica su extensión. El original no se toca.
    /// </summary>
    public static Task<string> SaveAsync(string src, string dst, CancellationToken ct, Action<MagickImage>? transform = null, uint? quality = null, bool optimizePng = false) =>
        SafeWrite.WriteAsync(dst, tmp => Task.Run(() =>
        {
            using var img = Load(src);
            transform?.Invoke(img);
            ct.ThrowIfCancellationRequested();
            var format = FormatFor(Formats.Ext(dst));
            Prepare(img, format);
            if (quality is { } q)
                img.Quality = q;
            img.Write(tmp, format);
            if (optimizePng && format == MagickFormat.Png)
                new ImageOptimizer().LosslessCompress(tmp);
        }, ct), ct);

    /// <summary>Carga la imagen (primer cuadro si es animada) con la orientación EXIF ya aplicada.</summary>
    public static MagickImage Load(string path)
    {
        var img = new MagickImage(path);
        img.AutoOrient();
        return img;
    }

    /// <summary>Codifica la imagen para incrustarla en un PDF: JPEG sobre fondo blanco.</summary>
    public static byte[] ToJpegBytes(string path, out uint width, out uint height)
    {
        using var img = Load(path);
        Flatten(img);
        img.Quality = 90;
        width = img.Width;
        height = img.Height;
        return img.ToByteArray(MagickFormat.Jpeg);
    }

    public static MagickFormat FormatFor(string ext) => ext switch
    {
        ".jpg" => MagickFormat.Jpeg,
        ".png" => MagickFormat.Png,
        ".webp" => MagickFormat.WebP,
        ".avif" => MagickFormat.Avif,
        ".bmp" => MagickFormat.Bmp,
        ".tiff" => MagickFormat.Tiff,
        ".ico" => MagickFormat.Ico,
        ".gif" => MagickFormat.Gif,
        ".heic" or ".heif" => MagickFormat.Heic,
        _ => throw new ConversionException($"No sé guardar imágenes {ext}."),
    };

    private static void Prepare(MagickImage img, MagickFormat format)
    {
        if (format is MagickFormat.Jpeg or MagickFormat.Bmp)
            Flatten(img);
        if (format == MagickFormat.Ico)
            img.Resize(new MagickGeometry(256, 256) { Greater = true });
    }

    private static void Flatten(MagickImage img)
    {
        if (!img.HasAlpha)
            return;
        img.BackgroundColor = MagickColors.White;
        img.Alpha(AlphaOption.Remove);
        img.Alpha(AlphaOption.Off);
    }
}

/// <summary>Error con un mensaje pensado para mostrarle al usuario.</summary>
public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
