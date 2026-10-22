using Palta.App.Theme;
using Forms = System.Windows.Forms;

namespace Palta.App.Shell;

/// <summary>Ícono en la bandeja con su menú.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;
    private readonly Dictionary<AppTheme, Forms.ToolStripMenuItem> _themes = [];

    public Tray(Action openSettings, Action openClipboard, Action<bool> setPaused, Action exit, AppTheme theme, Action<AppTheme> setTheme)
    {
        _pause = new Forms.ToolStripMenuItem("Pausar");
        _pause.Click += (_, _) =>
        {
            _pause.Checked = !_pause.Checked;
            _icon!.Text = _pause.Checked ? "Palta (en pausa)" : "Palta";
            setPaused(_pause.Checked);
        };

        // Atajo para cambiar el tema sin abrir Configuración.
        var themeMenu = new Forms.ToolStripMenuItem("Tema");
        foreach (var t in new[] { AppTheme.Windows, AppTheme.Glass })
        {
            var item = new Forms.ToolStripMenuItem(Look.Name(t));
            item.Click += (_, _) => setTheme(t);
            _themes[t] = item;
            themeMenu.DropDownItems.Add(item);
        }
        SetTheme(theme);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Portapapeles…", null, (_, _) => openClipboard());
        menu.Items.Add("Configuración…", null, (_, _) => openSettings());
        menu.Items.Add(themeMenu);
        menu.Items.Add(_pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Palta",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    /// <summary>Marca el tema activo en el menú.</summary>
    public void SetTheme(AppTheme theme)
    {
        foreach (var (t, item) in _themes)
            item.Checked = t == theme;
    }

    public void ShowTip(string title, string text) => _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
