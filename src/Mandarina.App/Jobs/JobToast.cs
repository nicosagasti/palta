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
/// Aviso de un trabajo al estilo de las notificaciones de macOS: tarjeta de vidrio con el ícono
/// de la app, título, archivo, una barra finísima y acciones de texto.
/// </summary>
internal sealed class JobToast : Window
{
    public const double CardWidth = 356;
    public const double CardHeight = 76;
    public const double ShadowMargin = 18;
    private const double Radius = 16;

    private static readonly BitmapImage AppIcon = LoadIcon();

    private readonly Job _job;
    private readonly Palette _palette = Palette.Current();
    private bool Dark => _palette.Dark;
    private readonly TextBlock _title;
    private readonly TextBlock _status;
    private readonly Border _barTrack;
    private readonly Border _barFill;
    private readonly Image _glass = new() { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Performance } };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _badge;
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
        var text = new SolidColorBrush(Glass.Text(dark));
        var secondary = new SolidColorBrush(Glass.Secondary(dark));

        var card = new Grid { Margin = new Thickness(ShadowMargin), RenderTransform = _slide };
        card.Children.Add(_glass);
        _glass.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), Radius, Radius);
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = new SolidColorBrush(Glass.Tint(dark)) });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = Glass.Specular(dark), Height = 40, VerticalAlignment = VerticalAlignment.Top });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius + 0.5), Margin = new Thickness(-0.75), BorderThickness = new Thickness(0.75), BorderBrush = Glass.Outline(dark) });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1), BorderBrush = Glass.Rim(dark) });

        // Ícono de la app con una insignia de estado chiquita abajo a la derecha.
        _badge = new TextBlock { FontFamily = Palette.IconFont, FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badgeDot = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(_palette.Accent),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(0x2A, 0x2A, 0x2F) : Colors.White),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -4, -4),
            Child = _badge,
            Visibility = Visibility.Collapsed,
            Tag = "badge",
        };
        var icon = new Grid { Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        icon.Children.Add(new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Background = new ImageBrush(AppIcon) { Stretch = Stretch.UniformToFill } });
        icon.Children.Add(badgeDot);

        _title = new TextBlock { Text = job.Operation.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis };
        _status = new TextBlock { Text = FileLine(job), FontSize = 12, Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        _barFill = new Border { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(_palette.Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _barTrack = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0, 0, 0)),
            Child = _barFill,
            ClipToBounds = true,
        };

        var head = new DockPanel();
        DockPanel.SetDock(_buttons, Dock.Right);
        head.Children.Add(_buttons);
        head.Children.Add(_title);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(head);
        info.Children.Add(_status);
        info.Children.Add(_barTrack);

        var content = new DockPanel { Margin = new Thickness(14, 10, 16, 10) };
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(info);
        card.Children.Add(content);

        var outer = new Grid();
        outer.Children.Add(Glass.RectShadow(Radius, ShadowMargin, dark));
        outer.Children.Add(card);
        Content = outer;

        AddButton("Cancelar", () => _job.Cancel());

        _autoClose = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal, (_, _) => FadeOut(), Dispatcher) { IsEnabled = false };
        MouseEnter += (_, _) => _autoClose.Stop();
        MouseLeave += (_, _) => { if (_job.State == JobState.Done) _autoClose.Start(); };

        SourceInitialized += (_, _) => Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);

        job.Changed += j => Dispatcher.BeginInvoke(() => Refresh(j));
        Refresh(job);
    }

    private static BitmapImage LoadIcon()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/mandarina.png");
        image.DecodePixelWidth = 96;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string FileLine(Job job)
    {
        var first = System.IO.Path.GetFileName(job.Files[0].TrimEnd('\\'));
        return job.Files.Count > 1 ? $"{first} y {job.Files.Count - 1} más" : first;
    }

    private void Refresh(Job job)
    {
        switch (job.State)
        {
            case JobState.Queued:
                _status.Text = $"En espera · {FileLine(job)}";
                break;
            case JobState.Running:
                SetProgress(job.Progress);
                _status.Text = FileLine(job);
                break;
            case JobState.Done:
                SetProgress(1);
                _barTrack.Visibility = Visibility.Collapsed;
                Badge("", _palette.Accent);
                _status.Text = job.Outputs.Count switch
                {
                    0 => job.Operation.Id == "copy-path" ? "Ruta copiada al portapapeles" : "Listo",
                    1 => System.IO.Path.GetFileName(job.Outputs[0]),
                    var n => $"{n} archivos creados",
                };
                _buttons.Children.Clear();
                if (job.Outputs.Count > 0)
                {
                    AddButton("Mostrar", () => Reveal(job.Outputs[0]));
                    AddButton("Abrir", () => Open(job.Outputs.Count == 1 ? job.Outputs[0] : System.IO.Path.GetDirectoryName(job.Outputs[0])!));
                }
                if (!IsMouseOver)
                    _autoClose.Start();
                break;
            case JobState.Failed:
                _barTrack.Visibility = Visibility.Collapsed;
                Badge("", _palette.Danger);
                _title.Text = $"No se pudo: {job.Operation.Label}";
                _status.Text = job.Error ?? "Error desconocido";
                _status.ToolTip = job.Error;
                _buttons.Children.Clear();
                AddButton("Cerrar", FadeOut);
                break;
            case JobState.Canceled:
                _barTrack.Visibility = Visibility.Collapsed;
                _status.Text = "Cancelado";
                _buttons.Children.Clear();
                _autoClose.Interval = TimeSpan.FromSeconds(1.5);
                _autoClose.Start();
                break;
        }
    }

    private void SetProgress(double p)
    {
        _barTrack.UpdateLayout();
        var width = Math.Max(0, (_barTrack.ActualWidth > 0 ? _barTrack.ActualWidth : CardWidth - 80) * Math.Clamp(p, 0.04, 1));
        _barFill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Badge(string glyph, Color color)
    {
        _badge.Text = glyph;
        if (_badge.Parent is Border dot)
        {
            dot.Background = new SolidColorBrush(color);
            dot.Visibility = Visibility.Visible;
        }
    }

    private void AddButton(string label, Action onClick)
    {
        var accent = new SolidColorBrush(Dark ? Lighter(_palette.Accent) : _palette.Accent);
        var text = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = accent, Margin = new Thickness(12, 0, 0, 0), Cursor = Cursors.Hand, Opacity = 0.9 };
        text.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        text.MouseEnter += (_, _) => text.Opacity = 1;
        text.MouseLeave += (_, _) => text.Opacity = 0.9;
        _buttons.Children.Add(text);
    }

    private static Color Lighter(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.4), (byte)(c.G + (255 - c.G) * 0.4), (byte)(c.B + (255 - c.B) * 0.4));

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Core.Log.Error($"Abrir {path}", e); }
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
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut } });
    }

    public void MoveTo(double top) =>
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    public void FadeOut()
    {
        if (_closing)
            return;
        _closing = true;
        _autoClose.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            _glass.Source = null;
            Close();
            Gone?.Invoke(this);
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24, TimeSpan.FromMilliseconds(200)));
        BeginAnimation(OpacityProperty, fade);
    }

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

/// <summary>Apila las tarjetas abajo a la derecha del área de trabajo.</summary>
internal sealed class ToastStack
{
    private const double Gap = 10;
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
