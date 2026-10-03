using PzlEv.Shared.Models;

namespace PzlEv.Modules.Projects.Models;

/// <summary>Wynik „Utwórz projekt”: czy projekt zarejestrowano, kroki wykonane (do pokazania) i problemy.</summary>
public sealed record CreateOutcome(bool Created, IReadOnlyList<string> Steps, IReadOnlyList<Issue> Issues);
