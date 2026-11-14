namespace DXAllert.Core;

/// <summary>Locator Maidenhead, distanza e azimut sul percorso corto.</summary>
public static class Geo
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Centro del quadrato del locator (4 o 6 caratteri). Null se non valido.</summary>
    public static (double Lat, double Lon)? LocatorToLatLon(string? locator)
    {
        if (string.IsNullOrWhiteSpace(locator)) return null;
        var l = locator.Trim().ToUpperInvariant();
        if (l.Length != 4 && l.Length != 6) return null;
        if (l[0] < 'A' || l[0] > 'R' || l[1] < 'A' || l[1] > 'R') return null;
        if (!char.IsDigit(l[2]) || !char.IsDigit(l[3])) return null;
        double lon = (l[0] - 'A') * 20 - 180 + (l[2] - '0') * 2;
        double lat = (l[1] - 'A') * 10 - 90 + (l[3] - '0');
        if (l.Length == 6)
        {
            if (l[4] < 'A' || l[4] > 'X' || l[5] < 'A' || l[5] > 'X') return null;
            lon += (l[4] - 'A') * (2.0 / 24) + 1.0 / 24;
            lat += (l[5] - 'A') * (1.0 / 24) + 0.5 / 24;
        }
        else
        {
            lon += 1.0;
            lat += 0.5;
        }
        return (lat, lon);
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = Rad(lat1), p2 = Rad(lat2), dp = Rad(lat2 - lat1), dl = Rad(lon2 - lon1);
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                   Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Azimut iniziale (gradi veri, 0-360) da punto 1 a punto 2.</summary>
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = Rad(lat1), p2 = Rad(lat2), dl = Rad(lon2 - lon1);
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
}
