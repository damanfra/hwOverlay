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

/// <param name="Bounds">Barra de tarefas principal.</param>
/// <param name="Tray">Área da bandeja (ícones + relógio); vazio se não encontrada.</param>
/// <param name="IsHorizontal">Falso se a barra estiver na lateral (Windows 10).</param>
/// <param name="IsHidden">Barra com ocultação automática recolhida no momento.</param>
public sealed record TaskbarInfo(PixelRect Bounds, PixelRect Tray, bool IsHorizontal, bool IsHidden);

/// <summary>
/// Localiza a barra de tarefas principal via Win32. O Windows 11 não tem API para colocar
/// conteúdo dentro da barra, então o modo "barra de tarefas" é uma janela sobreposta a ela.
/// </summary>
public static class TaskbarLocator
{
    public static TaskbarInfo? Find()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var r)) return null;

        var bounds = new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        if (bounds.IsEmpty) return null;

        var tray = default(PixelRect);
        var trayHwnd = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayHwnd != IntPtr.Zero && GetWindowRect(trayHwnd, out var t))
            tray = new PixelRect(t.Left, t.Top, t.Right, t.Bottom);

        // Ocultação automática: recolhida, a barra fica quase toda fora do monitor.
        var hidden = false;
        var monitor = MonitorFromWindow(taskbar, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var m = info.rcMonitor;
            var visibleW = Math.Min(bounds.Right, m.Right) - Math.Max(bounds.Left, m.Left);
            var visibleH = Math.Min(bounds.Bottom, m.Bottom) - Math.Max(bounds.Top, m.Top);
            hidden = visibleW < bounds.Width / 2 || visibleH < bounds.Height / 2;
        }

        return new TaskbarInfo(bounds, tray, bounds.Width >= bounds.Height, hidden);
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

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);
}
