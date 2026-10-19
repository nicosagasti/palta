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
/// Visualmente es un disco de vidrio sin divisiones: las opciones flotan y una "gota" de vidrio
/// más brillante se desliza hasta la que está bajo el cursor.
/// </summary>
internal sealed class WheelWindow : Window
{
    private const double Side = 400;
    private const double Outer = 148;
    private const double Inner = 40;
    private const double CenterRadius = 27;
    private const double DropRadius = 35;
    private const double LabelRadius = (Inner + Outer) / 2 + 6;
    private static readonly Point C = new(Side / 2, Side / 2 - 10);

    private readonly Catalog _catalog;
    private readonly Func<string?> _lastOperationId;
    private readonly Canvas _layer = new() { Width = Side, Height = Side };
    private readonly Canvas _sectorLayer = new() { Width = Side, Height = Side, IsHitTestVisible = false };
    private readonly Grid _root = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DispatcherTimer _emptyTimer;
    private readonly DispatcherTimer _closeTimer;

    private IntPtr _hwnd;
    private Palette _palette = Palette.Current();
    private bool Dark => _palette.Dark;
    private BitmapSource? _backdrop;
    private WheelKind _wheel;
    private IReadOnlyList<string> _files = [];
    private List<IOperation> _ops = [];
    private readonly List<FrameworkElement> _sectors = [];
    private int? _hover;
    private bool _keyboardMode;
    private bool _repeatMode;
    private bool _closing;
    private string? _message;

    // Gota de vidrio que marca la opción bajo el cursor.
    private readonly Grid _drop = new() { Width = DropRadius * 2, Height = DropRadius * 2, IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly TranslateTransform _dropMove = new();
    private readonly ScaleTransform _dropScale = new(0.8, 0.8);

    private Ellipse? _centerDisk;
    private TextBlock? _centerGlyph;
    private TextBlock? _caption;
    private Border? _captionChip;

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
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _drop.RenderTransform = new TransformGroup { Children = { _dropScale, _dropMove } };

        _root.RenderTransformOrigin = new Point(0.5, C.Y / Side);
        _root.RenderTransform = _scale;
        _root.Children.Add(_layer);
        // Fondo transparente pero "pintado": sin él, WPF no hace hit-test y no llega el drop.
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
        SetHover(0);
        Hide();
        Clear();
        _files = [];
        _ops = [];
    }

    /// <summary>Abre la rueda durante un arrastre; si ya está abierta, cambia de rueda en el lugar.</summary>
    public void ShowAt(WheelKind wheel, int x, int y)
    {
        Core.Log.Debug($"ShowAt {wheel} ({x},{y}) visible={IsVisible}");
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

    /// <summary>Tocaron otra vez la misma tecla: una sola opción con la última acción.</summary>
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
        Core.Log.Debug($"EndDrag visible={IsVisible}");
        if (IsVisible && !_keyboardMode)
            _closeTimer.Start();
    }

    private void Open(WheelKind wheel, int x, int y)
    {
        if (!_keyboardMode && _hwnd != IntPtr.Zero)
            Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
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
        var (left, top) = (x - px / 2, y - (int)Math.Round(C.Y * scale));
        _backdrop = GlassCapture.Capture(left, top, px, px) is { } shot ? Glass.Vibrant(shot, Dark) : null;

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
        Rebuild(animate: true);
    }

    private void OnNoFiles()
    {
        _emptyTimer.Stop();
        if (_files.Count == 0 && IsVisible)
        {
            _message = "Nada para hacer con esto";
            UpdateCaption();
        }
    }

    // ---------- Drag & drop ----------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        Core.Log.Debug($"DragEnter formats={string.Join(",", e.Data.GetFormats())} allowed={e.AllowedEffects}");
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
            UpdateCaption();
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
        Core.Log.Debug($"Drop hover={_hover} ops={_ops.Count}");
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
        Core.Log.Debug($"Choose {index}");
        if (index < 0 || index >= _ops.Count)
            return;
        var op = _ops[index];
        var files = _files;
        HideWheel();
        OperationChosen?.Invoke(op, files);
    }

    // ---------- Resaltado: la gota ----------

    private void UpdateHover(Point p) => SetHover(WheelGeometry.SectorAt(p.X - C.X, p.Y - C.Y, _ops.Count, Inner, Outer));

    private void SetHover(int? sector)
    {
        if (sector == _hover)
            return;
        var previous = _hover;
        _hover = sector;
        if (previous is >= 0 && previous < _sectors.Count)
            Emphasize(_sectors[previous.Value], false);
        if (sector is >= 0 && sector < _sectors.Count)
        {
            Emphasize(_sectors[sector.Value], true);
            MoveDrop(sector.Value, appear: previous is not >= 0);
        }
        else
            HideDrop();
        UpdateCenter();
    }

    private void MoveDrop(int index, bool appear)
    {
        var (x, y) = _ops.Count == 1 ? (0.0, -LabelRadius) : WheelGeometry.SectorCenter(index, _ops.Count, LabelRadius);
        var spring = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(appear ? 0 : 210);
        _dropMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = spring });
        _dropMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = spring });
        _drop.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
        var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(appear ? 180 : 120)) { EasingFunction = spring };
        _dropScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _dropScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void HideDrop()
    {
        _drop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        var shrink = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(120));
        _dropScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _dropScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private static void Emphasize(FrameworkElement content, bool on)
    {
        var s = (ScaleTransform)content.RenderTransform;
        var anim = new DoubleAnimation(on ? 1.07 : 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        if (content.Tag is TextBlock label)
            label.Opacity = on ? 1 : 0.72;
    }

    // ---------- Dibujo ----------

    /// <summary>Capas fijas del vidrio: fondo con vibrancia, tinte, brillo, filo y centro (sin sombra alrededor, a pedido).</summary>
    private void BuildStatic()
    {
        Clear();
        var dark = Dark;

        var disk = new EllipseGeometry(C, Outer, Outer);
        disk.Freeze();
        if (_backdrop is not null)
        {
            _layer.Children.Add(new Image
            {
                Source = _backdrop,
                Width = Side,
                Height = Side,
                Stretch = Stretch.Fill,
                Clip = disk,
                Effect = new BlurEffect { Radius = 18, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
            });
        }
        AddDisk(Outer, new SolidColorBrush(Glass.Tint(dark)));
        AddDisk(Outer, Glass.Specular(dark));
        Place(new Ellipse { Width = Outer * 2 + 1.5, Height = Outer * 2 + 1.5, Stroke = Glass.Outline(dark), StrokeThickness = 0.75 }, C.X, C.Y);
        Place(new Ellipse { Width = Outer * 2 - 1, Height = Outer * 2 - 1, Stroke = Glass.Rim(dark), StrokeThickness = 1 }, C.X, C.Y);

        // La gota: vidrio más claro con su propio filo de luz.
        _drop.Children.Clear();
        _drop.Children.Add(new Ellipse { Fill = new SolidColorBrush(dark ? Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)) });
        _drop.Children.Add(new Ellipse { Fill = Glass.Specular(dark) });
        _drop.Children.Add(new Ellipse { Stroke = Glass.Rim(dark), StrokeThickness = 1 });
        _drop.Opacity = 0;
        _dropScale.ScaleX = _dropScale.ScaleY = 0.8;
        Place(_drop, C.X, C.Y);
        _layer.Children.Add(_sectorLayer);

        // Centro: un círculo sereno con una ✕ tenue.
        _centerDisk = new Ellipse
        {
            Width = CenterRadius * 2,
            Height = CenterRadius * 2,
            Fill = new SolidColorBrush(Glass.Chip(dark)),
            Stroke = Glass.Rim(dark),
            StrokeThickness = 1,
        };
        Place(_centerDisk, C.X, C.Y);
        _centerGlyph = new TextBlock { FontFamily = Palette.IconFont, Text = "\uE711", FontSize = 11, Foreground = new SolidColorBrush(Glass.Secondary(dark)), IsHitTestVisible = false };
        Place(_centerGlyph, C.X, C.Y);

        // Cápsula debajo: qué rueda es y sobre qué archivo.
        _caption = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Glass.Text(dark)), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        _captionChip = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 5, 12, 6),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0xE0, 0x2A, 0x2A, 0x2F) : Color.FromArgb(0xEE, 0xFA, 0xFA, 0xFC)),
            BorderBrush = Glass.Outline(dark),
            BorderThickness = new Thickness(0.75),
            Child = _caption,
            IsHitTestVisible = false,
        };
        _layer.Children.Add(_captionChip);
        UpdateCaption();
        UpdateCenter();
    }

    private void Clear()
    {
        _layer.Children.Clear();
        _sectorLayer.Children.Clear();
        _sectors.Clear();
    }

    /// <summary>Opciones (se rehacen al cambiar de rueda o de archivos).</summary>
    private void Rebuild(bool animate)
    {
        _sectorLayer.Children.Clear();
        _sectors.Clear();
        _hover = null;
        HideDrop();

        var n = _ops.Count;
        for (var i = 0; i < n; i++)
        {
            var content = SectorContent(_ops[i], i);
            var (x, y) = n == 1 ? (0.0, -LabelRadius) : WheelGeometry.SectorCenter(i, n, LabelRadius);
            _sectorLayer.Children.Add(content);
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(content, C.X + x - content.DesiredSize.Width / 2);
            Canvas.SetTop(content, C.Y + y - content.DesiredSize.Height / 2);
            _sectors.Add(content);

            if (animate)
            {
                // Aparecen escalonadas, deslizándose apenas desde el centro hacia afuera.
                var delay = TimeSpan.FromMilliseconds(20 + i * 14);
                var len = Math.Max(1, Math.Sqrt(x * x + y * y));
                var slide = new TranslateTransform();
                var inner = (FrameworkElement)((Border)content).Child;
                inner.RenderTransform = slide;
                content.Opacity = 0;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { BeginTime = delay });
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-x / len * 8, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = delay, EasingFunction = ease });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-y / len * 8, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = delay, EasingFunction = ease });
            }
        }
        UpdateCaption();
        UpdateCenter();
    }

    private FrameworkElement SectorContent(IOperation op, int index)
    {
        var dark = Dark;
        var text = new SolidColorBrush(Glass.Text(dark));
        var isFormat = _wheel == WheelKind.Convert && !_repeatMode;
        var stack = new StackPanel { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        TextBlock? label = null;

        if (isFormat)
        {
            stack.Children.Add(FileBadge(op.Label));
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                FontFamily = Palette.IconFont,
                Text = _repeatMode ? "\uE72C" : op.Glyph,
                FontSize = 18,
                Foreground = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
            });
            label = new TextBlock
            {
                Text = _repeatMode ? $"Repetir: {op.Label}" : op.Label,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = text,
                Opacity = 0.72,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 76,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            stack.Children.Add(label);
        }
        if (_keyboardMode && index < 9 && !_repeatMode)
            stack.Children.Add(new TextBlock { Text = (index + 1).ToString(), FontSize = 9.5, Foreground = new SolidColorBrush(Glass.Tertiary(dark)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });

        // El contenedor lleva la escala del énfasis; el contenido, el deslizamiento de entrada.
        return new Border
        {
            Child = stack,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Tag = label,
        };
    }

    /// <summary>Ícono de documento con la extensión, como los de macOS: hoja clara, esquina doblada, sigla de color.</summary>
    private FrameworkElement FileBadge(string ext)
    {
        var dark = Dark;
        const double w = 32, h = 40, fold = 9, r = 4;
        var page = Geometry.Parse(FormattableString.Invariant($"M{r},0 H{w - fold} L{w},{fold} V{h - r} Q{w},{h} {w - r},{h} H{r} Q0,{h} 0,{h - r} V{r} Q0,0 {r},0 Z"));
        var corner = Geometry.Parse(FormattableString.Invariant($"M{w - fold},0 V{fold - 2.5} Q{w - fold},{fold} {w - fold + 2.5},{fold} H{w}"));
        var color = ext == "PDF" ? Color.FromRgb(0xE5, 0x48, 0x4D) : _palette.Accent;
        var grid = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Center };
        grid.Children.Add(new Path
        {
            Data = page,
            Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xEE, 0xEE, 0xF1), 90),
            Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            StrokeThickness = 0.75,
        });
        grid.Children.Add(new Path { Data = corner, Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), StrokeThickness = 0.75 });
        grid.Children.Add(new TextBlock
        {
            Text = ext,
            FontSize = ext.Length > 3 ? 8.5 : 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 7),
        });
        return grid;
    }

    private static Color Lighten(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.35), (byte)(c.G + (255 - c.G) * 0.35), (byte)(c.B + (255 - c.B) * 0.35));

    private void UpdateCenter()
    {
        if (_centerDisk is null || _centerGlyph is null)
            return;
        var onCenter = _hover == -1;
        _centerDisk.Fill = new SolidColorBrush(onCenter ? Palette.WithAlpha(_palette.Danger, 0x48) : Glass.Chip(Dark));
        _centerGlyph.Foreground = new SolidColorBrush(onCenter ? (Dark ? Colors.White : _palette.Danger) : Glass.Secondary(Dark));
    }

    private void UpdateCaption()
    {
        if (_caption is null || _captionChip is null)
            return;
        if (_files.Count > 0 && _ops.Count == 0)
            _message = "Nada para hacer con esto";
        else if (_files.Count > 0 && _message == "Nada para hacer con esto")
            _message = null;

        var wheelName = _wheel switch { WheelKind.Convert => "Convertir", WheelKind.Tools => "Herramientas", _ => "Acciones" };
        _caption.Inlines.Clear();
        if (_message is not null)
            _caption.Inlines.Add(new System.Windows.Documents.Run(_message) { Foreground = new SolidColorBrush(Glass.Secondary(Dark)) });
        else
        {
            _caption.Inlines.Add(new System.Windows.Documents.Run(wheelName) { Foreground = new SolidColorBrush(Glass.Secondary(Dark)) });
            if (_files.Count > 0)
            {
                var name = System.IO.Path.GetFileName(_files[0].TrimEnd('\\'));
                var more = _files.Count > 1 ? $"  +{_files.Count - 1}" : "";
                _caption.Inlines.Add(new System.Windows.Documents.Run("   " + name + more));
            }
        }
        _captionChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_captionChip, C.X - _captionChip.DesiredSize.Width / 2);
        Canvas.SetTop(_captionChip, C.Y + Outer + 14);
    }

    private void AddDisk(double radius, Brush fill) =>
        Place(new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, IsHitTestVisible = false }, C.X, C.Y);

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
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var spring = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = spring };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        // Un leve desenfoque que se aclara; se saca al terminar para no costar nada mientras está abierta.
        var blur = new BlurEffect { Radius = 6, RenderingBias = RenderingBias.Performance };
        _root.Effect = blur;
        var sharpen = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(150));
        sharpen.Completed += (_, _) => _root.Effect = null;
        blur.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
    }

    public void HideWheel()
    {
        _emptyTimer.Stop();
        _closeTimer.Stop();
        if (!IsVisible || _closing)
            return;
        _closing = true;
        var duration = TimeSpan.FromMilliseconds(120);
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) =>
        {
            if (!_closing)
                return;
            Hide();
            _closing = false;
            _backdrop = null;
            Clear();
            if (_keyboardMode)
                Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
            _keyboardMode = false;
            WheelHidden?.Invoke();
        };
        var shrink = new DoubleAnimation(0.95, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Para --snapshot: abre como en un arrastre, pero con archivos ya conocidos.</summary>
    public void ShowPreview(WheelKind wheel, IReadOnlyList<string> files, int x, int y)
    {
        _keyboardMode = false;
        _files = files;
        Open(wheel, x, y);
    }

    /// <summary>Para --snapshot: fuerza el resaltado de una opción (-1 = centro).</summary>
    public void SnapshotHover(int sector) => SetHover(sector);

    /// <summary>Para --snapshot: guarda lo que se ve ahora (fondo real + rueda) en PNG, a 2×.</summary>
    public void SaveSnapshot(string path, BitmapSource? under)
    {
        const double dpi = 192;
        var bitmap = new RenderTargetBitmap((int)(Side * 2), (int)(Side * 2), dpi, dpi, PixelFormats.Pbgra32);
        if (under is not null)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(under, new Rect(0, 0, Side, Side));
            bitmap.Render(visual);
        }
        bitmap.Render(_root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Para --snapshot: la región de pantalla que tiene detrás, en resolución completa.</summary>
    public (int X, int Y, int Size) ScreenRect(int x, int y)
    {
        var scale = Win32.ScaleAt(x, y);
        var px = (int)Math.Round(Side * scale);
        return (x - px / 2, y - (int)Math.Round(C.Y * scale), px);
    }

    /// <summary>Para --bench: cierra al instante, sin animación.</summary>
    public void HideNow()
    {
        _closing = true;
        BeginAnimation(OpacityProperty, null);
        Hide();
        _closing = false;
        _root.Effect = null;
        Clear();
        _backdrop = null;
    }
}
