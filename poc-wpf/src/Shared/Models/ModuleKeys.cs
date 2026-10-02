namespace PzlEv.Shared.Models;

/// <summary>
/// Klucze modułów – jedyny sposób, w jaki moduł wskazuje inny moduł (nawigacja przez INavigator).
/// Moduły nie odwołują się do swoich typów nawzajem (docs/architektura.md, rozdz. 5.3).
/// </summary>
public static class ModuleKeys
{
    public const string Dashboard = "dashboard";
    public const string Import = "import";
    public const string Mapping = "mapping";
    public const string MasterData = "master-data";
    public const string Projects = "projects";
    public const string Runs = "runs";
    public const string Reconciliation = "reconciliation";
    public const string Progress = "progress";
    public const string Evm = "evm";
    public const string Export = "export";
    public const string Administration = "administration";
    public const string Diagnostics = "diagnostics";
}
