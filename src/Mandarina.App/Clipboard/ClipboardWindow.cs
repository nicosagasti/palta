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
using Mandarina.App.Native;
using Mandarina.App.Theme;
using Mandarina.App.Wheel;
using Mandarina.Core.Clipboard;

namespace Mandarina.App.Clips;

/// <summary>
/// Panel de vidrio con el historial, al estilo Spotlight: búsqueda grande arriba, lista limpia
/// con secciones y atajos dibujados como teclas. Enter o clic pega en la app de antes; se puede
/// arrastrar cualquier elemento afuera, fijarlo o borrarlo.
/// </summary>
internal sealed class ClipboardWindow : Window
{
    private const double PanelWidth = 420;
    private const double PanelHeight = 540;
    private const double Radius = 18;
    /// <summary>Margen transparente alrededor del panel donde se dibuja la sombra.</summary>
    private const double ShadowMargin = 28;

    private readonly ClipboardHistory _history;
    private readonly ClipboardWatcher _watcher;
    private readonly Grid _root = new();
    private readonly Image _glass = new() { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance } };
    private readonly TextBox _search = new();
    private readonly TextBlock _placeholder = new() { Text = "Buscar en el portapapeles", FontSize = 16, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Dictionary<Guid, BitmapImage> _thumbs = [];
    private readonly List<Border> _rows = [];
    private Palette _palette = Palette.Current();
    private bool Dark => _palette.Dark;
    private IReadOnlyList<ClipItem> _shown = [];
    private int _selected;
    private IntPtr _previous;
    private IntPtr _hwnd;
    private Point? _dragStart;
    private bool _dragging;

    public ClipboardWindow(ClipboardHistory history, ClipboardWatcher watcher)
    {
        _history = history;
        _watcher = watcher;
        Title = "Mandarina · Portapapeles";
        Width = PanelWidth + ShadowMargin * 2;
        Height = PanelHeight + ShadowMargin * 2;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = Palette.UiFont;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // Sin barra de desplazamiento visible (como macOS): la rueda del mouse y las flechas alcanzan.
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _list, Padding = new Thickness(8, 0, 8, 0) };
        _empty = new TextBlock { TextAlignment = TextAlignment.Center, Margin = new Thickness(32, 64, 32, 0), TextWrapping = TextWrapping.Wrap, FontSize = 13, Visibility = Visibility.Collapsed };

        _root.Margin = new Thickness(ShadowMargin);
        _root.RenderTransformOrigin = new Point(0.5, 0.3);
        _root.RenderTransform = _scale;
        var outer = new Grid();
        Content = outer;
        outer.Children.Add(_root);

        _search.TextChanged += (_, _) => { _selected = 0; Refresh(); };
        _search.TextChanged += (_, _) => _placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (IsVisible && !_dragging) Close2(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Ventana de herramienta: no aparece en Alt+Tab ni en la barra de tareas.
            Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW);
        };
        history.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
    }

    /// <summary>Dónde va el panel (sin el margen de sombra), en píxeles físicos, para un cursor en (x, y).</summary>
    public static (int Left, int Top, int W, int H, int Margin) PanelRect(int x, int y)
    {
        var scale = Win32.ScaleAt(x, y);
        var (w, h) = ((int)(PanelWidth * scale), (int)(PanelHeight * scale));
        // Que no se salga de la pantalla: a la derecha del cursor si entra, si no, a la izquierda.
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
        var left = Math.Clamp(x + 12, area.Left + 8, area.Right - w - 8);
        var top = Math.Clamp(y - h / 3, area.Top + 8, area.Bottom - h - 8);
        return (left, top, w, h, (int)(ShadowMargin * scale));
    }

    public void ShowNear(int x, int y)
    {
        _previous = Win32.GetForegroundWindow();
        _palette = Palette.Current();
        var (left, top, w, h, margin) = PanelRect(x, y);
        _glass.Source = GlassCapture.Capture(left, top, w, h) is { } shot ? Glass.Vibrant(shot, Dark) : null;

        Build();
        _search.Text = "";
        _selected = 0;
        Refresh();

        Opacity = 0;
        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, left - margin, top - margin, 0, 0, Win32.SWP_NOSIZE);
        Activate();
        _search.Focus();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var grow = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void Close2()
    {
        _glass.Source = null;
        Hide();
    }

    // ---------- Armado ----------

    private void Build()
    {
        var dark = Dark;
        _root.Children.Clear();
        if (Content is Grid outer)
        {
            if (outer.Children.Count == 2)
                outer.Children.RemoveAt(0);
            outer.Children.Insert(0, Glass.RectShadow(Radius, ShadowMargin, dark));
        }

        var shape = new RectangleGeometry(new Rect(0, 0, PanelWidth, PanelHeight), Radius, Radius);
        _glass.Width = PanelWidth;
        _glass.Height = PanelHeight;
        _glass.Clip = shape;
        _root.Children.Add(_glass);
        _root.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = new SolidColorBrush(Glass.Tint(dark)) });
        _root.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), Background = Glass.Specular(dark), Height = 180, VerticalAlignment = VerticalAlignment.Top });
        _root.Children.Add(new Border { CornerRadius = new CornerRadius(Radius + 0.5), Margin = new Thickness(-0.75), BorderThickness = new Thickness(0.75), BorderBrush = Glass.Outline(dark) });
        _root.Children.Add(new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1), BorderBrush = Glass.Rim(dark) });

        var text = new SolidColorBrush(Glass.Text(dark));
        var secondary = new SolidColorBrush(Glass.Secondary(dark));
        var hairline = new SolidColorBrush(dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0));
        var layout = new DockPanel();

        // Búsqueda: lupa y un campo grande, sin caja.
        _search.Background = Brushes.Transparent;
        _search.Foreground = text;
        _search.CaretBrush = text;
        _search.BorderThickness = new Thickness(0);
        _search.FontSize = 16;
        _search.Padding = new Thickness(0);
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.SelectionBrush = new SolidColorBrush(_palette.Accent);
        _placeholder.Foreground = new SolidColorBrush(Glass.Tertiary(dark));
        if (_search.Parent is Panel oldParent)
            oldParent.Children.Remove(_search);
        if (_placeholder.Parent is Panel oldParent2)
            oldParent2.Children.Remove(_placeholder);
        var searchRow = new DockPanel { Margin = new Thickness(20, 16, 20, 14), Height = 26 };
        var lens = new TextBlock { FontFamily = Palette.IconFont, Text = "", FontSize = 15, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 12, 0) };
        DockPanel.SetDock(lens, Dock.Left);
        searchRow.Children.Add(lens);
        searchRow.Children.Add(new Grid { Children = { _placeholder, _search } });
        var top = new StackPanel();
        top.Children.Add(searchRow);
        top.Children.Add(new Border { Height = 1, Background = hairline });
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);

        // Pie: atajos como teclas y "Vaciar".
        var footer = new DockPanel { Margin = new Thickness(18, 9, 16, 10) };
        var clear = new TextBlock { Text = "Vaciar", FontSize = 11.5, Foreground = secondary, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Borra todo menos lo fijado" };
        clear.MouseEnter += (_, _) => clear.Foreground = text;
        clear.MouseLeave += (_, _) => clear.Foreground = secondary;
        clear.MouseLeftButtonUp += (_, e) => { e.Handled = true; _history.Clear(); };
        DockPanel.SetDock(clear, Dock.Right);
        footer.Children.Add(clear);
        var hints = new StackPanel { Orientation = Orientation.Horizontal };
        hints.Children.Add(Hint("↵", "Pegar"));
        hints.Children.Add(Hint("Ctrl P", "Fijar"));
        hints.Children.Add(Hint("Supr", "Borrar"));
        hints.Children.Add(Hint("Esc", "Cerrar"));
        footer.Children.Add(hints);
        var bottom = new StackPanel();
        bottom.Children.Add(new Border { Height = 1, Background = hairline });
        bottom.Children.Add(footer);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(bottom);

        _empty.Foreground = secondary;
        if (_scroll.Parent is Panel p1)
            p1.Children.Remove(_scroll);
        if (_empty.Parent is Panel p2)
            p2.Children.Remove(_empty);
        layout.Children.Add(new Grid { Margin = new Thickness(0, 6, 0, 4), Children = { _scroll, _empty } });
        _root.Children.Add(layout);
    }

    /// <summary>Una tecla dibujada (cápsula con borde fino) y lo que hace.</summary>
    private FrameworkElement Hint(string key, string action)
    {
        var dark = Dark;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(0.75),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0)),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(5, 0, 5, 1),
            MinWidth = 18,
            Child = new TextBlock { Text = key, FontSize = 10.5, Foreground = new SolidColorBrush(Glass.Secondary(dark)), HorizontalAlignment = HorizontalAlignment.Center },
        });
        panel.Children.Add(new TextBlock { Text = action, FontSize = 11, Foreground = new SolidColorBrush(Glass.Tertiary(dark)), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private void Refresh()
    {
        _shown = _history.Search(_search.Text);
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _shown.Count - 1));
        _list.Children.Clear();
        _rows.Clear();
        var pinned = _shown.Count(i => i.Pinned);
        for (var i = 0; i < _shown.Count; i++)
        {
            if (i == 0 && pinned > 0)
                _list.Children.Add(Section("Fijados"));
            if (i == pinned && pinned > 0)
                _list.Children.Add(Section("Recientes"));
            var row = Row(_shown[i], i);
            _rows.Add(row);
            _list.Children.Add(row);
        }
        _empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = _search.Text.Length > 0 ? "Nada coincide con la búsqueda." : "Todavía no copiaste nada.\nLo que copies va a aparecer acá.";
        foreach (var key in _thumbs.Keys.Where(k => _history.Items.All(i => i.Id != k)).ToList())
            _thumbs.Remove(key);
    }

    private TextBlock Section(string title) => new()
    {
        Text = title,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Glass.Tertiary(Dark)),
        Margin = new Thickness(12, 10, 0, 4),
    };

    private Border Row(ClipItem item, int index)
    {
        var dark = Dark;
        var text = new SolidColorBrush(Glass.Text(dark));
        var secondary = new SolidColorBrush(Glass.Secondary(dark));

        FrameworkElement icon = item.Kind == ClipKind.Image && Thumb(item) is { } thumb
            ? new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(0.75),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80)),
                Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill },
            }
            : new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Glass.Chip(dark)),
                BorderThickness = new Thickness(0.75),
                BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0)),
                Child = new TextBlock
                {
                    FontFamily = Palette.IconFont,
                    Text = item.Kind == ClipKind.Files ? "" : IsLink(item) ? "" : "",
                    FontSize = 15,
                    Foreground = secondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

        var kind = item.Kind switch { ClipKind.Text => IsLink(item) ? "Enlace" : "Texto", ClipKind.Image => "Imagen", _ => item.Files?.Count == 1 ? "Archivo" : "Archivos" };
        var info = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = item.Kind == ClipKind.Image ? "Imagen copiada" : item.Preview, Foreground = text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = $"{kind} · {Ago(item.CopiedAt)}", Foreground = new SolidColorBrush(Glass.Tertiary(dark)), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });

        var pin = Glyph(item.Pinned ? "" : "", item.Pinned ? "Desfijar" : "Fijar", () => _history.Pin(item.Id, !item.Pinned));
        var del = Glyph("", "Borrar", () => _history.Remove(item.Id));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { pin, del } };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(tools, 2);
        grid.Children.Add(tools);

        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 0, 0, 1),
            Child = grid,
            Tag = (pin, del, item),
        };
        Paint(row, index == _selected, hover: false);
        row.MouseEnter += (_, _) => Paint(row, index == _selected, hover: true);
        row.MouseLeave += (_, _) => Paint(row, index == _selected, hover: false);
        row.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        row.MouseMove += (_, e) => TryDrag(item, e);
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragStart is null || e.Handled)
                return;
            _dragStart = null;
            Paste(item);
        };
        return row;
    }

    /// <summary>Elegida: píldora suave con el acento. Bajo el mouse: apenas un velo. Íconos de fijar/borrar solo ahí.</summary>
    private void Paint(Border row, bool selected, bool hover)
    {
        var (pin, del, item) = ((TextBlock, TextBlock, ClipItem))row.Tag;
        var dark = Dark;
        row.Background = selected
            ? new SolidColorBrush(Palette.WithAlpha(_palette.Accent, (byte)(dark ? 0x52 : 0x30)))
            : hover ? new SolidColorBrush(dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0C, 0, 0, 0)) : Brushes.Transparent;
        var showTools = selected || hover;
        del.Opacity = showTools ? 0.75 : 0;
        pin.Opacity = showTools ? 0.75 : item.Pinned ? 0.45 : 0;
    }

    private static bool IsLink(ClipItem item) =>
        item.Kind == ClipKind.Text && Uri.TryCreate(item.Text?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private TextBlock Glyph(string glyph, string tip, Action onClick)
    {
        var t = new TextBlock { FontFamily = Palette.IconFont, Text = glyph, FontSize = 12, Foreground = new SolidColorBrush(Glass.Text(Dark)), Margin = new Thickness(4, 0, 0, 0), ToolTip = tip, Cursor = Cursors.Hand, Padding = new Thickness(5), Background = Brushes.Transparent };
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; _dragStart = null; onClick(); };
        return t;
    }

    private BitmapImage? Thumb(ClipItem item)
    {
        if (_thumbs.TryGetValue(item.Id, out var cached))
            return cached;
        try
        {
            return _thumbs[item.Id] = ClipboardWatcher.LoadImage(item.ImagePath!, 96);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "recién"
            : d.TotalHours < 1 ? $"hace {(int)d.TotalMinutes} min"
            : d.TotalDays < 1 ? $"hace {(int)d.TotalHours} h"
            : d.TotalDays < 2 ? "ayer"
            : t.ToString("d MMM");
    }

    // ---------- Acciones ----------

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close2();
                break;
            case Key.Down when _shown.Count > 0:
                Select(_selected + 1);
                break;
            case Key.Up when _shown.Count > 0:
                Select(_selected - 1);
                break;
            case Key.Enter when _shown.Count > 0:
                Paste(_shown[_selected]);
                break;
            case Key.Delete when _shown.Count > 0 && _search.Text.Length == 0:
                _history.Remove(_shown[_selected].Id);
                break;
            case Key.P when Keyboard.Modifiers == ModifierKeys.Control && _shown.Count > 0:
                _history.Pin(_shown[_selected].Id, !_shown[_selected].Pinned);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Select(int index)
    {
        var previous = _selected;
        _selected = Math.Clamp(index, 0, _shown.Count - 1);
        if (previous < _rows.Count)
            Paint(_rows[previous], false, false);
        if (_selected < _rows.Count)
        {
            Paint(_rows[_selected], true, false);
            _rows[_selected].BringIntoView();
        }
    }

    /// <summary>Lo pone en el portapapeles, vuelve a la app anterior y simula Ctrl+V.</summary>
    private void Paste(ClipItem item)
    {
        try
        {
            _watcher.Put(item);
        }
        catch (Exception e)
        {
            Core.Log.Error("Poner en el portapapeles", e);
            return;
        }
        _history.Touch(item.Id);
        Close2();
        if (_previous == IntPtr.Zero)
            return;
        Win32.SetForegroundWindow(_previous);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Normal, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            Win32.SendPaste();
        }, Dispatcher);
        timer.Start();
    }

    private void TryDrag(ClipItem item, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;
        var data = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Text:
                data.SetText(item.Text ?? "");
                break;
            case ClipKind.Files:
                data.SetFileDropList(ToCollection(item.Files!));
                break;
            case ClipKind.Image:
                // Con nombre legible: así queda el archivo al soltarlo en una carpeta.
                var named = Path.Combine(Path.GetTempPath(), "Mandarina", $"imagen {item.CopiedAt:yyyy-MM-dd HH.mm.ss}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(named)!);
                File.Copy(item.ImagePath!, named, overwrite: true);
                data.SetFileDropList(ToCollection([named]));
                data.SetImage(ClipboardWatcher.LoadImage(item.ImagePath!, 0));
                break;
        }
        _dragging = true;
        // Se oculta para no tapar el lugar donde lo vas a soltar.
        Close2();
        try
        {
            DragDrop.DoDragDrop(_list, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragging = false;
        }
    }

    private static System.Collections.Specialized.StringCollection ToCollection(IEnumerable<string> paths)
    {
        var c = new System.Collections.Specialized.StringCollection();
        c.AddRange(paths.ToArray());
        return c;
    }

    /// <summary>Para --snapshot: guarda el panel tal como se ve (con el fondo real debajo) en PNG, a 2×.</summary>
    public void SaveSnapshot(string path, BitmapSource? under)
    {
        var (w, h) = (Width, Height);
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
}
