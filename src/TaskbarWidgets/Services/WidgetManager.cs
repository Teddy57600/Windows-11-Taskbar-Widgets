using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.Services;

/// <summary>Un type de widget découvert (intégré ou plugin).</summary>
public sealed record WidgetDescriptor(
    string Id, string DisplayName, bool EnabledByDefault, int DefaultOrder,
    IReadOnlyList<WidgetMenuOption> MenuOptions, Type Type, string Source)
{
    public TaskbarWidget Create() => (TaskbarWidget)Activator.CreateInstance(Type)!;
}

/// <summary>
/// Découvre les widgets, applique settings.json, et fait tourner la boucle de mise à jour de chacun.
/// </summary>
public sealed class WidgetManager : IDisposable
{
    private readonly List<WidgetDescriptor> _available = [];
    private readonly List<TaskbarWidget> _active = [];
    private readonly List<RunningWidget> _running = [];

    public AppSettings Settings { get; private set; } = new();
    public IReadOnlyList<WidgetDescriptor> Available => _available;
    public IReadOnlyList<TaskbarWidget> Active => _active;

    /// <summary>Levé (thread UI) quand la liste des widgets actifs change.</summary>
    public event Action? WidgetsChanged;

    /// <summary>Dossiers où sont cherchés les plugins (*.dll).</summary>
    public static IReadOnlyList<string> PluginDirectories { get; } =
    [
        Path.Combine(AppContext.BaseDirectory, "Widgets"),
        Path.Combine(SettingsStore.DataDirectory, "Widgets"),
    ];

    /// <summary>Levé (thread UI) quand settings.json a été modifié de l'extérieur (position, écran…).</summary>
    public event Action? SettingsChanged;

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private string? _knownText;   // dernier contenu de settings.json lu ou écrit par l'application
    private int _readRetries;
    private bool _invalidReported;

    public void Load()
    {
        Settings = SettingsStore.Load();
        _knownText = TryReadText();
        Discover();
        Start();
    }

    /// <summary>Recherche de nouveaux plugins, relecture de settings.json et redémarrage de tous les widgets.</summary>
    public void Reload()
    {
        // Un fichier en cours d'édition et invalide ne doit pas être remplacé par les réglages par défaut.
        string? text = TryReadText();
        if (text is not null && !SettingsStore.TryParse(text, out _, out var error))
        {
            Log.Error("Rechargement annulé : settings.json est invalide.", error);
            return;
        }
        Stop();
        Load();
        WidgetsChanged?.Invoke();
    }

    /// <summary>
    /// Suit settings.json : toute modification enregistrée par l'utilisateur est appliquée en direct.
    /// Coût nul au repos : Windows notifie les écritures (aucune scrutation).
    /// </summary>
    public void WatchSettingsFile()
    {
        if (_watcher is not null)
            return;

        var dispatcher = Dispatcher.CurrentDispatcher;
        // Les éditeurs écrivent souvent en plusieurs fois (ou via un fichier temporaire renommé) :
        // on attend un court silence avant de relire.
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            ApplyExternalChange();
        }, dispatcher);
        _debounce.Stop();

        Directory.CreateDirectory(SettingsStore.DataDirectory);
        _watcher = new FileSystemWatcher(SettingsStore.DataDirectory, Path.GetFileName(SettingsStore.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        FileSystemEventHandler onChange = (_, _) => dispatcher.BeginInvoke(RestartDebounce);
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (_, _) => dispatcher.BeginInvoke(RestartDebounce);
        _watcher.Error += (_, e) => Log.Error("Surveillance de settings.json interrompue", e.GetException());
        _watcher.EnableRaisingEvents = true;
    }

    private void RestartDebounce()
    {
        _debounce?.Stop();
        _debounce?.Start();
    }

    private void ApplyExternalChange()
    {
        string? text;
        try
        {
            text = SettingsStore.ReadText();
        }
        catch (IOException)
        {
            // Fichier encore verrouillé par l'éditeur : on réessaie un peu plus tard.
            if (++_readRetries <= 10)
                RestartDebounce();
            return;
        }
        _readRetries = 0;

        // Fichier supprimé, ou écriture faite par l'application elle-même : rien à faire.
        if (text is null || text == _knownText)
            return;

        if (!SettingsStore.TryParse(text, out var updated, out var error))
        {
            // Probablement une édition en cours : on garde la configuration actuelle.
            if (!_invalidReported)
                Log.Error("settings.json invalide : modification ignorée jusqu'au prochain enregistrement valide.", error);
            _invalidReported = true;
            return;
        }
        _invalidReported = false;
        _knownText = text;

        bool widgetsChanged = SettingsStore.Canonical(updated.Widgets) != SettingsStore.Canonical(Settings.Widgets);
        if (widgetsChanged)
        {
            Settings = updated;
            Start(); // ne redémarre que les widgets dont la configuration a changé
            WidgetsChanged?.Invoke();
        }
        else
        {
            Settings = updated; // position, écran… : les widgets continuent de tourner
        }
        SettingsChanged?.Invoke();
    }

    private static string? TryReadText()
    {
        try { return SettingsStore.ReadText(); }
        catch (IOException) { return null; }
    }

    public WidgetConfig GetConfig(string id)
        => Settings.Widgets.First(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public void SetEnabled(string id, bool enabled)
    {
        GetConfig(id).Enabled = enabled;
        Save();
        Start();
        WidgetsChanged?.Invoke();
    }

    /// <summary>Valeur actuelle d'une option de widget (celle de settings.json, sinon la valeur par défaut).</summary>
    public string GetOption(string id, WidgetMenuOption option)
        => GetConfig(id).Options is { } options && options.TryGetValue(option.Key, out var value) ? value : option.DefaultValue;

    /// <summary>Modifie une option de widget, l'enregistre et redémarre ce widget seulement.</summary>
    public void SetOption(string id, string key, string value)
    {
        var config = GetConfig(id);
        config.Options ??= new Dictionary<string, string>();
        config.Options[key] = value;
        Save();
        Start();
        WidgetsChanged?.Invoke();
    }

    public void Save() => _knownText = SettingsStore.Save(Settings) ?? _knownText;

    private void Discover()
    {
        _available.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddFrom(typeof(WidgetManager).Assembly, "intégré");

        foreach (string directory in PluginDirectories)
        {
            if (!Directory.Exists(directory))
                continue;
            foreach (string dll in Directory.EnumerateFiles(directory, "*.dll"))
            {
                if (Path.GetFileName(dll).Equals("TaskbarWidgets.Sdk.dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    AddFrom(Assembly.LoadFrom(dll), dll);
                }
                catch (Exception ex)
                {
                    Log.Error($"Plugin ignoré : {dll}", ex);
                }
            }
        }

        var sorted = _available.OrderBy(d => d.DefaultOrder).ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
        _available.Clear();
        _available.AddRange(sorted);

        void AddFrom(Assembly assembly, string source)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !type.IsSubclassOf(typeof(TaskbarWidget)) || type.GetConstructor(Type.EmptyTypes) is null)
                    continue;
                try
                {
                    using var probe = (TaskbarWidget)Activator.CreateInstance(type)!;
                    if (seen.Add(probe.Id))
                        _available.Add(new WidgetDescriptor(
                            probe.Id, probe.DisplayName, probe.EnabledByDefault, probe.DefaultOrder,
                            probe.MenuOptions, type, source));
                    else
                        Log.Error($"Widget « {probe.Id} » en double ignoré ({source}).");
                }
                catch (Exception ex)
                {
                    Log.Error($"Impossible de créer le widget {type.FullName} ({source})", ex);
                }
            }
        }
    }

    private void Start()
    {
        // Ajoute à la configuration les widgets jamais vus (nouveau plugin, premier lancement).
        bool changed = false;
        foreach (var d in _available)
        {
            if (!Settings.Widgets.Any(c => string.Equals(c.Id, d.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Settings.Widgets.Add(new WidgetConfig { Id = d.Id, Enabled = d.EnabledByDefault });
                changed = true;
            }
        }
        if (changed)
            Save();

        // Les widgets dont la configuration n'a pas bougé continuent de tourner (pas de « – » d'une seconde) ;
        // les autres sont arrêtés ou (re)créés.
        var previous = _running.ToList();
        _running.Clear();
        _active.Clear();

        foreach (var config in Settings.Widgets.Where(c => c.Enabled))
        {
            var descriptor = _available.FirstOrDefault(d => string.Equals(d.Id, config.Id, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
                continue; // plugin désinstallé : on garde sa configuration

            string configKey = SettingsStore.Canonical(config);
            var reused = previous.FirstOrDefault(r => r.Descriptor == descriptor && r.ConfigKey == configKey);
            if (reused is not null)
            {
                previous.Remove(reused);
                _running.Add(reused);
                _active.Add(reused.Widget);
                continue;
            }

            TaskbarWidget widget;
            try
            {
                widget = descriptor.Create();
                if (!string.IsNullOrWhiteSpace(config.Label))
                    widget.Label = config.Label;
                if (TryParseColor(config.Color, out var color))
                    widget.Accent = color;
            }
            catch (Exception ex)
            {
                Log.Error($"Création du widget « {config.Id} » impossible", ex);
                continue;
            }

            var cts = new CancellationTokenSource();
            _running.Add(new RunningWidget(descriptor, widget, configKey, cts));
            _active.Add(widget);
            var context = new WidgetContext(
                config.Options ?? new Dictionary<string, string>(),
                Path.Combine(SettingsStore.DataDirectory, "data", descriptor.Id));
            _ = RunAsync(widget, context, cts.Token);
        }

        foreach (var obsolete in previous)
            obsolete.Cts.Cancel();
    }

    private void Stop()
    {
        // Chaque boucle libère son widget en sortant : pas d'appel concurrent à Update/Dispose.
        // (Les CancellationTokenSource ne sont pas libérés ici : la boucle utilise encore leur jeton.)
        foreach (var running in _running)
            running.Cts.Cancel();
        _running.Clear();
        _active.Clear();
    }

    private sealed record RunningWidget(WidgetDescriptor Descriptor, TaskbarWidget Widget, string ConfigKey, CancellationTokenSource Cts);

    private static async Task RunAsync(TaskbarWidget widget, WidgetContext context, CancellationToken token)
    {
        try
        {
            await Task.Run(() => widget.Initialize(context), token);

            // Les compteurs « taux » ont besoin de deux échantillons espacés : on attend un peu avant le premier.
            var firstDelay = widget.UpdateInterval < TimeSpan.FromSeconds(1) ? widget.UpdateInterval : TimeSpan.FromSeconds(1);
            await Task.Delay(firstDelay, token);

            using var timer = new PeriodicTimer(widget.UpdateInterval);
            do
            {
                try
                {
                    await Task.Run(widget.Update, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    widget.ReportError(ex.Message);
                    Log.Error($"Erreur de mise à jour du widget « {widget.Id} »", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            widget.ReportError($"Initialisation impossible : {ex.Message}");
            Log.Error($"Erreur d'initialisation du widget « {widget.Id} »", ex);
        }
        finally
        {
            try { await Task.Run(widget.Dispose); }
            catch (Exception ex) { Log.Error($"Erreur à la libération du widget « {widget.Id} »", ex); }
        }
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Stop();
        Stop();
    }
}
