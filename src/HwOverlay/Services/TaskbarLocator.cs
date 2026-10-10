using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HwOverlay.Services;

/// <summary>Retângulo em pixels físicos de tela (o app é PerMonitorV2).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <param name="Bounds">Barra de tarefas.</param>
/// <param name="Tray">Área da bandeja (ícones + relógio) ou, nas barras secundárias, só do relógio; vazio se não encontrada.</param>
/// <param name="IsHorizontal">Falso se a barra estiver na lateral (Windows 10).</param>
/// <param name="IsHidden">Barra com ocultação automática recolhida no momento.</param>
public sealed record TaskbarInfo(PixelRect Bounds, PixelRect Tray, bool IsHorizontal, bool IsHidden);

/// <summary>Monitor que tem barra de tarefas. <paramref name="Device"/> é o nome do Windows (ex.: \\.\DISPLAY2).</summary>
public sealed record TaskbarMonitor(string Device, int Number, int Width, int Height, bool IsPrimary);

/// <summary>
/// Localiza as barras de tarefas via Win32. O Windows 11 não tem API para colocar
/// conteúdo dentro da barra, então o modo "barra de tarefas" é uma janela sobreposta a ela.
/// A barra do monitor principal é Shell_TrayWnd; cada monitor extra (com "Mostrar a barra de tarefas
/// em todos os monitores" ligado) tem uma Shell_SecondaryTrayWnd.
/// </summary>
public static class TaskbarLocator
{
    /// <summary>Monitores que têm barra de tarefas no momento (o principal primeiro, depois por número).</summary>
    public static IReadOnlyList<TaskbarMonitor> ListMonitors()
    {
        var list = new List<TaskbarMonitor>();
        foreach (var taskbar in EnumerateTaskbars())
        {
            if (GetMonitor(taskbar) is not { } m || list.Any(x => x.Device == m.Device)) continue;
            list.Add(new TaskbarMonitor(m.Device, DisplayNumber(m.Device), m.Bounds.Width, m.Bounds.Height, m.IsPrimary));
        }

        return list.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Number).ToList();
    }

    /// <summary>
    /// Barra do monitor pedido (<paramref name="device"/>, ex.: \\.\DISPLAY2). Vazio, ou monitor sem barra
    /// (desconectado, barra só no principal), usa a barra do monitor principal.
    /// </summary>
    public static TaskbarInfo? Find(string? device = null)
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        var taskbar = primary;

        if (!string.IsNullOrEmpty(device))
        {
            foreach (var candidate in EnumerateTaskbars())
            {
                if (GetMonitor(candidate) is { } m && string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase))
                {
                    taskbar = candidate;
                    break;
                }
            }
        }

        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var r)) return null;

        var bounds = new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        if (bounds.IsEmpty) return null;

        // Principal: área de notificação inteira. Secundária: não tem bandeja, só o relógio (quando o Windows mostra).
        var tray = default(PixelRect);
        var trayHwnd = taskbar == primary
            ? FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null)
            : FindWindowEx(taskbar, IntPtr.Zero, "ClockButton", null);
        if (trayHwnd != IntPtr.Zero && GetWindowRect(trayHwnd, out var t))
            tray = new PixelRect(t.Left, t.Top, t.Right, t.Bottom);

        // Win11 desenha o relógio da barra secundária em XAML, sem janela própria: reserva uma área estimada
        // (relógio + data ≈ 2,5 × a altura da barra) para os mini-gauges não ficarem por cima dele.
        if (tray.IsEmpty && taskbar != primary && bounds.Width >= bounds.Height)
            tray = new PixelRect(bounds.Right - (int)(bounds.Height * 2.5), bounds.Top, bounds.Right, bounds.Bottom);

        // Ocultação automática: recolhida, a barra fica quase toda fora do monitor.
        var hidden = false;
        if (GetMonitor(taskbar) is { } monitor)
        {
            var m = monitor.Bounds;
            var visibleW = Math.Min(bounds.Right, m.Right) - Math.Max(bounds.Left, m.Left);
            var visibleH = Math.Min(bounds.Bottom, m.Bottom) - Math.Max(bounds.Top, m.Top);
            hidden = visibleW < bounds.Width / 2 || visibleH < bounds.Height / 2;
        }

        return new TaskbarInfo(bounds, tray, bounds.Width >= bounds.Height, hidden);
    }

    private static IEnumerable<IntPtr> EnumerateTaskbars()
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return secondary;
    }

    private static (string Device, PixelRect Bounds, bool IsPrimary)? GetMonitor(IntPtr window)
    {
        var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return null;

        var m = info.rcMonitor;
        return (info.szDevice, new PixelRect(m.Left, m.Top, m.Right, m.Bottom), (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    /// <summary>\\.\DISPLAY2 → 2 (costuma bater com a numeração de Configurações → Tela).</summary>
    private static int DisplayNumber(string device)
    {
        var digits = new string(device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    /// <summary>App em tela cheia (jogo, vídeo, apresentação): a barra some, os mini-gauges também.</summary>
    public static bool IsFullscreenAppRunning()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }

    /// <summary>
    /// A barra está clara? Segue "modo do Windows" (SystemUsesLightTheme). Com a cor de destaque
    /// aplicada à barra (ColorPrevalence, só no tema escuro), decide pela luminância do destaque.
    /// </summary>
    public static bool IsTaskbarLight()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var light = personalize?.GetValue("SystemUsesLightTheme") is int l && l != 0;
            var accentOnTaskbar = personalize?.GetValue("ColorPrevalence") is int p && p != 0;
            if (light || !accentOnTaskbar) return light;

            using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (accent?.GetValue("AccentColorMenu") is not int abgr) return false;

            // ABGR → luminância relativa aproximada.
            double r = abgr & 0xFF, g = (abgr >> 8) & 0xFF, b = (abgr >> 16) & 0xFF;
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 > 0.6;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Janela da barra de tarefas (para detectar quando ela vem para a frente).</summary>
    public static IntPtr TaskbarHandle => FindWindow("Shell_TrayWnd", null);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);
}
