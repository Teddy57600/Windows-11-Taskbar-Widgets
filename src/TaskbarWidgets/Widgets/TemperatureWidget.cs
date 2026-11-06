using System.Text;
using System.Windows.Media;
using TaskbarWidgets.Sdk;
using TaskbarWidgets.Sdk.Pdh;

namespace TaskbarWidgets.Widgets;

/// <summary>
/// Température la plus élevée parmi les zones thermiques ACPI (lisible sans droits administrateur).
/// </summary>
/// <remarks>
/// Ces zones sont exposées par le firmware : selon la carte mère, il s'agit du processeur, du chipset,
/// ou d'une valeur peu fiable. Certaines machines n'en exposent aucune (« N/A »).
/// </remarks>
public sealed class TemperatureWidget : SystemWidget
{
    private const double KelvinOffset = 273.15;

    private PdhQuery? _query;
    private PdhCounter? _highPrecision; // dixièmes de kelvin
    private PdhCounter? _standard;      // kelvins

    public override string Id => "temp";
    public override string DisplayName => "Température";
    public override string DefaultLabel => "TEMP";
    public override int DefaultOrder => 40;
    public override Color DefaultAccent => Color.FromRgb(0xFF, 0x7B, 0x7B);
    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);
    public override double MinValueWidth => 38;

    public override void Initialize(WidgetContext context)
    {
        _query = new PdhQuery();
        _highPrecision = _query.TryAddCounter(@"\Thermal Zone Information(*)\High Precision Temperature");
        _standard = _query.TryAddCounter(@"\Thermal Zone Information(*)\Temperature");
        _query.Collect();
    }

    public override void Update()
    {
        _query!.Collect();

        var zones = _highPrecision?.GetValues().Select(z => (z.Instance, Celsius: z.Value / 10 - KelvinOffset)).ToList();
        if (zones is null || zones.Count == 0)
            zones = _standard?.GetValues().Select(z => (z.Instance, Celsius: z.Value - KelvinOffset)).ToList();

        zones = zones?.Where(z => z.Celsius is > -50 and < 150).ToList();
        if (zones is null || zones.Count == 0)
        {
            Value = "N/A";
            Level = null;
            ToolTip = "Aucune zone thermique ACPI exposée par cette machine.";
            return;
        }

        double max = zones.Max(z => z.Celsius);
        Value = $"{max:0} °C";
        Level = max / 100;

        var tip = new StringBuilder("Zones thermiques :");
        foreach (var zone in zones.OrderByDescending(z => z.Celsius))
            tip.Append($"\n{zone.Instance} : {zone.Celsius:0.0} °C");
        ToolTip = tip.ToString();
    }

    public override void Dispose() => _query?.Dispose();
}
