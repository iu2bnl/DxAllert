using DXAllert.Core;

namespace DXAllert.Data;

/// <summary>
/// Contest dei prossimi 8 giorni dal WA7BNM Contest Calendar (contestcalendar.com). Le condizioni d'uso
/// del sito permettono di caricare il feed iCal in un'applicazione calendario per uso personale:
/// lo si scarica al più ogni 6 ore e si tiene una copia locale.
/// </summary>
public static class ContestFeed
{
    public const string Url = "https://www.contestcalendar.com/weeklycontcustom.php";
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    public static string CacheFile => Path.Combine(AppPaths.DataDir, "wa7bnm-week.ics");

    public static async Task<(List<IcsEvent> Events, DateTime? FetchedUtc)> LoadAsync(HttpClient http, CancellationToken ct)
    {
        var file = CacheFile;
        if (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge)
        {
            try
            {
                var text = await http.GetStringAsync(Url, ct);
                if (text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, text, ct);
                    Log.Info($"Contest della settimana (WA7BNM): {IcsParser.Parse(text).Count} eventi");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Warn($"WA7BNM non raggiungibile: {ex.Message}"); }
        }
        if (!File.Exists(file)) return (new(), null);
        return (IcsParser.Parse(await File.ReadAllTextAsync(file, ct)), File.GetLastWriteTimeUtc(file));
    }
}
