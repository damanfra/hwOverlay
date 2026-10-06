using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using HwOverlay.ViewModels;
using HwOverlay.Views;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.SolidColorBrush;

namespace HwOverlay.Services;

/// <summary>
/// Ícones vivos: um ícone por gauge na bandeja, com o valor desenhado nele e uma barrinha de progresso.
/// <para>
/// O Windows 11 lembra quais ícones o usuário fixou pelo par (executável, id do ícone), e o NotifyIcon
/// do WinForms numera os ícones pela ordem de criação. Por isso os ícones nunca são destruídos durante
/// a execução: ficam num "pool" e só são mostrados/escondidos — assim cada posição mantém o mesmo id
/// (e a fixação) entre execuções.
/// </para>
/// </summary>
public sealed class TrayGaugeIconService : IDisposable
{
    private const int MaxTooltip = 127; // limite do Windows (NotifyIcon lança exceção acima disso)

    private readonly OverlayViewModel _vm;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly List<Slot> _pool = [];
    private bool _lightTheme = TaskbarLocator.IsTaskbarLight();
    private bool _disposed;

    public TrayGaugeIconService(OverlayViewModel vm, Forms.ContextMenuStrip menu)
    {
        _vm = vm;
        _menu = menu;
        _vm.GaugesChanged += OnGaugesChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Duplo clique num ícone de valor.</summary>
    public event EventHandler? OpenSensorsRequested;

    /// <summary>Redesenha o que mudou. Chamado na thread de UI a cada leitura.</summary>
    public void Refresh()
    {
        if (_disposed) return;

        var gauges = _vm.TrayIconsEnabled
            ? _vm.Gauges.Where(g => g.ShowInTray).ToList()
            : [];

        var palette = _lightTheme ? TaskbarPalette.Light : TaskbarPalette.Dark;
        var size = IconSize();

        for (var i = 0; i < gauges.Count; i++)
        {
            if (i == _pool.Count) _pool.Add(CreateSlot());
            _pool[i].Show(gauges[i], palette, size);
        }

        for (var i = gauges.Count; i < _pool.Count; i++)
            _pool[i].Hide();
    }

    private Slot CreateSlot()
    {
        var icon = new Forms.NotifyIcon { ContextMenuStrip = _menu };
        icon.DoubleClick += (_, _) => OpenSensorsRequested?.Invoke(this, EventArgs.Empty);
        return new Slot(icon);
    }

    private void OnGaugesChanged(object? sender, EventArgs e) => Refresh();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.TrayIconsEnabled)) Refresh();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Evento vem de outra thread; o próximo Refresh (≤ 1 ciclo) já usa o tema novo.
        _lightTheme = TaskbarLocator.IsTaskbarLight();
        foreach (var slot in _pool) slot.Invalidate();
    }

    /// <summary>Tamanho de ícone pequeno no DPI da barra (16 px a 100%, 20 a 125%, 24 a 150%...).</summary>
    private static int IconSize()
    {
        var dpi = GetDpiForWindow(TaskbarLocator.TaskbarHandle);
        if (dpi == 0) dpi = 96;
        var size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        return size > 0 ? size : 16;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _vm.GaugesChanged -= OnGaugesChanged;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        foreach (var slot in _pool) slot.Dispose();
        _pool.Clear();
    }

    // ---------- Um ícone da bandeja ----------

    private sealed class Slot(Forms.NotifyIcon icon) : IDisposable
    {
        private Icon? _current;
        private string? _key;

        public void Show(GaugeViewModel gauge, TaskbarPalette palette, int size)
        {
            var text = IconText(gauge.Value, gauge.Config.Decimals);
            var fraction = Math.Round(gauge.Fraction * size) / size; // só redesenha se a barrinha mudar de pixel
            var theme = ReferenceEquals(palette, TaskbarPalette.Light) ? "claro" : "escuro";
            var key = $"{text}|{gauge.State}|{fraction}|{size}|{theme}";

            if (key != _key)
            {
                _key = key;
                var previous = _current;
                var textColor = StateColor(gauge.State, palette);
                var barColor = gauge.State == GaugeState.Normal ? ToDrawing(palette.Accent) : textColor;
                _current = Render(text, fraction, textColor, barColor, ToDrawing(palette.Track), size);
                icon.Icon = _current;
                if (previous is not null) DestroyIcon(previous.Handle);
            }

            var tooltip = $"{gauge.Label}: {gauge.ValueText} {gauge.UnitText}".Trim();
            icon.Text = tooltip.Length > MaxTooltip ? tooltip[..MaxTooltip] : tooltip;

            if (!icon.Visible) icon.Visible = true;
        }

        public void Hide()
        {
            if (icon.Visible) icon.Visible = false;
        }

        public void Invalidate() => _key = null;

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            if (_current is not null) DestroyIcon(_current.Handle);
            _current = null;
        }
    }

    /// <summary>
    /// Texto que cabe em 16 px: inteiro; uma casa decimal só abaixo de 10 (tensões, por exemplo);
    /// milhares viram "k" (1808 MHz → "1,8k").
    /// </summary>
    private static string IconText(double? value, int decimals)
    {
        if (value is not { } v) return "–";

        var abs = Math.Abs(v);
        if (abs >= 9999.5) return SensorFormatting.FormatNumber(v / 1000, 0) + "k";
        if (abs >= 999.5) return SensorFormatting.FormatNumber(v / 1000, 1) + "k";
        if (abs < 9.95 && decimals > 0) return SensorFormatting.FormatNumber(v, 1);
        return SensorFormatting.FormatNumber(v, 0);
    }

    private static Icon Render(string text, double fraction, Color textColor, Color barColor, Color trackColor, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Sem ClearType: em fundo transparente ele deixa franjas coloridas.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var barHeight = Math.Max(2, size / 8);
            var textArea = new RectangleF(0, 0, size, size - barHeight);

            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

            // Maior fonte em que o texto cabe na largura do ícone.
            var fontSize = size * 0.8f;
            Font font;
            while (true)
            {
                font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                if (fontSize <= size * 0.4f || g.MeasureString(text, font, int.MaxValue, format).Width <= size - 0.5f) break;
                font.Dispose();
                fontSize -= 0.5f;
            }

            using (font)
            using (var brush = new SolidBrush(textColor))
                g.DrawString(text, font, brush, textArea, format);

            // Barrinha de progresso embaixo (trilho + valor).
            var barTop = size - barHeight;
            using (var track = new SolidBrush(trackColor))
                g.FillRectangle(track, 1, barTop, size - 2, barHeight);

            var filled = (float)((size - 2) * Math.Clamp(fraction, 0, 1));
            if (filled > 0)
            {
                using var bar = new SolidBrush(barColor);
                g.FillRectangle(bar, 1, barTop, filled, barHeight);
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static Color StateColor(GaugeState state, TaskbarPalette palette) => ToDrawing(state switch
    {
        GaugeState.Warning => palette.Warning,
        GaugeState.Critical => palette.Critical,
        GaugeState.NoData => palette.NoData,
        _ => palette.Text,
    });

    private static Color ToDrawing(System.Windows.Media.Brush brush)
    {
        var c = ((WpfBrush)brush).Color;
        return Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    private const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
