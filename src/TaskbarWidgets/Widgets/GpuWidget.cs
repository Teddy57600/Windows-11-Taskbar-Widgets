using System.Windows.Media;
using TaskbarWidgets.Sdk;
using TaskbarWidgets.Sdk.Pdh;

namespace TaskbarWidgets.Widgets;

/// <summary>
/// Utilisation du GPU : comme le Gestionnaire des tâches, on additionne l'utilisation de chaque moteur
/// (3D, copie, décodage vidéo…) sur tous les processus, puis on garde le moteur le plus chargé.
/// </summary>
public sealed class GpuWidget : SystemWidget
{
    private PdhQuery? _query;
    private PdhCounter? _counter;

    public override string Id => "gpu";
    public override string DisplayName => "Carte graphique";
    public override string DefaultLabel => "GPU";
    public override int DefaultOrder => 30;
    public override Color DefaultAccent => Color.FromRgb(0x6C, 0xCB, 0x5F);

    // Les compteurs « GPU Engine » ont une instance par processus et par moteur : leur lecture coûte cher.
    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public override void Initialize(WidgetContext context)
    {
        _query = new PdhQuery();
        _counter = _query.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage")
                   ?? throw new InvalidOperationException("Compteurs GPU indisponibles (pilote WDDM 2.x requis).");
        _query.Collect();
    }

    public override void Update()
    {
        _query!.Collect();
        var samples = _counter!.GetValues();
        if (samples.Count == 0)
            return;

        // Instance : pid_1234_luid_0x00000000_0x0000D1B5_phys_0_eng_0_engtype_3D
        var busiest = samples
            .GroupBy(s => EngineKey(s.Instance))
            .Select(g => (Engine: g.Key, Value: g.Sum(s => s.Value)))
            .MaxBy(e => e.Value);

        double value = Math.Clamp(busiest.Value, 0, 100);
        Value = $"{value:0}%";
        Level = value / 100;
        ToolTip = $"GPU : {value:0.0} % (moteur {EngineType(busiest.Engine)})";
    }

    private static string EngineKey(string instance)
    {
        int index = instance.IndexOf("luid_", StringComparison.Ordinal);
        return index >= 0 ? instance[index..] : instance;
    }

    private static string EngineType(string key)
    {
        int index = key.IndexOf("engtype_", StringComparison.Ordinal);
        return index >= 0 ? key[(index + "engtype_".Length)..] : "?";
    }

    public override void Dispose() => _query?.Dispose();
}
