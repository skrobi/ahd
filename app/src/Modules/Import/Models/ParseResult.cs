using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Import.Models;

/// <summary>Wynik parsera: dane kanoniczne (gdy brak błędów) i problemy z numerami wierszy.</summary>
public sealed record ParseResult(IReadOnlyList<ActualsRow> Rows, IReadOnlyList<Issue> Issues, int ErrorCount);
