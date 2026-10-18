using Forms = System.Windows.Forms;

namespace Mandarina.App.Shell;

/// <summary>Ícono en la bandeja con su menú.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pause;

    public Tray(Action openSettings, Action<bool> setPaused, Action exit)
    {
        _pause = new Forms.ToolStripMenuItem("Pausar");
        _pause.Click += (_, _) =>
        {
            _pause.Checked = !_pause.Checked;
            _icon!.Text = _pause.Checked ? "Mandarina (en pausa)" : "Mandarina";
            setPaused(_pause.Checked);
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Configuración…", null, (_, _) => openSettings());
        menu.Items.Add(_pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "Mandarina",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    public void ShowTip(string title, string text) => _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
