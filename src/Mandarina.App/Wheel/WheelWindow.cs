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
using Mandarina.Core;
using Path = System.Windows.Shapes.Path;

namespace Mandarina.App.Wheel;

/// <summary>
/// La rueda: una única ventana redonda, transparente y siempre encima que se reutiliza.
/// En modo arrastre no toma el foco y recibe el drop OLE del Explorador; en modo teclado sí.
/// </summary>
internal sealed class WheelWindow : Window
{
    private const double Side = 340;
    private const double Outer = 150;
    private const double Inner = 48;
    private const double LabelRadius = (Inner + Outer) / 2 + 3;
    private const double GapDegrees = 1.1;
    private static readonly Point C = new(Side / 2, Side / 2);

    private readonly Catalog _catalog;
    private readonly Func<string?> _lastOperationId;
    private readonly Canvas _layer = new() { Width = Side, Height = Side };
    private readonly Grid _root = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DispatcherTimer _emptyTimer;
    private readonly DispatcherTimer _closeTimer;

    private IntPtr _hwnd;
    private Palette _palette = Palette.Current();
    private BitmapSource? _backdrop;
    private WheelKind _wheel;
    private IReadOnlyList<string> _files = [];
    private List<IOperation> _ops = [];
    private readonly List<(Path Shape, SolidColorBrush Fill, FrameworkElement Content)> _sectors = [];
    private int? _hover;
    private bool _keyboardMode;
    private bool _repeatMode;
    private bool _closing;
    private string? _message;

    private Ellipse? _centerDisk;
    private SolidColorBrush? _centerFill;
    private TextBlock? _centerTitle;
    private TextBlock? _centerGlyph;

    public event Action<IOperation, IReadOnlyList<string>>? OperationChosen;

    /// <summary>La rueda terminó de cerrarse.</summary>
    public event Action? WheelHidden;

    public WheelWindow(Catalog catalog, Func<string?> lastOperationId)
    {
        _catalog = catalog;
        _lastOperationId = lastOperationId;

        Title = "Mandarina";
        Width = Height = Side;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Focusable = true;
        FontFamily = Palette.UiFont;
        UseLayoutRounding = true;

        _root.RenderTransformOrigin = new Point(0.5, 0.5);
        _root.RenderTransform = _scale;
        _root.Children.Add(_layer);
        // Fondo casi invisible: sin él, los píxeles totalmente transparentes no reciben el drop.
        _root.Background = Brushes.Transparent;
        Content = _root;

        _emptyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(650), DispatcherPriority.Normal, (_, _) => OnNoFiles(), Dispatcher) { IsEnabled = false };
        _closeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Normal, (_, _) => HideWheel(), Dispatcher) { IsEnabled = false };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST);
        };

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetHover(null);
        Drop += OnDrop;
        MouseMove += (_, e) => { if (_keyboardMode) UpdateHover(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) => { if (_keyboardMode) ClickAt(e.GetPosition(this)); };
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (_keyboardMode && IsVisible) HideWheel(); };
    }

    /// <summary>Crea la ventana fuera de pantalla para que la primera apertura sea instantánea.</summary>
    public void WarmUp()
    {
        Opacity = 0;
        Left = -10000;
        Top = -10000;
        // Dibuja una rueda completa una vez para que el JIT y el layout ya estén calientes.
        _files = [@"C:\muestra.png"];
        _ops = OpsFor(WheelKind.Convert);
        BuildStatic();
        Rebuild(animate: false);
        Show();
        UpdateLayout();
        Highlight(0, true);
        Hide();
        _layer.Children.Clear();
        _sectors.Clear();
        _files = [];
        _ops = [];
    }

    /// <summary>Abre la rueda durante un arrastre; si ya está abierta, cambia de rueda en el lugar.</summary>
    public void ShowAt(WheelKind wheel, int x, int y)
    {
        if (IsVisible && !_closing)
        {
            _closeTimer.Stop();
            SwitchTo(wheel);
            return;
        }
        _keyboardMode = false;
        _files = [];
        Open(wheel, x, y);
        _emptyTimer.Start();
    }

    /// <summary>Abre la rueda para archivos ya elegidos (atajo sin arrastre); se maneja con teclado o clic.</summary>
    public void ShowForFiles(WheelKind wheel, IReadOnlyList<string> files, int x, int y)
    {
        _keyboardMode = true;
        _files = files;
        Open(wheel, x, y);
        Win32.RemoveExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
        Activate();
        Focus();
        if (_ops.Count > 0)
            SetHover(0);
    }

    /// <summary>Tocaron otra vez la misma tecla: un solo sector con la última acción.</summary>
    public void ShowRepeat()
    {
        if (!IsVisible || _lastOperationId() is not { } id || _catalog.ById(id) is not { } op)
            return;
        if (_files.Count > 0 && !op.AppliesTo(_files))
            return;
        _repeatMode = true;
        _ops = [op];
        Rebuild(animate: true);
    }

    /// <summary>Se soltó el botón: si no hubo drop sobre la rueda, se cierra sola en un momento.</summary>
    public void EndDrag()
    {
        if (IsVisible && !_keyboardMode)
            _closeTimer.Start();
    }

    private void Open(WheelKind wheel, int x, int y)
    {
        _closing = false;
        _closeTimer.Stop();
        _emptyTimer.Stop();
        _palette = Palette.Current();
        _wheel = wheel;
        _repeatMode = false;
        _message = null;
        _hover = null;

        var scale = Win32.ScaleAt(x, y);
        var px = (int)Math.Round(Side * scale);
        var (left, top) = (x - px / 2, y - px / 2);
        _backdrop = GlassCapture.Capture(left, top, px, px);

        _ops = OpsFor(wheel);
        BuildStatic();
        Rebuild(animate: true);

        Opacity = 0;
        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, left, top, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        AnimateOpen();
    }

    private List<IOperation> OpsFor(WheelKind wheel) => _files.Count == 0 ? [] : [.. _catalog.For(wheel, _files)];

    private void SwitchTo(WheelKind wheel)
    {
        _wheel = wheel;
        _repeatMode = false;
        _ops = OpsFor(wheel);
        UpdateCenter();
        Rebuild(animate: true);
    }

    private void OnNoFiles()
    {
        _emptyTimer.Stop();
        if (_files.Count == 0 && IsVisible)
        {
            _message = "Nada para hacer con esto";
            UpdateCenter();
        }
    }

    // ---------- Drag & drop ----------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _closeTimer.Stop();
        var files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
        if (files is { Length: > 0 } && !files.SequenceEqual(_files))
        {
            _emptyTimer.Stop();
            _files = files;
            _message = null;
            _ops = _repeatMode ? _ops.Where(o => o.AppliesTo(files)).ToList() : OpsFor(_wheel);
            Rebuild(animate: true);
        }
        else if (files is null)
        {
            _message = "Nada para hacer con esto";
            UpdateCenter();
        }
        OnDragOver(sender, e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        UpdateHover(e.GetPosition(this));
        // Nunca "Mover": el Explorador borraría el original después del drop.
        e.Effects = _hover is >= 0 ? PreferCopy(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
    }

    private static DragDropEffects PreferCopy(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    private void OnDrop(object sender, DragEventArgs e)
    {
        UpdateHover(e.GetPosition(this));
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (_hover is { } i and >= 0)
            Choose(i);
        else
            HideWheel();
    }

    // ---------- Teclado y clic ----------

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_keyboardMode)
            return;
        var n = _ops.Count;
        switch (e.Key)
        {
            case Key.Escape:
                HideWheel();
                break;
            case Key.Enter when _hover is >= 0:
                Choose(_hover.Value);
                break;
            case Key.Right or Key.Down when n > 0:
                SetHover(((_hover ?? -1) + 1 + n) % n);
                break;
            case Key.Left or Key.Up when n > 0:
                SetHover(((_hover ?? 0) - 1 + n) % n);
                break;
            case Key.Tab:
                SwitchTo(_wheel switch { WheelKind.Convert => WheelKind.Tools, WheelKind.Tools => WheelKind.Actions, _ => WheelKind.Convert });
                if (_ops.Count > 0)
                    SetHover(0);
                break;
            case >= Key.D1 and <= Key.D9 when e.Key - Key.D1 < n:
                Choose(e.Key - Key.D1);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when e.Key - Key.NumPad1 < n:
                Choose(e.Key - Key.NumPad1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void ClickAt(Point p)
    {
        var sector = WheelGeometry.SectorAt(p.X - C.X, p.Y - C.Y, _ops.Count, Inner, Outer);
        if (sector is >= 0)
            Choose(sector.Value);
        else
            HideWheel();
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _ops.Count)
            return;
        var op = _ops[index];
        var files = _files;
        PulseSector(index);
        HideWheel();
        OperationChosen?.Invoke(op, files);
    }

    // ---------- Resaltado ----------

    private void UpdateHover(Point p) => SetHover(WheelGeometry.SectorAt(p.X - C.X, p.Y - C.Y, _ops.Count, Inner, Outer));

    private void SetHover(int? sector)
    {
        if (sector == _hover)
            return;
        var previous = _hover;
        _hover = sector;
        if (previous is >= 0 && previous < _sectors.Count)
            Highlight(previous.Value, false);
        if (sector is >= 0 && sector < _sectors.Count)
            Highlight(sector.Value, true);
        UpdateCenter();
    }

    private void Highlight(int i, bool on)
    {
        var (shape, fill, content) = _sectors[i];
        // Un filo de luz alrededor del sector resaltado.
        shape.Stroke = on ? new SolidColorBrush(Color.FromArgb((byte)(_palette.Dark ? 0x70 : 0xB0), 0xFF, 0xFF, 0xFF)) : null;
        shape.StrokeThickness = 1;
        var target = on ? _palette.Hover : IdleSectorColor();
        fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(90)));
        var s = (ScaleTransform)content.RenderTransform;
        var anim = new DoubleAnimation(on ? 1.08 : 1, TimeSpan.FromMilliseconds(90)) { EasingFunction = new QuadraticEase() };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void PulseSector(int i)
    {
        if (i < _sectors.Count)
            _sectors[i].Fill.Color = Palette.WithAlpha(_palette.Hover, 0xB0);
    }

    private Color IdleSectorColor() => _palette.Dark ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF);

    // ---------- Dibujo ----------

    /// <summary>Capas fijas del vidrio: sombra, fondo desenfocado, refracción, tinte, brillo y bordes.</summary>
    private void BuildStatic()
    {
        _layer.Children.Clear();
        _sectors.Clear();
        var p = _palette;

        // Sombra suave debajo del disco.
        var shadow = new Ellipse
        {
            Width = (Outer + 16) * 2,
            Height = (Outer + 16) * 2,
            Fill = new RadialGradientBrush(
                [new GradientStop(Color.FromArgb((byte)(p.Dark ? 0x70 : 0x40), 0, 0, 0), 0.82), new GradientStop(Colors.Transparent, 1)]),
        };
        Place(shadow, C.X, C.Y + 6);

        var disk = new EllipseGeometry(C, Outer, Outer);
        if (_backdrop is not null)
        {
            // Fondo desenfocado (la captura ya viene a 1/4, el blur termina de suavizar).
            _layer.Children.Add(new Image
            {
                Source = _backdrop,
                Width = Side,
                Height = Side,
                Stretch = Stretch.Fill,
                Clip = disk,
                Effect = new BlurEffect { Radius = 14, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
            });
            // "Refracción": cerca del borde el fondo se ve un poco agrandado, como a través de una lente.
            _layer.Children.Add(new Image
            {
                Source = _backdrop,
                Width = Side,
                Height = Side,
                Stretch = Stretch.Fill,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.09, 1.09),
                Clip = SectorShapes.Ring(C, Outer - 16, Outer),
                Opacity = 0.6,
                Effect = new BlurEffect { Radius = 8, RenderingBias = RenderingBias.Performance },
            });
        }

        // Tinte del vidrio, un poco más claro hacia el centro (luz que atraviesa).
        AddDisk(Outer, new RadialGradientBrush(
            [new GradientStop(Palette.WithAlpha(p.Tint, (byte)(p.Tint.A * 0.7)), 0.3), new GradientStop(p.Tint, 1)]));

        // Brillo líquido: luz desde arriba, reflejo tenue abajo.
        AddDisk(Outer, new LinearGradientBrush(
            [
                new GradientStop(Color.FromArgb((byte)(p.Dark ? 0x30 : 0x55), 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.42),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.78),
                new GradientStop(Color.FromArgb((byte)(p.Dark ? 0x14 : 0x26), 0xFF, 0xFF, 0xFF), 1),
            ], new Point(0.5, 0), new Point(0.5, 1)));

        // Borde especular: brillante arriba a la izquierda, apagado al medio, rebote abajo a la derecha.
        var rim = new Ellipse
        {
            Width = Outer * 2,
            Height = Outer * 2,
            StrokeThickness = 1.4,
            Stroke = new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0.45),
                    new GradientStop(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.6),
                    new GradientStop(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 1),
                ], new Point(0.15, 0), new Point(0.85, 1)),
        };
        Place(rim, C.X, C.Y);
        var innerShade = new Ellipse
        {
            Width = Outer * 2 - 3,
            Height = Outer * 2 - 3,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb((byte)(p.Dark ? 0x40 : 0x14), 0, 0, 0)),
        };
        Place(innerShade, C.X, C.Y);

        // Centro: un disco de vidrio más denso con el nombre de la rueda o "×".
        _centerFill = new SolidColorBrush(p.CenterTint);
        _centerDisk = new Ellipse
        {
            Width = (Inner - 5) * 2,
            Height = (Inner - 5) * 2,
            Fill = _centerFill,
            StrokeThickness = 1,
            Stroke = new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 90),
        };
        Place(_centerDisk, C.X, C.Y);

        _centerGlyph = new TextBlock
        {
            FontFamily = Palette.IconFont,
            Text = "",
            FontSize = 16,
            Foreground = new SolidColorBrush(p.Text),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0,
        };
        _centerTitle = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(p.SubText),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = (Inner - 10) * 2,
        };
        var center = new Grid { Width = Inner * 2, Height = Inner * 2, IsHitTestVisible = false };
        center.Children.Add(_centerGlyph);
        center.Children.Add(_centerTitle);
        _centerGlyph.VerticalAlignment = VerticalAlignment.Center;
        _centerTitle.VerticalAlignment = VerticalAlignment.Center;
        Place(center, C.X, C.Y);
        UpdateCenter();
    }

    /// <summary>Sectores (se rehacen al cambiar de rueda o de archivos).</summary>
    private void Rebuild(bool animate)
    {
        foreach (var (shape, _, content) in _sectors)
        {
            _layer.Children.Remove(shape);
            _layer.Children.Remove(content);
        }
        _sectors.Clear();
        _hover = null;

        var n = _ops.Count;
        var insertAt = _layer.Children.IndexOf(_centerDisk!);
        for (var i = 0; i < n; i++)
        {
            var width = 360.0 / n;
            var geometry = n == 1
                ? SectorShapes.Ring(C, Inner + 2, Outer - 3)
                : SectorShapes.Wedge(C, Inner + 2, Outer - 3, i * width - width / 2 + GapDegrees, i * width + width / 2 - GapDegrees);
            var fill = new SolidColorBrush(IdleSectorColor());
            var shape = new Path { Data = geometry, Fill = fill, IsHitTestVisible = false };
            _layer.Children.Insert(insertAt++, shape);

            var content = SectorContent(_ops[i], i);
            var (x, y) = n == 1 ? (0.0, -LabelRadius) : WheelGeometry.SectorCenter(i, n, LabelRadius);
            Place(content, C.X + x, C.Y + y);
            _sectors.Add((shape, fill, content));

            if (animate)
            {
                content.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(30 + i * 12);
                content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)) { BeginTime = delay });
                var slide = (TranslateTransform)content.Tag;
                slide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        UpdateCenter();
    }

    private FrameworkElement SectorContent(IOperation op, int index)
    {
        var p = _palette;
        var isFormat = _wheel == WheelKind.Convert && !_repeatMode;
        var stack = new StackPanel { Width = 78, IsHitTestVisible = false };
        if (_repeatMode)
        {
            stack.Children.Add(new TextBlock { Text = "Repetir", FontSize = 11, Foreground = new SolidColorBrush(p.SubText), HorizontalAlignment = HorizontalAlignment.Center });
        }
        else if (!isFormat)
        {
            stack.Children.Add(new TextBlock
            {
                FontFamily = Palette.IconFont,
                Text = op.Glyph,
                FontSize = 17,
                Foreground = new SolidColorBrush(p.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }
        stack.Children.Add(new TextBlock
        {
            Text = op.Label,
            FontSize = isFormat ? 15 : 12,
            FontWeight = isFormat ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = new SolidColorBrush(p.Text),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (_keyboardMode && index < 9 && !_repeatMode)
        {
            stack.Children.Add(new TextBlock { Text = (index + 1).ToString(), FontSize = 9.5, Foreground = new SolidColorBrush(p.SubText), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        }
        // El contenedor lleva la escala del resaltado; el contenido, el deslizamiento de entrada.
        var slide = new TranslateTransform();
        stack.RenderTransform = slide;
        return new Border
        {
            Child = stack,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Tag = slide,
        };
    }

    private void UpdateCenter()
    {
        if (_centerTitle is null || _centerGlyph is null || _centerFill is null)
            return;
        var onCenter = _hover == -1;
        _centerTitle.Text = _message ?? (_wheel switch
        {
            WheelKind.Convert => "Convertir",
            WheelKind.Tools => "Herramientas",
            _ => "Acciones",
        });
        _centerTitle.FontSize = _message is null ? 11 : 9.5;
        _centerTitle.Opacity = onCenter ? 0 : 1;
        _centerGlyph.Opacity = onCenter ? 1 : 0;
        _centerFill.Color = onCenter ? Palette.WithAlpha(_palette.Danger, 0x55) : _palette.CenterTint;
    }

    private void AddDisk(double radius, Brush fill)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, IsHitTestVisible = false };
        Place(e, C.X, C.Y);
    }

    /// <summary>Agrega <paramref name="element"/> centrado en (x, y).</summary>
    private void Place(FrameworkElement element, double x, double y)
    {
        if (!_layer.Children.Contains(element))
            _layer.Children.Add(element);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = double.IsNaN(element.Width) ? element.DesiredSize : new Size(element.Width, double.IsNaN(element.Height) ? element.DesiredSize.Height : element.Height);
        Canvas.SetLeft(element, x - size.Width / 2);
        Canvas.SetTop(element, y - size.Height / 2);
    }

    // ---------- Animaciones ----------

    private void AnimateOpen()
    {
        var duration = TimeSpan.FromMilliseconds(160);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        var spring = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = spring };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    public void HideWheel()
    {
        _emptyTimer.Stop();
        _closeTimer.Stop();
        if (!IsVisible || _closing)
            return;
        _closing = true;
        var duration = TimeSpan.FromMilliseconds(110);
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) =>
        {
            if (!_closing)
                return;
            Hide();
            _closing = false;
            _backdrop = null;
            _layer.Children.Clear();
            _sectors.Clear();
            if (_keyboardMode)
                Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
            _keyboardMode = false;
            WheelHidden?.Invoke();
        };
        var shrink = new DoubleAnimation(0.94, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Para --snapshot: fuerza el resaltado de un sector (-1 = centro).</summary>
    public void SnapshotHover(int sector) => SetHover(sector);

    /// <summary>Para --snapshot: guarda lo que se ve ahora (fondo + rueda) en PNG, a 2×.</summary>
    public void SaveSnapshot(string path)
    {
        const double dpi = 192;
        var bitmap = new RenderTargetBitmap((int)(Side * 2), (int)(Side * 2), dpi, dpi, PixelFormats.Pbgra32);
        if (_backdrop is not null)
        {
            // Debajo, el fondo real sin desenfocar, para ver el vidrio en contexto.
            var under = new DrawingVisual();
            using (var dc = under.RenderOpen())
                dc.DrawImage(_backdrop, new Rect(0, 0, Side, Side));
            bitmap.Render(under);
        }
        bitmap.Render(_root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Para --bench: cierra al instante, sin animación.</summary>
    public void HideNow()
    {
        _closing = true;
        BeginAnimation(OpacityProperty, null);
        Hide();
        _closing = false;
        _layer.Children.Clear();
        _sectors.Clear();
        _backdrop = null;
    }
}
