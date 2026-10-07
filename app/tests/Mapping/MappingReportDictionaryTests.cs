using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Mapping;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Mapping;

/// <summary>
/// Słownik „Raport mapowań CES ↔ P1S” (docs/mapowanie-ces-p1s.md, rozdz. 2; docs/slowniki.md, rozdz. 2, 5.7): układ pliku,
/// walidacja i rozstrzyganie na wierszach słownika – bez bazy (testdata/Mapowanie/Raport_mapowan.csv).
/// </summary>
public sealed class MappingReportDictionaryTests
{
    private static DictionarySpec Spec => GlobalDictionaries.Get(GlobalDictionaries.MappingReport);

    /// <summary>Wiersze pliku po nagłówkach jak przy wczytaniu z Excela, znormalizowane; issues – wynik walidacji.</summary>
    private static (List<DictRow> Rows, List<Issue> Issues) Read(string path)
    {
        var data = TabularFileReader.Read(path);
        var rows = data.Rows
            .Select(cells => new DictRow(null, null, Spec.Columns.ToDictionary(c => c.Name, c =>
            {
                var i = data.Headers.ToList().FindIndex(h => string.Equals(h.Trim(), c.Name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i < cells.Length ? cells[i] : null;
            })))
            .ToList();
        var issues = new List<Issue>();
        var normalized = DictionaryValidator.Normalize(Spec, rows, issues);
        issues.AddRange(DictionaryValidator.Validate(Spec, normalized));
        return (normalized, issues);
    }

    private static string Sample => TestServices.TestData("Mapowanie", "Raport_mapowan.csv");

    private static DictRow Row(string src, string pspnr, params (string Column, string Value)[] values) =>
        new(null, null, Spec.Columns.ToDictionary(c => c.Name, string? (c) => c.Name switch
        {
            "src" => src,
            "pspnr" => pspnr,
            _ => values.FirstOrDefault(v => v.Column == c.Name).Value,
        }));

    [Fact]
    public void Dictionary_has_all_report_columns_with_key_src_and_pspnr()
    {
        var headers = TabularFileReader.Read(Sample).Headers;

        Assert.Equal(headers, Spec.Columns.Select(c => c.Name));
        Assert.Equal(["src", "pspnr"], Spec.KeyColumns.Select(c => c.Name));
        var table = Assert.Single(GlobalDictionaries.Tables, t => t.Spec.Code == GlobalDictionaries.MappingReport);
        Assert.Equal("dict.MappingReport", table.Table);
        Assert.Equal(Spec.Columns.Count, table.Columns.Select(c => c.Column).Distinct().Count());
        Assert.DoesNotContain(table.Columns, c => c.Column == "Project");   // kolumna słowników projektu
    }

    [Fact]
    public void Sample_report_is_valid_and_resolves_elements_by_pspnr()
    {
        var (rows, issues) = Read(Sample);

        Assert.Empty(issues);
        Assert.Equal(27, rows.Count);
        var entries = rows.Select((r, i) => MappingKeys.Entry(i + 1, r.Values)).ToList();
        var report = new ReportInfo(DateTimeOffset.Now, "test", entries);
        CesElement[] elements =
        [
            new("4D02U8000001", "4D02U8", true, 1, 1),   // SAP 14217226: odpowiednik 1:1
            new("4D02U8.RA", "4D02U8", true, 1, 1),      // CES 14023634: element tylko w CES → zlecenie sprzedaży
            new("4D02UH.02", "4D02UH", true, 1, 1),      // CES 14023713: .02 → element .03
            new("4D02U8000099", "4D02U8", true, 1, 1),   // spoza raportu – dziedziczy projekt CES
            new("4D02UR000001", "4D02UR", true, 1, 1),   // projekt CES 4D02UR → AC-LH8.1.03
        ];

        var resolution = MappingResolver.Resolve(elements, report, [], p1s: null, latestBatchId: null);

        MappingResult Of(string element) => resolution.Results.Single(r => r.CesElement == element);
        Assert.Equal((MappingStatuses.Report, "14217226", "AC-LH8.1.01.01"), (Of("4D02U8000001").Status, Of("4D02U8000001").TargetPspnr, Of("4D02U8000001").TargetWbs));
        Assert.Equal("raport mapowań, wiersz SAP 14217226", Of("4D02U8000001").Origin);
        Assert.Equal((MappingStatuses.Report, "14217225"), (Of("4D02U8.RA").Status, Of("4D02U8.RA").TargetPspnr));
        Assert.Equal((MappingStatuses.Report, "14217232"), (Of("4D02UH.02").Status, Of("4D02UH.02").TargetPspnr));
        Assert.Equal((MappingStatuses.Inherited, "14217225", "AC-LH8.1.01"), (Of("4D02U8000099").Status, Of("4D02U8000099").TargetPspnr, Of("4D02U8000099").TargetWbs));
        Assert.Equal((MappingStatuses.Inherited, "AC-LH8.1.03"), (Of("4D02UR000001").Status, Of("4D02UR000001").TargetWbs));
        Assert.DoesNotContain(resolution.Issues, i => i.Level == CheckLevel.Error);
    }

    [Fact]
    public void Wrong_src_and_duplicate_key_are_errors_and_ces_row_without_assignment_is_a_warning()
    {
        var issues = new List<Issue>();
        var rows = DictionaryValidator.Normalize(Spec,
        [
            Row("sap", "1"),
            Row("SAP", "1"),
            Row("XYZ", "2"),
            Row("CES", "3", ("wbs", "4D00AA000001")),   // bez celu P1S i bez pary project_ces → project_sap
            Row("CES", "4", ("wbs_ces", "4D00AA"), ("project_ces", "4D00AA"), ("project_sap", "AC-X")),
        ], issues);
        issues.AddRange(DictionaryValidator.Validate(Spec, rows));

        Assert.Equal("SAP", rows[0]["src"]);   // wartość z listy w pisowni słownika
        Assert.Contains(issues, i => i is { Level: CheckLevel.Error } && i.Message.StartsWith("src: 'XYZ'"));
        Assert.Contains(issues, i => i is { Level: CheckLevel.Error } && i.Message.StartsWith("Duplikat klucza SAP | 1"));
        var warning = Assert.Single(issues, i => i.Level == CheckLevel.Warning);
        Assert.StartsWith("Wiersz CES 3 bez przypisania", warning.Message);
    }
}
