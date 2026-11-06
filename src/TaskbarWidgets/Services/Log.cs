using System.IO;
namespace TaskbarWidgets.Services;

/// <summary>Journal minimal dans %APPDATA%\TaskbarWidgets\log.txt.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private const long MaxSize = 1024 * 1024;

    public static string FilePath => Path.Combine(SettingsStore.DataDirectory, "log.txt");

    public static void Error(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxSize)
                    file.Delete();
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{exception}{Environment.NewLine}");
            }
        }
        catch
        {
            // Le journal ne doit jamais faire planter l'application.
        }
    }
}
