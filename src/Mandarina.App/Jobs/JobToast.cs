using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Mandarina.App.Native;
using Mandarina.App.Theme;
using Mandarina.App.Wheel;
using Mandarina.Core;
using Path = System.Windows.Shapes.Path;

namespace Mandarina.App.Jobs;

/// <summary>
/// Aviso de un trabajo: una cápsula del mismo vidrio que la rueda. A la izquierda, un anillo de
/// progreso que al terminar se vuelve la miniatura del resultado (o ✓ / ✕); los botones aparecen
/// solo al pasar el mouse. Un clic en la cápsula abre el resultado.
/// </summary>
internal sealed class JobToast : Window
{
    public const double CardWidth = 340;
    public const double CardHeight = 60;
    public const double ShadowMargin = 18;
    private const double Radius = CardHeight / 2;
    private const double RingSize = 36;
    private const double RingStroke = 2.5;

    private readonly Job _job;
    private readonly Palette _palette = Palette.Current();
    private bool Dark => _palette.Dark;
    private readonly TextBlock _title;
    private readonly TextBlock _status;
    private readonly Image _glass = new() { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Performance } };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
    private readonly Grid _indicator = new() { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
    private readonly Path _ring = new() { StrokeThickness = RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly RotateTransform _spin = new(0, RingSize / 2, RingSize / 2);
    private readonly DispatcherTimer _autoClose;
    private readonly TranslateTransform _slide = new();
    private double _shownProgress;
    private bool _spinning;
    private bool _closing;

    public event Action<JobToast>? Gone;

    public bool Finished => _job.State is JobState.Done or JobState.Failed or JobState.Canceled;

    public JobToast(Job job)
    {
        _job = job;
        Width = CardWidth + ShadowMargin * 2;
        Height = CardHeight + ShadowMargin * 2;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        FontFamily = Palette.UiFont;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var dark = Dark;
        var card = new Grid { Margin = new Thickness(ShadowMargin), RenderTransform = _slide, Cursor = Cursors.Hand };
        _glass.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), Radius, Radius);
        card.Children.Add(_glass);
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = new SolidColorBrush(Glass.Tint(dark)) });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = Glass.Specular(dark) });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius + 0.5), Margin = new Thickness(-0.75), BorderThickness = new Thickness(0.75), BorderBrush = Glass.Outline(dark) });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1), BorderBrush = Glass.Rim(dark) });

        // Anillo: pista tenue + arco de progreso.
        _indicator.Children.Add(new Ellipse { Stroke = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0, 0, 0)), StrokeThickness = RingStroke });
        _ring.Stroke = new SolidColorBrush(RingColor());
        _ring.RenderTransform = _spin;
        _indicator.Children.Add(_ring);

        _title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Glass.Text(dark)), TextTrimming = TextTrimming.CharacterEllipsis };
        _status = new TextBlock { FontSize = 11.5, Foreground = new SolidColorBrush(Glass.Secondary(dark)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        info.Children.Add(_title);
        info.Children.Add(_status);

        var content = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
        DockPanel.SetDock(_indicator, Dock.Left);
        DockPanel.SetDock(_buttons, Dock.Right);
        content.Children.Add(_indicator);
        content.Children.Add(_buttons);
        content.Children.Add(info);
        card.Children.Add(content);
        card.MouseLeftButtonUp += (_, e) => { if (!e.Handled) OpenResult(); };

        var outer = new Grid();
        outer.Children.Add(Glass.RectShadow(Radius, ShadowMargin, dark));
        outer.Children.Add(card);
        Content = outer;

        _autoClose = new DispatcherTimer(TimeSpan.FromSeconds(4.5), DispatcherPriority.Normal, (_, _) => FadeOut(), Dispatcher) { IsEnabled = false };
        MouseEnter += (_, _) =>
        {
            _autoClose.Stop();
            _buttons.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        MouseLeave += (_, _) =>
        {
            if (_job.State != JobState.Failed)
                _buttons.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            if (_job.State == JobState.Done)
                _autoClose.Start();
        };

        SourceInitialized += (_, _) => Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);

        job.Changed += j => Dispatcher.BeginInvoke(() => Refresh(j));
        Refresh(job);
    }

    private Color Success => Dark ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x28, 0xA7, 0x45);

    private Color RingColor() => Dark ? Lighter(_palette.Accent, 0.6) : _palette.Accent;

    private static string FileLine(Job job)
    {
        var first = System.IO.Path.GetFileName(job.Files[0].TrimEnd('\\'));
        return job.Files.Count > 1 ? $"{first} y {job.Files.Count - 1} más" : first;
    }

    /// <summary>"JPG" → "Convirtiendo a JPG"; las herramientas y acciones se muestran tal cual.</summary>
    private static string Doing(IOperation op) => op.Wheel == WheelKind.Convert ? $"Convirtiendo a {op.Label}" : op.Label;

    private static string DoneTitle(IOperation op) => op.Wheel == WheelKind.Convert ? $"Convertido a {op.Label}" : $"{op.Label}: listo";

    private void Refresh(Job job)
    {
        switch (job.State)
        {
            case JobState.Queued:
                _title.Text = Doing(job.Operation);
                _status.Text = $"En espera · {FileLine(job)}";
                Spin(true);
                SetButtons(("", "Cancelar", () => _job.Cancel()));
                break;
            case JobState.Running:
                _title.Text = Doing(job.Operation);
                _status.Text = FileLine(job);
                if (job.Progress > 0)
                {
                    Spin(false);
                    SetProgress(job.Progress);
                }
                else
                    Spin(true);
                SetButtons(("", "Cancelar", () => _job.Cancel()));
                break;
            case JobState.Done:
                Spin(false);
                _title.Text = DoneTitle(job.Operation);
                _status.Text = job.Outputs.Count switch
                {
                    0 => job.Operation.Id == "copy-path" ? "Ruta copiada al portapapeles" : FileLine(job),
                    1 => System.IO.Path.GetFileName(job.Outputs[0]),
                    var n => $"{n} archivos creados",
                };
                ShowResult(job);
                if (job.Outputs.Count > 0)
                    SetButtons(("", "Mostrar en carpeta", () => Reveal(job.Outputs[0])), ("", "Abrir", OpenResult));
                else
                    SetButtons();
                if (!IsMouseOver)
                    _autoClose.Start();
                break;
            case JobState.Failed:
                Spin(false);
                _title.Text = $"No se pudo: {Doing(job.Operation).Replace("Convirtiendo", "convertir")}";
                _status.Text = job.Error ?? "Error desconocido";
                _status.ToolTip = job.Error;
                Symbol("", _palette.Danger);
                SetButtons(("", "Cerrar", FadeOut));
                _buttons.Opacity = 1;
                break;
            case JobState.Canceled:
                Spin(false);
                _title.Text = "Cancelado";
                _status.Text = FileLine(job);
                Symbol("", Glass.Secondary(Dark));
                SetButtons();
                _autoClose.Interval = TimeSpan.FromSeconds(1.5);
                _autoClose.Start();
                break;
        }
    }

    // ---------- Anillo ----------

    private void SetProgress(double p)
    {
        _shownProgress = Math.Clamp(p, 0.02, 0.999);
        _ring.Data = Arc(_shownProgress);
    }

    /// <summary>Arco del anillo desde arriba, en sentido horario, que cubre <paramref name="fraction"/> de la vuelta.</summary>
    private static Geometry Arc(double fraction)
    {
        var r = (RingSize - RingStroke) / 2;
        var c = RingSize / 2;
        var angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry([figure]);
        g.Freeze();
        return g;
    }

    /// <summary>Sin porcentaje conocido: un arco corto que gira. Se detiene en cuanto hay progreso o termina.</summary>
    private void Spin(bool on)
    {
        if (on == _spinning)
            return;
        _spinning = on;
        if (on)
        {
            _ring.Data = Arc(0.28);
            _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            _spin.Angle = 0;
        }
    }

    /// <summary>Al terminar: miniatura redonda si el resultado es una imagen; si no, un ✓ dentro del anillo lleno.</summary>
    private void ShowResult(Job job)
    {
        var image = job.Outputs.FirstOrDefault(o => Formats.KindOf(o) == FileKind.Image && System.IO.File.Exists(o));
        if (image is not null && Thumb(image) is { } thumb)
        {
            _indicator.Children.Clear();
            var circle = new Ellipse
            {
                Fill = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill },
                Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.6, 0.6),
                Opacity = 0,
            };
            _indicator.Children.Add(circle);
            Pop(circle);
            return;
        }
        Symbol("", Success); // Éxito en verde (el de macOS): con acentos grises de Windows el ✓ se perdía.
    }

    private void Symbol(string glyph, Color color)
    {
        _indicator.Children.Clear();
        _indicator.Children.Add(new Ellipse { Stroke = new SolidColorBrush(color), StrokeThickness = RingStroke });
        var mark = new TextBlock
        {
            FontFamily = Palette.IconFont,
            Text = glyph,
            FontSize = 14,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.6, 0.6),
            Opacity = 0,
        };
        _indicator.Children.Add(mark);
        Pop(mark);
    }

    private static void Pop(UIElement element)
    {
        var s = (ScaleTransform)element.RenderTransform;
        var spring = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = spring };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
    }

    private static BitmapImage? Thumb(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 96;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // WebP/AVIF/HEIC sin códec de Windows: queda el ✓.
            return null;
        }
    }

    // ---------- Botones redondos ----------

    private void SetButtons(params (string Glyph, string Tip, Action OnClick)[] buttons)
    {
        _buttons.Children.Clear();
        foreach (var (glyph, tip, onClick) in buttons)
            _buttons.Children.Add(RoundButton(glyph, tip, onClick));
    }

    private Border RoundButton(string glyph, string tip, Action onClick)
    {
        var dark = Dark;
        var idle = new SolidColorBrush(Glass.Chip(dark));
        var hover = new SolidColorBrush(dark ? Color.FromArgb(0x4A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var button = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(6, 0, 0, 0),
            Background = idle,
            BorderThickness = new Thickness(0.75),
            BorderBrush = Glass.Outline(dark),
            ToolTip = tip,
            Child = new TextBlock
            {
                FontFamily = Palette.IconFont,
                Text = glyph,
                FontSize = 11,
                Foreground = new SolidColorBrush(Glass.Text(dark)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = idle;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    private static Color Lighter(Color c, double amount) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    private void OpenResult()
    {
        if (_job.State != JobState.Done || _job.Outputs.Count == 0)
            return;
        var target = _job.Outputs.Count == 1 ? _job.Outputs[0] : System.IO.Path.GetDirectoryName(_job.Outputs[0])!;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Core.Log.Error($"Abrir {target}", e); }
        FadeOut();
    }

    private static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception e) { Core.Log.Error($"Mostrar {path}", e); }
    }

    /// <summary>Aparece deslizándose desde la derecha, como en macOS.</summary>
    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        // El vidrio toma lo que hay detrás una vez, en píxeles físicos.
        var scale = Win32.ScaleAt((int)left, (int)top);
        var (x, y) = ((int)((left + ShadowMargin) * scale), (int)((top + ShadowMargin) * scale));
        _glass.Source = GlassCapture.Capture(x, y, (int)(CardWidth * scale), (int)(CardHeight * scale)) is { } shot ? Glass.Vibrant(shot, Dark) : null;
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(36, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut } });
    }

    public void MoveTo(double top) =>
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    public void FadeOut()
    {
        if (_closing)
            return;
        _closing = true;
        _autoClose.Stop();
        Spin(false);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            _glass.Source = null;
            Close();
            Gone?.Invoke(this);
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24, TimeSpan.FromMilliseconds(180)));
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Para --snapshot: muestra los botones como si estuviera el mouse encima.</summary>
    public void RevealButtons() => _buttons.Opacity = 1;

    /// <summary>Para --snapshot.</summary>
    public void SaveSnapshot(string path, BitmapSource? under)
    {
        var bitmap = new RenderTargetBitmap((int)(Width * 2), (int)(Height * 2), 192, 192, PixelFormats.Pbgra32);
        if (under is not null)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(under, new Rect(0, 0, Width, Height));
            bitmap.Render(visual);
        }
        bitmap.Render((Visual)Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }
}

/// <summary>Apila las cápsulas abajo a la derecha del área de trabajo.</summary>
internal sealed class ToastStack
{
    private const double Gap = 8;
    private const double Edge = 14;
    private readonly List<JobToast> _toasts = [];

    public JobToast Add(Job job)
    {
        var toast = new JobToast(job);
        toast.Gone += t =>
        {
            _toasts.Remove(t);
            Relayout();
        };
        _toasts.Insert(0, toast);
        var area = SystemParameters.WorkArea;
        toast.ShowAt(area.Right - JobToast.CardWidth - Edge - JobToast.ShadowMargin, TopFor(0));
        Relayout();
        // Más de 5 visibles es ruido: las más viejas terminadas se van.
        foreach (var old in _toasts.Skip(5).Where(t => t.Finished).ToList())
            old.FadeOut();
        return toast;
    }

    private static double TopFor(int index) =>
        SystemParameters.WorkArea.Bottom - Edge - JobToast.ShadowMargin - JobToast.CardHeight - (JobToast.CardHeight + Gap) * index;

    private void Relayout()
    {
        for (var i = 0; i < _toasts.Count; i++)
            _toasts[i].MoveTo(TopFor(i));
    }
}
