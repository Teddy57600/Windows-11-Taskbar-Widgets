using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarWidgets.Services;

public enum TaskbarAnchor
{
    /// <summary>À partir du bord gauche de la barre (après le bouton Widgets de Windows).</summary>
    Left,
    /// <summary>À partir du bord droit (avant la zone de notification).</summary>
    Right,
}

public sealed class AppSettings
{
    /// <summary>Bord de la barre des tâches auquel les widgets sont accrochés.</summary>
    public TaskbarAnchor Anchor { get; set; } = TaskbarAnchor.Left;

    /// <summary>
    /// Écran dont la barre des tâches accueille les widgets (nom de périphérique, ex. « \\.\DISPLAY2 »).
    /// <c>null</c> = écran principal. Si l'écran n'est plus disponible, l'écran principal est utilisé.
    /// </summary>
    public string? Screen { get; set; }

    /// <summary>Distance (DIP) entre ce bord et les widgets.</summary>
    public double Offset { get; set; } = 70;

    /// <summary>Masquer les widgets quand une application est en plein écran.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    /// <summary>Widgets, dans l'ordre d'affichage.</summary>
    public List<WidgetConfig> Widgets { get; set; } = [];
}

public sealed class WidgetConfig
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Libellé personnalisé (sinon celui du widget).</summary>
    public string? Label { get; set; }

    /// <summary>Couleur personnalisée, ex. « #4CC2FF ».</summary>
    public string? Color { get; set; }

    /// <summary>Options propres au widget.</summary>
    public Dictionary<string, string>? Options { get; set; }
}

internal static class SettingsStore
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarWidgets");

    public static string FilePath => Path.Combine(DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("settings.json illisible : une copie .bak est conservée et les réglages par défaut sont utilisés.", ex);
            try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch { }
        }
        return new AppSettings();
    }

    /// <summary>Contenu actuel du fichier (<c>null</c> s'il n'existe pas), lu sans gêner un éditeur qui l'a ouvert.</summary>
    /// <exception cref="IOException">Fichier momentanément verrouillé (enregistrement en cours).</exception>
    public static string? ReadText()
    {
        if (!File.Exists(FilePath))
            return null;
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static bool TryParse(string json, out AppSettings settings, out Exception? error)
    {
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            settings = new AppSettings();
            error = ex;
            return false;
        }
    }

    /// <summary>Forme canonique d'une valeur (pour comparer deux configurations, indépendamment de la mise en page).</summary>
    public static string Canonical<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>Enregistre et renvoie le texte écrit (<c>null</c> en cas d'échec).</summary>
    public static string? Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string text = JsonSerializer.Serialize(settings, Json);
            File.WriteAllText(FilePath, text);
            return text;
        }
        catch (Exception ex)
        {
            Log.Error("Impossible d'enregistrer settings.json", ex);
            return null;
        }
    }
}
