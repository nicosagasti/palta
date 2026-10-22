using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Palta.App.Shell;
using Palta.App.Theme;
using Palta.Core.Clipboard;
using AppSettings = Palta.Core.Settings;

namespace Palta.App.Settings;

/// <summary>Configuración: qué tecla abre cada rueda, el atajo sin arrastre y el arranque con Windows.</summary>
internal sealed class SettingsWindow : Window
{
    private static readonly string[] Keys = ["Shift", "Ctrl", "Alt"];

    private readonly ComboBox _convert = KeyBox();
    private readonly ComboBox _tools = KeyBox();
    private readonly ComboBox _actions = KeyBox();
    private readonly TextBox _hotkey = new() { IsReadOnly = true, IsReadOnlyCaretVisible = false, MinWidth = 180 };
    private readonly TextBox _clipHotkey = new() { IsReadOnly = true, IsReadOnlyCaretVisible = false, MinWidth = 180 };
    private readonly CheckBox _clipEnabled = new() { Content = "Guardar historial del portapapeles" };
    private readonly ComboBox _theme = new() { MinWidth = 180 };
    private readonly ComboBox _clipDays = new() { MinWidth = 180 };
    private readonly ComboBox _clipItems = new() { MinWidth = 180 };
    private readonly ComboBox _clipSpace = new() { MinWidth = 180 };
    private readonly TextBlock _clipUsage = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ClipboardHistory? _history;
    private readonly CheckBox _startup = new() { Content = "Arrancar con Windows" };
    private readonly CheckBox _shortcut = new() { Content = "Acceso directo en el escritorio", Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    /// <summary>Devuelve null si todo bien, o un mensaje de error (p. ej. atajo ocupado).</summary>
    public Func<AppSettings, string?>? Saving { get; set; }

    public SettingsWindow(AppSettings current, string? hotkeyProblem, ClipboardHistory? history = null)
    {
        _history = history;
        Title = "Palta · Configuración";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Palette.UiFont;
        ThemeMode = ThemeMode.System;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/palta.png"));

        _convert.SelectedItem = current.ConvertKey;
        _tools.SelectedItem = current.ToolsKey;
        _actions.SelectedItem = current.ActionsKey;
        _hotkey.Text = current.Hotkey;
        _startup.IsChecked = current.StartWithWindows;
        _shortcut.IsChecked = DesktopShortcut.Exists;
        _hotkey.PreviewKeyDown += CaptureHotkey;
        _clipHotkey.Text = current.ClipboardHotkey;
        _clipHotkey.PreviewKeyDown += CaptureHotkey;
        _clipEnabled.IsChecked = current.ClipboardEnabled;
        Fill(_clipDays, current.ClipboardKeepDays, [(1, "1 día"), (7, "7 días"), (30, "30 días"), (90, "90 días"), (365, "1 año"), (0, "Nunca")], n => $"{n} días");
        Fill(_clipItems, current.ClipboardMaxItems, [(50, "50 elementos"), (200, "200 elementos"), (500, "500 elementos"), (1000, "1000 elementos"), (0, "Sin límite")], n => $"{n} elementos");
        Fill(_clipSpace, current.ClipboardMaxImageMegabytes, [(100, "100 MB"), (300, "300 MB"), (1024, "1 GB"), (5120, "5 GB"), (0, "Sin límite")], n => $"{n} MB");
        UpdateUsage();
        Fill(_theme, (int)Look.Parse(current.Theme), [((int)AppTheme.Windows, Look.Name(AppTheme.Windows)), ((int)AppTheme.Glass, Look.Name(AppTheme.Glass))], _ => Look.Name(AppTheme.Windows));
        // Si se cambia desde la bandeja con esta ventana abierta, que no lo pise al guardar.
        void OnLook() => Dispatcher.BeginInvoke(() => _theme.SelectedItem = ((IEnumerable<Choice>)_theme.ItemsSource).First(c => c.Value == (int)Look.Current));
        Look.Changed += OnLook;
        Closed += (_, _) => Look.Changed -= OnLook;
        if (hotkeyProblem is not null)
            _error.Text = hotkeyProblem;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Row("Tema", _theme));
        panel.Children.Add(new TextBlock { Text = "Windows: oscuro, opaco y con las notificaciones del sistema. Liquid glass: vidrio translúcido y avisos propios. También se cambia desde el ícono de la bandeja.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16), Opacity = 0.7 });
        panel.Children.Add(new TextBlock { Text = "Mientras arrastrás un archivo, tocá una tecla para abrir una rueda.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Opacity = 0.8 });
        panel.Children.Add(Row("Convertir", _convert));
        panel.Children.Add(Row("Herramientas", _tools));
        panel.Children.Add(Row("Acciones", _actions));
        panel.Children.Add(new TextBlock { Text = "Sin arrastrar: seleccioná archivos en el Explorador (o copialos) y apretá el atajo.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8), Opacity = 0.8 });
        panel.Children.Add(Row("Atajo", _hotkey));
        panel.Children.Add(new TextBlock { Text = "Historial de lo que copiaste: textos, imágenes y archivos.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8), Opacity = 0.8 });
        panel.Children.Add(Row("Portapapeles", _clipHotkey));
        panel.Children.Add(_clipEnabled);
        panel.Children.Add(Row("Borrar después de", _clipDays));
        panel.Children.Add(Row("Guardar hasta", _clipItems));
        panel.Children.Add(Row("Espacio de imágenes", _clipSpace));
        panel.Children.Add(_clipUsage);
        if (_history is not null)
        {
            var clear = new Button { Content = "Borrar historial…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            clear.Click += (_, _) =>
            {
                var answer = MessageBox.Show(this, "Se borra todo lo que copiaste, salvo lo fijado. No se puede deshacer.", "Borrar historial", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
                if (answer != MessageBoxResult.OK)
                    return;
                _history.Clear();
                UpdateUsage();
            };
            panel.Children.Add(clear);
        }
        panel.Children.Add(new Border { Height = 12 });
        panel.Children.Add(_startup);
        panel.Children.Add(_shortcut);
        panel.Children.Add(_error);

        var save = new Button { Content = "Guardar", IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static ComboBox KeyBox() => new() { ItemsSource = Keys, MinWidth = 180 };

    private sealed record Choice(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Opciones fijas; si el settings.json trae otro número (editado a mano), se suma tal cual para no perderlo.</summary>
    private static void Fill(ComboBox box, int current, (int Value, string Label)[] options, Func<int, string> custom)
    {
        var items = options.Select(o => new Choice(o.Value, o.Label)).ToList();
        current = Math.Max(0, current);
        if (items.All(c => c.Value != current))
            items.Insert(items.Count - 1, new Choice(current, custom(current)));
        box.ItemsSource = items;
        box.SelectedItem = items.First(c => c.Value == current);
    }

    private static int ValueOf(ComboBox box) => (box.SelectedItem as Choice)?.Value ?? 0;

    private void UpdateUsage()
    {
        if (_history is null)
            return;
        var count = _history.Items.Count;
        var mb = _history.ImageBytes / (1024.0 * 1024.0);
        var space = mb < 1 ? "menos de 1 MB" : $"{mb:0} MB";
        _clipUsage.Text = $"Ahora hay {count} {(count == 1 ? "elemento" : "elementos")} y las imágenes ocupan {space}. Lo fijado no se borra nunca.";
    }

    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
            return;
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0)
        {
            _error.Text = "El atajo necesita al menos Ctrl, Alt, Shift o Win.";
            return;
        }
        parts.Add(key.ToString());
        ((TextBox)sender).Text = string.Join("+", parts);
        _error.Text = "";
    }

    private void Save()
    {
        var chosen = new[] { _convert.SelectedItem as string, _tools.SelectedItem as string, _actions.SelectedItem as string };
        if (chosen.Distinct().Count() != 3)
        {
            _error.Text = "Cada rueda necesita una tecla distinta.";
            return;
        }
        if (!HotkeyManager.TryParse(_clipHotkey.Text, out _, out _) || _clipHotkey.Text == _hotkey.Text)
        {
            _error.Text = "El atajo del portapapeles no es válido o es igual al de la rueda.";
            return;
        }
        if (!HotkeyManager.TryParse(_hotkey.Text, out _, out _))
        {
            _error.Text = "El atajo no es válido.";
            return;
        }
        var settings = new AppSettings
        {
            ConvertKey = chosen[0]!,
            ToolsKey = chosen[1]!,
            ActionsKey = chosen[2]!,
            Hotkey = _hotkey.Text,
            StartWithWindows = _startup.IsChecked == true,
            DesktopShortcut = _shortcut.IsChecked == true,
            ClipboardHotkey = _clipHotkey.Text,
            ClipboardEnabled = _clipEnabled.IsChecked == true,
            Theme = Look.ToSetting((AppTheme)ValueOf(_theme)),
            ClipboardKeepDays = ValueOf(_clipDays),
            ClipboardMaxItems = ValueOf(_clipItems),
            ClipboardMaxImageMegabytes = ValueOf(_clipSpace),
        };
        if (Saving?.Invoke(settings) is { } problem)
        {
            _error.Text = problem;
            return;
        }
        Close();
    }
}
