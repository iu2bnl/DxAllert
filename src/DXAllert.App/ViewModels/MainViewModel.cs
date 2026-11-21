using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DXAllert.App.Services;
using DXAllert.Core;
using DXAllert.Data;

namespace DXAllert.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private const int MaxSpotRows = 500;

    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly HttpClient _http = Http.Create();
    private readonly Repository _repo = new();
    private readonly AlertEngine _alerts = new();
    private readonly DxpeditionService _service;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private AppSettings _settings = SettingsStore.Load();
    private CtyResolver? _cty;
    private List<Dxpedition> _all = new();
    private string _allSignature = "";
    private Dictionary<string, DxRow> _activeByCall = new();
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private DateTime _lastDay = DateTime.UtcNow.Date;
    private int _tick;
    private bool _refreshing;
    private string _webText = "";
    private readonly List<TelnetClusterClient> _clusters = new();
    private readonly Dictionary<string, string> _clusterTexts = new();
    private CancellationTokenSource? _clusterCts;
    private DxSummitSpotSource? _web;
    private readonly QrzClient _qrz;
    private WorkedLog? _worked;

    public MainViewModel()
    {
        _service = new DxpeditionService(_http, _repo);
        _qrz = new QrzClient(_http);
        _qrz.SetCredentials(_settings.QrzUser, Secret.Unprotect(_settings.QrzPasswordProtected));
        SpotsView = CollectionViewSource.GetDefaultView(Spots);
        SpotsView.Filter = SpotFilter;
        SpotBands = new[] { "Tutte le bande" }.Concat(BandPlan.AllBands).ToArray();
        SpotModes = new[] { "Tutti i modi" }.Concat(BandPlan.AllModes).ToArray();
        Months = new[] { "Tutti i mesi" }
            .Concat(Enumerable.Range(1, 12).Select(m => Capitalize(It.DateTimeFormat.GetMonthName(m)))).ToArray();
        LoadSettingsIntoEditor();
        _timer.Tick += (_, _) => OnTick();
    }

    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], It) + s[1..];

    public AppSettings Settings => _settings;
    public bool NeedsSetup => string.IsNullOrWhiteSpace(_settings.Callsign);

    /// <summary>JSON da passare alla mappa.</summary>
    public event Action<string>? MapDataChanged;
    public event Action<Alert>? AlertRaised;
    public event Action<bool>? ThemeChanged;
    /// <summary>Nominativo o locator cambiati: le minimappe ridisegnano il QTH.</summary>
    public event Action? HomeChanged;

    public ObservableCollection<DxRow> OnAir { get; } = new();
    public ObservableCollection<DxRow> Active { get; } = new();
    public ObservableCollection<DxRow> Planned { get; } = new();
    public ObservableCollection<SpotRow> Spots { get; } = new();
    public ICollectionView SpotsView { get; }


    public string[] Months { get; }
    public string[] Continents { get; } = { "Tutti i continenti", "EU", "AF", "AS", "NA", "SA", "OC", "AN" };

    [ObservableProperty] private string _utcClock = "";
    [ObservableProperty] private string _statusText = "Avvio…";
    [ObservableProperty] private string _spotStatus = "Cluster: in attesa";
    [ObservableProperty] private string _sourcesText = "";
    [ObservableProperty] private DxRow? _selectedActive;
    [ObservableProperty] private DxRow? _selectedPlanned;
    [ObservableProperty] private bool _showAllSpots;
    [ObservableProperty] private int _selectedMonth;
    [ObservableProperty] private int _selectedContinent;
    [ObservableProperty] private string _entityFilter = "";
    [ObservableProperty] private bool _calendarView = true;
    [ObservableProperty] private DxRow? _selectedOnAir;
    [ObservableProperty] private string _onAirSummary = "";
    [ObservableProperty] private string _onAirHeader = "On air";
    [ObservableProperty] private string _activeSummary = "";
    [ObservableProperty] private string _plannedSummary = "";

    partial void OnShowAllSpotsChanged(bool value) => SpotsView.Refresh();

    // ---------------------------------------------------------------- riquadro QRZ.com

    public QrzPanel Qrz { get; } = new();
    [ObservableProperty] private SpotRow? _selectedSpot;

    // Il valore null arriva anche quando le tabelle si ricostruiscono: il riquadro resta com'è.
    partial void OnSelectedActiveChanged(DxRow? value) { if (value is not null) _ = ShowQrzAsync(value.Callsign); }
    partial void OnSelectedOnAirChanged(DxRow? value) { if (value is not null) _ = ShowQrzAsync(value.Callsign); }
    partial void OnSelectedSpotChanged(SpotRow? value) { if (value is not null) _ = ShowQrzAsync(value.DxCall); }

    private async Task ShowQrzAsync(string call, bool force = false)
    {
        if (!force && call == Qrz.Call) return;
        if (!_qrz.HasCredentials)
        {
            Qrz.Clear(call, "Per vedere la scheda inserisci utente e password di QRZ.com nella scheda Impostazioni.");
            return;
        }
        Qrz.Clear(call, "Ricerca su QRZ.com…");
        try
        {
            var info = await _qrz.LookupAsync(call, _cts.Token);
            // "3B9/M0CFW" spesso non ha una scheda propria: si prova il nominativo di casa.
            var home = call.Split('/').OrderByDescending(p => p.Length).First();
            var viaHome = info is null && home != call && CallsignUtil.IsFullCallsign(home);
            if (viaHome) info = await _qrz.LookupAsync(home, _cts.Token);
            if (Qrz.Call != call) return; // nel frattempo è stata selezionata un'altra riga
            if (info is null)
            {
                Qrz.Status = "Nominativo non presente su QRZ.com.";
                return;
            }
            var notes = new List<string>();
            if (viaHome) notes.Add($"Scheda di {home}: {call} non ha una scheda propria.");
            if (_qrz.Subscription.Contains("non-subscriber", StringComparison.OrdinalIgnoreCase))
                notes.Add("Account senza abbonamento XML: QRZ fornisce solo i dati di base.");
            Qrz.Show(info, Geo.LocatorToLatLon(_settings.Locator), string.Join(" ", notes));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn($"QRZ {call}: {ex.Message}");
            if (Qrz.Call != call) return;
            Qrz.Status = ex is QrzException
                ? $"QRZ.com ha risposto: {ex.Message.Trim()}. Controlla utente e password nelle Impostazioni."
                : $"QRZ.com non raggiungibile: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- log ADIF (già collegati)

    [ObservableProperty] private string _workedStats = "Nessun log importato.";

    /// <summary>Rilegge il log dal database. L'entità DXCC è ricavata dal nominativo con cty.dat,
    /// così i nomi coincidono con quelli delle tabelle (i nomi scritti nei file ADIF variano).</summary>
    private async Task LoadWorkedLogAsync()
    {
        var cty = _cty;
        var log = await Task.Run(() =>
        {
            var entries = _repo.GetWorkedLog();
            return entries.Count == 0 ? null
                : new WorkedLog(entries.Select(e => e with { DxccName = cty?.Resolve(e.Callsign)?.Name ?? e.DxccName }));
        });
        _worked = log;
        WorkedStats = log is null
            ? "Nessun log importato."
            : $"Log importato: {log.Count} QSO · {log.UniqueCallsigns} nominativi · {log.UniqueEntities} entità DXCC.";
    }

    public async Task ImportAdifAsync(string path)
    {
        WorkedStats = "Importazione in corso…";
        try
        {
            var entries = await Task.Run(() => AdifParser.Parse(File.ReadAllText(path)));
            if (entries.Count == 0)
            {
                WorkedStats = "Nel file non ho trovato QSO: controlla che sia un file ADIF (.adi). Il log precedente non è stato toccato.";
                return;
            }
            await Task.Run(() => _repo.ReplaceWorkedLog(entries));
            await LoadWorkedLogAsync();
            Log.Info($"Log ADIF importato: {entries.Count} QSO");
            Rebuild();
        }
        catch (Exception ex)
        {
            Log.Error("Importazione ADIF non riuscita", ex);
            WorkedStats = $"Importazione non riuscita: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearWorked()
    {
        await Task.Run(() => _repo.ClearWorkedLog());
        _worked = null;
        WorkedStats = "Nessun log importato.";
        Rebuild();
    }

    // Filtri della scheda "Tempo reale"
    public string[] SpotBands { get; }
    public string[] SpotModes { get; }
    [ObservableProperty] private int _selectedSpotBand;
    [ObservableProperty] private int _selectedSpotMode;
    [ObservableProperty] private string _spotFreqFrom = "";
    [ObservableProperty] private string _spotFreqTo = "";
    [ObservableProperty] private string _spotFilterMessage = "";
    private double? _freqFromKhz;
    private double? _freqToKhz;

    partial void OnSelectedSpotBandChanged(int value) => SpotsView.Refresh();
    partial void OnSelectedSpotModeChanged(int value) => SpotsView.Refresh();
    partial void OnSpotFreqFromChanged(string value) => UpdateFreqFilter();
    partial void OnSpotFreqToChanged(string value) => UpdateFreqFilter();

    private void UpdateFreqFilter()
    {
        var okFrom = TryParseFreq(SpotFreqFrom, out _freqFromKhz);
        var okTo = TryParseFreq(SpotFreqTo, out _freqToKhz);
        SpotFilterMessage = okFrom && okTo ? "" : "Frequenza non valida: scrivi un numero in kHz (es. 14000) o in MHz (es. 14.0).";
        SpotsView.Refresh();
    }

    /// <summary>Vuoto = nessun limite. I valori sotto 1000 sono presi come MHz.</summary>
    private static bool TryParseFreq(string text, out double? khz)
    {
        khz = null;
        var t = text.Trim().Replace(',', '.');
        if (t.Length == 0) return true;
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0) return false;
        khz = v < 1000 ? v * 1000 : v;
        return true;
    }

    private bool SpotFilter(object o)
    {
        var row = (SpotRow)o;
        if (!ShowAllSpots && !row.IsDxpedition) return false;
        if (SelectedSpotBand > 0 && row.Band != SpotBands[SelectedSpotBand]) return false;
        if (SelectedSpotMode > 0 && row.Mode != SpotModes[SelectedSpotMode]) return false;
        if (_freqFromKhz is { } from && row.Spot.FreqKhz < from) return false;
        if (_freqToKhz is { } to && row.Spot.FreqKhz > to) return false;
        return true;
    }

    /// <summary>Imbuto accanto alla banda di uno spot: filtra la tabella su quella banda.</summary>
    [RelayCommand]
    private void FilterBand(string? band)
    {
        var i = Array.IndexOf(SpotBands, band);
        if (i > 0) SelectedSpotBand = i;
    }

    [RelayCommand]
    private void ClearSpotFilters()
    {
        SelectedSpotBand = 0;
        SelectedSpotMode = 0;
        SpotFreqFrom = "";
        SpotFreqTo = "";
    }
    /// <summary>Posizione del QTH per le minimappe (null se il locator non è impostato).</summary>
    public string HomeJson()
    {
        var h = Geo.LocatorToLatLon(_settings.Locator);
        return JsonSerializer.Serialize(h is { } p
            ? new { lat = p.Lat, lon = p.Lon, call = _settings.Callsign, locator = _settings.Locator }
            : null);
    }

    partial void OnSelectedMonthChanged(int value) => RebuildPlanned();
    partial void OnSelectedContinentChanged(int value) => RebuildPlanned();
    partial void OnEntityFilterChanged(string value) => RebuildPlanned();

    // ---------------------------------------------------------------- avvio

    public async Task StartAsync()
    {
        _timer.Start();
        OnTick();
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "cty.dat");
            _cty = await Task.Run(() => CtyProvider.LoadAsync(_http, bundled, _cts.Token));
            Log.Info($"cty.dat caricato: {_cty.Entities.Count} entità, versione {_cty.Version}");
            await LoadWorkedLogAsync();

            // Prima la cache (subito a schermo), poi le fonti in rete.
            await RefreshAsync(offlineOnly: true);
            await RefreshAsync(offlineOnly: false);
            _ = Task.Run(() => _repo.PurgeSpots());
            StartSpotSources();
        }
        catch (Exception ex)
        {
            Log.Error("Avvio non riuscito", ex);
            StatusText = $"Errore all'avvio: {ex.Message}";
        }
    }

    public void Shutdown()
    {
        _cts.Cancel();
        _timer.Stop();
    }

    private void StartSpotSources()
    {
        StartClusters();
        _web = new DxSummitSpotSource(_http, () => _settings.UseWebSpotFallback && !AnyClusterConnected);
        _web.SpotReceived += s => _ui.BeginInvoke(() => OnSpot(s));
        _web.StatusChanged += t => _ui.BeginInvoke(() => { _webText = t; UpdateSpotStatus(); });
        _ = Task.Run(() => _web.RunAsync(_cts.Token));
    }

    private bool AnyClusterConnected
    {
        get { lock (_clusters) return _clusters.Any(c => c.IsConnected); }
    }

    /// <summary>Apre una connessione per ogni nodo dell'elenco; richiamata quando l'elenco cambia.</summary>
    private void StartClusters()
    {
        _clusterCts?.Cancel();
        var cts = _clusterCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        lock (_clusters) _clusters.Clear();
        _clusterTexts.Clear();
        foreach (var node in _settings.ClusterNodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TelnetClusterClient.TryParseNode(node, out var host, out var port)) continue;
            var client = new TelnetClusterClient(host, port, () => _settings.Callsign);
            client.SpotReceived += s => _ui.BeginInvoke(() => OnSpot(s));
            client.StatusChanged += t => _ui.BeginInvoke(() =>
            {
                if (cts.IsCancellationRequested) return;
                _clusterTexts[client.Node] = t;
                UpdateSpotStatus();
            });
            lock (_clusters) _clusters.Add(client);
            _ = Task.Run(() => client.RunAsync(cts.Token));
        }
        UpdateSpotStatus();
    }

    private void UpdateSpotStatus()
    {
        List<TelnetClusterClient> clients;
        lock (_clusters) clients = _clusters.ToList();
        string text;
        if (clients.Count == 0) text = "Cluster: nessun nodo configurato";
        else if (clients.Count == 1) text = "Cluster: " + _clusterTexts.GetValueOrDefault(clients[0].Node, "in attesa");
        else
        {
            var up = clients.Where(c => c.IsConnected).Select(c => c.Name).ToList();
            text = $"Cluster: {up.Count} di {clients.Count} nodi connessi" + (up.Count > 0 ? $" ({string.Join(", ", up)})" : "");
        }
        SpotStatus = !clients.Any(c => c.IsConnected) && _web is { IsConnected: true } ? $"{text} · {_webText}" : text;
    }

    // ---------------------------------------------------------------- orologio e controlli periodici

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        UtcClock = $"{now:HH:mm:ss} UTC ({now.ToLocalTime():HH:mm})";
        if (!_refreshing && _cty is not null && now - _lastRefreshUtc >= RefreshInterval) _ = RefreshAsync(offlineOnly: false);
        if (++_tick % 30 != 0) return;

        var changed = false;
        foreach (var r in Active)
        {
            var was = r.IsOnAir;
            r.UpdateOnAir(now);
            changed |= was != r.IsOnAir;
        }
        if (now.Date != _lastDay)
        {
            _lastDay = now.Date;
            Rebuild(); // a mezzanotte UTC le pianificate di oggi diventano attive
        }
        else if (changed) { UpdateSummaries(); PushMap(); }
        UpdateSpotStatus();
    }

    // ---------------------------------------------------------------- elenco DXpedition

    [RelayCommand]
    private Task RefreshNow() => RefreshAsync(offlineOnly: false);

    private async Task RefreshAsync(bool offlineOnly)
    {
        if (_cty is null || _refreshing) return;
        _refreshing = true;
        if (!offlineOnly)
        {
            // Segnato subito: se l'aggiornamento fallisce si riprova al prossimo intervallo, non ogni secondo.
            if (_lastRefreshUtc == DateTime.MinValue) StatusText = "Aggiornamento delle fonti in corso…";
            _lastRefreshUtc = DateTime.UtcNow;
        }
        try
        {
            var cty = _cty;
            var result = await Task.Run(() => _service.RefreshAsync(cty, offlineOnly, _cts.Token));
            // Con l'aggiornamento ogni minuto le tabelle si ricostruiscono solo se i dati sono cambiati,
            // così selezione e scorrimento non saltano.
            var signature = JsonSerializer.Serialize(result.Items);
            if (signature != _allSignature)
            {
                _allSignature = signature;
                _all = result.Items;
                Rebuild();
            }

            SourcesText = string.Join("   ", result.Sources.Select(s =>
                s.Ok ? $"✔ {s.Name} ({s.Count})"
                : s.FromCache ? $"⚠ {s.Name}: dalla cache del {Fmt.DateTimeUtc(s.FetchedUtc!.Value)}"
                : $"✖ {s.Name}: non disponibile"));
            if (!offlineOnly)
            {
                var failed = result.Sources.Count(s => !s.Ok);
                StatusText = $"Ultimo aggiornamento: {Fmt.DateTimeUtc(DateTime.UtcNow)}" +
                             (failed > 0 ? $" · {failed} fonti non raggiunte (uso la cache)" : "") +
                             " · le liste si aggiornano ogni minuto";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Aggiornamento non riuscito", ex);
            StatusText = $"Aggiornamento non riuscito: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    /// <summary>Ricostruisce le liste "Attive ora" e "Pianificate" dai dati uniti.</summary>
    private void Rebuild()
    {
        var now = DateTime.UtcNow;
        var home = Geo.LocatorToLatLon(_settings.Locator);
        var previous = _activeByCall;
        _activeByCall = new Dictionary<string, DxRow>();
        var selected = SelectedActive?.Callsign;

        Active.Clear();
        foreach (var d in _all.Where(d => d.StatusAt(now) == DxStatus.Active && Wanted(d))
                     .OrderBy(d => d.EndUtc ?? DateTime.MaxValue).ThenBy(d => d.Callsign))
        {
            var row = new DxRow(d, home, AlertEngine.IsFavorite(d, _settings), _worked);
            var key = CallsignUtil.Normalize(d.Callsign);
            // Conserva gli spot già ricevuti; al primo avvio li rilegge dallo storico.
            var spots = previous.TryGetValue(key, out var old)
                ? old.RecentSpots
                : _repo.RecentSpots(d.Callsign, 8);
            foreach (var s in spots.OrderBy(s => s.TimeUtc)) row.AddSpot(s);
            Active.Add(row);
            _activeByCall[key] = row;
        }
        SelectedActive = Active.FirstOrDefault(r => r.Callsign == selected);
        RebuildPlanned();
        UpdateSummaries();
        PushMap();
    }

    private bool Wanted(Dxpedition d) => _settings.IncludeSpecialEvents || !d.IsSpecialEvent;

    /// <summary>Filtri di continente ed entità/nominativo della scheda "Pianificate".</summary>
    private bool MatchesPlannedFilters(Dxpedition d)
    {
        var continent = SelectedContinent > 0 ? Continents[SelectedContinent] : null;
        var text = EntityFilter.Trim();
        return Wanted(d) &&
               (continent is null || d.Entity?.Continent == continent) &&
               (text.Length == 0 ||
                d.Callsign.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                (d.Entity?.Name ?? d.EntityName).Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildPlanned()
    {
        var now = DateTime.UtcNow;
        var home = Geo.LocatorToLatLon(_settings.Locator);
        var year = now.Year;
        bool Matches(Dxpedition d) => MatchesPlannedFilters(d);

        static bool InMonth(Dxpedition d, int y, int m)
        {
            var first = new DateTime(y, m, 1);
            var last = first.AddMonths(1).AddDays(-1);
            return (d.StartUtc ?? d.EndUtc ?? DateTime.MaxValue).Date <= last && (d.EndUtc ?? d.StartUtc ?? DateTime.MinValue).Date >= first;
        }

        Planned.Clear();
        foreach (var d in _all.Where(d => d.StatusAt(now) is DxStatus.Planned or DxStatus.Undated && Matches(d))
                     .Where(d => SelectedMonth == 0 || (d.StartUtc is not null && InMonth(d, year, SelectedMonth)))
                     .OrderBy(d => d.StartUtc ?? DateTime.MaxValue).ThenBy(d => d.Callsign))
            Planned.Add(new DxRow(d, home, AlertEngine.IsFavorite(d, _settings), _worked));

        PlannedSummary = $"{Planned.Count} pianificate";
        BuildCalendar();
    }

    private void UpdateSummaries()
    {
        ActiveSummary = $"{Active.Count} attive · {Active.Count(r => r.IsOnAir)} on air adesso";
        RebuildOnAir();
    }

    /// <summary>Scheda "On air": le attive spottate negli ultimi 15 minuti, la più recente in alto.</summary>
    private void RebuildOnAir()
    {
        var rows = Active.Where(r => r.IsOnAir).OrderByDescending(r => r.LastSpot?.TimeUtc).ToList();
        if (!rows.SequenceEqual(OnAir))
        {
            var selected = SelectedOnAir?.Callsign;
            OnAir.Clear();
            foreach (var r in rows) OnAir.Add(r);
            SelectedOnAir = OnAir.FirstOrDefault(r => r.Callsign == selected);
        }
        OnAirHeader = OnAir.Count > 0 ? $"On air ({OnAir.Count})" : "On air";
        OnAirSummary = OnAir.Count switch
        {
            0 => "Nessuna DXpedition on air in questo momento. Compaiono qui appena il cluster le spotta, e restano finché arrivano spot (ultimi 15 minuti).",
            1 => "1 DXpedition on air · spottata negli ultimi 15 minuti",
            var n => $"{n} DXpedition on air · spottate negli ultimi 15 minuti",
        };
    }

    // ---------------------------------------------------------------- calendario delle pianificate

    public ObservableCollection<CalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<CalendarEntry> DayEntries { get; } = new();
    public IReadOnlyList<LegendItem> Legend => ContinentColors.Legend;
    public string[] WeekDays { get; } = { "LUN", "MAR", "MER", "GIO", "VEN", "SAB", "DOM" };

    private DateTime _calendarMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    [ObservableProperty] private string _calendarTitle = "";
    [ObservableProperty] private string _calendarSummary = "";
    [ObservableProperty] private CalendarDay? _selectedDay;
    [ObservableProperty] private CalendarEntry? _selectedDayEntry;
    [ObservableProperty] private string _selectedDayTitle = "";
    [ObservableProperty] private string _selectedDaySubtitle = "";

    partial void OnSelectedDayChanged(CalendarDay? value)
    {
        DayEntries.Clear();
        SelectedDayEntry = null;
        if (value is null) { SelectedDayTitle = SelectedDaySubtitle = ""; return; }
        foreach (var r in value.Rows
                     .OrderByDescending(r => r.Start?.Date == value.Date)
                     .ThenBy(r => r.End ?? r.Start).ThenBy(r => r.Callsign))
            DayEntries.Add(new CalendarEntry(r, value.Date));
        SelectedDayTitle = Capitalize(value.Date.ToString("dddd d MMMM yyyy", It));
        var starting = value.Rows.Count(r => r.Start?.Date == value.Date);
        SelectedDaySubtitle = value.Rows.Count == 0
            ? "Nessuna DXpedition in questo giorno."
            : $"{value.ActiveText}" + (starting > 0 ? $" · {starting} iniziano" : "");
    }

    [RelayCommand]
    private void PrevMonth() { _calendarMonth = _calendarMonth.AddMonths(-1); BuildCalendar(); }

    [RelayCommand]
    private void NextMonth() { _calendarMonth = _calendarMonth.AddMonths(1); BuildCalendar(); }

    [RelayCommand]
    private void ThisMonth()
    {
        var today = DateTime.UtcNow.Date;
        _calendarMonth = new DateTime(today.Year, today.Month, 1);
        SelectedDay = null;
        BuildCalendar();
    }

    /// <summary>Sei settimane da lunedì, con le DXpedition (pianificate, attive e concluse) che toccano ciascun giorno.</summary>
    private void BuildCalendar()
    {
        var home = Geo.LocatorToLatLon(_settings.Locator);
        var today = DateTime.UtcNow.Date;
        var first = _calendarMonth;
        var gridStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var gridEnd = gridStart.AddDays(42);

        var rows = _all
            .Where(d => d.StartUtc is { } s && s.Date < gridEnd && (d.EndUtc ?? s).Date >= gridStart && MatchesPlannedFilters(d))
            .OrderBy(d => d.StartUtc).ThenBy(d => d.Callsign)
            .Select(d => new DxRow(d, home, AlertEngine.IsFavorite(d, _settings), _worked))
            .ToList();

        static bool On(DxRow r, DateTime day) => r.Start!.Value.Date <= day && (r.End ?? r.Start)!.Value.Date >= day;
        var perDay = Enumerable.Range(0, 42).Select(i => rows.Where(r => On(r, gridStart.AddDays(i))).ToList()).ToList();
        var busiest = Math.Max(1, perDay.Max(l => l.Count));

        var keep = SelectedDay?.Date;
        CalendarDays.Clear();
        for (int i = 0; i < 42; i++)
        {
            var day = gridStart.AddDays(i);
            CalendarDays.Add(new CalendarDay(day, day.Month == first.Month, day == today, perDay[i], perDay[i].Count / (double)busiest));
        }

        var monthName = Capitalize(first.ToString("MMMM yyyy", It));
        CalendarTitle = monthName;
        var inMonth = rows.Count(r => r.Start!.Value.Date <= first.AddMonths(1).AddDays(-1) && (r.End ?? r.Start)!.Value.Date >= first);
        var startsInMonth = rows.Count(r => r.Start!.Value.Year == first.Year && r.Start.Value.Month == first.Month);
        CalendarSummary = $"{inMonth} DXpedition nel mese · {startsInMonth} iniziano";

        bool InShownMonth(DateTime? d) => d is { } x && x.Year == first.Year && x.Month == first.Month;
        var target = InShownMonth(keep) ? keep : InShownMonth(today) ? today : first;
        SelectedDay = CalendarDays.FirstOrDefault(d => d.Date == target);
    }

    // ---------------------------------------------------------------- spot e avvisi

    private void OnSpot(Spot spot)
    {
        _activeByCall.TryGetValue(CallsignUtil.Normalize(spot.DxCall), out var row);
        // Stesso spot già ricevuto dall'altra fonte.
        if (Spots.Take(40).Any(s => s.Spot.DxCall == spot.DxCall && s.Spot.Spotter == spot.Spotter &&
                                    Math.Abs(s.Spot.FreqKhz - spot.FreqKhz) < 0.2 &&
                                    Math.Abs((s.Spot.TimeUtc - spot.TimeUtc).TotalMinutes) < 3))
            return;

        Spots.Insert(0, new SpotRow(spot, row, row is null ? _cty?.Resolve(spot.DxCall) : null, _worked, _cty?.Resolve(spot.Spotter)));
        while (Spots.Count > MaxSpotRows) Spots.RemoveAt(Spots.Count - 1);
        if (row is null) return;

        var wasOnAir = row.IsOnAir;
        row.AddSpot(spot);
        _ = Task.Run(() => _repo.AddSpot(spot));
        if (row.IsOnAir && DateTime.UtcNow - spot.TimeUtc <= DxRow.OnAirWindow &&
            _alerts.OnSpot(spot, row.Dx, _settings, DateTime.UtcNow) is { } alert)
            AlertRaised?.Invoke(alert);
        if (wasOnAir != row.IsOnAir) UpdateSummaries();
        PushMap();
    }

    // ---------------------------------------------------------------- mappa

    private DateTime _lastMapPush = DateTime.MinValue;
    private bool _mapPushPending;

    /// <summary>Invia i dati alla mappa, al massimo una volta ogni 2 secondi.</summary>
    public void PushMap()
    {
        if (_mapPushPending) return;
        var wait = TimeSpan.FromSeconds(2) - (DateTime.UtcNow - _lastMapPush);
        if (wait > TimeSpan.Zero)
        {
            _mapPushPending = true;
            _ = _ui.InvokeAsync(async () =>
            {
                await Task.Delay(wait);
                _mapPushPending = false;
                PushMap();
            });
            return;
        }
        _lastMapPush = DateTime.UtcNow;
        MapDataChanged?.Invoke(BuildMapJson());
    }

    public string BuildMapJson()
    {
        var home = Geo.LocatorToLatLon(_settings.Locator);
        var now = DateTime.UtcNow;
        var planned = _all.Where(d => d.StatusAt(now) == DxStatus.Planned && Wanted(d) && d.Entity is not null)
            .Select(d => new DxRow(d, home, false));
        object Item(DxRow r, string status) => new
        {
            call = r.Callsign, entity = r.Entity, lat = r.Dx.Entity!.Latitude, lon = r.Dx.Entity.Longitude,
            status, start = r.StartText, end = r.EndText, bands = r.Bands, modes = r.Modes, qsl = r.Qsl,
            iota = r.Iota, sources = r.Sources, cq = r.CqZone, itu = r.ItuZone, cont = r.Continent,
            dist = r.DistanceKm, az = r.Azimuth,
            spots = r.RecentSpots.Take(5).Select(s => new
            {
                t = Fmt.Time(s.TimeUtc), f = s.FreqKhz.ToString("0.0", CultureInfo.InvariantCulture),
                m = s.Mode, by = s.Spotter, c = s.Comment,
            }),
        };
        var data = new
        {
            dark = _settings.DarkTheme,
            home = home is { } h ? new { lat = h.Lat, lon = h.Lon, call = _settings.Callsign, locator = _settings.Locator } : null,
            items = Active.Where(r => r.Dx.Entity is not null).Select(r => Item(r, r.IsOnAir ? "onair" : "active"))
                .Concat(planned.Select(r => Item(r, "planned"))),
        };
        return JsonSerializer.Serialize(data);
    }

    // ---------------------------------------------------------------- impostazioni

    public ObservableCollection<CheckItem> BandChecks { get; } = new();
    public ObservableCollection<CheckItem> ModeChecks { get; } = new();

    [ObservableProperty] private string _editCallsign = "";
    [ObservableProperty] private string _editLocator = "";
    [ObservableProperty] private string _editClusterNodes = "";

    [ObservableProperty] private bool _editWebFallback;
    [ObservableProperty] private string _editFavorites = "";
    [ObservableProperty] private bool _editAlertOnSpot;
    [ObservableProperty] private bool _editAlertOnlyFavorites;
    [ObservableProperty] private bool _editAlertSound;
    [ObservableProperty] private bool _editIncludeSpecial;
    [ObservableProperty] private bool _editDarkTheme;
    [ObservableProperty] private bool _editAutostart;
    [ObservableProperty] private string _settingsMessage = "";
    [ObservableProperty] private string _editQrzUser = "";
    [ObservableProperty] private string _qrzPasswordHint = "";
    /// <summary>Password digitata nel campo delle impostazioni (non legata in XAML: PasswordBox non lo consente).</summary>
    public string EditQrzPassword { get; set; } = "";
    /// <summary>Chiede alla finestra di svuotare il campo password dopo il salvataggio.</summary>
    public event Action? QrzPasswordSaved;

    private void UpdateQrzHint() =>
        QrzPasswordHint = _settings.QrzPasswordProtected.Length > 0
            ? "Password salvata (cifrata). Lascia vuoto per non cambiarla."
            : "Nessuna password salvata.";

    private void LoadSettingsIntoEditor()
    {
        var s = _settings;
        EditCallsign = s.Callsign;
        EditLocator = s.Locator;
        EditClusterNodes = string.Join(Environment.NewLine, s.ClusterNodes);

        EditWebFallback = s.UseWebSpotFallback;
        EditFavorites = string.Join(Environment.NewLine, s.Favorites);
        EditAlertOnSpot = s.AlertOnSpot;
        EditAlertOnlyFavorites = s.AlertOnlyFavorites;
        EditAlertSound = s.AlertSound;
        EditIncludeSpecial = s.IncludeSpecialEvents;
        EditDarkTheme = s.DarkTheme;
        EditAutostart = Autostart.IsEnabled();
        EditQrzUser = s.QrzUser;
        UpdateQrzHint();
        BandChecks.Clear();
        foreach (var b in BandPlan.AllBands) BandChecks.Add(new CheckItem(b, s.Bands.Contains(b)));
        ModeChecks.Clear();
        foreach (var m in BandPlan.AllModes) ModeChecks.Add(new CheckItem(m, s.Modes.Contains(m)));
        SettingsMessage = NeedsSetup ? "Inserisci nominativo e locator, poi premi Salva: il collegamento al cluster parte subito dopo." : "";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var call = EditCallsign.Trim().ToUpperInvariant();
        var locator = EditLocator.Trim();
        if (call.Length > 0 && !CallsignUtil.IsFullCallsign(call))
        {
            SettingsMessage = "Il nominativo non sembra valido (esempio: IZ2ABC).";
            return;
        }
        if (locator.Length > 0 && Geo.LocatorToLatLon(locator) is null)
        {
            SettingsMessage = "Il locator non è valido: servono 4 o 6 caratteri (esempio: JN55 oppure JN55ab).";
            return;
        }
        var nodes = new List<string>();
        foreach (var line in EditClusterNodes.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TelnetClusterClient.TryParseNode(line, out var host, out var port))
            {
                SettingsMessage = $"Nodo cluster non valido: \"{line}\". Scrivi host:porta, per esempio dxc.ve7cc.net:23.";
                return;
            }
            nodes.Add($"{host}:{port}");
        }


        // QRZ: campo password vuoto = resta quella salvata; utente vuoto = credenziali rimosse.
        var qrzUser = EditQrzUser.Trim();
        var qrzPassword = qrzUser.Length == 0 ? ""
            : EditQrzPassword.Length > 0 ? Secret.Protect(EditQrzPassword)
            : _settings.QrzPasswordProtected;

        var themeChanged = _settings.DarkTheme != EditDarkTheme;
        var nodesChanged = !nodes.SequenceEqual(_settings.ClusterNodes, StringComparer.OrdinalIgnoreCase);
        _settings = new AppSettings
        {
            Callsign = call,
            Locator = locator.Length >= 4 ? locator[..2].ToUpperInvariant() + locator[2..4] + (locator.Length == 6 ? locator[4..].ToLowerInvariant() : "") : "",
            ClusterNodes = nodes,

            UseWebSpotFallback = EditWebFallback,
            Bands = BandChecks.Where(c => c.IsChecked).Select(c => c.Name).ToList(),
            Modes = ModeChecks.Where(c => c.IsChecked).Select(c => c.Name).ToList(),
            Favorites = EditFavorites.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            AlertOnSpot = EditAlertOnSpot,
            AlertOnlyFavorites = EditAlertOnlyFavorites,
            AlertSound = EditAlertSound,
            IncludeSpecialEvents = EditIncludeSpecial,
            DarkTheme = EditDarkTheme,
            QrzUser = qrzUser,
            QrzPasswordProtected = qrzPassword,
        };
        try
        {
            SettingsStore.Save(_settings);
            Autostart.Set(EditAutostart);
            SettingsMessage = $"Impostazioni salvate alle {Fmt.Time(DateTime.UtcNow)}.";
            Log.Info("Impostazioni salvate");
        }
        catch (Exception ex)
        {
            Log.Error("Salvataggio impostazioni non riuscito", ex);
            SettingsMessage = $"Salvataggio non riuscito: {ex.Message}";
        }
        EditLocator = _settings.Locator;
        EditCallsign = _settings.Callsign;
        OnPropertyChanged(nameof(NeedsSetup));
        EditClusterNodes = string.Join(Environment.NewLine, _settings.ClusterNodes);
        _qrz.SetCredentials(_settings.QrzUser, Secret.Unprotect(_settings.QrzPasswordProtected));
        EditQrzPassword = "";
        QrzPasswordSaved?.Invoke();
        UpdateQrzHint();
        if (Qrz.HasCall) _ = ShowQrzAsync(Qrz.Call, force: true);
        if (themeChanged) ThemeChanged?.Invoke(_settings.DarkTheme);
        if (nodesChanged && _web is not null) StartClusters();
        HomeChanged?.Invoke();
        Rebuild();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _settings.DarkTheme = !_settings.DarkTheme;
        EditDarkTheme = _settings.DarkTheme;
        try { SettingsStore.Save(_settings); } catch (Exception ex) { Log.Error("Salvataggio tema", ex); }
        ThemeChanged?.Invoke(_settings.DarkTheme);
        PushMap();
    }

    [RelayCommand]
    private void TestAlert() =>
        AlertRaised?.Invoke(new Alert(AlertKind.OnAir, "Avviso di prova", "Così appaiono gli avvisi di DXAllert.", new Dxpedition()));

    [RelayCommand]
    private void OpenLog()
    {
        Log.Info("Apertura del file di log");
        Shell(Log.CurrentFile);
    }

    [RelayCommand]
    private void OpenLogFolder() => Shell(Log.Directory);

    public static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"Impossibile aprire {target}", ex); }
    }
}
