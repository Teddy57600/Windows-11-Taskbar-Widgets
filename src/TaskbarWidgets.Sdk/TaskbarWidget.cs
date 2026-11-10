using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace TaskbarWidgets.Sdk;

/// <summary>
/// Classe de base d'un widget de la barre des tâches.
/// </summary>
/// <remarks>
/// <para>Cycle de vie : le constructeur (sans paramètre, léger) → <see cref="Initialize"/> → <see cref="Update"/>
/// toutes les <see cref="UpdateInterval"/> → <see cref="Dispose"/>.</para>
/// <para><see cref="Initialize"/>, <see cref="Update"/> et <see cref="Dispose"/> s'exécutent sur un thread
/// d'arrière-plan, jamais en même temps. Ils peuvent modifier <see cref="Value"/>, <see cref="Level"/> et
/// <see cref="ToolTip"/> directement : WPF relaie ces changements vers l'interface.</para>
/// <para>Par défaut, l'hôte affiche le widget avec la vue standard (barre de niveau + libellé + valeur).
/// Surchargez <see cref="CreateView"/> pour fournir votre propre contenu WPF.</para>
/// </remarks>
public abstract class TaskbarWidget : INotifyPropertyChanged, IDisposable
{
    private string? _label;
    private Color? _accent;
    private Brush? _accentBrush;
    private string _value = "–";
    private double? _level;
    private string? _toolTip;

    /// <summary>Identifiant stable, utilisé dans settings.json (ex. « cpu »).</summary>
    public abstract string Id { get; }

    /// <summary>Nom affiché dans le menu contextuel.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Libellé court affiché au-dessus de la valeur (ex. « CPU »).</summary>
    public abstract string DefaultLabel { get; }

    /// <summary>Couleur d'accent par défaut (libellé et barre).</summary>
    public virtual Color DefaultAccent => Color.FromRgb(0x4C, 0xC2, 0xFF);

    /// <summary>Le widget est-il activé quand il est découvert pour la première fois ?</summary>
    public virtual bool EnabledByDefault => true;

    /// <summary>Rang proposé au premier ajout dans settings.json (plus petit = plus à gauche).</summary>
    public virtual int DefaultOrder => 100;

    /// <summary>Intervalle entre deux appels à <see cref="Update"/>.</summary>
    public virtual TimeSpan UpdateInterval => TimeSpan.FromSeconds(1);

    /// <summary>Largeur minimale (DIP) de la zone de texte, pour éviter que la barre ne « saute ».</summary>
    public virtual double MinValueWidth => 34;

    /// <summary>Libellé effectif (celui de settings.json s'il est défini, sinon <see cref="DefaultLabel"/>).</summary>
    public string Label
    {
        get => _label ?? DefaultLabel;
        set => SetField(ref _label, value);
    }

    /// <summary>Couleur d'accent effective.</summary>
    public Color Accent
    {
        get => _accent ?? DefaultAccent;
        set
        {
            _accent = value;
            _accentBrush = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccentBrush));
        }
    }

    /// <summary>Pinceau gelé correspondant à <see cref="Accent"/>.</summary>
    public Brush AccentBrush
    {
        get
        {
            if (_accentBrush is null)
            {
                var brush = new SolidColorBrush(Accent);
                brush.Freeze();
                _accentBrush = brush;
            }
            return _accentBrush;
        }
    }

    /// <summary>Texte principal (ex. « 28% »).</summary>
    public string Value
    {
        get => _value;
        protected internal set => SetField(ref _value, value);
    }

    /// <summary>Remplissage de la barre verticale, de 0 à 1 ; <c>null</c> = barre vide.</summary>
    public double? Level
    {
        get => _level;
        protected set => SetField(ref _level, value is double v ? Math.Clamp(v, 0, 1) : null);
    }

    /// <summary>Info-bulle affichée au survol ; <c>null</c> = aucune.</summary>
    public string? ToolTip
    {
        get => _toolTip;
        protected internal set => SetField(ref _toolTip, value);
    }

    /// <summary>Préparation (ouverture de compteurs, lecture des options…). Thread d'arrière-plan.</summary>
    public virtual void Initialize(WidgetContext context) { }

    /// <summary>Rafraîchit les valeurs. Thread d'arrière-plan.</summary>
    public abstract void Update();

    /// <summary>Vue WPF personnalisée (thread UI). <c>null</c> = vue standard. Son DataContext est le widget.</summary>
    public virtual FrameworkElement? CreateView() => null;

    /// <summary>Options réglables depuis le menu contextuel (aucune par défaut).</summary>
    public virtual IReadOnlyList<WidgetMenuOption> MenuOptions => [];

    /// <summary>Clic gauche sur le widget (thread UI).</summary>
    public virtual void OnClick() { }

    /// <summary>Libère les ressources. Thread d'arrière-plan.</summary>
    public virtual void Dispose() { }

    /// <summary>Affiche une erreur dans le widget (appelé par l'hôte).</summary>
    public void ReportError(string message)
    {
        Value = "!";
        Level = null;
        ToolTip = message;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
