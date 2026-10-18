using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mandarina.App.Shell;
using Mandarina.App.Theme;
using AppSettings = Mandarina.Core.Settings;

namespace Mandarina.App.Settings;

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
    private readonly CheckBox _startup = new() { Content = "Arrancar con Windows" };
    private readonly CheckBox _shortcut = new() { Content = "Acceso directo en el escritorio", Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    /// <summary>Devuelve null si todo bien, o un mensaje de error (p. ej. atajo ocupado).</summary>
    public Func<AppSettings, string?>? Saving { get; set; }

    public SettingsWindow(AppSettings current, string? hotkeyProblem)
    {
        Title = "Mandarina · Configuración";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Palette.UiFont;
        ThemeMode = ThemeMode.System;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/mandarina.png"));

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
        if (hotkeyProblem is not null)
            _error.Text = hotkeyProblem;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Mientras arrastrás un archivo, tocá una tecla para abrir una rueda.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Opacity = 0.8 });
        panel.Children.Add(Row("Convertir", _convert));
        panel.Children.Add(Row("Herramientas", _tools));
        panel.Children.Add(Row("Acciones", _actions));
        panel.Children.Add(new TextBlock { Text = "Sin arrastrar: seleccioná archivos en el Explorador (o copialos) y apretá el atajo.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8), Opacity = 0.8 });
        panel.Children.Add(Row("Atajo", _hotkey));
        panel.Children.Add(new TextBlock { Text = "Historial de lo que copiaste: textos, imágenes y archivos.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8), Opacity = 0.8 });
        panel.Children.Add(Row("Portapapeles", _clipHotkey));
        panel.Children.Add(_clipEnabled);
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
        };
        if (Saving?.Invoke(settings) is { } problem)
        {
            _error.Text = problem;
            return;
        }
        Close();
    }
}
