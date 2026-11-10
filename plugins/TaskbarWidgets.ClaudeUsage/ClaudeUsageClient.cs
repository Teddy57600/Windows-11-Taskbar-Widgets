using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>Taux d'utilisation d'une fenêtre de limite (0 à 100 %) et date de sa réinitialisation.</summary>
internal sealed record UsageWindow(string Key, double Utilization, DateTimeOffset? ResetsAt);

internal sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? SevenDay, IReadOnlyList<UsageWindow> Others, DateTimeOffset FetchedAt);

internal sealed class ClaudeUsageException(string message) : Exception(message);

/// <summary>
/// Lit l'utilisation de l'abonnement auprès du service de Claude Code
/// (le même que celui de la commande /usage). Lecture seule.
/// </summary>
internal static class ClaudeUsageClient
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static UsageSnapshot Fetch(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("TaskbarWidgets/1.0");
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = Http.SendAsync(request).GetAwaiter().GetResult();
        }
        catch (HttpRequestException ex)
        {
            throw new ClaudeUsageException($"Service injoignable : {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new ClaudeUsageException("Le service n'a pas répondu à temps.");
        }

        using (response)
        {
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.OK)
                return Parse(body, DateTimeOffset.Now);

            // La raison donnée par le service (jamais le jeton) aide à comprendre un refus.
            string reason = ServiceReason(body);
            string detail = $"({(int)response.StatusCode}{(reason.Length > 0 ? " · " + reason : "")})";
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden when reason.Contains("scope", StringComparison.OrdinalIgnoreCase) =>
                    new ClaudeUsageException(
                        $"Ce jeton n'a pas le droit de lire l'utilisation {detail}.\n" +
                        "Les jetons de « claude setup-token » sont limités à l'utilisation des modèles :\n" +
                        "connectez-vous avec /login dans Claude Code en ligne de commande, le widget utilisera cette connexion."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new ClaudeUsageException($"Connexion refusée par le service {detail}.\nJeton expiré, révoqué ou mal copié."),
                HttpStatusCode.TooManyRequests =>
                    new ClaudeUsageException($"Trop de demandes {detail} : nouvel essai à la prochaine actualisation."),
                _ => new ClaudeUsageException($"Réponse inattendue du service {detail}."),
            };
        }
    }

    /// <summary>Extrait « type : message » d'une erreur JSON du service (<c>{"error":{"type":…,"message":…}}</c>).</summary>
    private static string ServiceReason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return "";
            string type = error.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            string message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            string reason = string.Join(" : ", new[] { type, message }.Where(s => s.Length > 0));
            return reason.Length > 160 ? reason[..160] + "…" : reason;
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>
    /// Format attendu : <c>{ "five_hour": { "utilization": 42.0, "resets_at": "2026-11-10T14:00:00+00:00" },
    /// "seven_day": { ... }, ... }</c>. Les autres fenêtres éventuelles (par modèle) sont conservées pour l'info-bulle.
    /// </summary>
    public static UsageSnapshot Parse(string json, DateTimeOffset fetchedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            UsageWindow? fiveHour = null, sevenDay = null;
            var others = new List<UsageWindow>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (ReadWindow(property.Name, property.Value) is not { } window)
                    continue;
                switch (property.Name)
                {
                    case "five_hour": fiveHour = window; break;
                    case "seven_day": sevenDay = window; break;
                    default: others.Add(window); break;
                }
            }

            if (fiveHour is null && sevenDay is null)
                throw new ClaudeUsageException("Réponse sans fenêtre de 5 heures ni de 7 jours : abonnement Pro ou Max requis.");
            return new UsageSnapshot(fiveHour, sevenDay, others, fetchedAt);
        }
        catch (JsonException ex)
        {
            throw new ClaudeUsageException($"Réponse illisible : {ex.Message}");
        }
    }

    private static UsageWindow? ReadWindow(string key, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("utilization", out var utilization)
            || utilization.ValueKind != JsonValueKind.Number)
            return null;

        DateTimeOffset? resetsAt = null;
        if (element.TryGetProperty("resets_at", out var reset) && reset.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            resetsAt = parsed;

        return new UsageWindow(key, Math.Clamp(utilization.GetDouble(), 0, 100), resetsAt);
    }
}
