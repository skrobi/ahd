using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Mapping.Data;
using PzlEv.Modules.Mapping.Models;
using PzlEv.Modules.Mapping.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Mapping;

/// <summary>
/// Mapowanie na bazie testowej: raport mapowań i ACTUALS przez import (parsery MAPOWANIA i ACTUALS), struktura P1S
/// z PZLPROD (TestPzlProd), rozstrzyganie, korekty z historią i problemy po imporcie (G2).
/// </summary>
public sealed class MappingServiceTests : IDisposable
{
    private const string ReportHeader =
        "src;pspnr;pspnr_sap;pspnr_ces;pspnr_parent;project;project_sap;project_sap_org;project_ces;wbs;wbs_sap;wbs_ces;wbs_desc;network";

    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-mapping-").FullName;
    private TestDatabase? _database;
    private TestPzlProd? _prod;
    private AppServices _app = null!;
    private ImportService _import = null!;
    private MappingService _mapping = null!;

    /// <summary>Baza z presetami (ACTUALS_PAF), definicja raportu mapowań (prefiks MAPOWANIA_) i PZLPROD testowy.</summary>
    private void Use(bool pzlProd = true)
    {
        _database = new TestDatabase(presets: true);
        _app = _services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), _app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        Assert.True(config.SaveDefinition(new DefinitionInput(null, null, "MAPOWANIA", "MAPOWANIA_", "Raport mapowań SAP↔CES", "MAPOWANIA", Active: true)).Success);
        _import = new ImportService(new SqlImportStore(_database.Sql), _app);
        _prod = pzlProd ? new TestPzlProd(P1sSample.Elements) : null;
        _mapping = Mapping();
    }

    private MappingService Mapping() =>
        new(new SqlMappingStore(_database!.Sql, _services.Clock, _services.User), _prod is null ? null : new SqlPzlProdSource(_prod.Sql), _app.Journal, _app.Problems);

    /// <summary>ACTUALS_PAF_01.csv (2DI473001001/002, 4D03GZ000001/041) i raport: 2DI473001001 → PSPNR 1003, projekt 2DI473 → AC-I39.1.01.</summary>
    private void ImportSamples(params string[] extraReportLines)
    {
        Directory.CreateDirectory(_app.Config.ImportFolder);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(_app.Config.ImportFolder, "ACTUALS_PAF_01.csv"), overwrite: true);
        File.WriteAllLines(Path.Combine(_app.Config.ImportFolder, "MAPOWANIA_SAP_CES.csv"),
        [
            ReportHeader,
            "SAP;1003;1003;50001;1002;AC-I39;AC-I39.1.01;AC-I39;2DI473;AC-I39.1.01.01;AC-I39.1.01.01;2DI473001001;ENG;",
            "CES;60002;1002;;;2DI473;AC-I39.1.01;AC-I39;2DI473;2DI473001099;;;Element tylko w CES;",
            .. extraReportLines,
        ]);
        var run = _import.Run();
        Assert.All(run.Files, f => Assert.Equal(FileDecisions.Imported, f.Decision));
        Assert.Equal(2, run.Files.Count);
    }

    private static MappingResult Of(MappingState state, string element) => state.Results.Single(r => r.CesElement == element);

    public void Dispose()
    {
        _prod?.Dispose();
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [SqlFact]
    public void Report_from_excel_import_and_pzlprod_structure_resolve_ces_elements()
    {
        Use();
        ImportSamples();

        var state = _mapping.Load();

        Assert.Null(state.P1sError);
        Assert.Equal(P1sSample.Elements.Count, state.Tree.Sum(n => n.ElementCount));
        Assert.Equal("MAPOWANIA_SAP_CES.csv", state.Report!.FileName);
        Assert.Equal(2, state.Report.Entries.Count);
        Assert.Equal((MappingStatuses.Report, "00001003", "AC-I39.1.01.01"),
            (Of(state, "2DI473001001").Status, Of(state, "2DI473001001").TargetPspnr, Of(state, "2DI473001001").TargetWbs));
        Assert.Equal((MappingStatuses.Inherited, "AC-I39.1.01"), (Of(state, "2DI473001002").Status, Of(state, "2DI473001002").TargetWbs));
        Assert.Equal(MappingStatuses.Unmapped, Of(state, "4D03GZ000001").Status);
        Assert.Equal(MappingStatuses.Unmapped, Of(state, "4D03GZ000041").Status);
        Assert.All(state.Results, r => Assert.True(r.IsNew));   // pierwszy import
        Assert.Equal(2, state.Issues.Count(i => i.Level == CheckLevel.Warning && i.Message.Contains("bez przypisania")));
    }

    [SqlFact]
    public void Element_correction_needs_justification_when_changing_report_and_keeps_history()
    {
        Use();
        ImportSamples();
        var state = _mapping.Load();

        var withoutReason = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Element, "2DI473001001", "00001004", null), state);
        Assert.False(withoutReason.Success);
        Assert.Contains(withoutReason.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("wymaga uzasadnienia"));

        var saved = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Element, "2DI473001001", "1004", "koszt materiału MFG"), state);
        Assert.True(saved.Success, saved.Message);
        Assert.Contains(saved.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("usunięty"));   // LOEKZ = X

        state = _mapping.Load();
        var result = Of(state, "2DI473001001");
        Assert.Equal((MappingStatuses.Override, "00001004"), (result.Status, result.TargetPspnr));
        var correction = Assert.Single(state.Corrections);
        Assert.Equal(("REPORT: AC-I39.1.01.01", "AC-I39.1.01.02"), (correction.PreviousTarget, correction.TargetWbs));

        // zmiana celu – nowa wersja; zapis na nieaktualnej wersji odrzucony
        Assert.True(_mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Element, "2DI473001001", "00001003", null, correction.RowId, correction.Version), state).Success);
        var stale = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Element, "2DI473001001", "00001002", "inny", correction.RowId, correction.Version), state);
        Assert.False(stale.Success);
        Assert.Contains("zmienił", stale.Message);

        // usunięcie korekty – wraca przypisanie z raportu; historia ma trzy wersje
        state = _mapping.Load();
        Assert.True(_mapping.DeleteCorrection(Assert.Single(state.Corrections)).Success);
        state = _mapping.Load();
        Assert.Empty(state.Corrections);
        Assert.Equal(MappingStatuses.Report, Of(state, "2DI473001001").Status);
        var history = _mapping.History(CorrectionKinds.Element, "2DI473001001");
        Assert.Equal(["dodana", "zmieniona", "usunięta 2026-10-02"], history.Select(h => h.Change));
        Assert.Equal(1, history.Select(h => h.RowId).Distinct().Count());
        Assert.Contains(_app.Journal.Recent(10), e => e.Message.StartsWith("Korekta elementu CES 2DI473001001 → AC-I39.1.01.02 dodana (było: REPORT: AC-I39.1.01.01); uzasadnienie: koszt materiału MFG"));
        Assert.Contains(_app.Journal.Recent(10), e => e.Message == "Korekta elementu CES 2DI473001001 → AC-I39.1.01.01 usunięta");
    }

    [SqlFact]
    public void Correction_of_unmapped_element_gives_override_without_justification()
    {
        Use();
        ImportSamples();
        var state = _mapping.Load();
        var unmapped = Of(state, "4D03GZ000001");
        Assert.Equal(MappingStatuses.Unmapped, unmapped.Status);

        var saved = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Element, unmapped.CesElement, "00001003", null), state);

        Assert.True(saved.Success, saved.Message);
        Assert.Empty(saved.Issues);
        var result = Of(_mapping.Load(), "4D03GZ000001");
        Assert.Equal((MappingStatuses.Override, "AC-I39.1.01.01"), (result.Status, result.TargetWbs));
        Assert.Equal("UNMAPPED", Assert.Single(_mapping.History(CorrectionKinds.Element, "4D03GZ000001")).PreviousTarget);
    }

    [SqlFact]
    public void Project_correction_assigns_elements_outside_report_without_justification_when_report_has_no_target()
    {
        Use();
        ImportSamples();
        var state = _mapping.Load();

        var saved = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Project, "4D03GZ", "00002001", null), state);

        Assert.True(saved.Success, saved.Message);
        Assert.Contains(saved.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("nieaktywny"));   // Z_ACTIVE puste
        state = _mapping.Load();
        Assert.All(new[] { "4D03GZ000001", "4D03GZ000041" }, e =>
            Assert.Equal((MappingStatuses.Inherited, "MC-00.001"), (Of(state, e).Status, Of(state, e).TargetWbs)));

        // korekta projektu z celem z raportu (2DI473 → AC-I39.1.01) zmieniająca go – wymaga uzasadnienia
        var change = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Project, "2DI473", "00001001", null), state);
        Assert.False(change.Success);
        Assert.Contains(change.Issues, i => i.Message.Contains("wymaga uzasadnienia"));
    }

    [SqlFact]
    public void New_unmapped_elements_with_cost_are_recorded_as_problems_once_per_import()
    {
        Use();
        ImportSamples();
        var state = _mapping.Load();

        Assert.Equal(2, _mapping.RecordNewElementProblems(state));
        Assert.Equal(0, _mapping.RecordNewElementProblems(_mapping.Load()));

        var problems = _app.Problems.ByReference($"g2:{state.LatestBatchId}");
        Assert.Equal(["4D03GZ000001", "4D03GZ000041"], problems.Select(p => p.Element).Order());
        Assert.All(problems, p => Assert.Equal((CheckLevel.Warning, MappingService.Area), (p.Level, p.Area)));
    }

    [SqlFact]
    public void Without_pzlprod_mapping_shows_what_is_missing_and_correction_is_rejected()
    {
        Use(pzlProd: false);
        ImportSamples();

        var state = _mapping.Load();

        Assert.Equal(MappingService.NoPzlProd, state.P1sError);
        Assert.Empty(state.Tree);
        Assert.Equal(MappingStatuses.Report, Of(state, "2DI473001001").Status);
        var result = _mapping.SaveCorrection(new CorrectionInput(CorrectionKinds.Project, "4D03GZ", "00002001", null), state);
        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Message.Contains("PzlProd"));
    }

    [SqlFact]
    public void Without_imported_report_all_elements_are_unmapped_and_no_report_is_shown()
    {
        Use();
        Directory.CreateDirectory(_app.Config.ImportFolder);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(_app.Config.ImportFolder, "ACTUALS_PAF_01.csv"));
        _import.Run();

        var state = _mapping.Load();

        Assert.Null(state.Report);
        Assert.Equal(4, state.Results.Count(r => r.Status == MappingStatuses.Unmapped));
    }

    [SqlFact]
    public void Pzlprod_source_reads_log_wbs_as_text_and_reports_flag_values()
    {
        _prod = new TestPzlProd(P1sSample.Elements);
        var source = new SqlPzlProdSource(_prod.Sql);

        var elements = source.Elements();
        var root = elements.Single(e => e.WbsElement == "AC-I39");
        Assert.Equal(("00001000", "", 1, "Internal Work"), (root.Pspnr, root.Parent, root.Level, root.CategoryGroup));
        Assert.True(elements.Single(e => e.WbsElement == "MC-00.001").IsInactive);
        Assert.True(elements.Single(e => e.WbsElement == "AC-I39.1.01.02").IsDeleted);
        Assert.Equal("Rozwój", Assert.Single(source.Groups()).Description);

        var check = source.Check();
        Assert.Equal((P1sSample.Elements.Count, 1), (check.Elements, check.Groups));
        Assert.Contains(check.Flags, f => f is { Column: "Z_ACTIVE", Value: "X", Rows: 7 });
        Assert.Contains(check.Flags, f => f is { Column: "Z_ACTIVE", Value: "", Rows: 1 } && f.Display == "(puste)");
        Assert.Contains(check.Flags, f => f is { Column: "LOEKZ", Value: "X", Rows: 1 });
    }
}
