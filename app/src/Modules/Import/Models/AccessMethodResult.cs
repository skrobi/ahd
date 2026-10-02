namespace PzlEv.Modules.Import.Models;

public static class AccessStatus
{
    public const string Works = "DZIAŁA";
    public const string Fails = "NIE DZIAŁA";
    public const string Skipped = "POMINIĘTO";
}

/// <summary>Plik widziany przez metodę dostępu: nazwa i (gdy znany) adres do pobrania.</summary>
public sealed record AccessFile(string Name, string? Url);

/// <summary>Wynik jednej metody testu dostępu (bez zawartości plików – tylko status, szczegóły, nazwy).</summary>
public sealed record AccessMethodResult(string Code, string Method, string Status, IReadOnlyList<string> Details, IReadOnlyList<AccessFile> Files)
{
    public bool Works => Status == AccessStatus.Works;

    public string Title => $"[{Status}] {Code}. {Method}";

    /// <summary>Szczegóły i do 10 nazw plików – jak w raporcie narzędzia w Pythonie.</summary>
    public string Text => string.Join(Environment.NewLine,
        Details
            .Concat(Files.Take(10).Select(f => $"• {f.Name}"))
            .Concat(Files.Count > 10 ? [$"… i {Files.Count - 10} więcej"] : []));
}
