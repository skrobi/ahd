using Serilog;

namespace PzlEv.Shared.Utils.Ui.Notifications;

/// <summary>
/// E-mail przez zainstalowany Outlook użytkownika (COM, bez bibliotek i bez konfiguracji serwera): wiadomość wysyła
/// konto analityka. Brak Outlooka albo błąd – tylko wpis w logu (powiadomienie Windows i tak się pokazuje).
/// </summary>
public static class OutlookMail
{
    private static readonly ILogger Logger = Log.ForContext("Module", "shell");

    /// <param name="to">Adres odbiorcy; null – bieżący użytkownik Outlooka (wiadomość do siebie).</param>
    public static bool Send(string? to, string subject, string body)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type is null)
            {
                Logger.Information("E-mail nie wysłany – brak Outlooka");
                return false;
            }
            dynamic outlook = Activator.CreateInstance(type)!;
            dynamic mail = outlook.CreateItem(0);   // olMailItem
            mail.Subject = subject;
            mail.Body = body;
            dynamic recipient = mail.Recipients.Add(to ?? outlook.Session.CurrentUser.Address);
            recipient.Resolve();
            mail.Send();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "E-mail przez Outlook nieudany");
            return false;
        }
    }
}
