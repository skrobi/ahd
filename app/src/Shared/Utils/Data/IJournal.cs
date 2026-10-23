using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Dziennik zdarzeń: każda akcja zapisu zostawia wpis z kontem użytkownika.</summary>
public interface IJournal
{
    void Add(string area, string message, string? scope = null);

    IReadOnlyList<JournalEntry> Recent(int count);

    /// <summary>Ostatnie wpisy obszaru o zakresie scope (np. kod przebiegu), od najnowszego.</summary>
    IReadOnlyList<JournalEntry> ForScope(string area, string scope, int count);
}
