using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Rejestr problemów (meta.Problem) – wspólny dla importu, mapowania, etapów i silnika EVM.</summary>
public interface IProblemLog
{
    void Add(string area, string check, Issue issue, string? reference = null);

    IReadOnlyList<ProblemRecord> ByReference(string reference);

    /// <summary>Problemy nierozwiązane (najstarsze pierwsze).</summary>
    IReadOnlyList<ProblemRecord> Open();

    /// <summary>Oznacza otwarte problemy jako rozwiązane (kto i kiedy – bieżący użytkownik); zwraca liczbę zmienionych.</summary>
    int Resolve(IReadOnlyCollection<long> ids, string resolution);
}
