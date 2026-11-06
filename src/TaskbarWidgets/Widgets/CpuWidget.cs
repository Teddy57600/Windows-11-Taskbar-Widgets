using System.Windows.Media;
using TaskbarWidgets.Sdk;
using TaskbarWidgets.Sdk.Pdh;

namespace TaskbarWidgets.Widgets;

/// <summary>Utilisation du processeur (même mesure que le Gestionnaire des tâches).</summary>
public sealed class CpuWidget : SystemWidget
{
    private PdhQuery? _query;
    private PdhCounter? _counter;

    public override string Id => "cpu";
    public override string DisplayName => "Processeur";
    public override string DefaultLabel => "CPU";
    public override int DefaultOrder => 10;
    public override Color DefaultAccent => Color.FromRgb(0x4C, 0xC2, 0xFF);

    public override void Initialize(WidgetContext context)
    {
        _query = new PdhQuery();
        _counter = _query.TryAddCounter(@"\Processor Information(_Total)\% Processor Utility")
                   ?? _query.TryAddCounter(@"\Processor(_Total)\% Processor Time")
                   ?? throw new InvalidOperationException("Compteur processeur introuvable.");
        _query.Collect();
    }

    public override void Update()
    {
        _query!.Collect();
        if (_counter!.GetValue() is not double value)
            return;

        value = Math.Clamp(value, 0, 100);
        Value = $"{value:0}%";
        Level = value / 100;
        ToolTip = $"Processeur : {value:0.0} %";
    }

    public override void Dispose() => _query?.Dispose();
}
