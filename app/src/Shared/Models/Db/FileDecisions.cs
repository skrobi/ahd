namespace PzlEv.Shared.Models.Db;

/// <summary>Decyzje importu dla pliku (docs/pipeline-fazy.md, G1).</summary>
public static class FileDecisions
{
    public const string Imported = "zaimportowany";
    public const string Skipped = "pominięty";
    public const string Duplicate = "duplikat";
    public const string Unrecognized = "nierozpoznany";
    public const string Error = "błąd";

    /// <summary>Decyzje, po których te same metadane pliku pozwalają go pominąć przy kolejnym imporcie.</summary>
    public static readonly IReadOnlySet<string> Settled = new HashSet<string> { Imported, Skipped, Duplicate };
}
