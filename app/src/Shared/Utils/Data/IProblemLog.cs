using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Rejestr problemów (meta.Problem) – wspólny dla importu, mapowania, etapów i silnika EVM.</summary>
public interface IProblemLog
{
    void Add(string area, string check, Issue issue, string? reference = null);

    IReadOnlyList<ProblemRecord> ByReference(string reference);

    IReadOnlyList<ProblemRecord> Open();
}
