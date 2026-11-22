using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DXAllert.App.ViewModels;
using DXAllert.Core;
using DXAllert.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DXAllert.App;

public partial class MainWindow : Window
{
    private const string MapHost = "dxallert.map";
    private readonly MainViewModel _vm;
    private bool _mapStarted;
    private bool _mapReady;

    /// <summary>Minimappa a destra di una scheda: mostra il QTH, la DXpedition selezionata e il percorso corto.</summary>
    private sealed class MiniMap
    {
        public MiniMap(WebView2 view, string dataDir, Func<MiniTarget?> current)
        {
            View = view;
            DataDir = dataDir;
            Current = current;
        }

        public WebView2 View { get; }
        public string DataDir { get; }
        public Func<MiniTarget?> Current { get; }
        public bool Started { get; set; }
        public bool Ready { get; set; }
    }

    private sealed record MiniTarget(double Lat, double Lon, string Call, string Entity);

    private readonly MiniMap[] _miniMaps;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.MapDataChanged += json => { if (_mapReady) _ = RunMapScript($"dxUpdate({json})"); };
        vm.QrzPasswordSaved += () => QrzPassword.Clear();
        vm.CountyDataChanged += () => Dispatcher.BeginInvoke(() => { PushCounties(); PushRegions(); });
        vm.ThemeChanged += _ => { PushCounties(); PushRegions(); };
        if (vm.NeedsSetup) Tabs.SelectedItem = SettingsTab;

        _miniMaps = new[]
        {
            new MiniMap(ActiveMiniMap, "WebView2MiniActive", () => Target(_vm.SelectedActive)),
            new MiniMap(SpotMiniMap, "WebView2MiniSpot", () => _vm.SelectedSpot is { EntityLat: { } lat, EntityLon: { } lon } s
                ? new MiniTarget(lat, lon, s.DxCall, s.Entity) : null),
        };
        vm.PropertyChanged += (_, args) =>
        {
            var view = args.PropertyName switch
            {
                nameof(MainViewModel.SelectedActive) => ActiveMiniMap,
                nameof(MainViewModel.SelectedSpot) => SpotMiniMap,
                _ => null,
            };
            if (view is not null) ShowOnMini(_miniMaps.First(m => m.View == view));
        };
        vm.HomeChanged += () =>
        {
            foreach (var m in _miniMaps.Where(m => m.Ready)) { SendHome(m); ShowOnMini(m); }
        };
    }

    private static MiniTarget? Target(DxRow? row) =>
        row?.Dx.Entity is { } e ? new MiniTarget(e.Latitude, e.Longitude, row.Callsign, row.Entity) : null;

    private void QrzPassword_Changed(object sender, RoutedEventArgs e) => _vm.EditQrzPassword = QrzPassword.Password;

    private async void ImportAdif_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Scegli il file ADIF del tuo log",
            Filter = "Log ADIF (*.adi;*.adif)|*.adi;*.adif|Tutti i file (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true) await _vm.ImportAdifAsync(dialog.FileName);
    }

    private void QrzCall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string call && call.Length > 0)
            MainViewModel.Shell($"https://www.qrz.com/db/{Uri.EscapeDataString(call)}");
    }

    private void HamQthCall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string call && call.Length > 0)
            MainViewModel.Shell($"https://www.hamqth.com/{Uri.EscapeDataString(call)}");
    }

    private void OpenContestRules_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ContestRow { HasUrl: true } row) MainViewModel.Shell(row.Url);
    }

    private void OpenQrz_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Qrz.HasCall) MainViewModel.Shell($"https://www.qrz.com/db/{Uri.EscapeDataString(_vm.Qrz.Call)}");
    }

    /// <summary>
    /// I file delle mappe (html, Leaflet, dati) sono serviti dal programma stesso: così funziona
    /// anche se la cartella d'installazione non è leggibile dal processo di WebView2.
    /// </summary>
    private static void ServeMapAssets(CoreWebView2 core)
    {
        var mapDir = Path.Combine(AppContext.BaseDirectory, "Assets", "map");
        core.AddWebResourceRequestedFilter($"https://{MapHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            var name = Path.GetFileName(new Uri(args.Request.Uri).AbsolutePath);
            var file = Path.Combine(mapDir, name);
            if (name.Length == 0 || !File.Exists(file))
            {
                args.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var type = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".png" => "image/png",
                _ => "application/octet-stream",
            };
            args.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(File.ReadAllBytes(file)), 200, "OK", $"Content-Type: {type}");
        };
    }

    // ---------------------------------------------------------------- contee USA

    private bool _countyStarted, _countyReady;

    // Come le altre mappe, si inizializza la prima volta che la scheda viene aperta.
    private async void CountyMap_Loaded(object sender, RoutedEventArgs e)
    {
        if (_countyStarted) return;
        _countyStarted = true;
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, "WebView2Counties"));
            await CountyMap.EnsureCoreWebView2Async(env);
            var core = CountyMap.CoreWebView2;
            ServeMapAssets(core);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, args) => { args.Handled = true; MainViewModel.Shell(args.Uri); };
            core.NavigationStarting += (_, args) =>
            {
                if (new Uri(args.Uri).Host == MapHost) return;
                args.Cancel = true;
                MainViewModel.Shell(args.Uri);
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) return;
                _countyReady = true;
                CountyMessage.Visibility = Visibility.Collapsed;
                PushCounties();
            };
            core.Navigate($"https://{MapHost}/counties.html");
        }
        catch (Exception ex)
        {
            Log.Error("Mappa delle contee non disponibile", ex);
            CountyMap.Visibility = Visibility.Collapsed;
            CountyMessage.Text = "La mappa non è disponibile: manca il componente Microsoft Edge WebView2 Runtime.";
        }
    }

    // ---------------------------------------------------------------- prefetture giapponesi e province cinesi

    private bool _japanStarted, _chinaStarted, _japanReady, _chinaReady;

    private void JapanMap_Loaded(object sender, RoutedEventArgs e)
    {
        if (_japanStarted) return;
        _japanStarted = true;
        StartRegionMap(JapanMap, JapanMessage, "WebView2Japan", "jp", () => _japanReady = true);
    }

    private void ChinaMap_Loaded(object sender, RoutedEventArgs e)
    {
        if (_chinaStarted) return;
        _chinaStarted = true;
        StartRegionMap(ChinaMap, ChinaMessage, "WebView2China", "cn", () => _chinaReady = true);
    }

    // Come la mappa delle contee: si inizializza la prima volta che la scheda viene aperta.
    private async void StartRegionMap(WebView2 view, System.Windows.Controls.TextBlock message, string profile, string set, Action ready)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, profile));
            await view.EnsureCoreWebView2Async(env);
            var core = view.CoreWebView2;
            ServeMapAssets(core);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, args) => { args.Handled = true; MainViewModel.Shell(args.Uri); };
            core.NavigationStarting += (_, args) =>
            {
                if (new Uri(args.Uri).Host == MapHost) return;
                args.Cancel = true;
                MainViewModel.Shell(args.Uri);
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) return;
                ready();
                message.Visibility = Visibility.Collapsed;
                PushRegions();
            };
            core.Navigate($"https://{MapHost}/regions.html?set={set}");
        }
        catch (Exception ex)
        {
            Log.Error($"Mappa {set} non disponibile", ex);
            view.Visibility = Visibility.Collapsed;
            message.Text = "La mappa non è disponibile: manca il componente Microsoft Edge WebView2 Runtime.";
        }
    }

    private async void PushRegions()
    {
        try
        {
            if (_japanReady) await JapanMap.ExecuteScriptAsync($"regionUpdate({_vm.BuildRegionJson("jp")})");
            if (_chinaReady) await ChinaMap.ExecuteScriptAsync($"regionUpdate({_vm.BuildRegionJson("cn")})");
        }
        catch (Exception ex) { Log.Warn($"Mappa prefetture/province: {ex.Message}"); }
    }

    private async void PushCounties()
    {
        if (!_countyReady) return;
        try { await CountyMap.ExecuteScriptAsync($"countyUpdate({_vm.BuildCountyJson()})"); }
        catch (Exception ex) { Log.Warn($"Mappa contee: {ex.Message}"); }
    }

    // La mappa si inizializza la prima volta che la scheda viene aperta.
    private async void Map_Loaded(object sender, RoutedEventArgs e)
    {
        if (_mapStarted) return;
        _mapStarted = true;
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, "WebView2"));
            await Map.EnsureCoreWebView2Async(env);
            var core = Map.CoreWebView2;
            ServeMapAssets(core);

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // I link (es. crediti OpenStreetMap) si aprono nel browser predefinito.
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                MainViewModel.Shell(args.Uri);
            };
            core.NavigationStarting += (_, args) =>
            {
                if (new Uri(args.Uri).Host == MapHost) return;
                args.Cancel = true;
                MainViewModel.Shell(args.Uri);
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) return;
                _mapReady = true;
                MapMessage.Visibility = Visibility.Collapsed;
                _ = RunMapScript($"dxUpdate({_vm.BuildMapJson()})");
            };
            core.Navigate($"https://{MapHost}/map.html");
        }
        catch (Exception ex)
        {
            Log.Error("Mappa non disponibile", ex);
            Map.Visibility = Visibility.Collapsed;
            MapMessage.Text = "La mappa non è disponibile: manca il componente Microsoft Edge WebView2 Runtime. " +
                              "Si scarica gratis da developer.microsoft.com/microsoft-edge/webview2 (Evergreen Bootstrapper).";
        }
    }

    // Ogni minimappa si inizializza la prima volta che la sua scheda viene aperta.
    private async void MiniMap_Loaded(object sender, RoutedEventArgs e)
    {
        var m = _miniMaps.First(x => x.View == sender);
        if (m.Started) return;
        m.Started = true;
        await InitMiniMapAsync(m);
    }

    private async Task InitMiniMapAsync(MiniMap m)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, m.DataDir));
            await m.View.EnsureCoreWebView2Async(env);
            var core = m.View.CoreWebView2;
            ServeMapAssets(core);

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, args) => { args.Handled = true; };
            core.NavigationStarting += (_, args) =>
            {
                if (new Uri(args.Uri).Host != MapHost) args.Cancel = true;
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) return;
                m.Ready = true;
                SendHome(m);
                ShowOnMini(m);
            };
            core.Navigate($"https://{MapHost}/minimap.html");
        }
        catch (Exception ex) { Log.Warn($"Minimappa non disponibile: {ex.Message}"); }
    }

    private void SendHome(MiniMap m) => _ = RunMiniScript(m.View, $"setHome({_vm.HomeJson()})");

    private void ShowOnMini(MiniMap m)
    {
        if (!m.Ready) return;
        var t = m.Current();
        var script = t is null
            ? "clearDx()"
            : $"showDx({JsonSerializer.Serialize(new { lat = t.Lat, lon = t.Lon, call = t.Call, entity = t.Entity })})";
        _ = RunMiniScript(m.View, script);
    }

    private static async Task RunMiniScript(WebView2 miniMap, string script)
    {
        try { await miniMap.ExecuteScriptAsync(script); }
        catch (Exception ex) { Log.Warn($"Minimappa script: {ex.Message}"); }
    }

    private async Task RunMapScript(string script)
    {
        try { await Map.ExecuteScriptAsync(script); }
        catch (Exception ex) { Log.Warn($"Mappa: {ex.Message}"); }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf((sender as FrameworkElement)?.DataContext) is { } row) MainViewModel.Shell(row.Url);
    }

    private void ShowOnMap_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf((sender as FrameworkElement)?.DataContext) is { } row) FocusOnMap(row);
    }

    private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowOf((e.OriginalSource as FrameworkElement)?.DataContext) is { } row) FocusOnMap(row);
    }

    /// <summary>La DXpedition di una riga: direttamente, o quella a cui appartiene uno spot.</summary>
    private static DxRow? RowOf(object? item) => item switch
    {
        DxRow row => row,
        SpotRow spot => spot.Dx,
        _ => null,
    };

    private void DayList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is CalendarEntry entry) FocusOnMap(entry.Row);
    }

    private async void FocusOnMap(DxRow row)
    {
        Tabs.SelectedItem = MapTab;
        // Alla prima apertura la mappa deve ancora caricarsi.
        for (int i = 0; i < 50 && !_mapReady; i++) await Task.Delay(200);
        if (_mapReady) await RunMapScript($"dxFocus({JsonSerializer.Serialize(row.Callsign)})");
    }
}
