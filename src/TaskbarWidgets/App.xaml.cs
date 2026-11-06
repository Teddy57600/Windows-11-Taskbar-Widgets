using System.Windows;
using TaskbarWidgets.Services;

namespace TaskbarWidgets;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private WidgetManager? _manager;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\TaskbarWidgets.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Exception non gérée (UI)", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Exception non observée", args.Exception);
            args.SetObserved();
        };

        _manager = new WidgetManager();
        _manager.Load();
        _manager.WatchSettingsFile();

        var window = new TaskbarHostWindow(_manager);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _manager?.Dispose();
        if (_ownsMutex)
            _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
