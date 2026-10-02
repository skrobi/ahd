namespace PzlEv.Shared.Utils.Data;

/// <summary>Użytkownik aplikacji – konto Windows / AD i rola (docs/uprawnienia.md).</summary>
public interface ICurrentUser
{
    /// <summary>DOMENA\login.</summary>
    string Account { get; }

    string Role { get; }
}
