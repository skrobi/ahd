namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Typy projektu (docs/funkcjonalnosc.md, F01; docs/slowniki.md, rozdz. 4) – kod w bazie (META_Project.ProjectType),
/// nazwa i słowniki projektu wymagane do uruchomienia przebiegu.
/// </summary>
public static class ProjectTypes
{
    public const string Internal = "WEWNETRZNY";
    public const string Sac = "SAC";
    public const string Cas = "CAS";

    /// <summary>Kolejność wyboru w kreatorze (kolejność typów projektów – tasks/README.md).</summary>
    public static IReadOnlyList<string> All { get; } = [Internal, Sac, Cas];

    public static string Label(string type) => type switch
    {
        Sac => "SAC (Sikorsky)",
        Cas => "CAS Compliance",
        Internal => "Wewnętrzny",
        _ => type,
    };

    public static string Description(string type) => type switch
    {
        Sac => "Waluta wyniku USD; stawki wydziałów i kursy walut; plik dla Cobra.",
        Cas => "Waluta wyniku PLN; przeliczenie kosztów według stawek CAS.",
        Internal => "Waluta wyniku PLN; własne szablony i kalkulacje EV.",
        _ => "",
    };

    public static bool IsKnown(string type) => All.Contains(type);
}
