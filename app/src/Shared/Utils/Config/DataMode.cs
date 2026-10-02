namespace PzlEv.Shared.Utils.Config;

/// <summary>Skąd moduły biorą dane: warstwa w pamięci (do czasu bazy TEST) albo MS SQL (F10).</summary>
public enum DataMode
{
    InMemory,
    Sql,
}
