using System.Windows.Media;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.Sample.Uptime;

/// <summary>
/// Widget d'exemple : durée depuis le démarrage de Windows.
/// La barre se remplit sur une période configurable (option « fullDays », 7 jours par défaut).
/// </summary>
public sealed class UptimeWidget : TaskbarWidget
{
    private double _fullDays = 7;

    public override string Id => "sample.uptime";
    public override string DisplayName => "Durée de fonctionnement (exemple)";
    public override string DefaultLabel => "UP";
    public override int DefaultOrder => 200;
    public override Color DefaultAccent => Color.FromRgb(0xFF, 0xB9, 0x00);
    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(30);
    public override double MinValueWidth => 44;

    public override void Initialize(WidgetContext context)
    {
        // Exemple d'option lue dans settings.json : "options": { "fullDays": "14" }
        _fullDays = Math.Max(1, context.GetOption("fullDays", 7.0));
    }

    public override void Update()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        Value = uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}j {uptime.Hours:00}h"
            : $"{uptime.Hours}h {uptime.Minutes:00}m";
        Level = uptime.TotalDays / _fullDays;
        ToolTip = $"Windows a démarré le {DateTime.Now - uptime:dddd d MMMM à HH:mm}";
    }
}
