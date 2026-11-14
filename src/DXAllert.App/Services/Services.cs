using System.Media;
using DXAllert.Core;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace DXAllert.App.Services;

/// <summary>
/// Cifratura della password di QRZ con la protezione dati di Windows (DPAPI): il valore salvato
/// si può decifrare solo dallo stesso utente Windows su questo computer.
/// </summary>
public static class Secret
{
    public static string Protect(string plain) =>
        plain.Length == 0 ? "" : Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plain), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    public static string Unprotect(string stored)
    {
        if (stored.Length == 0) return "";
        try
        {
            return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(stored), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            Log.Warn($"Password QRZ salvata non leggibile ({ex.GetType().Name}): va reinserita nelle impostazioni");
            return "";
        }
    }
}

/// <summary>Avvio con Windows: stessa voce di registro scritta dall'installer.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DXAllert";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        if (enabled == IsEnabled()) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        else key.DeleteValue(ValueName, false);
    }
}

/// <summary>Notifiche toast di Windows e suono.</summary>
public static class Notifier
{
    public static void Show(Alert alert, bool sound)
    {
        Log.Info($"Avviso: {alert.Title} | {alert.Text.Replace('\n', ' ')}");
        try
        {
            var toast = new ToastContentBuilder()
                .AddText(alert.Title)
                .AddText(alert.Text)
                .AddAttributionText("DXAllert");
            // Il suono lo gestiamo noi, così l'opzione "suono" vale sempre.
            toast.AddAudio(new ToastAudio { Silent = true });
            toast.Show();
        }
        catch (Exception ex) { Log.Error("Notifica toast non riuscita", ex); }
        if (sound)
        {
            try { SystemSounds.Exclamation.Play(); } catch { /* nessuna scheda audio */ }
        }
    }
}
