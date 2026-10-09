using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Shared.Utils.Mapping;

/// <summary>
/// Rozstrzyganie mapowania elementów CES (docs/mapowanie-ces-p1s.md, rozdz. 5): 1. korekta elementu → OVERRIDE,
/// 2. raport mapowań → REPORT, 3. WBS spoza raportu: korekta projektu CES, a bez niej odpowiednik projektu CES
/// (project_sap) → INHERITED, 4. brak → UNMAPPED (z propozycją: najczęstszy cel elementów tego samego projektu CES).
/// Reguły walidacji – rozdz. 10; element UNMAPPED z kosztem – WARNING (koszt nie trafi do EV).
/// </summary>
public static class MappingResolver
{
    public static MappingResolution Resolve(
        IReadOnlyList<CesElement> elements,
        ReportInfo? report,
        IReadOnlyList<CorrectionRow> corrections,
        IReadOnlyList<P1sElement>? p1s,
        long? latestBatchId)
    {
        var issues = new List<Issue>();
        var targets = new Targets(p1s, report);

        var reportElements = new Dictionary<string, ReportEntry>();
        foreach (var group in (report?.Entries ?? [])
                     .Where(e => e.CesElement.Length > 0 && (e.TargetPspnr.Length > 0 || e.TargetWbs.Length > 0))
                     .GroupBy(e => MappingKeys.Key(e.CesElement)))
        {
            var first = group.OrderBy(e => e.RowNumber).First();
            reportElements[group.Key] = first;
            var distinct = group.Select(targets.Describe).Distinct().ToList();
            if (distinct.Count > 1)
                issues.Add(Issue.Error($"Element CES {first.CesElement}: w raporcie mapowań kilka celów P1S ({string.Join(", ", distinct)}) – element CES może mieć jeden cel; przyjęto wiersz {first.Label}", first.CesElement));
        }

        var reportProjects = new Dictionary<string, ReportEntry>();
        foreach (var group in (report?.Entries ?? [])
                     .Where(e => e.CesProject.Length > 0 && e.SapProject.Length > 0)
                     .GroupBy(e => MappingKeys.Key(e.CesProject)))
        {
            var first = group.OrderBy(e => e.RowNumber).First();
            reportProjects[group.Key] = first;
            var distinct = group.Select(e => e.SapProject).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count > 1)
                issues.Add(Issue.Error($"Projekt CES {first.CesProject}: w raporcie mapowań kilka odpowiedników project_sap ({string.Join(", ", distinct)}); przyjęto {first.SapProject} (wiersz {first.Label})", first.CesProject));
        }

        var elementCorrections = corrections.Where(c => c.Kind == CorrectionKinds.Element && c.IsActive).ToDictionary(c => MappingKeys.Key(c.CesKey));
        var projectCorrections = corrections.Where(c => c.Kind == CorrectionKinds.Project && c.IsActive).ToDictionary(c => MappingKeys.Key(c.CesKey));

        var results = new List<MappingResult>();
        foreach (var element in elements.OrderBy(e => e.WbsElement, StringComparer.Ordinal))
        {
            var key = MappingKeys.Key(element.WbsElement);
            var isNew = latestBatchId is { } batch && element.FirstBatchId == batch;
            if (elementCorrections.TryGetValue(key, out var correction))
            {
                var (pspnr, wbs) = targets.Of(correction.TargetPspnr, correction.TargetWbs);
                results.Add(new MappingResult(element.WbsElement, element.Project, MappingStatuses.Override, pspnr, wbs,
                    $"korekta elementu CES ({correction.RecordedBy}, {correction.RecordedAt.ToLocalTime():yyyy-MM-dd})", element.HasCost, isNew));
            }
            else if (reportElements.TryGetValue(key, out var entry))
            {
                var (pspnr, wbs) = targets.Of(entry.TargetPspnr, entry.TargetWbs);
                results.Add(new MappingResult(element.WbsElement, element.Project, MappingStatuses.Report, pspnr, wbs,
                    $"raport mapowań, wiersz {entry.Label}", element.HasCost, isNew));
            }
            else if (projectCorrections.TryGetValue(MappingKeys.Key(element.Project), out var projectCorrection))
            {
                var (pspnr, wbs) = targets.Of(projectCorrection.TargetPspnr, projectCorrection.TargetWbs);
                results.Add(new MappingResult(element.WbsElement, element.Project, MappingStatuses.Inherited, pspnr, wbs,
                    $"korekta projektu CES {element.Project} ({projectCorrection.RecordedBy}, {projectCorrection.RecordedAt.ToLocalTime():yyyy-MM-dd})", element.HasCost, isNew));
            }
            else if (reportProjects.TryGetValue(MappingKeys.Key(element.Project), out var project))
            {
                var (pspnr, wbs) = targets.Of("", project.SapProject);
                results.Add(new MappingResult(element.WbsElement, element.Project, MappingStatuses.Inherited, pspnr, wbs,
                    $"projekt CES {element.Project} → {project.SapProject} (raport mapowań, wiersz {project.Label})", element.HasCost, isNew));
            }
            else
            {
                results.Add(new MappingResult(element.WbsElement, element.Project, MappingStatuses.Unmapped, "", "", "brak przypisania", element.HasCost, isNew));
            }
        }

        results = WithProposals(results);
        issues.AddRange(TargetIssues(results, corrections, targets));
        foreach (var unmapped in results.Where(r => r is { Status: MappingStatuses.Unmapped, HasCost: true }))
            issues.Add(Issue.Warning($"Element CES {unmapped.CesElement} (projekt {unmapped.CesProject}) bez przypisania do P1S – jego koszt nie trafi do EV żadnego projektu", unmapped.CesElement));
        return new MappingResolution(results, issues);
    }

    /// <summary>
    /// Cel z raportu mapowań dla elementu CES (wiersz raportu) albo projektu CES (project_sap); null – raport go nie
    /// przypisuje. Korekta o innym celu wymaga uzasadnienia (rozdz. 4).
    /// </summary>
    public static (string Pspnr, string Wbs)? ReportTarget(ReportInfo? report, IReadOnlyList<P1sElement>? p1s, string kind, string cesKey)
    {
        var key = MappingKeys.Key(cesKey);
        var targets = new Targets(p1s, report);
        var entries = (report?.Entries ?? []).OrderBy(e => e.RowNumber);
        if (kind == CorrectionKinds.Element)
        {
            var entry = entries.FirstOrDefault(e => MappingKeys.Key(e.CesElement) == key && (e.TargetPspnr.Length > 0 || e.TargetWbs.Length > 0));
            return entry is null ? null : targets.Of(entry.TargetPspnr, entry.TargetWbs);
        }
        var project = entries.FirstOrDefault(e => MappingKeys.Key(e.CesProject) == key && e.SapProject.Length > 0);
        return project is null ? null : targets.Of("", project.SapProject);
    }

    /// <summary>Propozycja dla elementu bez przypisania: najczęstszy cel elementów tego samego projektu CES.</summary>
    private static List<MappingResult> WithProposals(List<MappingResult> results)
    {
        var byProject = results
            .Where(r => r.IsMapped && r.CesProject.Length > 0)
            .GroupBy(r => MappingKeys.Key(r.CesProject))
            .ToDictionary(g => g.Key, g => g.GroupBy(r => (r.TargetPspnr, r.TargetWbs)).OrderByDescending(t => t.Count()).ThenBy(t => t.Key.TargetWbs).First().Key);
        return results
            .Select(r => r.Status == MappingStatuses.Unmapped && byProject.TryGetValue(MappingKeys.Key(r.CesProject), out var proposal)
                ? r with { ProposalPspnr = proposal.TargetPspnr, ProposalWbs = proposal.TargetWbs }
                : r)
            .ToList();
    }

    /// <summary>Cele spoza LOG.WBS (WARNING) i korekty wskazujące element nieaktywny albo usunięty (WARNING).</summary>
    private static IEnumerable<Issue> TargetIssues(List<MappingResult> results, IReadOnlyList<CorrectionRow> corrections, Targets targets)
    {
        if (!targets.HasP1s)
            yield break;
        foreach (var group in results.Where(r => r.IsMapped && targets.Find(r.TargetPspnr, r.TargetWbs) is null)
                     .GroupBy(r => r.Target)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var sample = string.Join(", ", group.Take(5).Select(r => r.CesElement)) + (group.Count() > 5 ? $" i {group.Count() - 5} innych" : "");
            yield return Issue.Warning($"Cel P1S {group.Key} nie istnieje w LOG.WBS – elementy CES: {sample}", group.Key);
        }
        foreach (var correction in corrections.Where(c => c.IsActive))
        {
            if (targets.Find(correction.TargetPspnr, correction.TargetWbs) is { IsGreyed: true } target)
                yield return Issue.Warning($"{correction.KindLabel} {correction.CesKey}: cel {target.WbsElement} jest {(target.IsDeleted ? "usunięty" : "nieaktywny")} w P1S", correction.CesKey);
        }
    }

    /// <summary>Wyszukiwanie celów w LOG.WBS po PSPNR albo kodzie WBS; bez LOG.WBS – kody z raportu.</summary>
    private sealed class Targets
    {
        private readonly Dictionary<string, P1sElement> _byPspnr = new();
        private readonly Dictionary<string, P1sElement> _byWbs = new();
        private readonly Dictionary<string, string> _reportPspnrByWbs = new();

        public Targets(IReadOnlyList<P1sElement>? p1s, ReportInfo? report)
        {
            HasP1s = p1s is not null;
            foreach (var element in p1s ?? [])
            {
                if (element.Pspnr.Length > 0)
                    _byPspnr.TryAdd(MappingKeys.Key(element.Pspnr), element);
                if (element.WbsElement.Length > 0)
                    _byWbs.TryAdd(MappingKeys.Key(element.WbsElement), element);
            }
            foreach (var entry in (report?.Entries ?? []).Where(e => e.TargetWbs.Length > 0 && e.TargetPspnr.Length > 0))
                _reportPspnrByWbs.TryAdd(MappingKeys.Key(entry.TargetWbs), entry.TargetPspnr);
        }

        public bool HasP1s { get; }

        public P1sElement? Find(string pspnr, string wbs) =>
            pspnr.Length > 0 && _byPspnr.TryGetValue(MappingKeys.Key(pspnr), out var byPspnr) ? byPspnr
            : pspnr.Length == 0 && wbs.Length > 0 && _byWbs.TryGetValue(MappingKeys.Key(wbs), out var byWbs) ? byWbs
            : null;

        /// <summary>PSPNR i kod WBS celu – uzupełnione z LOG.WBS albo (kod → PSPNR) z raportu.</summary>
        public (string Pspnr, string Wbs) Of(string pspnr, string wbs)
        {
            if (Find(pspnr, wbs) is { } element)
                return (element.Pspnr, element.WbsElement);
            if (pspnr.Length == 0 && wbs.Length > 0 && _reportPspnrByWbs.TryGetValue(MappingKeys.Key(wbs), out var fromReport))
                return Find(fromReport, "") is { } found ? (found.Pspnr, found.WbsElement) : (fromReport, wbs);
            return (pspnr, wbs);
        }

        public string Describe(ReportEntry entry)
        {
            var (pspnr, wbs) = Of(entry.TargetPspnr, entry.TargetWbs);
            return wbs.Length > 0 ? wbs : pspnr;
        }
    }
}
