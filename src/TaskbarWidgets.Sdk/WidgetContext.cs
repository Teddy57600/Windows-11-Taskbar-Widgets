using System.Globalization;

namespace TaskbarWidgets.Sdk;

/// <summary>
/// Informations transmises à un widget lors de son initialisation.
/// </summary>
public sealed class WidgetContext
{
    public WidgetContext(IReadOnlyDictionary<string, string> options, string dataDirectory)
    {
        Options = options;
        DataDirectory = dataDirectory;
    }

    /// <summary>Options libres du widget, lues dans settings.json (« options »).</summary>
    public IReadOnlyDictionary<string, string> Options { get; }

    /// <summary>Dossier où le widget peut stocker ses propres fichiers.</summary>
    public string DataDirectory { get; }

    public string? GetOption(string key)
        => Options.TryGetValue(key, out var value) ? value : null;

    public string GetOption(string key, string defaultValue)
        => GetOption(key) ?? defaultValue;

    public double GetOption(string key, double defaultValue)
        => double.TryParse(GetOption(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    public bool GetOption(string key, bool defaultValue)
        => bool.TryParse(GetOption(key), out var v) ? v : defaultValue;
}
