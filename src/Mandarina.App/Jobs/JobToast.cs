using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mandarina.App.Native;
using Mandarina.App.Theme;
using Mandarina.App.Wheel;
using Mandarina.Core;

namespace Mandarina.App.Jobs;

/// <summary>
/// Aviso de un trabajo al estilo de las notificaciones de macOS Tahoe: vidrio muy transparente,
/// esquinas grandes, encabezado chico (ícono · Mandarina · ahora), título y archivo, y la miniatura
/// del resultado a la derecha. Las acciones y la ✕ aparecen al pasar el mouse; un clic abre el resultado.
/// </summary>
internal sealed class JobToast : Window
{
    public const double CardWidth = 364;
    public const double CardHeight = 86;
    public const double ShadowMargin = 20;
    private const double Radius = 22;
    private const double Thumb = 46;

    private static readonly BitmapImage AppIcon = LoadIcon();

    private readonly Job _job;
    private readonly Palette _palette = Palette.Current();
    private bool Dark => _palette.Dark;
    private readonly TextBlock _title;
    private readonly TextBlock _body;
    private readonly TextBlock _time;
    private readonly Border _barTrack;
    private readonly Border _barFill;
    private readonly Border _attachment = new() { Width = Thumb, Height = Thumb, CornerRadius = new CornerRadius(11), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Image _glass = new() { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 22, RenderingBias = RenderingBias.Performance } };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
    private readonly Border _close;
    private readonly DispatcherTimer _autoClose;
    private readonly TranslateTransform _slide = new();
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
        // Vidrio "claro" de Tahoe: menos tinte que la rueda, el fondo se ve más.
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = new SolidColorBrush(dark ? Color.FromArgb(0x78, 0x1C, 0x1C, 0x21) : Color.FromArgb(0xA8, 0xF6, 0xF6, 0xF8)) });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = Glass.Specular(dark), Height = 44, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.7 });
        // Borde apenas insinuado: una luz finita, sin aro marcado.
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(0.75), BorderBrush = SoftEdge(dark) });

        var text = new SolidColorBrush(Glass.Text(dark));
        var secondary = new SolidColorBrush(Glass.Secondary(dark));
        var tertiary = new SolidColorBrush(Glass.Tertiary(dark));

        // Encabezado: ícono chico · Mandarina ............ ahora / acciones
        var header = new DockPanel { Height = 20 };
        var icon = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = new ImageBrush(AppIcon) { Stretch = Stretch.UniformToFill }, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        _time = new TextBlock { Text = "ahora", FontSize = 11, Foreground = tertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        _actions.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { _time, _actions } };
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(new TextBlock { Text = "Mandarina", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = secondary, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        _title = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
        _body = new TextBlock { FontSize = 12.5, Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        _barFill = new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Bright(_palette.Accent)), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _barTrack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 7, 0, 0),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0)),
            Child = _barFill,
            ClipToBounds = true,
            Visibility = Visibility.Collapsed,
        };

        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(header);
        column.Children.Add(_title);
        column.Children.Add(_body);
        column.Children.Add(_barTrack);

        var content = new DockPanel { Margin = new Thickness(16, 10, 14, 11) };
        DockPanel.SetDock(_attachment, Dock.Right);
        content.Children.Add(_attachment);
        content.Children.Add(column);
        card.Children.Add(content);
        card.MouseLeftButtonUp += (_, e) => { if (!e.Handled) OpenResult(); };

        // La ✕ de macOS: arriba a la izquierda, mitad afuera de la tarjeta, solo con el mouse encima.
        _close = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0xF0, 0x3A, 0x3A, 0x40) : Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.75),
            BorderBrush = Glass.Outline(dark),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(ShadowMargin - 7, ShadowMargin - 7, 0, 0),
            Opacity = 0,
            ToolTip = "Cerrar",
            Cursor = Cursors.Hand,
            Child = new TextBlock { FontFamily = Palette.IconFont, Text = "", FontSize = 8, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        _close.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!Finished) _job.Cancel(); FadeOut(); };

        var outer = new Grid();
        outer.Children.Add(Glass.RectShadow(Radius, ShadowMargin, dark));
        outer.Children.Add(card);
        outer.Children.Add(_close);
        Content = outer;

        _autoClose = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) => FadeOut(), Dispatcher) { IsEnabled = false };
        MouseEnter += (_, _) => { _autoClose.Stop(); Hover(true); };
        MouseLeave += (_, _) =>
        {
            Hover(false);
            if (_job.State is JobState.Done or JobState.Failed)
                _autoClose.Start();
        };

        SourceInitialized += (_, _) => Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);

        job.Changed += j => Dispatcher.BeginInvoke(() => Refresh(j));
        Refresh(job);
    }

    private static Brush SoftEdge(bool dark) => Glass.Frozen(new LinearGradientBrush(
        [
            new GradientStop(Color.FromArgb((byte)(dark ? 0x4C : 0xD0), 0xFF, 0xFF, 0xFF), 0),
            new GradientStop(Color.FromArgb((byte)(dark ? 0x14 : 0x60), 0xFF, 0xFF, 0xFF), 0.5),
            new GradientStop(Color.FromArgb((byte)(dark ? 0x22 : 0x80), 0xFF, 0xFF, 0xFF), 1),
        ], new Point(0.5, 0), new Point(0.5, 1)));

    /// <summary>Con el mouse encima: la hora se va, aparecen las acciones y la ✕.</summary>
    private void Hover(bool on)
    {
        var d = TimeSpan.FromMilliseconds(on ? 120 : 160);
        var hasActions = _actions.Children.Count > 0;
        _actions.BeginAnimation(OpacityProperty, new DoubleAnimation(on && hasActions ? 1 : 0, d));
        _time.BeginAnimation(OpacityProperty, new DoubleAnimation(on && hasActions ? 0 : 1, d));
        _close.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, d));
    }

    private static BitmapImage LoadIcon()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/mandarina.png");
        image.DecodePixelWidth = 48;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private Color Bright(Color c) => Dark ? Color.FromRgb((byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45)) : c;

    private static string FileLine(Job job)
    {
        var first = System.IO.Path.GetFileName(job.Files[0].TrimEnd('\\'));
        return job.Files.Count > 1 ? $"{first} y {job.Files.Count - 1} más" : first;
    }

    private static string Doing(IOperation op) => op.Wheel == WheelKind.Convert ? $"Convirtiendo a {op.Label}…" : $"{op.Label}…";

    private static string DoneTitle(IOperation op) => op.Wheel == WheelKind.Convert ? $"Convertido a {op.Label}" : op.Id switch
    {
        "copy-path" => "Ruta copiada",
        "copy-to" => "Copiado",
        "move-to" => "Movido",
        _ => $"{op.Label}: listo",
    };

    private void Refresh(Job job)
    {
        switch (job.State)
        {
            case JobState.Queued:
                _title.Text = Doing(job.Operation);
                _body.Text = $"En espera · {FileLine(job)}";
                SetActions(("Cancelar", () => _job.Cancel()));
                break;
            case JobState.Running:
                _title.Text = Doing(job.Operation);
                _body.Text = FileLine(job);
                _barTrack.Visibility = Visibility.Visible;
                SetProgress(Math.Max(job.Progress, 0.06));
                SetActions(("Cancelar", () => _job.Cancel()));
                break;
            case JobState.Done:
                _barTrack.Visibility = Visibility.Collapsed;
                _title.Text = DoneTitle(job.Operation);
                _body.Text = job.Outputs.Count switch
                {
                    0 => FileLine(job),
                    1 => System.IO.Path.GetFileName(job.Outputs[0]),
                    var n => $"{n} archivos · {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(job.Outputs[0]))}",
                };
                ShowAttachment(job);
                if (job.Outputs.Count > 0)
                    SetActions(("Mostrar en carpeta", () => Reveal(job.Outputs[0])), ("Abrir", OpenResult));
                else
                    SetActions();
                if (!IsMouseOver)
                    _autoClose.Start();
                break;
            case JobState.Failed:
                _barTrack.Visibility = Visibility.Collapsed;
                _title.Text = job.Operation.Wheel == WheelKind.Convert ? $"No se pudo convertir a {job.Operation.Label}" : $"No se pudo: {job.Operation.Label}";
                _body.Text = job.Error ?? "Error desconocido";
                _body.ToolTip = job.Error;
                // Como las notificaciones de Windows, también se va sola, pero da más tiempo para leerlo.
                _autoClose.Interval = TimeSpan.FromSeconds(8);
                if (!IsMouseOver)
                    _autoClose.Start();
                ShowSymbol("", _palette.Danger);
                SetActions();
                break;
            case JobState.Canceled:
                _barTrack.Visibility = Visibility.Collapsed;
                _title.Text = "Cancelado";
                _body.Text = FileLine(job);
                SetActions();
                _autoClose.Interval = TimeSpan.FromSeconds(1.5);
                _autoClose.Start();
                break;
        }
    }

    private void SetProgress(double p)
    {
        _barTrack.UpdateLayout();
        var full = _barTrack.ActualWidth > 0 ? _barTrack.ActualWidth : CardWidth - 30;
        _barFill.BeginAnimation(WidthProperty, new DoubleAnimation(full * Math.Clamp(p, 0, 1), TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>La miniatura del resultado, como el adjunto de una notificación; aparece con un pequeño rebote.</summary>
    private void ShowAttachment(Job job)
    {
        var image = job.Outputs.FirstOrDefault(o => Formats.KindOf(o) == FileKind.Image && System.IO.File.Exists(o));
        if (image is null || LoadThumb(image) is not { } thumb)
            return;
        _attachment.Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill };
        _attachment.BorderThickness = new Thickness(0.75);
        _attachment.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
        _attachment.Child = null;
        Pop(_attachment);
    }

    private void ShowSymbol(string glyph, Color color)
    {
        _attachment.Background = new SolidColorBrush(Palette.WithAlpha(color, (byte)(Dark ? 0x33 : 0x22)));
        _attachment.BorderThickness = new Thickness(0);
        _attachment.Child = new TextBlock { FontFamily = Palette.IconFont, Text = glyph, FontSize = 20, Foreground = new SolidColorBrush(Bright(color)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Pop(_attachment);
    }

    private static void Pop(FrameworkElement element)
    {
        element.Visibility = Visibility.Visible;
        var s = new ScaleTransform(0.7, 0.7);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = s;
        element.Opacity = 0;
        var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    private static BitmapImage? LoadThumb(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 120;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // WebP/AVIF/HEIC sin códec de Windows: sin miniatura.
            return null;
        }
    }

    // ---------- Acciones: píldoras de vidrio ----------

    private void SetActions(params (string Label, Action OnClick)[] actions)
    {
        _actions.Children.Clear();
        foreach (var (label, onClick) in actions)
            _actions.Children.Add(Pill(label, onClick));
        if (IsMouseOver)
            Hover(true);
    }

    private Border Pill(string label, Action onClick)
    {
        var dark = Dark;
        var idle = new SolidColorBrush(dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        var hover = new SolidColorBrush(dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Colors.White);
        var pill = new Border
        {
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = idle,
            BorderThickness = new Thickness(0.5),
            BorderBrush = Glass.Outline(dark),
            Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Glass.Text(dark)), VerticalAlignment = VerticalAlignment.Center },
        };
        pill.MouseEnter += (_, _) => pill.Background = hover;
        pill.MouseLeave += (_, _) => pill.Background = idle;
        pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return pill;
    }

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

    private Vector _leaveTo = new(1, 0);

    /// <summary>Entra deslizándose desde <paramref name="enterFrom"/> con un resorte suave, como en macOS.</summary>
    /// <param name="left">Posición en unidades del monitor de destino (píxeles / <paramref name="scale"/>).</param>
    /// <param name="scale">Escala del monitor de destino.</param>
    public void ShowAt(double left, double top, double scale, Vector enterFrom, Vector leaveTo)
    {
        _leaveTo = leaveTo;
        // Con monitores de escalas distintas, WPF convierte Left/Top con la escala del monitor donde
        // está la ventana, no con la del destino. Primero se la lleva en píxeles reales al monitor de
        // destino (Windows le ajusta el tamaño a esa escala) y recién ahí se fija Left/Top.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        const uint flags = 0x0001 /* SWP_NOSIZE */ | 0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */;
        Win32.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(left * scale), (int)Math.Round(top * scale), 0, 0, flags);
        Left = left;
        Top = top;
        // El vidrio toma lo que hay detrás una vez, en píxeles físicos.
        var (x, y) = ((int)((left + ShadowMargin) * scale), (int)((top + ShadowMargin) * scale));
        _glass.Source = GlassCapture.Capture(x, y, (int)(CardWidth * scale), (int)(CardHeight * scale)) is { } shot ? Glass.Vibrant(shot, Dark) : null;
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        var spring = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
        var enter = TimeSpan.FromMilliseconds(380);
        if (enterFrom.X != 0)
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(enterFrom.X * CardWidth * 0.35, 0, enter) { EasingFunction = spring });
        if (enterFrom.Y != 0)
            _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(enterFrom.Y * CardHeight * 0.6, 0, enter) { EasingFunction = spring });
    }

    public void MoveTo(double top) =>
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    /// <summary>Se va hacia el borde de la pantalla, más rápido de lo que entró.</summary>
    public void FadeOut()
    {
        if (_closing)
            return;
        _closing = true;
        _autoClose.Stop();
        var duration = TimeSpan.FromMilliseconds(220);
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) =>
        {
            _glass.Source = null;
            Close();
            Gone?.Invoke(this);
        };
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        if (_leaveTo.X != 0)
            _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_leaveTo.X * CardWidth * 0.4, duration) { EasingFunction = ease });
        if (_leaveTo.Y != 0)
            _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_leaveTo.Y * CardHeight * 0.6, duration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Para --snapshot: muestra el estado "mouse encima".</summary>
    public void RevealButtons() => Hover(true);

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

/// <summary>
/// Apila los avisos en la esquina donde aparecen las notificaciones de Windows (ver <see cref="ToastPlacement"/>),
/// el más nuevo pegado al borde, como el centro de notificaciones de macOS.
/// </summary>
internal sealed class ToastStack
{
    private const double Gap = 8;
    private const double Edge = 12;
    private readonly List<JobToast> _toasts = [];
    private ToastPlacement _placement = ToastPlacement.Default;
    private Rect _area;

    public JobToast Add(Job job)
    {
        var toast = new JobToast(job);
        toast.Gone += t =>
        {
            _toasts.Remove(t);
            Relayout();
        };
        _toasts.Insert(0, toast);
        _placement = ToastPlacement.Current();
        var (px, scale) = _placement.Screen();
        _area = new Rect(px.Left / scale, px.Top / scale, px.Width / scale, px.Height / scale);
        toast.ShowAt(LeftFor(), TopFor(0), scale, _placement.EnterFrom(), _placement.LeaveTo());
        Relayout();
        // Más de 4 visibles es ruido: las más viejas terminadas se van.
        foreach (var old in _toasts.Skip(4).Where(t => t.Finished).ToList())
            old.FadeOut();
        return toast;
    }

    private double LeftFor() => _placement.Horizontal switch
    {
        "left" => _area.Left + Edge + _placement.HorizontalDistance,
        "center" => _area.Left + (_area.Width - JobToast.CardWidth) / 2,
        _ => _area.Right - JobToast.CardWidth - Edge - _placement.HorizontalDistance,
    } - JobToast.ShadowMargin;

    /// <summary>Abajo, la pila crece hacia arriba; arriba o al centro, hacia abajo.</summary>
    private double TopFor(int index)
    {
        var step = (JobToast.CardHeight + Gap) * index;
        return _placement.Vertical switch
        {
            "bottom" => _area.Bottom - Edge - _placement.VerticalDistance - JobToast.CardHeight - step,
            "center" => _area.Top + (_area.Height - JobToast.CardHeight) / 2 + step,
            _ => _area.Top + Edge + _placement.VerticalDistance + step,
        } - JobToast.ShadowMargin;
    }

    private void Relayout()
    {
        for (var i = 0; i < _toasts.Count; i++)
            _toasts[i].MoveTo(TopFor(i));
    }
}
