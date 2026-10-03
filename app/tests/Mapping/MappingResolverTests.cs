using PzlEv.Modules.Mapping.Models;
using PzlEv.Modules.Mapping.Services;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Mapping;

/// <summary>Kolejność rozstrzygania (docs/mapowanie-ces-p1s.md, rozdz. 5) i reguły walidacji (rozdz. 10).</summary>
public sealed class MappingResolverTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(2));

    // raport: element 2DI473001001 → PSPNR 1003 (w Excelu bez zer wiodących); projekt 2DI473 → AC-I39.1.01
    private static readonly ReportInfo Report = new(1, "MAPOWANIA_01.xlsx", At,
    [
        new ReportEntry(1, "SAP", "2DI473001001", "2DI473", "1003", "AC-I39.1.01.01", "AC-I39.1.01"),
        new ReportEntry(2, "CES", "2DI473001099", "2DI473", "1002", "", "AC-I39.1.01"),
    ]);

    private static readonly CesElement[] Elements =
    [
        new("2DI473001001", "2DI473", true, 1, 2),
        new("2DI473001002", "2DI473", true, 1, 2),
        new("4D03GZ000001", "4D03GZ", true, 2, 2),
        new("4D03GZ000041", "4D03GZ", false, 1, 2),
    ];

    private static CorrectionRow Correction(string kind, string key, string pspnr, string wbs) =>
        new(1, 10, 1, kind, key, pspnr, wbs, null, "uzasadnienie", At.Date, null, At, "test", null, null);

    private static MappingResult Of(MappingResolution resolution, string element) => resolution.Results.Single(r => r.CesElement == element);

    [Fact]
    public void Each_path_report_inherited_override_unmapped()
    {
        var corrections = new[] { Correction(CorrectionKinds.Element, "4D03GZ000041", "00002001", "MC-00.001") };

        var resolution = MappingResolver.Resolve(Elements, Report, corrections, P1sSample.Elements, latestBatchId: 2);

        var report = Of(resolution, "2DI473001001");
        Assert.Equal((MappingStatuses.Report, "00001003", "AC-I39.1.01.01"), (report.Status, report.TargetPspnr, report.TargetWbs));
        var inherited = Of(resolution, "2DI473001002");
        Assert.Equal((MappingStatuses.Inherited, "00001002", "AC-I39.1.01"), (inherited.Status, inherited.TargetPspnr, inherited.TargetWbs));
        Assert.Equal("projekt CES 2DI473 → AC-I39.1.01 (raport mapowań, wiersz 1)", inherited.Origin);
        var overridden = Of(resolution, "4D03GZ000041");
        Assert.Equal((MappingStatuses.Override, "MC-00.001"), (overridden.Status, overridden.TargetWbs));
        var unmapped = Of(resolution, "4D03GZ000001");
        Assert.Equal(MappingStatuses.Unmapped, unmapped.Status);
        Assert.Equal("MC-00.001", unmapped.Proposal);   // najczęstszy cel elementów projektu 4D03GZ
        Assert.True(unmapped.IsNew);
        Assert.False(report.IsNew);
    }

    [Fact]
    public void Element_correction_wins_over_report_and_project_correction_over_report_project()
    {
        var corrections = new[]
        {
            Correction(CorrectionKinds.Element, "2DI473001001", "00001004", "AC-I39.1.01.02"),
            Correction(CorrectionKinds.Project, "2DI473", "00001001", "AC-I39.1"),
        };

        var resolution = MappingResolver.Resolve(Elements, Report, corrections, P1sSample.Elements, latestBatchId: 2);

        Assert.Equal(MappingStatuses.Override, Of(resolution, "2DI473001001").Status);
        var inherited = Of(resolution, "2DI473001002");
        Assert.Equal((MappingStatuses.Inherited, "AC-I39.1"), (inherited.Status, inherited.TargetWbs));
        Assert.StartsWith("korekta projektu CES 2DI473", inherited.Origin);
        // korekta wskazuje element usunięty w P1S – WARNING
        Assert.Contains(resolution.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("AC-I39.1.01.02 jest usunięty"));
    }

    [Fact]
    public void Report_with_two_targets_for_one_element_is_an_error_and_unmapped_cost_is_a_warning()
    {
        var report = Report with
        {
            Entries = [.. Report.Entries, new ReportEntry(3, "SAP", "2DI473001001", "2DI473", "2001", "MC-00.001", "AC-I39.1.01")],
        };

        var resolution = MappingResolver.Resolve(Elements, report, [], P1sSample.Elements, latestBatchId: 2);

        var error = Assert.Single(resolution.Issues, i => i.Level == CheckLevel.Error);
        Assert.Contains("2DI473001001", error.Message);
        Assert.Contains("AC-I39.1.01.01, MC-00.001", error.Message);
        Assert.Equal("AC-I39.1.01.01", Of(resolution, "2DI473001001").TargetWbs);   // pierwszy wiersz raportu
        Assert.Contains(resolution.Issues, i => i.Level == CheckLevel.Warning && i.Message.StartsWith("Element CES 4D03GZ000001"));
        Assert.DoesNotContain(resolution.Issues, i => i.Message.StartsWith("Element CES 4D03GZ000041"));   // bez kosztu
    }

    [Fact]
    public void Target_outside_log_wbs_is_a_warning_and_without_pzlprod_codes_come_from_report()
    {
        var report = Report with { Entries = [new ReportEntry(1, "SAP", "2DI473001001", "2DI473", "7777", "AC-X.1", "")] };

        var withP1s = MappingResolver.Resolve(Elements, report, [], P1sSample.Elements, latestBatchId: 2);
        Assert.Contains(withP1s.Issues, i => i.Level == CheckLevel.Warning && i.Message.StartsWith("Cel P1S AC-X.1 nie istnieje w LOG.WBS"));

        var withoutP1s = MappingResolver.Resolve(Elements, report, [], p1s: null, latestBatchId: 2);
        var result = Of(withoutP1s, "2DI473001001");
        Assert.Equal((MappingStatuses.Report, "7777", "AC-X.1"), (result.Status, result.TargetPspnr, result.TargetWbs));
        Assert.DoesNotContain(withoutP1s.Issues, i => i.Message.Contains("LOG.WBS"));
    }

    [Fact]
    public void Report_target_for_corrections_of_element_and_project()
    {
        Assert.Equal(("00001003", "AC-I39.1.01.01"), MappingResolver.ReportTarget(Report, P1sSample.Elements, CorrectionKinds.Element, "2DI473001001"));
        Assert.Equal(("00001002", "AC-I39.1.01"), MappingResolver.ReportTarget(Report, P1sSample.Elements, CorrectionKinds.Project, "2di473"));
        Assert.Null(MappingResolver.ReportTarget(Report, P1sSample.Elements, CorrectionKinds.Element, "4D03GZ000001"));
    }

    [Theory]
    [InlineData("00012345", "12345")]
    [InlineData(" 12345 ", "12345")]
    [InlineData("0000", "0")]
    [InlineData("ac-i39.1", "AC-I39.1")]
    public void Keys_ignore_leading_zeros_spaces_and_case(string value, string expected) => Assert.Equal(expected, MappingKeys.Key(value));
}
