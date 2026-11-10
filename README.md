<div align="center">

<img src="src/TaskbarWidgets/Assets/TaskbarWidgets-256.png" alt="Logo Taskbar Widgets" width="128" height="128" />

# Taskbar Widgets

**Vos indicateurs système, directement dans la barre des tâches de Windows 11.**

CPU · RAM · GPU · température · débit réseau, et vos propres widgets en C#.

[![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11&logoColor=white)](#prérequis)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-68217A)](#fonctionnement)
[![Plugins](https://img.shields.io/badge/plugins-C%23-239120?logo=csharp&logoColor=white)](#écrire-un-widget)
[![Licence MIT](https://img.shields.io/badge/licence-MIT-green)](LICENSE)

<img src="docs/screenshot.png" alt="Widgets CPU, RAM, GPU, TEMP, réseau et durée de fonctionnement dans la barre des tâches" width="520" />

</div>

---

## Table des matières

- [Points forts](#points-forts)
- [Prérequis](#prérequis)
- [Compiler et lancer](#compiler-et-lancer)
  - [Version distribuable : `dist\`](#version-distribuable--dist)
- [Utilisation](#utilisation)
- [Configuration](#configuration)
- [Widget Claude Code](#widget-claude-code)
  - [Prévision sur 7 jours](#prévision-sur-7-jours)
- [Écrire un widget](#écrire-un-widget)
- [Fonctionnement](#fonctionnement)
- [Structure](#structure)
- [Licence](#licence)

## Points forts

- 📊 **Widgets intégrés** : processeur, mémoire, GPU, température, réception et envoi réseau.
- 🤖 **Claude Code** : utilisation de l'abonnement sur 5 heures et 7 jours, et prévision au prochain reset.
- 🧩 **Plugins** : une DLL .NET dérivant de `TaskbarWidget` suffit pour ajouter un widget.
- 🖥️ **Multi-écran** : affichage sur la barre des tâches de l'écran de votre choix.
- ⚡ **Configuration en direct** : `settings.json` est appliqué dès son enregistrement.
- 🌗 **Thème clair / sombre** suivi automatiquement, masquage en plein écran.
- 🪶 **Léger** : environ 0,3 % d'un cœur processeur en fonctionnement.
- 📦 **Autonome** : un seul `.exe`, runtime .NET inclus, rien à installer.

## Prérequis

- Windows 11 (fonctionne aussi sous Windows 10)
- SDK .NET 10

## Compiler et lancer

```bash
dotnet build -c Release
```

```bash
src/TaskbarWidgets/bin/Release/net10.0-windows/TaskbarWidgets.exe
```

### Version distribuable : `dist\`

```bash
pwsh tools/publish.ps1
```

Le script régénère `dist\` à la racine : `TaskbarWidgets.exe` est un exécutable **autonome en un seul fichier**
(runtime .NET inclus, rien à installer). Copiez le dossier où vous voulez et lancez l'exe.
Le plugin d'exemple (durée de fonctionnement) est placé dans `dist\Widgets\` ; ajoutez `-NoSample` pour l'exclure.

## Utilisation

- **Clic droit** sur les widgets : activer/désactiver chaque widget, position (gauche/droite, décalage),
  écran (barre des tâches de n'importe quel écran ; nécessite « Afficher ma barre des tâches sur tous les écrans »),
  lancement au démarrage de Windows, édition de `settings.json`, rechargement, quitter.
- **Clic gauche** sur un widget système : ouvre le Gestionnaire des tâches.
- **Survol** : détails (zones thermiques, cartes réseau, mémoire utilisée…).

Les widgets sont masqués automatiquement quand la barre est masquée (masquage automatique)
ou qu'une application passe en plein écran, et se réattachent si `explorer.exe` redémarre.
Le thème clair/sombre de la barre est suivi.

## Configuration

`%APPDATA%\TaskbarWidgets\settings.json` (créé au premier lancement). **Les modifications sont appliquées
en direct** dès l'enregistrement du fichier (sans scrutation : Windows prévient l'application). Seuls les widgets
dont la configuration change sont redémarrés. Un fichier invalide (édition en cours) est ignoré, jamais écrasé.
Ajouter une nouvelle DLL de plugin demande en revanche « Recharger » dans le menu.

```jsonc
{
  "screen": "\\\\.\\DISPLAY2", // écran qui accueille les widgets ; absent = écran principal
  "anchor": "Left",          // Left : après le bouton Widgets de Windows ; Right : avant la zone de notification
  "offset": 70,              // distance au bord, en pixels logiques (DIP)
  "hideOnFullscreen": true,
  "widgets": [               // ordre d'affichage
    { "id": "cpu", "enabled": true },
    { "id": "ram", "enabled": true, "color": "#E39FF6" },
    { "id": "gpu", "enabled": true },
    { "id": "temp", "enabled": true },
    { "id": "net-down", "enabled": true, "label": "↓", "options": { "interface": "Wi-Fi" } },
    { "id": "net-up", "enabled": false }
  ]
}
```

| Id | Widget | Source |
|---|---|---|
| `cpu` | Utilisation processeur | compteur `Processor Information\% Processor Utility` (comme le Gestionnaire des tâches) |
| `ram` | Mémoire physique utilisée | `GlobalMemoryStatusEx` |
| `gpu` | Moteur GPU le plus chargé | compteurs `GPU Engine` |
| `temp` | Température maximale des zones ACPI | compteurs `Thermal Zone Information` |
| `net-down` / `net-up` | Débit réseau reçu / envoyé | compteurs `Network Interface` |

Options réseau : `interface` (ne compter que les cartes dont le nom contient ce texte),
`includeVirtual` (`"true"` pour inclure Hyper-V, VPN…).

> La température vient des zones thermiques exposées par le firmware (lisibles sans droits administrateur).
> Selon la carte mère, ce n'est pas forcément la température du cœur du processeur, et certaines machines
> n'en exposent aucune (le widget affiche alors « N/A »).

Les erreurs sont journalisées dans `%APPDATA%\TaskbarWidgets\log.txt`.

## Widget Claude Code

Le plugin `claude-usage` (livré dans `dist\Widgets\`) affiche l'utilisation de votre abonnement Claude Code
(Pro ou Max), comme la commande `/usage` :

- **barre de gauche** (orange) : fenêtre glissante de **5 heures** ;
- **barre de droite** (sable) : fenêtre de **7 jours** ;
- **valeur** : `42% · 18%` (5 h · 7 j) ; **info-bulle** : heures de réinitialisation et temps restant ;
- **clic** : ouvre la page d'utilisation de claude.ai.

**Fréquence de mise à jour** : clic droit > *Réglages des widgets* > *Claude Code – utilisation* > *Actualisation*
(de toutes les minutes à toutes les heures, 5 minutes par défaut), ou l'option `refreshMinutes` dans `settings.json` :

```jsonc
{ "id": "claude-usage", "enabled": true, "options": { "refreshMinutes": "10" } }
```

**Connexion** : le widget utilise la connexion de Claude Code en ligne de commande (`/login`), sans jamais
renouveler ni modifier le jeton. Il le cherche dans cet ordre :

1. le fichier indiqué par l'option `credentialsFile` ;
2. `%CLAUDE_CONFIG_DIR%\.credentials.json`, puis `%USERPROFILE%\.claude\.credentials.json` ;
3. en dernier recours, la variable d'environnement `CLAUDE_CODE_OAUTH_TOKEN`.

> Les jetons créés par `claude setup-token` **ne conviennent pas** : ils sont limités à l'utilisation des modèles
> et le service refuse de leur donner l'utilisation (`403 … scope requirement user:profile`).
>
> Le jeton de connexion expire au bout de quelques heures ; Claude Code le renouvelle quand il tourne.
> Si Claude Code n'a pas été utilisé depuis un moment, le widget l'indique et reprend dès que Claude Code a servi.

Le jeton n'est envoyé qu'au service d'Anthropic (`api.anthropic.com`) et n'est jamais journalisé.
En cas d'erreur temporaire, les dernières valeurs restent affichées et l'info-bulle signale le problème.

> Ce service est celui qu'utilise Claude Code ; il n'est pas documenté publiquement et peut évoluer.

### Prévision sur 7 jours

Le widget `claude-forecast` (libellé **PRÉV 7J**) estime l'utilisation de la fenêtre de 7 jours **au moment de sa
réinitialisation**, en prolongeant le rythme de consommation jusqu'au reset :

```
prévision = utilisation actuelle + rythme × temps restant avant le reset
```

- **couleur** : vert sous 80 %, orange de 80 à 100 %, rouge au-delà (sauf si `color` est fixée dans `settings.json`) ;
- **info-bulle** : date du reset, utilisation actuelle, rythme en % par jour, date à laquelle 100 % serait atteint
  si la prévision dépasse la limite, et marge quotidienne pour finir à 100 % ;
- **méthode** (clic droit > *Réglages des widgets* > *Claude Code – prévision 7 jours* > *Méthode de prévision*,
  ou l'option `method`) :
  - `window` (par défaut) : rythme moyen depuis le début de la fenêtre, le plus stable ;
  - `24h` : rythme des dernières 24 heures, plus réactif. Il s'appuie sur un historique local
    (`%APPDATA%\TaskbarWidgets\data\claude-forecast\history-7d.json`, un point toutes les 10 minutes) et se rabat
    sur la moyenne de la fenêtre tant que cet historique couvre moins de 3 heures.

Pendant les 6 premières heures d'une fenêtre, la prévision n'est pas affichée (trop instable). Les deux widgets
Claude partagent la même lecture du service : activer les deux ne double pas les requêtes.

## Écrire un widget

1. Créez une bibliothèque de classes `net10.0-windows` avec `<UseWPF>true</UseWPF>`.
2. Référencez `TaskbarWidgets.Sdk` **sans le copier** (`Private="false"`) : il est fourni par l'application.
3. Dérivez de `TaskbarWidget` :

```csharp
using System.Windows.Media;
using TaskbarWidgets.Sdk;

public sealed class HelloWidget : TaskbarWidget
{
    public override string Id => "hello";                 // clé dans settings.json
    public override string DisplayName => "Bonjour";      // nom dans le menu
    public override string DefaultLabel => "HELLO";       // petit libellé coloré
    public override Color DefaultAccent => Colors.Orange;
    public override TimeSpan UpdateInterval => TimeSpan.FromSeconds(5);

    public override void Initialize(WidgetContext context) { /* options : context.GetOption("clé") */ }

    public override void Update()                          // thread d'arrière-plan
    {
        Value = DateTime.Now.ToString("HH:mm");           // texte principal
        Level = DateTime.Now.Minute / 60.0;               // barre verticale 0..1 (null = vide)
        ToolTip = "Info-bulle";
    }
}
```

4. Copiez la DLL dans `%APPDATA%\TaskbarWidgets\Widgets\` (menu « Ouvrir le dossier des plugins »)
   ou dans le dossier `Widgets\` à côté de l'exécutable, puis « Recharger ».

Pour aller plus loin :
- `CreateView()` : renvoyez votre propre `FrameworkElement` WPF (son `DataContext` est le widget) ;
- `OnClick()` : action au clic gauche ;
- `MenuOptions` : options proposées dans le menu (clic droit > Réglages des widgets), enregistrées dans `options` ;
- `MinValueWidth`, `DefaultOrder`, `EnabledByDefault` ;
- `PdhQuery` : lecture simple des compteurs de performance Windows (chemins anglais, indépendants de la langue).

Exemple complet : [samples/TaskbarWidgets.Sample.Uptime](samples/TaskbarWidgets.Sample.Uptime) (il se copie
automatiquement dans le dossier `Widgets` de l'application à chaque compilation).

## Fonctionnement

Windows 11 ne propose pas d'API pour ajouter du contenu dans la barre des tâches (les « deskbands » ont disparu).
L'application crée donc une fenêtre WPF transparente, sans bouton ni entrée Alt+Tab, qui ne prend jamais le focus,
et la pose sur la barre :

- la fenêtre est **possédée par `Shell_TrayWnd`**, ce qui la maintient au-dessus de la barre même quand on clique dessus ;
- toutes les 500 ms, elle suit la position, la hauteur et le DPI de la barre, et se masque en plein écran
  (`SHQueryUserNotificationState`) ou quand la barre se masque ;
- le message `TaskbarCreated` signale un redémarrage d'`explorer.exe`.

Si l'écran choisi est débranché ou n'a plus de barre des tâches, les widgets reviennent sur l'écran principal
(et retournent sur l'écran choisi dès qu'il réapparaît). En plein écran, seuls les widgets de l'écran concerné sont masqués.

Limite connue : le décalage gauche (`offset`) est commun à tous les écrans ; il est à ajuster
si le bouton Widgets de Windows affiche la météo (il est alors plus large) ou si les icônes sont alignées à gauche.

## Structure

```
src/TaskbarWidgets.Sdk/     API des widgets (TaskbarWidget, WidgetContext, PdhQuery, Units)
src/TaskbarWidgets/         application hôte WPF + widgets intégrés
plugins/…ClaudeUsage/       plugins Claude Code : utilisation (vue à deux barres) et prévision 7 jours
samples/…Sample.Uptime/     exemple de plugin minimal
tools/publish.ps1           génère dist\ (exécutable autonome prêt à l'emploi)
tools/make-icon.ps1         génère l'icône de l'exécutable (src/TaskbarWidgets/Assets/TaskbarWidgets.ico)
```

## Licence

Distribué sous licence [MIT](LICENSE).
