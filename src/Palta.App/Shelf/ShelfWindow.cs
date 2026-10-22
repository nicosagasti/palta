using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Palta.App.Native;
using Palta.App.Theme;
using Palta.App.Wheel;
using Palta.Core;

namespace Palta.App.Shelf;

/// <summary>
/// "Flotar" (el Float de Tangerine): un estante de vidrio, siempre encima, que se mueve arrastrándolo.
/// Guarda archivos para volver a arrastrarlos a cualquier lado; también se le pueden soltar más encima.
/// Una sola ventana, reutilizada: se vacía al cerrarla.
/// </summary>
internal sealed class ShelfWindow : Window
{
    private const double PanelWidth = 292;
    /// <summary>Esquinas: amplias en Liquid glass, las de Windows 11 (8 px) en el tema Windows.</summary>
    private static double Radius => Look.Translucent ? 18 : 8;
    private const double ShadowMargin = 22;
    private const double MaxListHeight = 330;

    private readonly List<string> _files = [];
    private readonly Grid _card = new();
    // Pincel de fondo (no Image): una imagen impondría su tamaño natural a la ventana.
    private readonly Border _glass = new() { Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance } };
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _count = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0) };
    private readonly Border _dropRing = new() { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(2), Opacity = 0, IsHitTestVisible = false };
    private readonly Border _allChip = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DispatcherTimer _recapture;
    private readonly List<Border> _rows = [];
    private Palette _palette = Palette.Current();
    private bool Dark => _palette.Dark;
    private IntPtr _hwnd;
    private int _selected = -1;
    private Point? _dragStart;
    private bool _placed;

    public ShelfWindow()
    {
        Title = "Palta · Estante";
        Width = PanelWidth + ShadowMargin * 2;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        FontFamily = Palette.UiFont;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = MaxListHeight, Content = _list, Padding = new Thickness(6, 0, 6, 0) };
        _card.Margin = new Thickness(ShadowMargin);
        _card.RenderTransformOrigin = new Point(0.5, 0.5);
        _card.RenderTransform = _scale;
        _card.SizeChanged += (_, _) => _glass.Clip = new RectangleGeometry(new Rect(0, 0, _card.ActualWidth, _card.ActualHeight), Radius, Radius);

        // Mover: arrastrando desde cualquier parte que no sea un archivo.
        _card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled)
                return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        // Después de mover, el vidrio vuelve a tomar lo que tiene detrás (una sola vez, no en cada cuadro).
        _recapture = new DispatcherTimer(TimeSpan.FromMilliseconds(220), DispatcherPriority.Background, (_, _) => Recapture(), Dispatcher) { IsEnabled = false };
        LocationChanged += (_, _) => { if (IsVisible) { _recapture.Stop(); _recapture.Start(); } };

        DragEnter += (_, e) => OnDragOverShelf(e, enter: true);
        DragOver += (_, e) => OnDragOverShelf(e, enter: false);
        DragLeave += (_, _) => ShowDropRing(false);
        Drop += OnDrop;
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW);
        };
        Content = new Grid();
    }

    /// <summary>Agrega archivos al estante (sin repetir) y lo muestra; la primera vez, junto al cursor.</summary>
    public void Add(IReadOnlyList<string> files)
    {
        foreach (var f in files)
            if (!_files.Contains(f, StringComparer.OrdinalIgnoreCase))
                _files.Add(f);
        if (!IsVisible)
            Open();
        else
            Refresh();
    }

    private void Open()
    {
        _palette = Palette.Current();
        Build();
        Refresh();
        Opacity = 0;
        if (!_placed)
        {
            Win32.GetCursorPos(out var cursor);
            var scale = Win32.ScaleAt(cursor.X, cursor.Y);
            var area = SystemParameters.WorkArea;
            Left = Math.Clamp(cursor.X / scale + 24, area.Left, area.Right - Width);
            Top = Math.Clamp(cursor.Y / scale - 80, area.Top, area.Bottom - 260);
            _placed = true;
        }
        Show();
        Recapture();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var grow = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    /// <summary>Cerrar vacía el estante; se va más rápido de lo que vino.</summary>
    public void CloseShelf()
    {
        var shrink = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) =>
        {
            Hide();
            _files.Clear();
            _glass.Background = null;
            _recapture.Stop();
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        BeginAnimation(OpacityProperty, fade);
    }

    private void Recapture()
    {
        _recapture.Stop();
        if (_hwnd == IntPtr.Zero || !IsVisible)
            return;
        var topLeft = _card.PointToScreen(new Point(0, 0));
        var bottomRight = _card.PointToScreen(new Point(_card.ActualWidth, _card.ActualHeight));
        var (w, h) = ((int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
        if (w <= 0 || h <= 0)
            return;
        // Antes de verse por primera vez está transparente: se puede capturar directo.
        var shot = Opacity < 0.01
            ? GlassCapture.Backdrop((int)topLeft.X, (int)topLeft.Y, w, h, Dark)
            : GlassCapture.BackdropBehind(_hwnd, (int)topLeft.X, (int)topLeft.Y, w, h, Dark);
        _glass.Background = shot is not null ? new ImageBrush(shot) { Stretch = Stretch.Fill } : null;
    }

    // ---------- Armado (receta de la casa: sombra → fondo → tinte → especular → outline → rim) ----------

    private void Build()
    {
        var dark = Dark;
        var outer = (Grid)Content;
        outer.Children.Clear();
        outer.Children.Add(Glass.RectShadow(Radius, ShadowMargin, dark));
        outer.Children.Add(_card);
        _card.Children.Clear();

        _card.Children.Add(_glass);
        _card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = new SolidColorBrush(Glass.Tint(dark)) });
        _card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = Glass.Specular(dark), Height = 80, VerticalAlignment = VerticalAlignment.Top });
        _card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius + 0.5), Margin = new Thickness(-0.75), BorderThickness = new Thickness(0.75), BorderBrush = Glass.Outline(dark) });
        _card.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1), BorderBrush = Glass.Rim(dark) });
        _dropRing.BorderBrush = new SolidColorBrush(_palette.Accent);
        _card.Children.Add(_dropRing);

        var text = new SolidColorBrush(Glass.Text(dark));
        var layout = new DockPanel { Background = Brushes.Transparent };

        // Encabezado: "Estante · 3" y la ✕. Es también la manija para mover.
        var header = new DockPanel { Margin = new Thickness(16, 12, 10, 8), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        var close = RoundButton("", "Cerrar y vaciar", CloseShelf);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { FontFamily = Palette.IconFont, Text = "", FontSize = 13, Foreground = new SolidColorBrush(Glass.Secondary(dark)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        title.Children.Add(new TextBlock { Text = "Estante", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = text, VerticalAlignment = VerticalAlignment.Center });
        _count.Foreground = new SolidColorBrush(Glass.Tertiary(dark));
        if (_count.Parent is Panel p)
            p.Children.Remove(_count);
        title.Children.Add(_count);
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        // Pie: arrastrar todos juntos + pista.
        var footer = new DockPanel { Margin = new Thickness(12, 6, 12, 12) };
        BuildAllChip();
        DockPanel.SetDock(_allChip, Dock.Right);
        footer.Children.Add(_allChip);
        footer.Children.Add(new TextBlock { Text = "Arrastrá para soltar · Soltá acá para sumar", FontSize = 10.5, Foreground = new SolidColorBrush(Glass.Tertiary(dark)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        if (_scroll.Parent is Panel sp)
            sp.Children.Remove(_scroll);
        layout.Children.Add(_scroll);
        _card.Children.Add(layout);
    }

    private void BuildAllChip()
    {
        var dark = Dark;
        _allChip.CornerRadius = new CornerRadius(10);
        _allChip.Padding = new Thickness(10, 3, 10, 4);
        _allChip.Background = new SolidColorBrush(Glass.Chip(dark));
        _allChip.BorderThickness = new Thickness(0.75);
        _allChip.BorderBrush = Glass.Outline(dark);
        _allChip.Cursor = Cursors.Hand;
        _allChip.ToolTip = "Arrastrá todos juntos";
        _allChip.Child = new TextBlock { FontSize = 11, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Glass.Text(dark)) };
        _allChip.PreviewMouseLeftButtonDown -= AllChipDown;
        _allChip.PreviewMouseLeftButtonDown += AllChipDown;
        _allChip.MouseMove -= AllChipMove;
        _allChip.MouseMove += AllChipMove;
    }

    private void AllChipDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        e.Handled = true;
    }

    private void AllChipMove(object sender, MouseEventArgs e) => TryDrag(_files.ToList(), e);

    private void Refresh()
    {
        var existing = _files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
        _files.Clear();
        _files.AddRange(existing);
        _list.Children.Clear();
        _rows.Clear();
        for (var i = 0; i < _files.Count; i++)
        {
            var row = Row(_files[i], i);
            _rows.Add(row);
            _list.Children.Add(row);
        }
        _selected = Math.Min(_selected, _files.Count - 1);
        _count.Text = _files.Count > 0 ? $"· {_files.Count}" : "";
        ((TextBlock)_allChip.Child).Text = $"Todos ({_files.Count})";
        _allChip.Visibility = _files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_files.Count == 0)
            _list.Children.Add(new TextBlock { Text = "Soltá archivos acá.", FontSize = 12, Foreground = new SolidColorBrush(Glass.Secondary(Dark)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 18) });
    }

    private Border Row(string path, int index)
    {
        var dark = Dark;
        var isDir = Directory.Exists(path);
        var name = Path.GetFileName(path.TrimEnd('\\'));
        var folder = Path.GetFileName(Path.GetDirectoryName(path.TrimEnd('\\')) ?? "") is { Length: > 0 } f ? f : path;

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        info.Children.Add(new TextBlock { Text = name, FontSize = 12.5, Foreground = new SolidColorBrush(Glass.Text(dark)), TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = isDir ? $"Carpeta · {folder}" : folder, FontSize = 10.5, Foreground = new SolidColorBrush(Glass.Tertiary(dark)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });

        var remove = RoundButton("", "Quitar del estante", () => { _files.Remove(path); Refresh(); });
        remove.Width = remove.Height = 20;
        remove.Opacity = 0;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(FileIcon(path, isDir));
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 2), Child = grid, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        void Paint(bool hover)
        {
            row.Background = index == _selected ? new SolidColorBrush(_palette.Hover) : hover ? new SolidColorBrush(Glass.RowHover(dark)) : Brushes.Transparent;
            remove.Opacity = hover || index == _selected ? 0.85 : 0;
        }
        Paint(false);
        row.Tag = (Action<bool>)Paint;
        row.MouseEnter += (_, _) => Paint(true);
        row.MouseLeave += (_, _) => Paint(false);
        row.PreviewMouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(this); };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true; // que no mueva la ventana
        row.MouseMove += (_, e) => TryDrag([path], e);
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragStart is null || e.Handled)
                return;
            _dragStart = null;
            if (e.ClickCount >= 1)
                Select(index);
        };
        row.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) OpenFile(path); };
        return row;
    }

    /// <summary>Miniatura para imágenes; para el resto, el ícono que muestra el Explorador.</summary>
    private FrameworkElement FileIcon(string path, bool isDir)
    {
        var dark = Dark;
        ImageSource? source = null;
        if (!isDir && Formats.KindOf(path) == FileKind.Image)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 80;
                image.EndInit();
                image.Freeze();
                source = image;
            }
            catch (Exception)
            {
                // Sin códec (WebP/AVIF/HEIC): queda el ícono.
            }
        }
        if (source is not null)
            return new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0.75), BorderBrush = Glass.Outline(dark), Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill } };

        var box = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Glass.Chip(dark)), BorderThickness = new Thickness(0.75), BorderBrush = Glass.Outline(dark) };
        if (!isDir && ShellIcon(path) is { } shellIcon)
            box.Child = new Image { Source = shellIcon, Width = 22, Height = 22 };
        else
            box.Child = new TextBlock { FontFamily = Palette.IconFont, Text = isDir ? "" : "", FontSize = 15, Foreground = new SolidColorBrush(Glass.Secondary(dark)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return box;
    }

    private static ImageSource? ShellIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Border RoundButton(string glyph, string tip, Action onClick)
    {
        var dark = Dark;
        var idle = new SolidColorBrush(Glass.Chip(dark));
        var hover = new SolidColorBrush(Glass.Text(dark)) { Opacity = 0.18 };
        var button = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = idle,
            BorderThickness = new Thickness(0.75),
            BorderBrush = Glass.Outline(dark),
            ToolTip = tip,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { FontFamily = Palette.IconFont, Text = glyph, FontSize = 9, Foreground = new SolidColorBrush(Glass.Secondary(dark)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = idle;
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return button;
    }

    // ---------- Arrastrar afuera / soltar adentro ----------

    private void TryDrag(List<string> paths, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || paths.Count == 0)
            return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;
        var data = new DataObject();
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(paths.ToArray());
        data.SetFileDropList(list);
        // Solo copiar o enlazar: soltar en el Explorador nunca mueve el original por accidente.
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Link);
    }

    private void OnDragOverShelf(DragEventArgs e, bool enter)
    {
        var ours = e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(f => !_files.Contains(f, StringComparer.OrdinalIgnoreCase));
        e.Effects = ours ? (e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.Link) : DragDropEffects.None;
        if (enter)
            ShowDropRing(ours);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        ShowDropRing(false);
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            Add(files);
    }

    private void ShowDropRing(bool on) =>
        _dropRing.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.9 : 0, TimeSpan.FromMilliseconds(on ? 90 : 140)));

    // ---------- Teclado ----------

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseShelf();
                break;
            case Key.Down when _files.Count > 0:
                Select(Math.Min(_selected + 1, _files.Count - 1));
                break;
            case Key.Up when _files.Count > 0:
                Select(Math.Max(_selected - 1, 0));
                break;
            case Key.Enter when _selected >= 0:
                OpenFile(_files[_selected]);
                break;
            case Key.Delete when _selected >= 0:
                _files.RemoveAt(_selected);
                Refresh();
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && _files.Count > 0:
                var list = new System.Collections.Specialized.StringCollection();
                list.AddRange(_selected >= 0 ? [_files[_selected]] : _files.ToArray());
                try { Clipboard.SetFileDropList(list); } catch (System.Runtime.InteropServices.COMException) { }
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Select(int index)
    {
        var previous = _selected;
        _selected = index;
        if (previous >= 0 && previous < _rows.Count)
            ((Action<bool>)_rows[previous].Tag)(false);
        if (_selected >= 0 && _selected < _rows.Count)
        {
            ((Action<bool>)_rows[_selected].Tag)(false);
            _rows[_selected].BringIntoView();
        }
        Activate();
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Core.Log.Error($"Abrir {path}", e); }
    }

    /// <summary>Para --snapshot.</summary>
    public void SaveSnapshot(string path, BitmapSource? under)
    {
        UpdateLayout();
        var (w, h) = (ActualWidth, ActualHeight);
        var bitmap = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        if (under is not null)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(under, new Rect(0, 0, w, h));
            bitmap.Render(visual);
        }
        bitmap.Render((Visual)Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Para --snapshot: fija dónde aparece.</summary>
    public void PlaceAt(double left, double top)
    {
        Left = left;
        Top = top;
        _placed = true;
    }
}
