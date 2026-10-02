namespace PzlEv.Shared.Utils.Data;

/// <summary>Konto Windows bieżącej sesji. Do czasu ról z grup AD (F10) – zawsze rola Analityk (v1).</summary>
public sealed class WindowsUser : ICurrentUser
{
    public string Account { get; } = $@"{Environment.UserDomainName}\{Environment.UserName}";

    public string Role => "Analityk";
}
