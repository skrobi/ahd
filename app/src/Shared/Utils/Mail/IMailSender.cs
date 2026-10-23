namespace PzlEv.Shared.Utils.Mail;

/// <summary>Wysyłka e-maila (powiadomienia CAM); błąd wysyłki – wyjątek z opisem.</summary>
public interface IMailSender
{
    void Send(string to, string subject, string body);
}
