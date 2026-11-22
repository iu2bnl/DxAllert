using System.Windows;
using DXAllert.App.Services;
using DXAllert.App.ViewModels;
using DXAllert.Core;
using DXAllert.Data;
using WinForms = System.Windows.Forms;

namespace DXAllert.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private WinForms.NotifyIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Con una cartella dati diversa (DXALLERT_DATA_DIR) log e istanza sono separati da quella normale.
        var instance = "";
        if (AppPaths.CustomDataDir is { } dir)
        {
            Log.Directory = System.IO.Path.Combine(dir, "logs");
            instance = "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(dir.ToUpperInvariant())))[..8];
        }

        // Una sola istanza: la seconda chiede alla prima di mostrarsi ed esce.
        _singleInstance = new Mutex(true, "DXAllert.SingleInstance" + instance, out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "DXAllert.ShowWindow" + instance);
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Errore non gestito", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Errore fatale", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Errore in un'attività in background", args.Exception);
            args.SetObserved();
        };

        Log.Info($"DXAllert {typeof(App).Assembly.GetName().Version} avviato");
        _vm = new MainViewModel();
        ApplyTheme(_vm.Settings.DarkTheme);
        _vm.ThemeChanged += ApplyTheme;
        _vm.AlertRaised += alert => Notifier.Show(alert, _vm.Settings.AlertSound);
        _vm.UpdateReady += (installer, args) => Dispatcher.BeginInvoke(() => ExitApp(installer, args));

        _window = new MainWindow(_vm);
        _window.Closing += (_, args) =>
        {
            // Chiudere la finestra non chiude il programma: resta nell'area di notifica.
            args.Cancel = true;
            _window.Hide();
            if (_trayHintShown) return;
            _trayHintShown = true;
            _tray?.ShowBalloonTip(4000, "DXAllert continua a monitorare",
                "Il programma resta nell'area di notifica. Per chiuderlo: clic destro sull'icona, poi Esci.",
                WinForms.ToolTipIcon.Info);
        };
        CreateTray();

        var waiter = new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
        }) { IsBackground = true };
        waiter.Start();

        if (!e.Args.Contains("--minimized")) ShowWindow();
        _ = _vm.StartAsync();
    }

    private void CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Apri DXAllert", null, (_, _) => ShowWindow());
        menu.Items.Add("Aggiorna adesso", null, (_, _) => _vm?.RefreshNowCommand.Execute(null));
        menu.Items.Add("Apri il file di log", null, (_, _) => _vm?.OpenLogCommand.Execute(null));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => ExitApp());

        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "DXAllert – monitoraggio DXpedition",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) ShowWindow();
        };
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Chiude il programma; con un installer lo lancia dopo aver liberato il mutex dell'istanza,
    /// così l'installer non trova DXAllert ancora in esecuzione.</summary>
    private void ExitApp(string? installer = null, string installerArgs = "")
    {
        Log.Info(installer is null ? "DXAllert chiuso dall'utente" : $"DXAllert chiuso per l'aggiornamento: {installer}");
        _vm?.Shutdown();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (installer is not null)
        {
            try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); _singleInstance = null; } catch { }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer, installerArgs) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("Avvio dell'installer non riuscito", ex); }
        }
        Shutdown();
    }

    private void ApplyTheme(bool dark)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative),
        };
        Resources.MergedDictionaries[0] = dict;
    }
}
