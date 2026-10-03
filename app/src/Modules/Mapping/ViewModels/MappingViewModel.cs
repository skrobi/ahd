using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Mapping.Models;
using PzlEv.Modules.Mapping.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Mapping.ViewModels;

/// <summary>
/// Ekran Mapowanie CES ↔ P1S (docs/mapowanie-ces-p1s.md, rozdz. 11): elementy CES ze statusem i celem (filtr, szukaj),
/// drzewo P1S z elementami CES pod celami i węzłem „Nieprzypisane” (z propozycją celu), korekta elementu albo projektu
/// CES z uzasadnieniem, usunięcie korekty i historia. Bez kwot. Po odczycie – problemy G2 dla nowych elementów.
/// </summary>
public sealed class MappingViewModel : ObservableObject
{
    public const string AllStatuses = "wszystkie";
    public const string NewOnly = "nowe z ostatniego importu";

    private static readonly ILogger Logger = Log.ForContext("Module", "mapping");

    private readonly MappingService _service;
    private MappingState? _state;
    private bool _loading;
    private string _status = "Wczytywanie…";
    private string _source = "";
    private string? _p1sError;
    private IReadOnlyList<Issue> _checks = [];
    private IReadOnlyList<MappingResult> _results = [];
    private IReadOnlyList<P1sTreeItem> _tree = [];
    private string _search = "";
    private string _statusFilter = AllStatuses;
    private MappingResult? _selected;
    private string _p1sSearch = "";
    private IReadOnlyList<P1sElement> _p1sMatches = [];
    private P1sElement? _target;
    private bool _projectCorrection;
    private string _justification = "";
    private CorrectionRow? _activeCorrection;
    private IReadOnlyList<CorrectionRow> _history = [];
    private string _saveMessage = "";
    private IReadOnlyList<Issue> _saveIssues = [];

    public MappingViewModel(MappingService service)
    {
        _service = service;
        Refresh = new AsyncRelayCommand(Load, () => !_loading);
        SaveCorrection = new AsyncRelayCommand(Save, () => !_loading && _state is not null && Selected is not null && Target is not null);
        DeleteCorrection = new AsyncRelayCommand(Delete, () => !_loading && ActiveCorrection is not null);
        UseProposal = new RelayCommand(_ => Target = _state?.FindP1s(Selected!.ProposalPspnr ?? ""), _ => Selected is { ProposalPspnr.Length: > 0 });
        Refresh.Execute(null);
    }

    public ICommand Refresh { get; }

    public ICommand SaveCorrection { get; }

    public ICommand DeleteCorrection { get; }

    public ICommand UseProposal { get; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Raport mapowań i PZLPROD, z których wynika stan.</summary>
    public string Source { get => _source; private set => SetProperty(ref _source, value); }

    public string? P1sError
    {
        get => _p1sError;
        private set
        {
            if (SetProperty(ref _p1sError, value))
                OnPropertyChanged(nameof(HasP1sError));
        }
    }

    public bool HasP1sError => P1sError is not null;

    /// <summary>Reguły walidacji (rozdz. 10) i elementy bez przypisania z kosztem.</summary>
    public IReadOnlyList<Issue> Checks { get => _checks; private set => SetProperty(ref _checks, value); }

    public IReadOnlyList<string> StatusFilters { get; } =
        [AllStatuses, NewOnly, MappingStatuses.Unmapped, MappingStatuses.Report, MappingStatuses.Inherited, MappingStatuses.Override];

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
                ApplyFilter();
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
                ApplyFilter();
        }
    }

    public IReadOnlyList<MappingResult> Results { get => _results; private set => SetProperty(ref _results, value); }

    public IReadOnlyList<P1sTreeItem> Tree { get => _tree; private set => SetProperty(ref _tree, value); }

    public MappingResult? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                LoadCorrection();
        }
    }

    /// <summary>Korekta projektu CES zamiast korekty elementu CES.</summary>
    public bool ProjectCorrection
    {
        get => _projectCorrection;
        set
        {
            if (SetProperty(ref _projectCorrection, value))
                LoadCorrection();
        }
    }

    public string CorrectionKey => Selected is null ? "" : ProjectCorrection ? $"projekt CES {Selected.CesProject}" : $"element CES {Selected.CesElement}";

    public string P1sSearch
    {
        get => _p1sSearch;
        set
        {
            if (SetProperty(ref _p1sSearch, value))
                P1sMatches = FindP1s(value);
        }
    }

    public IReadOnlyList<P1sElement> P1sMatches { get => _p1sMatches; private set => SetProperty(ref _p1sMatches, value); }

    /// <summary>Wybór w wynikach wyszukiwania – ustawia cel (wyczyszczenie listy przy nowym wyszukiwaniu celu nie zmienia).</summary>
    public P1sElement? SelectedMatch
    {
        get => null;
        set
        {
            if (value is not null)
                Target = value;
        }
    }

    /// <summary>Cel korekty: wybrany w wynikach wyszukiwania albo w drzewie P1S.</summary>
    public P1sElement? Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value))
                OnPropertyChanged(nameof(TargetText));
        }
    }

    public string TargetText => Target is null ? "wybierz element P1S w drzewie albo wyszukaj" : Target.Label;

    public string Justification { get => _justification; set => SetProperty(ref _justification, value); }

    public CorrectionRow? ActiveCorrection { get => _activeCorrection; private set => SetProperty(ref _activeCorrection, value); }

    public IReadOnlyList<CorrectionRow> History { get => _history; private set => SetProperty(ref _history, value); }

    public string SaveMessage { get => _saveMessage; private set => SetProperty(ref _saveMessage, value); }

    public IReadOnlyList<Issue> SaveIssues { get => _saveIssues; private set => SetProperty(ref _saveIssues, value); }

    /// <summary>Wybór w drzewie: element P1S – cel korekty; element CES – wybrany element CES.</summary>
    public void SelectTreeItem(object? item)
    {
        switch (item)
        {
            case P1sTreeItem { Element: { } element }:
                Target = element;
                break;
            case CesTreeItem ces:
                Selected = ces.Result;
                break;
        }
    }

    private async Task Load()
    {
        _loading = true;
        Status = "Wczytywanie mapowania i struktury P1S…";
        try
        {
            var (state, problems) = await Task.Run(() =>
            {
                var loaded = _service.Load();
                return (loaded, _service.RecordNewElementProblems(loaded));
            });
            _state = state;
            P1sError = state.P1sError;
            Source = $"Raport mapowań: {state.Report?.Describe ?? "nie zaimportowano (Import, parser MAPOWANIA)"} · " +
                     $"P1S: {(state.P1s is null ? "niedostępne" : $"{state.P1s.Count} elementów LOG.WBS")}";
            Checks = state.Issues.OrderBy(i => i.Level == CheckLevel.Error ? 0 : 1).ToList();
            Tree = BuildTree(state);
            ApplyFilter();
            var counts = state.Results.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
            Status = $"Elementy CES: {state.Results.Count} · REPORT {counts.GetValueOrDefault(MappingStatuses.Report)} · " +
                     $"INHERITED {counts.GetValueOrDefault(MappingStatuses.Inherited)} · OVERRIDE {counts.GetValueOrDefault(MappingStatuses.Override)} · " +
                     $"UNMAPPED {counts.GetValueOrDefault(MappingStatuses.Unmapped)} · nowe {state.Results.Count(r => r.IsNew)}" +
                     (problems > 0 ? $" · dopisano {problems} problemów (G2)" : "");
            LoadCorrection();
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            Logger.Error(ex, "Odczyt mapowania nieudany – brak tabel");
            Status = $"Brak tabel mapowania w bazie – wykonaj migrację 005 (Diagnostyka → Migracja). {ex.Message}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Odczyt mapowania nieudany");
            Status = $"Odczyt mapowania nieudany: {ex.Message}";
        }
        finally
        {
            _loading = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ApplyFilter()
    {
        if (_state is null)
            return;
        var text = Search.Trim();
        Results = _state.Results
            .Where(r => StatusFilter == AllStatuses || (StatusFilter == NewOnly ? r.IsNew : r.Status == StatusFilter))
            .Where(r => text.Length == 0 || r.CesElement.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || r.CesProject.Contains(text, StringComparison.OrdinalIgnoreCase) || r.Target.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private IReadOnlyList<P1sElement> FindP1s(string text)
    {
        text = text.Trim();
        if (_state?.P1s is null || text.Length < 2)
            return [];
        return _state.P1s
            .Where(e => e.WbsElement.Contains(text, StringComparison.OrdinalIgnoreCase) || e.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || e.Pspnr.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.WbsElement, StringComparer.Ordinal)
            .Take(200)
            .ToList();
    }

    /// <summary>Korekta (bieżąca) i historia dla wybranego elementu albo projektu CES.</summary>
    private void LoadCorrection()
    {
        OnPropertyChanged(nameof(CorrectionKey));
        SaveMessage = "";
        SaveIssues = [];
        if (_state is null || Selected is null)
        {
            ActiveCorrection = null;
            History = [];
            return;
        }
        var (kind, key) = KindAndKey();
        ActiveCorrection = _state.Corrections.FirstOrDefault(c => c.Kind == kind && MappingKeys.Key(c.CesKey) == MappingKeys.Key(key));
        Target = ActiveCorrection is { } correction ? _state.FindP1s(correction.TargetPspnr) : Target;
        Justification = ActiveCorrection?.Justification ?? "";
        try
        {
            History = _service.History(kind, key).Reverse().ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Odczyt historii korekty nieudany");
            History = [];
        }
    }

    private (string Kind, string Key) KindAndKey() =>
        ProjectCorrection ? (CorrectionKinds.Project, Selected!.CesProject) : (CorrectionKinds.Element, Selected!.CesElement);

    private Task Save()
    {
        var (kind, key) = KindAndKey();
        return AfterSave(_service.SaveCorrection(
            new CorrectionInput(kind, key, Target!.Pspnr, Justification, ActiveCorrection?.RowId, ActiveCorrection?.Version), _state!));
    }

    private Task Delete() => AfterSave(_service.DeleteCorrection(ActiveCorrection!));

    /// <summary>Po zapisie – stan od nowa (rozstrzyganie z nową korektą), ten sam element CES wybrany, wynik zapisu widoczny.</summary>
    private async Task AfterSave(CorrectionSaveResult result)
    {
        if (result.Success && Selected is { } selected)
        {
            await Load();
            Selected = _state?.Results.FirstOrDefault(r => r.CesElement == selected.CesElement) ?? selected;
        }
        SaveMessage = result.Message;
        SaveIssues = result.Issues;
    }

    /// <summary>Drzewo P1S z elementami CES pod celami; „Nieprzypisane” (wg projektu CES, z propozycją) i „Cel spoza LOG.WBS”.</summary>
    private static IReadOnlyList<P1sTreeItem> BuildTree(MappingState state)
    {
        var byTarget = state.Results.Where(r => r.IsMapped && r.TargetPspnr.Length > 0)
            .GroupBy(r => MappingKeys.Key(r.TargetPspnr))
            .ToDictionary(g => g.Key, g => g.ToList());
        var placed = new HashSet<MappingResult>(ReferenceEqualityComparer.Instance);
        var roots = new List<P1sTreeItem>();

        var unmapped = state.Results.Where(r => !r.IsMapped).ToList();
        if (unmapped.Count > 0)
        {
            var node = new P1sTreeItem($"Nieprzypisane ({unmapped.Count})") { IsExpanded = true, CesCount = unmapped.Count };
            foreach (var project in unmapped.GroupBy(r => r.CesProject).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var proposal = project.First().Proposal;
                var projectNode = new P1sTreeItem($"Projekt CES {project.Key}{(proposal.Length > 0 ? $" – propozycja: {proposal}" : "")}") { CesCount = project.Count() };
                projectNode.Children.AddRange(project.Select(r => new CesTreeItem(r)));
                node.Children.Add(projectNode);
            }
            roots.Add(node);
        }

        foreach (var node in state.Tree)
            roots.Add(Convert(node, byTarget, placed));

        var outside = state.Results.Where(r => r.IsMapped && !placed.Contains(r)).ToList();
        if (outside.Count > 0)
        {
            var node = new P1sTreeItem($"Cel spoza LOG.WBS ({outside.Count})") { CesCount = outside.Count };
            foreach (var target in outside.GroupBy(r => r.Target).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var targetNode = new P1sTreeItem(target.Key) { CesCount = target.Count() };
                targetNode.Children.AddRange(target.Select(r => new CesTreeItem(r)));
                node.Children.Add(targetNode);
            }
            roots.Add(node);
        }
        return roots;
    }

    private static P1sTreeItem Convert(P1sNode node, Dictionary<string, List<MappingResult>> byTarget, HashSet<MappingResult> placed)
    {
        var item = new P1sTreeItem(node.Title, node.Element, node.IsGreyed);
        if (node.Element is not null && byTarget.TryGetValue(MappingKeys.Key(node.Element.Pspnr), out var mapped))
        {
            item.Children.AddRange(mapped.Select(r => new CesTreeItem(r)));
            foreach (var result in mapped)
                placed.Add(result);
            item.CesCount += mapped.Count;
        }
        foreach (var child in node.Children)
        {
            var converted = Convert(child, byTarget, placed);
            item.CesCount += converted.CesCount;
            item.Children.Add(converted);
        }
        return item;
    }
}
