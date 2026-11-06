using System.Windows.Media;
using TaskbarWidgets.Sdk;
using TaskbarWidgets.Sdk.Pdh;

namespace TaskbarWidgets.Widgets;

/// <summary>
/// Débit réseau, toutes cartes physiques confondues.
/// </summary>
/// <remarks>
/// Options (settings.json) :
/// <list type="bullet">
/// <item><c>interface</c> : ne compter que les cartes dont le nom contient ce texte (ex. « Wi-Fi »).</item>
/// <item><c>includeVirtual</c> : « true » pour compter aussi les cartes virtuelles (Hyper-V, VPN…).</item>
/// </list>
/// </remarks>
public abstract class NetworkWidget : SystemWidget
{
    private static readonly string[] VirtualAdapterMarkers =
        ["Hyper-V Virtual", "Virtual Ethernet", "VirtualBox", "VMware", "isatap", "Teredo", "Loopback", "WAN Miniport"];

    private PdhQuery? _query;
    private PdhCounter? _counter;
    private string? _interfaceFilter;
    private bool _includeVirtual;

    protected abstract string CounterName { get; }
    protected abstract string DirectionText { get; }

    public override double MinValueWidth => 56;

    public override void Initialize(WidgetContext context)
    {
        _interfaceFilter = context.GetOption("interface");
        _includeVirtual = context.GetOption("includeVirtual", false);
        _query = new PdhQuery();
        _counter = _query.TryAddCounter($@"\Network Interface(*)\{CounterName}")
                   ?? throw new InvalidOperationException("Compteurs réseau indisponibles.");
        _query.Collect();
    }

    public override void Update()
    {
        _query!.Collect();
        var adapters = _counter!.GetValues().Where(Accept).ToList();
        double total = adapters.Sum(a => a.Value);

        Value = Units.BytesPerSecond(total);
        // Échelle logarithmique : 1 KB/s ≈ 40 %, 100 MB/s = 100 %.
        Level = Math.Log10(total + 1) / 8;
        ToolTip = adapters.Count == 0
            ? "Aucune carte réseau active."
            : $"{DirectionText} : {Units.BytesPerSecond(total)}\n" +
              string.Join("\n", adapters.OrderByDescending(a => a.Value).Select(a => $"{a.Instance} : {Units.BytesPerSecond(a.Value)}"));
    }

    private bool Accept(PdhInstanceValue adapter)
    {
        if (_interfaceFilter is not null)
            return adapter.Instance.Contains(_interfaceFilter, StringComparison.OrdinalIgnoreCase);
        return _includeVirtual || !VirtualAdapterMarkers.Any(m => adapter.Instance.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    public override void Dispose() => _query?.Dispose();
}

public sealed class NetworkDownloadWidget : NetworkWidget
{
    public override string Id => "net-down";
    public override string DisplayName => "Réseau – réception";
    public override string DefaultLabel => "D";
    public override int DefaultOrder => 50;
    public override Color DefaultAccent => Color.FromRgb(0x5E, 0xD1, 0x6A);
    protected override string CounterName => "Bytes Received/sec";
    protected override string DirectionText => "Réception";
}

public sealed class NetworkUploadWidget : NetworkWidget
{
    public override string Id => "net-up";
    public override string DisplayName => "Réseau – envoi";
    public override string DefaultLabel => "U";
    public override int DefaultOrder => 60;
    public override Color DefaultAccent => Color.FromRgb(0xFF, 0xB9, 0x00);
    public override bool EnabledByDefault => false;
    protected override string CounterName => "Bytes Sent/sec";
    protected override string DirectionText => "Envoi";
}
