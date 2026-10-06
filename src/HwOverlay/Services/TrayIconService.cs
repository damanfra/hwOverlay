using System.Windows;
using Forms = System.Windows.Forms;

namespace HwOverlay.Services;

/// <summary>
/// Ícone na bandeja do sistema. É a "porta de volta" quando o overlay está travado
/// (click-through) e não recebe cliques.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleOverlay;
    private readonly Forms.ToolStripMenuItem _clickThrough;
    private readonly Forms.ToolStripMenuItem _taskbarMode;
    private bool _disposed;

    public TrayIconService()
    {
        _icon = new Forms.NotifyIcon
        {
            Text = "HwOverlay",
            Icon = LoadIcon(),
            Visible = true,
        };

        var menu = new Forms.ContextMenuStrip();

        var openSensors = new Forms.ToolStripMenuItem("Sensores e configuração...");
        openSensors.Font = new System.Drawing.Font(openSensors.Font, System.Drawing.FontStyle.Bold);
        openSensors.Click += (_, _) => OpenSensorsRequested?.Invoke(this, EventArgs.Empty);

        _toggleOverlay = new Forms.ToolStripMenuItem("Mostrar overlay") { CheckOnClick = false };
        _toggleOverlay.Click += (_, _) => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);

        _clickThrough = new Forms.ToolStripMenuItem("Travar overlay (mouse atravessa)") { CheckOnClick = false };
        _clickThrough.Click += (_, _) => ToggleClickThroughRequested?.Invoke(this, EventArgs.Empty);

        _taskbarMode = new Forms.ToolStripMenuItem("Modo barra de tarefas (ultra compacto)") { CheckOnClick = false };
        _taskbarMode.Click += (_, _) => ToggleTaskbarModeRequested?.Invoke(this, EventArgs.Empty);

        var exit = new Forms.ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(openSensors);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_toggleOverlay);
        menu.Items.Add(_clickThrough);
        menu.Items.Add(_taskbarMode);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenSensorsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenSensorsRequested;
    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? ToggleClickThroughRequested;
    public event EventHandler? ToggleTaskbarModeRequested;
    public event EventHandler? ExitRequested;

    public void SetState(bool overlayVisible, bool clickThrough, bool taskbarMode)
    {
        _toggleOverlay.Checked = overlayVisible;
        _clickThrough.Checked = clickThrough;
        _clickThrough.Enabled = !taskbarMode; // na barra a janela não flutua: travar não se aplica
        _taskbarMode.Checked = taskbarMode;
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(3000);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch
        {
            // cai no ícone padrão
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
