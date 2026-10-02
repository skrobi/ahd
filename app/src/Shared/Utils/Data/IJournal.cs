using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Dziennik zdarzeń: każda akcja zapisu zostawia wpis z kontem użytkownika.</summary>
public interface IJournal
{
    void Add(string area, string message, string? scope = null);

    IReadOnlyList<JournalEntry> Recent(int count);
}
