using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>
/// Prévision de l'utilisation de la fenêtre de 7 jours au moment de sa réinitialisation,
/// en prolongeant le rythme de consommation jusqu'au reset.
/// </summary>
/// <remarks>
/// Options (settings.json, ou clic droit &gt; Réglages des widgets) :
/// <list type="bullet">
/// <item><c>method</c> : <c>window</c> (rythme moyen depuis le début de la fenêtre, par défaut)
/// ou <c>24h</c> (rythme des dernières 24 heures, mesuré sur un historique local).</item>
/// <item><c>refreshMinutes</c> : intervalle d'actualisation (5 minutes par défaut).</item>
/// <item><c>credentialsFile</c> : comme le widget « Claude Code – utilisation ».</item>
/// </list>
/// La fenêtre de 7 jours est supposée commencer 7 jours avant son heure de réinitialisation.
/// </remarks>
public sealed class ClaudeForecastWidget : TaskbarWidget
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);
    private static readonly TimeSpan MinElapsed = TimeSpan.FromHours(6);   // avant : prévision trop instable
    private static readonly TimeSpan MinHistory = TimeSpan.FromHours(3);   // historique minimal pour « 24 h »
    private static readonly Color Good = Color.FromRgb(0x6C, 0xCB, 0x5F);
    private static readonly Color Warning = Color.FromRgb(0xFF, 0xB9, 0x00);
    private static readonly Color Over = Color.FromRgb(0xFF, 0x6B, 0x6B);

    private TimeSpan _interval = TimeSpan.FromMinutes(5);
    private ClaudeCredentials _credentials = new(null);
    private UsageHistory? _history;
    private bool _recentRate;
    private bool _dynamicColor = true;

    public override string Id => "claude-forecast";
    public override string DisplayName => "Claude Code – prévision 7 jours";
    public override string DefaultLabel => "PRÉV 7J";
    public override Color DefaultAccent => Color.FromRgb(0xE9, 0xC4, 0x6A);
    public override int DefaultOrder => 71;
    public override double MinValueWidth => 44;
    public override TimeSpan UpdateInterval => _interval;

    public override IReadOnlyList<WidgetMenuOption> MenuOptions { get; } =
    [
        new WidgetMenuOption("method", "Méthode de prévision", "window",
        [
            new WidgetMenuChoice("window", "Rythme moyen depuis le début de la fenêtre"),
            new WidgetMenuChoice("24h", "Rythme des dernières 24 heures"),
        ]),
        ClaudeOptions.Refresh,
    ];

    public override void Initialize(WidgetContext context)
    {
        _interval = ClaudeOptions.ReadRefresh(context);
        _credentials = new ClaudeCredentials(context.GetOption("credentialsFile"));
        _recentRate = string.Equals(context.GetOption("method"), "24h", StringComparison.OrdinalIgnoreCase);
        _history = new UsageHistory(context.DataDirectory);
        // Une couleur choisie dans settings.json reste fixe ; sinon la couleur suit la prévision.
        _dynamicColor = Accent == DefaultAccent;
        Value = "…";
        ToolTip = "Claude Code : calcul de la prévision…";
    }

    public override void Update()
    {
        UsageSnapshot snapshot;
        try
        {
            snapshot = UsageService.Get(_credentials);
        }
        catch (ClaudeUsageException ex)
        {
            if (Value is "…")
                Value = "—";
            ToolTip = $"Claude Code – prévision sur 7 jours\n{ex.Message}";
            return;
        }

        var now = DateTimeOffset.Now;
        if (snapshot.SevenDay is not { ResetsAt: { } reset } seven)
        {
            Show("—", null, "Fenêtre de 7 jours indisponible pour cet abonnement.");
            return;
        }
        if (reset <= now)
        {
            Show("0%", 0, "La fenêtre de 7 jours vient d'être réinitialisée : prévision à la prochaine actualisation.");
            return;
        }

        double used = seven.Utilization;
        _history?.Add(now, used, reset);

        var start = reset - Window;
        var elapsed = now - start;
        var remaining = reset - now;

        // Rythme en points de pourcentage par heure.
        string method;
        double? rate = null;
        if (_recentRate && _history?.Reference(now, TimeSpan.FromHours(24)) is { } reference && now - reference.At >= MinHistory)
        {
            var span = now - reference.At;
            rate = Math.Max(0, (used - reference.Utilization) / span.TotalHours);
            method = $"rythme des dernières {FormatHours(span)}";
        }
        else
        {
            method = _recentRate
                ? "moyenne depuis le début de la fenêtre (historique local encore trop court pour les dernières 24 h)"
                : "moyenne depuis le début de la fenêtre";
            if (elapsed >= MinElapsed)
                rate = used / elapsed.TotalHours;
        }

        if (rate is not double perHour)
        {
            Show("…", used / 100,
                $"Trop tôt pour une prévision fiable : la fenêtre a commencé il y a {ClaudeOptions.Remaining(elapsed)}.\n" +
                $"Utilisation actuelle : {used:0} % · réinitialisation le {ClaudeOptions.LongDate(reset)}");
            return;
        }

        double projected = used + perHour * remaining.TotalHours;
        var tip = new StringBuilder("Claude Code – prévision sur 7 jours");
        tip.Append($"\nPrévu à la réinitialisation ({ClaudeOptions.LongDate(reset)}) : {projected:0} %");
        tip.Append($"\nActuel : {used:0} % après {ClaudeOptions.Remaining(elapsed)} ({elapsed / Window:P0} de la fenêtre)");
        tip.Append($"\nRythme : {(perHour * 24).ToString("0.0", ClaudeOptions.French)} % par jour ({method})");

        if (projected > 100 && perHour > 0)
        {
            var full = now + TimeSpan.FromHours((100 - used) / perHour);
            tip.Append(used >= 100
                ? "\n⚠ Limite des 7 jours atteinte"
                : $"\n⚠ À ce rythme, 100 % atteints le {ClaudeOptions.LongDate(full)} (dans {ClaudeOptions.Remaining(full - now)})");
        }
        if (used < 100)
        {
            double budget = (100 - used) / Math.Max(remaining.TotalDays, 1.0 / 24);
            tip.Append($"\nMarge pour finir à 100 % : {budget.ToString("0.0", ClaudeOptions.French)} % par jour d'ici le reset");
        }
        tip.Append($"\nMis à jour à {snapshot.FetchedAt:HH:mm} · actualisation {ClaudeOptions.RefreshText(_interval)}");
        tip.Append("\nClic : ouvrir la page d'utilisation de claude.ai");

        Show($"{projected:0}%", projected / 100, tip.ToString());
        if (_dynamicColor)
            Accent = projected >= 100 ? Over : projected >= 80 ? Warning : Good;
    }

    public override void OnClick()
    {
        try { Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true }); }
        catch (Exception) { /* pas de navigateur par défaut : rien à faire */ }
    }

    private void Show(string value, double? level, string toolTip)
    {
        Value = value;
        Level = level;
        ToolTip = toolTip.StartsWith("Claude Code", StringComparison.Ordinal)
            ? toolTip
            : $"Claude Code – prévision sur 7 jours\n{toolTip}";
    }

    private static string FormatHours(TimeSpan span)
        => span.TotalHours >= 23.5 ? "24 heures" : $"{span.TotalHours:0} heures";
}
