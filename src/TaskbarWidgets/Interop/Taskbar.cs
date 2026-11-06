using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static TaskbarWidgets.Interop.NativeMethods;

namespace TaskbarWidgets.Interop;

/// <summary>Une barre des tâches et l'écran qui la porte.</summary>
internal sealed record TaskbarInfo(IntPtr Hwnd, IntPtr Monitor, string DeviceName, RECT MonitorBounds, bool IsPrimary)
{
    /// <summary>Numéro Windows de l'écran (\\.\DISPLAY2 → 2), celui affiché dans Paramètres &gt; Affichage.</summary>
    public int Number
    {
        get
        {
            int i = DeviceName.Length;
            while (i > 0 && char.IsAsciiDigit(DeviceName[i - 1]))
                i--;
            return int.TryParse(DeviceName.AsSpan(i), out int n) ? n : 0;
        }
    }
}

/// <summary>Barres des tâches : Shell_TrayWnd (écran principal) et Shell_SecondaryTrayWnd (autres écrans).</summary>
internal static class Taskbar
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>Toutes les barres des tâches, la principale en premier puis par numéro d'écran.</summary>
    public static List<TaskbarInfo> FindAll()
    {
        var result = new List<TaskbarInfo>();

        IntPtr primary = FindWindowW("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && Describe(primary) is { } p)
            result.Add(p);

        // Présentes seulement si « Afficher la barre des tâches sur tous les écrans » est activé.
        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowExW(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (Describe(secondary) is { } s)
                result.Add(s);
        }

        return result.OrderByDescending(t => t.IsPrimary).ThenBy(t => t.Number).ToList();
    }

    /// <summary>
    /// Barre de l'écran demandé (nom de périphérique, ex. « \\.\DISPLAY2 ») ; à défaut, celle de l'écran principal.
    /// </summary>
    public static TaskbarInfo? Find(string? deviceName, out bool fellBack)
    {
        var all = FindAll();
        var match = deviceName is null
            ? null
            : all.FirstOrDefault(t => string.Equals(t.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        fellBack = deviceName is not null && match is null;
        return match ?? all.FirstOrDefault(t => t.IsPrimary) ?? all.FirstOrDefault();
    }

    public static int MonitorCount => GetSystemMetrics(SM_CMONITORS);

    /// <summary>Libellé lisible, ex. « Écran 2 – 2560 × 1440, à droite ».</summary>
    public static string Describe(TaskbarInfo taskbar, TaskbarInfo? primary)
    {
        var b = taskbar.MonitorBounds;
        string text = $"Écran {taskbar.Number} – {b.Width} × {b.Height}";
        if (taskbar.IsPrimary)
            return text + " (principal)";
        if (primary is null)
            return text;

        var p = primary.MonitorBounds;
        double dx = (b.Left + b.Right - p.Left - p.Right) / 2.0;
        double dy = (b.Top + b.Bottom - p.Top - p.Bottom) / 2.0;
        string where = Math.Abs(dx) >= Math.Abs(dy)
            ? (dx > 0 ? "à droite" : "à gauche")
            : (dy > 0 ? "en dessous" : "au-dessus");
        return $"{text}, {where}";
    }

    private static TaskbarInfo? Describe(IntPtr hwnd)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
            return null;
        return new TaskbarInfo(hwnd, monitor, info.szDevice, info.rcMonitor, (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    /// <summary>La barre est-elle affichée (pas masquée automatiquement) ?</summary>
    public static bool IsShown(TaskbarInfo taskbar, RECT rect)
    {
        if (!IsWindowVisible(taskbar.Hwnd) || rect.Height <= 0)
            return false;

        // En masquage automatique, la barre glisse hors de l'écran : on regarde la part encore visible.
        var screen = taskbar.MonitorBounds;
        int visible = Math.Min(rect.Bottom, screen.Bottom) - Math.Max(rect.Top, screen.Top);
        return visible >= rect.Height * 0.75;
    }

    /// <summary>Une application plein écran (jeu, vidéo, présentation) occupe-t-elle cet écran ?</summary>
    public static bool IsFullscreenAppRunning(TaskbarInfo taskbar)
    {
        if (SHQueryUserNotificationState(out int state) != 0)
            return false;
        if (state == QUNS_PRESENTATION_MODE)
            return true;
        if (state is not (QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN))
            return false;

        // L'état est global : on ne masque que si l'application plein écran est sur notre écran.
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsShellWindow(foreground))
            return false;
        return MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST) == taskbar.Monitor;
    }

    private static bool IsShellWindow(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassNameW(hwnd, name, name.Capacity);
        return ShellClasses.Contains(name.ToString(), StringComparer.Ordinal);
    }

    /// <summary>La barre des tâches utilise-t-elle le thème clair ?</summary>
    public static bool UsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
