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
/// Panel de vidrio con el historial: Enter o clic pega en la app de antes, se puede arrastrar
/// cualquier elemento afuera, fijarlo o borrarlo. Escribir filtra.
/// </summary>
internal sealed class ClipboardWindow : Window
{
    private const double PanelWidth = 380;
    private const double PanelHeight = 520;
    private const double Radius = 18;

    private readonly ClipboardHistory _history;
    private readonly ClipboardWatcher _watcher;
    private readonly Grid _root = new();
    private readonly Image _glass = new() { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Performance } };
    private readonly TextBox _search = new();
    private readonly TextBlock _placeholder = new() { Text = "Buscar…", Margin = new Thickness(12, 7, 0, 0), FontSize = 13, IsHitTestVisible = false };
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Dictionary<Guid, BitmapImage> _thumbs = [];
    private Palette _palette = Palette.Current();
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
        Width = PanelWidth;
        Height = PanelHeight;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = Palette.UiFont;
        UseLayoutRounding = true;

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Margin = new Thickness(8, 0, 8, 0) };
        _empty = new TextBlock { TextAlignment = TextAlignment.Center, Margin = new Thickness(24, 48, 24, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        _root.RenderTransformOrigin = new Point(0.5, 0.5);
        _root.RenderTransform = _scale;
        Content = _root;

        _search.TextChanged += (_, _) => { _selected = 0; Refresh(); };
        _search.TextChanged += (_, _) => _placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (IsVisible && !_dragging) Close2(); };
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        history.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
    }

    public void ShowNear(int x, int y)
    {
        _previous = Win32.GetForegroundWindow();
        _palette = Palette.Current();
        var scale = Win32.ScaleAt(x, y);
        var (w, h) = ((int)(PanelWidth * scale), (int)(PanelHeight * scale));
        // Que no se salga de la pantalla: a la derecha y abajo del cursor si entra, si no, al revés.
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
        var left = Math.Clamp(x + 12, area.Left + 8, area.Right - w - 8);
        var top = Math.Clamp(y - h / 3, area.Top + 8, area.Bottom - h - 8);
        _glass.Source = GlassCapture.Capture(left, top, w, h);

        Build();
        _search.Text = "";
        _selected = 0;
        Refresh();

        Opacity = 0;
        Show();
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, left, top, 0, 0, Win32.SWP_NOSIZE);
        Activate();
        _search.Focus();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
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
        var p = _palette;
        _root.Children.Clear();
        var shape = new RectangleGeometry(new Rect(0, 0, PanelWidth, PanelHeight), Radius, Radius);
        _glass.Width = PanelWidth;
        _glass.Height = PanelHeight;
        _glass.Clip = shape;
        _root.Children.Add(_glass);
        _root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Radius),
            Background = new LinearGradientBrush(
                [new GradientStop(Palette.WithAlpha(p.Tint, (byte)Math.Min(255, p.Tint.A + 0x30)), 0), new GradientStop(Palette.WithAlpha(p.Tint, (byte)Math.Min(255, p.Tint.A + 0x50)), 1)],
                new Point(0, 0), new Point(0, 1)),
            BorderThickness = new Thickness(1.2),
            BorderBrush = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 0.5), new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 1)],
                new Point(0, 0), new Point(1, 1)),
        });

        var text = new SolidColorBrush(p.Text);
        var sub = new SolidColorBrush(p.SubText);
        var layout = new DockPanel { Margin = new Thickness(0, 14, 0, 10) };

        var header = new DockPanel { Margin = new Thickness(18, 0, 16, 10) };
        var clear = new TextBlock { Text = "Vaciar", FontSize = 12, Foreground = new SolidColorBrush(p.Accent), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Borra todo menos lo fijado" };
        clear.MouseLeftButtonUp += (_, e) => { e.Handled = true; _history.Clear(); };
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(new TextBlock { Text = "Portapapeles", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = text });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        _search.Background = new SolidColorBrush(Color.FromArgb((byte)(p.Dark ? 0x22 : 0x55), 0xFF, 0xFF, 0xFF));
        _search.Foreground = text;
        _search.CaretBrush = text;
        _search.BorderThickness = new Thickness(0);
        _search.Padding = new Thickness(10, 7, 10, 7);
        _search.FontSize = 13;
        var searchBox = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(16, 0, 16, 10), Child = new Grid { Children = { _search, Placeholder(sub) } } };
        DockPanel.SetDock(searchBox, Dock.Top);
        layout.Children.Add(searchBox);

        var hint = new TextBlock { Text = "Enter pega · arrastrá para soltar · Supr borra · Ctrl+P fija", FontSize = 10.5, Foreground = sub, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(hint, Dock.Bottom);
        layout.Children.Add(hint);

        _empty.Foreground = sub;
        var body = new Grid { Children = { _scroll, _empty } };
        layout.Children.Add(body);
        _root.Children.Add(layout);
    }

    private TextBlock Placeholder(Brush sub)
    {
        _placeholder.Foreground = sub;
        return _placeholder;
    }

    private void Refresh()
    {
        _shown = _history.Search(_search.Text);
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _shown.Count - 1));
        _list.Children.Clear();
        for (var i = 0; i < _shown.Count; i++)
            _list.Children.Add(Row(_shown[i], i));
        _empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = _search.Text.Length > 0 ? "Nada coincide con la búsqueda." : "Todavía no copiaste nada.\nLo que copies va a aparecer acá.";
        foreach (var key in _thumbs.Keys.Where(k => _history.Items.All(i => i.Id != k)).ToList())
            _thumbs.Remove(key);
    }

    private FrameworkElement Row(ClipItem item, int index)
    {
        var p = _palette;
        var selected = index == _selected;
        var text = new SolidColorBrush(p.Text);
        var sub = new SolidColorBrush(p.SubText);

        FrameworkElement icon = item.Kind == ClipKind.Image && Thumb(item) is { } thumb
            ? new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill } }
            : new TextBlock
            {
                FontFamily = Palette.IconFont,
                Text = item.Kind == ClipKind.Files ? "" : "",
                FontSize = 18,
                Width = 44,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = sub,
            };

        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = item.Preview, Foreground = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = Ago(item.CopiedAt), Foreground = sub, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) });

        var pin = Glyph(item.Pinned ? "" : "", item.Pinned ? "Desfijar" : "Fijar", () => _history.Pin(item.Id, !item.Pinned));
        pin.Opacity = item.Pinned ? 0.9 : 0;
        var del = Glyph("", "Borrar", () => _history.Remove(item.Id));
        del.Opacity = 0;
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
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(selected ? p.Hover : Color.FromArgb((byte)(p.Dark ? 0x0C : 0x26), 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(selected ? 1 : 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            Child = grid,
            Cursor = Cursors.Hand,
            Tag = index,
        };
        row.MouseEnter += (_, _) => { del.Opacity = 0.8; pin.Opacity = 0.8; };
        row.MouseLeave += (_, _) => { del.Opacity = 0; pin.Opacity = item.Pinned ? 0.9 : 0; };
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

    private TextBlock Glyph(string glyph, string tip, Action onClick)
    {
        var t = new TextBlock { FontFamily = Palette.IconFont, Text = glyph, FontSize = 13, Foreground = new SolidColorBrush(_palette.Text), Margin = new Thickness(8, 0, 2, 0), ToolTip = tip, Cursor = Cursors.Hand, Padding = new Thickness(4) };
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
        _selected = Math.Clamp(index, 0, _shown.Count - 1);
        Refresh();
        if (_list.Children[_selected] is FrameworkElement row)
            row.BringIntoView();
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
}
