using PzlEv.Shared.Utils.Mapping;
using PzlEv.Modules.Mapping.Models;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using static PzlEv.Shared.Utils.Mapping.MappingKeys;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Mapping.Services;

/// <summary>
/// Mapowanie CES ↔ P1S (docs/mapowanie-ces-p1s.md; G2 – docs/pipeline-fazy.md): stan z bazy PZL-EV i PZLPROD,
/// rozstrzyganie, korekty elementu i projektu CES z uzasadnieniem i historią, problemy po imporcie.
/// </summary>
public sealed class MappingService(IMappingStore store, IPzlProdSource? prod, IJournal journal, IProblemLog problems)
{
    public const string Area = "Mapowanie";

    /// <summary>Komunikat, gdy w pzl-ev.json nie ma sekcji PzlProd.</summary>
    public const string NoPzlProd = "Brak połączenia z PZLPROD – dodaj sekcję PzlProd w pzl-ev.json (Environments.<Env>.PzlProd: Server, Database, Schema).";

    public MappingState Load()
    {
        IReadOnlyList<P1sElement>? p1s = null;
        string? p1sError = prod is null ? NoPzlProd : null;
        if (prod is not null)
        {
            try
            {
                p1s = prod.Elements();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                p1sError = $"Odczyt PZLPROD (LOG.WBS) nieudany: {ex.Message}";
            }
        }
        var report = store.Report();
        var elements = store.CesElements();
        var corrections = store.ActiveCorrections();
        // Ostatni import z danymi ACTUALS – elementy, które pojawiły się w nim pierwszy raz, są „nowe” (G2).
        var latestBatch = elements.Count == 0 ? (long?)null : elements.Max(e => e.LastBatchId);
        var resolution = MappingResolver.Resolve(elements, report, corrections, p1s, latestBatch);
        return new MappingState(p1s, p1sError, p1s is null ? [] : P1sTreeBuilder.Build(p1s), report, elements, corrections, resolution, latestBatch);
    }

    public IReadOnlyList<CorrectionRow> History(string kind, string cesKey) => store.History(kind, cesKey);

    /// <summary>
    /// Korekta elementu albo projektu CES (rozdz. 4, 10): cel musi być elementem LOG.WBS; zmiana przypisania z raportu
    /// wymaga uzasadnienia (ERROR); cel nieaktywny albo usunięty – zapis z ostrzeżeniem (WARNING).
    /// </summary>
    public CorrectionSaveResult SaveCorrection(CorrectionInput input, MappingState state)
    {
        input = input with { CesKey = input.CesKey.Trim(), TargetPspnr = input.TargetPspnr.Trim(), Justification = input.Justification?.Trim() is { Length: > 0 } j ? j : null };
        var issues = new List<Issue>();
        var at = input.CesKey;
        if (input.Kind is not (CorrectionKinds.Element or CorrectionKinds.Project))
            issues.Add(Issue.Error($"Nieznany rodzaj korekty {input.Kind}", at));
        if (input.CesKey.Length == 0)
            issues.Add(Issue.Error(input.Kind == CorrectionKinds.Project ? "Projekt CES: pole wymagane" : "Element CES: pole wymagane", at));
        if (state.P1s is null)
            issues.Add(Issue.Error($"Cel korekty sprawdza się w LOG.WBS – {state.P1sError}", at));
        var target = state.FindP1s(input.TargetPspnr);
        if (state.P1s is not null && target is null)
            issues.Add(Issue.Error(input.TargetPspnr.Length == 0 ? "Cel P1S: wybierz element P1S" : $"Cel P1S {input.TargetPspnr} nie istnieje w LOG.WBS", at));

        var reportTarget = MappingResolver.ReportTarget(state.Report, state.P1s, input.Kind, input.CesKey);
        if (reportTarget is { } fromReport && target is not null && input.Justification is null && Key(fromReport.Pspnr) != Key(target.Pspnr))
            issues.Add(Issue.Error($"Zmiana przypisania z raportu mapowań ({(fromReport.Wbs.Length > 0 ? fromReport.Wbs : fromReport.Pspnr)} → {target.WbsElement}) wymaga uzasadnienia", at));
        if (issues.Any(i => i.Level == CheckLevel.Error))
            return new CorrectionSaveResult(false, issues, "Korekta ma błędy – nie zapisano.");
        if (target!.IsGreyed)
            issues.Add(Issue.Warning($"Cel {target.WbsElement} jest {(target.IsDeleted ? "usunięty" : "nieaktywny")} w P1S", at));

        var previous = Previous(input, state);
        var previousText = previous is null ? null : previous.IsMapped ? $"{previous.Status}: {previous.Target}" : previous.Status;
        var conflict = store.SaveCorrection(input with { TargetPspnr = target.Pspnr }, target.WbsElement, previousText);
        if (conflict is not null)
            return new CorrectionSaveResult(false, issues, conflict);

        journal.Add(Area, $"{Capitalized(CorrectionKinds.Label(input.Kind))} {input.CesKey} → {target.WbsElement}" +
                          $"{(input.RowId is null ? " dodana" : " zmieniona")}{(previousText is null ? "" : $" (było: {previousText})")}" +
                          $"{(input.Justification is null ? "" : $"; uzasadnienie: {input.Justification}")}");
        return new CorrectionSaveResult(true, issues, $"Zapisano korektę {input.CesKey} → {target.WbsElement}.");
    }

    /// <summary>Usunięcie korekty (zamknięcie ValidTo) – wraca przypisanie z raportu albo dziedziczenie.</summary>
    public CorrectionSaveResult DeleteCorrection(CorrectionRow correction)
    {
        var conflict = store.CloseCorrection(correction.RowId, correction.Version);
        if (conflict is not null)
            return new CorrectionSaveResult(false, [], conflict);
        journal.Add(Area, $"{Capitalized(correction.KindLabel)} {correction.CesKey} → {correction.TargetWbs} usunięta");
        return new CorrectionSaveResult(true, [], $"Usunięto korektę {correction.CesKey}.");
    }

    public const string ProblemReferencePrefix = "g2:";

    /// <summary>
    /// G2 po imporcie: nowe elementy CES (pierwszy raz w ostatnim imporcie) bez przypisania i z kosztem – WARNING
    /// w rejestrze problemów (raz na import, odwołanie g2:&lt;partia&gt;). Otwarte problemy G2 elementów, które mają już
    /// przypisanie albo nie mają kosztu, są rozwiązywane automatycznie. Zwraca liczbę dopisanych i rozwiązanych.
    /// </summary>
    public (int Added, int Resolved) SyncProblems(MappingState state)
    {
        var open = problems.Open().Where(p => p.Area == Area && p.Reference is { } r && r.StartsWith(ProblemReferencePrefix, StringComparison.Ordinal)).ToList();
        var results = state.Results.GroupBy(r => Key(r.CesElement)).ToDictionary(g => g.Key, g => g.First());
        var resolved = 0;
        foreach (var problem in open)
        {
            var result = results.GetValueOrDefault(Key(problem.Element));
            var resolution = result switch
            {
                null => "elementu CES nie ma już w danych ACTUALS",
                { IsMapped: true } => $"element przypisany: {result.Status} → {result.Target}",
                { HasCost: false } => "element bez kosztu",
                _ => null,
            };
            if (resolution is not null)
                resolved += problems.Resolve([problem.Id], resolution);
        }

        if (state.LatestBatchId is not { } batch)
            return (0, resolved);
        var reference = $"{ProblemReferencePrefix}{batch}";
        var recorded = open.Where(p => p.Reference == reference).Select(p => Key(p.Element)).ToHashSet();
        var added = 0;
        foreach (var result in state.Results.Where(r => r is { IsNew: true, Status: MappingStatuses.Unmapped, HasCost: true }))
        {
            if (recorded.Contains(Key(result.CesElement)))
                continue;
            problems.Add(Area, "element bez przypisania",
                Issue.Warning($"Nowy element CES (projekt {result.CesProject}) bez przypisania do P1S – koszt nie trafi do EV" +
                              (result.Proposal.Length > 0 ? $"; propozycja: {result.Proposal}" : ""), result.CesElement),
                reference);
            added++;
        }
        return (added, resolved);
    }

    /// <summary>Przypisanie przed korektą: elementu CES – jego wynik; projektu CES – wynik dowolnego jego elementu spoza raportu.</summary>
    private static MappingResult? Previous(CorrectionInput input, MappingState state)
    {
        var key = Key(input.CesKey);
        return input.Kind == CorrectionKinds.Element
            ? state.Results.FirstOrDefault(r => Key(r.CesElement) == key)
            : state.Results.FirstOrDefault(r => Key(r.CesProject) == key && r.Status is MappingStatuses.Inherited or MappingStatuses.Unmapped);
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
