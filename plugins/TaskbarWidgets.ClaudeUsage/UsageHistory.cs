using System.IO;
using System.Text.Json;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>
/// Historique local de l'utilisation sur 7 jours (un point toutes les 10 minutes au plus),
/// limité à la fenêtre en cours : il sert à mesurer le rythme récent de consommation.
/// </summary>
internal sealed class UsageHistory
{
    private static readonly TimeSpan MinSpacing = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _path;
    private List<Sample> _samples = [];

    public sealed record Sample(DateTimeOffset At, double Utilization, DateTimeOffset ResetsAt);

    public UsageHistory(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "history-7d.json");
        try
        {
            if (File.Exists(_path))
                _samples = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _samples = []; // historique illisible : on repart de zéro
        }
    }

    public IReadOnlyList<Sample> Samples => _samples;

    /// <summary>Ajoute un point et oublie ceux d'une fenêtre précédente.</summary>
    public void Add(DateTimeOffset at, double utilization, DateTimeOffset resetsAt)
    {
        // L'heure de réinitialisation renvoyée varie de quelques secondes : même fenêtre à 1 h près.
        _samples.RemoveAll(s => !SameWindow(s.ResetsAt, resetsAt));
        if (_samples.Count > 0 && at - _samples[^1].At < MinSpacing)
            return;
        _samples.Add(new Sample(at, utilization, resetsAt));
        Save();
    }

    /// <summary>
    /// Point le plus proche de <paramref name="lookback"/> avant <paramref name="now"/>
    /// (ou, à défaut, le plus ancien de la fenêtre).
    /// </summary>
    public Sample? Reference(DateTimeOffset now, TimeSpan lookback)
    {
        if (_samples.Count == 0)
            return null;
        var target = now - lookback;
        return _samples.LastOrDefault(s => s.At <= target) ?? _samples[0];
    }

    private static bool SameWindow(DateTimeOffset a, DateTimeOffset b) => (a - b).Duration() < TimeSpan.FromHours(1);

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_samples, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pas d'historique sur disque : la prévision « 24 h » se rabattra sur la moyenne de la fenêtre.
        }
    }
}
