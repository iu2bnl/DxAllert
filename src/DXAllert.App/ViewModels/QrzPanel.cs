using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DXAllert.Core;
using DXAllert.Data;

namespace DXAllert.App.ViewModels;

public sealed record QrzField(string Label, string Value);

/// <summary>Riquadro con la scheda QRZ.com del nominativo selezionato.</summary>
public partial class QrzPanel : ObservableObject
{
    public const string SelectHint = "Seleziona una riga per vedere la scheda di QRZ.com.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCall))]
    private string _call = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _status = SelectHint;
    [ObservableProperty] private string? _imageUrl;

    public bool HasCall => Call.Length > 0;
    public ObservableCollection<QrzField> Fields { get; } = new();

    public void Clear(string call, string status)
    {
        Call = call;
        Name = "";
        ImageUrl = null;
        Status = status;
        Fields.Clear();
    }

    public void Show(QrzInfo info, (double Lat, double Lon)? home, string note)
    {
        Name = info.Name;
        ImageUrl = info.ImageUrl.Length > 0 ? info.ImageUrl : null;
        Status = note;
        Fields.Clear();
        Add("Nominativo QRZ", string.Equals(info.Call, Call, StringComparison.OrdinalIgnoreCase) ? "" : info.Call);
        Add("Indirizzo", info.Address);
        Add("Paese", info.Country);
        Add("Locator", info.Grid);
        if (home is { } h && info.Latitude is { } lat && info.Longitude is { } lon)
            Add("Da casa", $"{Geo.DistanceKm(h.Lat, h.Lon, lat, lon):0} km · azimut {Geo.BearingDeg(h.Lat, h.Lon, lat, lon):0}°");
        Add("Zone", string.Join(" · ", new[]
        {
            info.CqZone.Length > 0 ? $"CQ {info.CqZone}" : "", info.ItuZone.Length > 0 ? $"ITU {info.ItuZone}" : "",
        }.Where(s => s.Length > 0)));
        Add("IOTA", info.Iota);
        Add("QSL manager", info.QslManager);
        Add("Conferme", string.Join(" · ", new[]
        {
            Flag("LoTW", info.Lotw), Flag("eQSL", info.Eqsl), Flag("cartacea", info.PaperQsl),
        }.Where(s => s.Length > 0)));
        Add("E-mail", info.Email);
        Add("Sito", info.Web);
        Add("Altri nominativi", info.Aliases);
    }

    private void Add(string label, string value)
    {
        if (value.Length > 0) Fields.Add(new QrzField(label, value));
    }

    // QRZ usa 1/0 per sì/no; vuoto = non dichiarato.
    private static string Flag(string label, string value) => value switch
    {
        "1" => $"{label} sì",
        "0" => $"{label} no",
        _ => "",
    };
}
