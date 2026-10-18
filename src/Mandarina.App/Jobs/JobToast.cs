using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Mandarina.App.Native;
using Mandarina.App.Shell;
using Mandarina.App.Theme;
using Mandarina.Core;

namespace Mandarina.App.Jobs;

/// <summary>Tarjeta de un trabajo en la esquina: progreso, abrir, mostrar en carpeta, cancelar.</summary>
internal sealed class JobToast : Window
{
    public const double CardWidth = 340;
    public const double CardHeight = 84;

    private readonly Job _job;
    private readonly Palette _palette = Palette.Current();
    private readonly TextBlock _status;
    private readonly TextBlock _icon;
    private readonly ProgressBar _bar;
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly DispatcherTimer _autoClose;
    private bool _closing;

    public event Action<JobToast>? Gone;

    public JobToast(Job job)
    {
        _job = job;
        Width = CardWidth;
        Height = CardHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        FontFamily = Palette.UiFont;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });

        var text = new SolidColorBrush(_palette.Text);
        var sub = new SolidColorBrush(_palette.SubText);
        _icon = new TextBlock { FontFamily = Palette.IconFont, FontSize = 18, Foreground = new SolidColorBrush(_palette.Accent), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0), Text = "" };
        var title = new TextBlock { Text = job.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis };
        _status = new TextBlock { Text = "En cola…", FontSize = 12, Foreground = sub, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        _bar = new ProgressBar { Height = 3, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(_palette.Accent), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), BorderThickness = new Thickness(0), IsIndeterminate = true };

        var info = new StackPanel();
        info.Children.Add(title);
        info.Children.Add(_status);
        info.Children.Add(_bar);

        var grid = new Grid { Margin = new Thickness(16, 12, 12, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(_icon);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(_buttons, 1);
        Grid.SetRow(_buttons, 1);
        grid.Children.Add(_buttons);
        Content = grid;

        AddButton("Cancelar", () => _job.Cancel());

        _autoClose = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal, (_, _) => FadeOut(), Dispatcher) { IsEnabled = false };
        MouseEnter += (_, _) => _autoClose.Stop();
        MouseLeave += (_, _) => { if (_job.State == JobState.Done) _autoClose.Start(); };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
            Backdrop.Apply(this, Win32.DWMSBT_TRANSIENTWINDOW, _palette.Dark);
        };

        job.Changed += j => Dispatcher.BeginInvoke(() => Refresh(j));
        Refresh(job);
    }

    private void Refresh(Job job)
    {
        switch (job.State)
        {
            case JobState.Queued:
                _status.Text = "En cola…";
                break;
            case JobState.Running:
                _bar.IsIndeterminate = job.Progress <= 0;
                _bar.Value = job.Progress;
                _status.Text = job.Progress > 0 ? $"{job.Progress:P0}" : "Trabajando…";
                break;
            case JobState.Done:
                _bar.IsIndeterminate = false;
                _bar.Value = 1;
                _icon.Text = "";
                _status.Text = job.Outputs.Count switch
                {
                    0 => job.Operation.Id == "copy-path" ? "Ruta copiada" : "Listo",
                    1 => System.IO.Path.GetFileName(job.Outputs[0]),
                    var n => $"{n} archivos",
                };
                _buttons.Children.Clear();
                if (job.Outputs.Count > 0)
                {
                    AddButton("Abrir", () => Open(job.Outputs.Count == 1 ? job.Outputs[0] : System.IO.Path.GetDirectoryName(job.Outputs[0])!));
                    AddButton("Mostrar en carpeta", () => Reveal(job.Outputs[0]));
                }
                if (!IsMouseOver)
                    _autoClose.Start();
                break;
            case JobState.Failed:
                _bar.Visibility = Visibility.Collapsed;
                _icon.Text = "";
                _icon.Foreground = new SolidColorBrush(_palette.Danger);
                _status.Text = job.Error ?? "Error";
                _status.ToolTip = job.Error;
                _buttons.Children.Clear();
                AddButton("Cerrar", FadeOut);
                break;
            case JobState.Canceled:
                _status.Text = "Cancelado";
                _bar.Visibility = Visibility.Collapsed;
                _buttons.Children.Clear();
                _autoClose.Interval = TimeSpan.FromSeconds(1.5);
                _autoClose.Start();
                break;
        }
    }

    private void AddButton(string label, Action onClick)
    {
        var text = new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(_palette.Accent), Margin = new Thickness(12, 4, 0, 0), Cursor = Cursors.Hand };
        text.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        text.MouseEnter += (_, _) => text.TextDecorations = TextDecorations.Underline;
        text.MouseLeave += (_, _) => text.TextDecorations = null;
        _buttons.Children.Add(text);
    }

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

    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    public void MoveTo(double top) =>
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    public void FadeOut()
    {
        if (_closing)
            return;
        _closing = true;
        _autoClose.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            Close();
            Gone?.Invoke(this);
        };
        BeginAnimation(OpacityProperty, fade);
    }
}

/// <summary>Apila las tarjetas abajo a la derecha del área de trabajo.</summary>
internal sealed class ToastStack
{
    private const double Margin = 12;
    private readonly List<JobToast> _toasts = [];

    public void Add(Job job)
    {
        var toast = new JobToast(job);
        toast.Gone += t =>
        {
            _toasts.Remove(t);
            Relayout();
        };
        _toasts.Insert(0, toast);
        var area = SystemParameters.WorkArea;
        toast.ShowAt(area.Right - JobToast.CardWidth - Margin, area.Bottom - JobToast.CardHeight - Margin);
        Relayout();
        // Más de 5 visibles es ruido: las más viejas terminadas se van.
        foreach (var old in _toasts.Skip(5).ToList())
            old.FadeOut();
    }

    private void Relayout()
    {
        var area = SystemParameters.WorkArea;
        for (var i = 0; i < _toasts.Count; i++)
            _toasts[i].MoveTo(area.Bottom - (JobToast.CardHeight + Margin) * (i + 1));
    }
}
