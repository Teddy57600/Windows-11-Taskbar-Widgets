using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TaskbarWidgets.Interop;
using TaskbarWidgets.Sdk;
using TaskbarWidgets.Services;
using TaskbarWidgets.Views;
using static TaskbarWidgets.Interop.NativeMethods;

namespace TaskbarWidgets;

/// <summary>
/// Fenêtre transparente posée sur la barre des tâches.
/// </summary>
/// <remarks>
/// La fenêtre est « possédée » par Shell_TrayWnd : Windows la garde donc toujours au-dessus de la barre,
/// même quand on clique dessus. Un minuteur suit la position et la taille de la barre (DPI, masquage
/// automatique, redémarrage d'explorer.exe) et la masque quand une application passe en plein écran.
/// </remarks>
public partial class TaskbarHostWindow : Window
{
    private readonly WidgetManager _manager;
    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd;
    private IntPtr _taskbar;
    private uint _taskbarCreatedMessage;
    private bool _hidden;
    private RECT _lastBounds;
    private bool? _lightTheme;
    private TaskbarInfo? _current;
    private bool _screenUnavailable;

    public TaskbarHostWindow(WidgetManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _manager.WidgetsChanged += () => { BuildWidgets(); UpdatePlacement(); };
        _manager.SettingsChanged += () => { _lastBounds = default; UpdatePlacement(); };

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => { ApplyTheme(); UpdatePlacement(); };

        Root.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        WidgetsPanel.SizeChanged += (_, _) => UpdatePlacement(); // une valeur plus large : on suit tout de suite
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Pas de bouton dans la barre, pas d'Alt+Tab, et la fenêtre ne vole jamais le focus.
        long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        BuildWidgets();
        ApplyTheme();
        UpdatePlacement();
        _timer.Start();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        // Passage sur un écran d'une autre échelle : WPF impose sa propre position, on replace aussitôt.
        base.OnDpiChanged(oldDpi, newDpi);
        _lastBounds = default;
        Dispatcher.BeginInvoke(UpdatePlacement, DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMessage)
        {
            // explorer.exe a redémarré : nouvelle barre des tâches, il faut se réattacher.
            _taskbar = IntPtr.Zero;
            _lastBounds = default;
            Dispatcher.BeginInvoke(UpdatePlacement, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private void BuildWidgets()
    {
        WidgetsPanel.Children.Clear();
        foreach (var widget in _manager.Active)
        {
            FrameworkElement view;
            try
            {
                view = widget.CreateView() ?? new MetricView();
            }
            catch (Exception ex)
            {
                Log.Error($"Vue du widget « {widget.Id} » impossible à créer", ex);
                view = new MetricView();
            }
            view.DataContext = widget;
            view.MouseLeftButtonUp += (_, _) => SafeClick(widget);

            WidgetsPanel.Children.Add(new Rectangle
            {
                Width = 1,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)FindResource("SeparatorBrush"),
            });
            WidgetsPanel.Children.Add(view);
        }
    }

    private static void SafeClick(TaskbarWidget widget)
    {
        try { widget.OnClick(); }
        catch (Exception ex) { Log.Error($"Clic sur le widget « {widget.Id} »", ex); }
    }

    private void ApplyTheme()
    {
        bool light = Taskbar.UsesLightTheme();
        if (_lightTheme == light)
            return;
        _lightTheme = light;

        Resources["ValueBrush"] = Frozen(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        Resources["BarTrackBrush"] = Frozen(light ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Resources["SeparatorBrush"] = Frozen(light ? Color.FromArgb(0x33, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        foreach (var separator in WidgetsPanel.Children.OfType<Rectangle>())
            separator.Fill = (Brush)Resources["SeparatorBrush"];

        static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    private void UpdatePlacement()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        var settings = _manager.Settings;
        var target = Taskbar.Find(settings.Screen, out _screenUnavailable);
        _current = target;
        if (target is null)
        {
            SetHidden(true);
            return;
        }

        IntPtr taskbar = target.Hwnd;
        if (taskbar != _taskbar)
        {
            // Nouvelle barre (changement d'écran ou redémarrage d'explorer.exe) : elle devient propriétaire.
            _taskbar = taskbar;
            _lastBounds = default;
            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, taskbar);
        }

        if (WidgetsPanel.Children.Count == 0
            || !GetWindowRect(taskbar, out var bar)
            || !Taskbar.IsShown(target, bar)
            || (settings.HideOnFullscreen && Taskbar.IsFullscreenAppRunning(target)))
        {
            SetHidden(true);
            return;
        }

        uint dpi = GetDpiForWindow(taskbar);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var content = WidgetsPanel.DesiredSize;
        Canvas.SetTop(WidgetsPanel, Math.Max(0, Math.Round((bar.Height / scale - content.Height) / 2)));
        int width = (int)Math.Ceiling(content.Width * scale);
        int height = bar.Height;
        int offset = (int)Math.Round(settings.Offset * scale);
        int x = settings.Anchor == TaskbarAnchor.Right ? bar.Right - offset - width : bar.Left + offset;
        int y = bar.Top;

        var bounds = new RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
        if (!bounds.Equals(_lastBounds))
        {
            _lastBounds = bounds;
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
        }
        else
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        SetHidden(false);
    }

    private void SetHidden(bool hidden)
    {
        if (_hidden == hidden)
            return;
        _hidden = hidden;
        ShowWindow(_hwnd, hidden ? SW_HIDE : SW_SHOWNOACTIVATE);
    }

    private void ShowMenu()
    {
        var settings = _manager.Settings;
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem { Header = "Taskbar Widgets", IsEnabled = false });
        menu.Items.Add(new Separator());

        foreach (var descriptor in _manager.Available)
        {
            var item = new MenuItem
            {
                Header = descriptor.DisplayName,
                IsCheckable = true,
                IsChecked = _manager.GetConfig(descriptor.Id).Enabled,
                ToolTip = descriptor.Source == "intégré" ? null : descriptor.Source,
            };
            string id = descriptor.Id;
            item.Click += (_, _) => _manager.SetEnabled(id, item.IsChecked);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var position = new MenuItem { Header = "Position" };
        var left = new MenuItem { Header = "Ancrer à gauche", IsCheckable = true, IsChecked = settings.Anchor == TaskbarAnchor.Left };
        var right = new MenuItem { Header = "Ancrer à droite", IsCheckable = true, IsChecked = settings.Anchor == TaskbarAnchor.Right };
        left.Click += (_, _) => ChangeSettings(s => s.Anchor = TaskbarAnchor.Left);
        right.Click += (_, _) => ChangeSettings(s => s.Anchor = TaskbarAnchor.Right);
        var closer = new MenuItem { Header = "Rapprocher du bord", StaysOpenOnClick = true };
        var farther = new MenuItem { Header = "Éloigner du bord", StaysOpenOnClick = true };
        closer.Click += (_, _) => ChangeSettings(s => s.Offset = Math.Max(0, s.Offset - 8));
        farther.Click += (_, _) => ChangeSettings(s => s.Offset += 8);
        var fullscreen = new MenuItem { Header = "Masquer en plein écran", IsCheckable = true, IsChecked = settings.HideOnFullscreen };
        fullscreen.Click += (_, _) => ChangeSettings(s => s.HideOnFullscreen = fullscreen.IsChecked);
        position.Items.Add(left);
        position.Items.Add(right);
        position.Items.Add(new Separator());
        position.Items.Add(closer);
        position.Items.Add(farther);
        position.Items.Add(new Separator());
        position.Items.Add(fullscreen);
        menu.Items.Add(position);
        menu.Items.Add(BuildScreenMenu(settings));

        var startup = new MenuItem { Header = "Lancer au démarrage de Windows", IsCheckable = true, IsChecked = SafeIsStartupEnabled() };
        startup.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(startup.IsChecked); }
            catch (Exception ex) { Log.Error("Démarrage automatique", ex); }
        };
        menu.Items.Add(startup);

        var openSettings = new MenuItem { Header = "Modifier settings.json…" };
        openSettings.Click += (_, _) => OpenSettingsFile();
        menu.Items.Add(openSettings);

        var openPlugins = new MenuItem { Header = "Ouvrir le dossier des plugins" };
        openPlugins.Click += (_, _) => OpenPluginFolder();
        menu.Items.Add(openPlugins);

        var reload = new MenuItem { Header = "Recharger" };
        reload.Click += (_, _) => _manager.Reload();
        menu.Items.Add(reload);

        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "Quitter" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        // La fenêtre est WS_EX_NOACTIVATE : on l'active le temps du menu pour qu'il se ferme
        // correctement quand on clique ailleurs.
        SetForegroundWindow(_hwnd);
        menu.PlacementTarget = Root;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private MenuItem BuildScreenMenu(AppSettings settings)
    {
        var screen = new MenuItem { Header = "Écran" };
        var taskbars = Taskbar.FindAll();
        var primary = taskbars.FirstOrDefault(t => t.IsPrimary);

        foreach (var taskbar in taskbars)
        {
            var item = new MenuItem
            {
                Header = Taskbar.Describe(taskbar, primary),
                IsCheckable = true,
                IsChecked = _current is not null && taskbar.DeviceName == _current.DeviceName,
            };
            // L'écran principal est enregistré comme « null » : les widgets le suivent s'il change.
            string? device = taskbar.IsPrimary ? null : taskbar.DeviceName;
            item.Click += (_, _) => ChangeSettings(s => s.Screen = device);
            screen.Items.Add(item);
        }

        if (_screenUnavailable)
        {
            screen.Items.Add(new Separator());
            screen.Items.Add(new MenuItem
            {
                Header = $"{settings.Screen} indisponible : affichage sur l'écran principal",
                IsEnabled = false,
            });
        }

        if (taskbars.Count < Taskbar.MonitorCount)
        {
            screen.Items.Add(new Separator());
            screen.Items.Add(new MenuItem
            {
                Header = "Pour les autres écrans, activez « Afficher ma barre des tâches sur tous les écrans »",
                IsEnabled = false,
            });
        }

        return screen;
    }

    private void ChangeSettings(Action<AppSettings> change)
    {
        change(_manager.Settings);
        _manager.Save();
        _lastBounds = default;
        UpdatePlacement();
    }

    private static bool SafeIsStartupEnabled()
    {
        try { return StartupManager.IsEnabled; }
        catch { return false; }
    }

    private void OpenSettingsFile()
    {
        _manager.Save(); // garantit que le fichier existe
        try
        {
            Process.Start(new ProcessStartInfo(SettingsStore.FilePath) { UseShellExecute = true });
        }
        catch
        {
            // Aucune application associée aux .json : on se rabat sur le Bloc-notes.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{SettingsStore.FilePath}\"") { UseShellExecute = true });
        }
    }

    private static void OpenPluginFolder()
    {
        string folder = WidgetManager.PluginDirectories[1];
        System.IO.Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
