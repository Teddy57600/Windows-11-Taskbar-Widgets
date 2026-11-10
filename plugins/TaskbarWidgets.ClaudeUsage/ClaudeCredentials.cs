using System.IO;
using System.Text.Json;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>
/// Trouve le jeton OAuth de Claude Code, sans jamais le modifier ni le renouveler
/// (le renouvellement reste l'affaire de Claude Code, sinon sa propre connexion serait invalidée).
/// </summary>
/// <remarks>
/// Ordre de recherche :
/// <list type="number">
/// <item>le fichier indiqué par l'option <c>credentialsFile</c> ;</item>
/// <item><c>%CLAUDE_CONFIG_DIR%\.credentials.json</c>, puis <c>%USERPROFILE%\.claude\.credentials.json</c>
/// (créés par <c>/login</c> dans Claude Code) ;</item>
/// <item>en dernier recours, la variable d'environnement <c>CLAUDE_CODE_OAUTH_TOKEN</c>.</item>
/// </list>
/// Les jetons de <c>claude setup-token</c> ne conviennent pas : le service refuse (403, portée
/// <c>user:profile</c> manquante) car ils sont limités à l'utilisation des modèles.
/// Le fichier est relu à chaque actualisation : Claude Code y écrit le jeton renouvelé.
/// </remarks>
internal sealed class ClaudeCredentials
{
    public const string TokenEnvironmentVariable = "CLAUDE_CODE_OAUTH_TOKEN";

    private readonly string? _credentialsFile;

    public ClaudeCredentials(string? credentialsFile) => _credentialsFile = credentialsFile;

    /// <summary>Renvoie le jeton, ou <c>null</c> avec la raison (en français) dans <paramref name="problem"/>.</summary>
    public string? TryGetToken(out string? problem)
    {
        problem = null;

        string? file = CandidateFiles().FirstOrDefault(File.Exists);
        if (file is null)
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                Source = $"variable {TokenEnvironmentVariable}";
                return Clean(fromEnvironment);
            }

            problem = "Connexion Claude Code introuvable.\n" +
                      "Connectez-vous avec /login dans Claude Code en ligne de commande (fichier .credentials.json),\n" +
                      "ou indiquez l'emplacement de ce fichier dans l'option « credentialsFile » du widget.";
            return null;
        }

        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || !oauth.TryGetProperty("accessToken", out var tokenElement)
                || tokenElement.GetString() is not { Length: > 0 } token)
            {
                problem = "Aucune connexion claude.ai dans le fichier de Claude Code : connectez-vous avec /login dans Claude Code.";
                return null;
            }

            if (oauth.TryGetProperty("expiresAt", out var expires) && expires.TryGetInt64(out long expiresMs)
                && DateTimeOffset.FromUnixTimeMilliseconds(expiresMs) < DateTimeOffset.UtcNow)
            {
                problem = $"Jeton expiré depuis le {DateTimeOffset.FromUnixTimeMilliseconds(expiresMs).LocalDateTime:g}.\n" +
                          "Ouvrez Claude Code : il le renouvellera, et le widget reprendra tout seul.";
                return null;
            }

            Source = $"fichier {file}";
            return Clean(token);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            problem = $"Lecture de la connexion Claude Code impossible : {ex.Message}";
            return null;
        }
    }

    /// <summary>D'où vient le dernier jeton trouvé (pour l'info-bulle ; jamais le jeton lui-même).</summary>
    public string? Source { get; private set; }

    /// <summary>
    /// Un jeton ne contient ni espace ni retour à la ligne : on retire ceux d'une copie depuis un terminal
    /// (jeton coupé sur deux lignes), ainsi que d'éventuels guillemets autour.
    /// </summary>
    private static string Clean(string token)
        => new string(token.Where(c => !char.IsWhiteSpace(c)).ToArray()).Trim('"', '\'');

    /// <summary>Indice de forme, sans rien révéler du jeton.</summary>
    public static string? ShapeHint(string token)
        => token.StartsWith("sk-ant-", StringComparison.Ordinal)
            ? null
            : "Le jeton ne commence pas par « sk-ant- » : il a peut-être été mal copié.";

    private IEnumerable<string> CandidateFiles()
    {
        if (!string.IsNullOrWhiteSpace(_credentialsFile))
            yield return Environment.ExpandEnvironmentVariables(_credentialsFile);

        string? configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
            yield return Path.Combine(configDir, ".credentials.json");

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
    }
}
