using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.ClaudeUsage;

/// <summary>
/// Utilisation de l'abonnement Claude Code : fenêtre glissante de 5 heures et fenêtre de 7 jours.
/// </summary>
/// <remarks>
/// Options (settings.json, ou clic droit &gt; Réglages des widgets) :
/// <list type="bullet">
/// <item><c>refreshMinutes</c> : intervalle d'actualisation en minutes (5 par défaut, 1 minimum).</item>
/// <item><c>credentialsFile</c> : chemin d'un fichier .credentials.json de Claude Code (facultatif).</item>
/// </list>
/// </remarks>
public sealed class ClaudeUsageWidget : TaskbarWidget
{
    private static readonly CultureInfo French = ClaudeOptions.French;
    private static readonly Brush SevenDayFill = Frozen(Color.FromRgb(0xE9, 0xC4, 0x6A));

    private TimeSpan _interval = TimeSpan.FromMinutes(5);
    private ClaudeCredentials _credentials = new(null);
    private UsageSnapshot? _last;
    private double? _fiveHourLevel;
    private double? _sevenDayLevel;

    public override string Id => "claude-usage";
    public override string DisplayName => "Claude Code – utilisation";
    public override string DefaultLabel => "CLAUDE";
    public override Color DefaultAccent => Color.FromRgb(0xD9, 0x77, 0x57);
    public override int DefaultOrder => 70;
    public override double MinValueWidth => 62;
    public override TimeSpan UpdateInterval => _interval;

    public override IReadOnlyList<WidgetMenuOption> MenuOptions { get; } = [ClaudeOptions.Refresh];

    /// <summary>Remplissage de la barre « 5 heures » (0 à 1).</summary>
    public double? FiveHourLevel
    {
        get => _fiveHourLevel;
        private set => SetField(ref _fiveHourLevel, value);
    }

    /// <summary>Remplissage de la barre « 7 jours » (0 à 1).</summary>
    public double? SevenDayLevel
    {
        get => _sevenDayLevel;
        private set => SetField(ref _sevenDayLevel, value);
    }

    /// <summary>Couleur de la barre « 7 jours ».</summary>
    public Brush SevenDayBrush => SevenDayFill;

    public override void Initialize(WidgetContext context)
    {
        _interval = ClaudeOptions.ReadRefresh(context);
        _credentials = new ClaudeCredentials(context.GetOption("credentialsFile"));
        Value = "…";
        ToolTip = "Claude Code : lecture de l'utilisation…";
    }

    public override void Update()
    {
        try
        {
            _last = UsageService.Get(_credentials);
            Show(_last, problem: null);
        }
        catch (ClaudeUsageException ex)
        {
            ShowProblem(ex.Message);
        }
    }

    public override FrameworkElement CreateView() => new ClaudeUsageView();

    public override void OnClick()
    {
        try { Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true }); }
        catch (Exception) { /* pas de navigateur par défaut : rien à faire */ }
    }

    /// <summary>En cas d'erreur, on garde les dernières valeurs connues en le signalant.</summary>
    private void ShowProblem(string problem)
    {
        if (_last is not null)
        {
            Show(_last, problem);
            return;
        }
        Value = "—";
        FiveHourLevel = SevenDayLevel = null;
        Level = null;
        ToolTip = $"Claude Code – utilisation\n{problem}";
    }

    private void Show(UsageSnapshot snapshot, string? problem)
    {
        var now = DateTimeOffset.Now;
        double? five = Effective(snapshot.FiveHour, now);
        double? seven = Effective(snapshot.SevenDay, now);

        FiveHourLevel = five / 100;
        SevenDayLevel = seven / 100;
        Level = Math.Max(five ?? 0, seven ?? 0) / 100;
        Value = $"{Percent(five)} · {Percent(seven)}";

        var tip = new StringBuilder("Claude Code – utilisation de l'abonnement");
        tip.Append(Describe("Fenêtre de 5 heures", snapshot.FiveHour, now, sameDayFormat: true));
        tip.Append(Describe("Fenêtre de 7 jours", snapshot.SevenDay, now, sameDayFormat: false));
        foreach (var other in snapshot.Others)
            tip.Append(Describe(Prettify(other.Key), other, now, sameDayFormat: false));
        tip.Append($"\nMis à jour à {snapshot.FetchedAt:HH:mm} · actualisation {ClaudeOptions.RefreshText(_interval)}");
        if (problem is not null)
            tip.Append($"\n⚠ {problem}");
        tip.Append("\nClic : ouvrir la page d'utilisation de claude.ai");
        ToolTip = tip.ToString();
    }

    /// <summary>Une fenêtre dont l'heure de réinitialisation est passée repart de 0 %.</summary>
    private static double? Effective(UsageWindow? window, DateTimeOffset now)
        => window is null ? null : window.ResetsAt is { } reset && reset <= now ? 0 : window.Utilization;

    private static string Percent(double? value) => value is double v ? $"{v:0}%" : "–";

    private static string Describe(string title, UsageWindow? window, DateTimeOffset now, bool sameDayFormat)
    {
        if (window is null)
            return $"\n{title} : indisponible";

        double value = Effective(window, now) ?? 0;
        string line = $"\n{title} : {value.ToString("0", French)} %";
        if (window.ResetsAt is not { } reset)
            return line;
        if (reset <= now)
            return line + " — réinitialisée";

        var local = reset.ToLocalTime();
        string when = sameDayFormat && local.Date == now.Date
            ? $"à {local:HH:mm}"
            : $"le {ClaudeOptions.LongDate(reset)}";
        return $"{line} — réinitialisation {when} (dans {ClaudeOptions.Remaining(reset - now)})";
    }

    /// <summary>« seven_day_opus » → « Fenêtre de 7 jours (opus) ».</summary>
    private static string Prettify(string key)
        => key.StartsWith("seven_day_", StringComparison.Ordinal)
            ? $"Fenêtre de 7 jours ({key["seven_day_".Length..].Replace('_', ' ')})"
            : key.Replace('_', ' ');

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
