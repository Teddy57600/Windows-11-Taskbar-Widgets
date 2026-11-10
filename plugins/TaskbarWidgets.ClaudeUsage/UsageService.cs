using System.Globalization;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>
/// Lecture partagée de l'utilisation : les widgets Claude d'un même affichage n'interrogent le service
/// qu'une fois par période (une réponse de moins de 50 s est réutilisée).
/// </summary>
internal static class UsageService
{
    private static readonly object Gate = new();
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(50);
    private static (string Token, UsageSnapshot Snapshot)? _cache;

    /// <summary>Utilisation actuelle, ou <see cref="ClaudeUsageException"/> avec un message prêt à afficher.</summary>
    public static UsageSnapshot Get(ClaudeCredentials credentials)
    {
        string? token = credentials.TryGetToken(out string? problem);
        if (token is null)
            throw new ClaudeUsageException(problem ?? "Connexion Claude Code introuvable.");

        lock (Gate)
        {
            if (_cache is { } cached && cached.Token == token && DateTimeOffset.Now - cached.Snapshot.FetchedAt < MaxAge)
                return cached.Snapshot;

            try
            {
                var snapshot = ClaudeUsageClient.Fetch(token);
                _cache = (token, snapshot);
                return snapshot;
            }
            catch (ClaudeUsageException ex)
            {
                string message = $"{ex.Message}\nJeton lu dans : {credentials.Source}";
                if (ClaudeCredentials.ShapeHint(token) is { } hint)
                    message += $"\n{hint}";
                throw new ClaudeUsageException(message);
            }
        }
    }
}

/// <summary>Réglages communs aux widgets Claude.</summary>
internal static class ClaudeOptions
{
    public static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly int[] RefreshChoices = [1, 2, 5, 10, 15, 30, 60];

    public static WidgetMenuOption Refresh { get; } = new(
        "refreshMinutes", "Actualisation", "5",
        RefreshChoices.Select(m => new WidgetMenuChoice(
            m.ToString(CultureInfo.InvariantCulture),
            m == 1 ? "Toutes les minutes" : m == 60 ? "Toutes les heures" : $"Toutes les {m} minutes")).ToList());

    public static TimeSpan ReadRefresh(WidgetContext context)
        => TimeSpan.FromMinutes(Math.Clamp(context.GetOption("refreshMinutes", 5.0), 1, 24 * 60));

    public static string RefreshText(TimeSpan interval)
        => interval.TotalMinutes switch
        {
            <= 1 => "toutes les minutes",
            60 => "toutes les heures",
            var m => $"toutes les {m:0} minutes",
        };

    public static string Remaining(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays} j {span.Hours} h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} h {span.Minutes:00} min";
        return $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} min";
    }

    /// <summary>Ex. « jeudi 12 novembre à 04:59 ».</summary>
    public static string LongDate(DateTimeOffset when)
        => when.ToLocalTime().ToString("dddd d MMMM 'à' HH:mm", French);
}
