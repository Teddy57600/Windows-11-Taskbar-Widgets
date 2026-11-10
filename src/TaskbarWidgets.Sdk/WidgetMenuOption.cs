namespace TaskbarWidgets.Sdk;

/// <summary>Un choix possible pour une <see cref="WidgetMenuOption"/>.</summary>
/// <param name="Value">Valeur enregistrée dans les options du widget (settings.json).</param>
/// <param name="Label">Texte affiché dans le menu.</param>
public sealed record WidgetMenuChoice(string Value, string Label);

/// <summary>
/// Option d'un widget proposée dans le menu contextuel (clic droit &gt; Réglages des widgets).
/// </summary>
/// <remarks>
/// Le choix de l'utilisateur est enregistré dans <c>options[Key]</c> du widget dans settings.json,
/// puis le widget est redémarré : il relit la valeur dans <see cref="WidgetContext"/> lors de
/// <see cref="TaskbarWidget.Initialize"/>.
/// </remarks>
/// <param name="Key">Clé de l'option (ex. « refreshMinutes »).</param>
/// <param name="Header">Titre du sous-menu (ex. « Actualisation »).</param>
/// <param name="DefaultValue">Valeur utilisée quand l'option n'est pas définie.</param>
/// <param name="Choices">Choix proposés.</param>
public sealed record WidgetMenuOption(string Key, string Header, string DefaultValue, IReadOnlyList<WidgetMenuChoice> Choices);
