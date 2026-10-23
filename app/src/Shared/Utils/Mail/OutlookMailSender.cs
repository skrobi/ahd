namespace PzlEv.Shared.Utils.Mail;

/// <summary>
/// E-mail przez Outlooka zainstalowanego na stanowisku (COM, późne wiązanie – bez pakietów Office): wiadomość ze skrzynki zalogowanego
/// użytkownika, bez konfiguracji serwera poczty. Bez Outlooka (np. inny system) – wyjątek „Outlook niedostępny”.
/// </summary>
public sealed class OutlookMailSender : IMailSender
{
    private const int MailItem = 0;   // OlItemType.olMailItem

    public void Send(string to, string subject, string body)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Outlook dostępny tylko w Windows – powiadomienie nie wysłane");
        var type = Type.GetTypeFromProgID("Outlook.Application")
                   ?? throw new InvalidOperationException("Outlook niedostępny na tym stanowisku – powiadomienie nie wysłane");
        dynamic outlook = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Nie udało się uruchomić Outlooka");
        dynamic mail = outlook.CreateItem(MailItem);
        mail.To = to;
        mail.Subject = subject;
        mail.Body = body;
        mail.Send();
    }
}
