namespace PzlEv.Shared.Utils.Config;

/// <summary>
/// Połączenie z bazą MS SQL środowiska (pzl-ev.json, Environments.&lt;Env&gt;.Sql): serwer, baza, schemat i sygnatura
/// tabel (docs/model-danych.md, rozdz. 2). Logowanie kontem AD użytkownika (Integrated Security) – bez hasła w pliku.
/// ConnectionString – pełny ciąg połączenia zamiast pól Server / Database (np. testy na innej bazie).
/// </summary>
public sealed record SqlSettings(
    string Server,
    string Database,
    string Schema,
    string TablePrefix,
    bool TrustServerCertificate = false,
    string? ConnectionString = null)
{
    public string Describe => $"{(ConnectionString is null ? $"{Server} / {Database}" : "baza z ConnectionString")}, tabele {Schema}.{TablePrefix}*";
}
