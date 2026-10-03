using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Zakres projektu po stronie P1S (docs/performance-objectives.md, rozdz. 2, 4.1): kody P1S węzłów nakładki
/// (Legacy WBS z Excela i cel z mapowania CES ↔ P1S) oraz elementy LOG.WBS leżące pod celami mapowania.
/// Element P1S należy do projektu, gdy jest jednym z tych kodów, leży pod nim w LOG.WBS albo jego kod zaczyna się od
/// kodu z kropką (np. AC-CAB.6.38.01 pod AC-CAB.6.38).
/// </summary>
public sealed class P1sScope
{
    private readonly Dictionary<string, string> _below;

    /// <param name="roots">Kody P1S węzłów nakładki.</param>
    /// <param name="below">Elementy LOG.WBS pod celami mapowania: (kod elementu, kod celu).</param>
    public P1sScope(IEnumerable<string> roots, IEnumerable<(string Code, string Root)> below)
    {
        Roots = roots.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Below = below.ToList();
        _below = new Dictionary<string, string>();
        foreach (var (code, root) in Below)
            _below.TryAdd(MappingKeys.Key(code), root);
    }

    public static P1sScope Empty { get; } = new([], []);

    public IReadOnlyList<string> Roots { get; }

    public IReadOnlyList<(string Code, string Root)> Below { get; }

    /// <summary>Kod P1S węzła nakładki obejmujący element (najdłuższy pasujący); null – element poza zakresem.</summary>
    public string? RootOf(string p1sElement)
    {
        var exact = Roots.FirstOrDefault(r => MappingKeys.Key(r) == MappingKeys.Key(p1sElement));
        if (exact is not null)
            return exact;
        if (_below.TryGetValue(MappingKeys.Key(p1sElement), out var root))
            return root;
        return Roots
            .Where(r => p1sElement.StartsWith(r + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Length)
            .FirstOrDefault();
    }

    /// <summary>Elementy P1S zakresu w kolejności: kody węzłów, potem elementy LOG.WBS pod celami (szablon „WP i CAM”).</summary>
    public IReadOnlyList<string> Elements() =>
        Roots.Concat(Below.Select(b => b.Code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
