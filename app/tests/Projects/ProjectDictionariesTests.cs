using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Walidacja słowników projektu (F4.3; docs/slowniki.md, rozdz. 5.2, 5.3, 5.5, 5.6).</summary>
public sealed class ProjectDictionariesTests
{
    private static readonly ProjectDictionaryContext Context = new(
        new P1sScope(["AC-CAB.6.38", "AC-CAB.6.38.03"], []),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AC-CAB.6.38.09"] = "S70I" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Anna Nowak", "PZL\\anowak" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WP-1", "WP-2" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Labor", "Material" });

    private static List<Issue> Validate(string dictionary, params Dictionary<string, string?>[] rows)
    {
        var spec = ProjectDictionaries.For(dictionary, Context);
        var issues = new List<Issue>();
        var normalized = DictionaryValidator.Normalize(spec, rows.Select(r => new DictRow(null, null, r)).ToList(), issues);
        issues.AddRange(DictionaryValidator.Validate(spec, normalized));
        return issues;
    }

    private static Dictionary<string, string?> Wp(string element, string? wp, string? cam, string? category = "Labor") =>
        new() { ["Element P1S"] = element, ["WP"] = wp, ["CAM"] = cam, ["Cost Category"] = category };

    private static Dictionary<string, string?> Plan(string wp, string? hours, string? material, string? start = null, string? end = null) =>
        new() { ["WP"] = wp, ["BAC HOURS"] = hours, ["BAC MATERIAL"] = material, ["Baseline Start"] = start, ["Baseline Koniec"] = end };

    private static Dictionary<string, string?> Excl(string? ce, string? wbs, string? partner, string? description) =>
        new() { ["Cost Element"] = ce, ["WBS Element"] = wbs, ["Partner object"] = partner, ["Opis"] = description };

    private static bool HasError(List<Issue> issues, string text) => issues.Any(i => i.Level == CheckLevel.Error && i.Message.Contains(text));

    [Fact]
    public void WpCam_valid_rows_under_legacy_wbs_pass()
    {
        var issues = Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38", "WP-1", "Anna Nowak"), Wp("ac-cab.6.38.03.01", "WP-2", "PZL\\anowak", "material"));
        Assert.Empty(issues);
    }

    [Fact]
    public void WpCam_rules()
    {
        Assert.True(HasError(Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.7.01", "WP-1", "Anna Nowak")), "poza zakresem"));
        Assert.True(HasError(Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.380", "WP-1", "Anna Nowak")), "poza zakresem"));
        Assert.True(HasError(Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38.09", "WP-1", "Anna Nowak")), "należy do projektu S70I"));
        Assert.True(HasError(Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38.01", "WP-1", "Anna Nowak"), Wp("AC-CAB.6.38.01", "WP-2", "Anna Nowak")), "Duplikat klucza"));
        // WP bez CAM – zapis dozwolony (CAM uzupełnia się później), ostrzeżenie; przebieg blokuje gotowość projektu.
        var withoutCam = Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38.01", "WP-1", null));
        Assert.DoesNotContain(withoutCam, i => i.Level == CheckLevel.Error);
        Assert.Contains(withoutCam, i => i.Level == CheckLevel.Warning && i.Message.Contains("WP WP-1 bez CAM"));
        var outside = Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38.01", "WP-1", "Jan Obcy"));
        Assert.Contains(outside, i => i.Level == CheckLevel.Warning && i.Message.Contains("spoza listy osób"));
        Assert.DoesNotContain(outside, i => i.Level == CheckLevel.Error);
    }

    [Fact]
    public void WpCam_cost_category_comes_from_project_wbs_categories()
    {
        // Kategoria spoza słownika „Kategorie WBS” projektu – ostrzeżenie (nie blokuje zapisu dawnych wartości).
        var outside = Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38.01", "WP-1", "Anna Nowak", "Overhead"));
        Assert.Contains(outside, i => i.Level == CheckLevel.Warning && i.Message.Contains("„Overhead” spoza słownika „Kategorie WBS”"));
        Assert.DoesNotContain(outside, i => i.Level == CheckLevel.Error);
        // Pusta kategoria – bez uwag; słownik kategorii pusty (Categories null) – bez kontroli.
        Assert.Empty(Validate(ProjectDictionaries.WpCam, Wp("AC-CAB.6.38.01", "WP-1", "Anna Nowak", null)));
        var spec = ProjectDictionaries.For(ProjectDictionaries.WpCam, Context with { Categories = null });
        var rows = DictionaryValidator.Normalize(spec, [new DictRow(null, null, Wp("AC-CAB.6.38.01", "WP-1", "Anna Nowak", "Overhead"))], []);
        Assert.Empty(DictionaryValidator.Validate(spec, rows));
        // Kolumna powiązana ze słownikiem projektu – lista wyboru w tabeli i strukturze.
        Assert.Equal(ProjectDictionaries.WbsCategories, spec.Columns.Single(c => c.Name == "Cost Category").Lookup);
    }

    [Fact]
    public void Wbs_categories_dictionary_is_optional_and_unique_by_category()
    {
        var item = ProjectDictionaries.Item(ProjectDictionaries.WbsCategories);
        Assert.True(item.Stored);
        Assert.Empty(item.RequiredFor);
        Assert.Contains(ProjectDictionaries.ForType(ProjectTypes.Sac), i => i.Code == ProjectDictionaries.WbsCategories);
        Assert.Contains(ProjectDictionaries.Tables, t => t.Spec.Code == ProjectDictionaries.WbsCategories);
        Assert.Empty(Validate(ProjectDictionaries.WbsCategories, Cat("Production", "produkcja"), Cat("Programs", null)));
        Assert.True(HasError(Validate(ProjectDictionaries.WbsCategories, Cat("Production", null), Cat("production", null)), "Duplikat klucza"));
        Assert.True(HasError(Validate(ProjectDictionaries.WbsCategories, Cat(null, "bez nazwy")), "pole wymagane"));
    }

    [Fact]
    public void Structure_category_options_keep_values_outside_dictionary()
    {
        var categories = new List<LookupOption> { new("Production", "Production"), new("Programs", "Programs") };
        var wpCam = new[] { new DictRow(null, null, Wp("A", "WP-1", "x", "Labor")), new DictRow(null, null, Wp("B", "WP-2", "x", "production")) };
        Assert.Equal(["Production", "Programs", "Labor"], ProjectService.CategoryOptions(categories, wpCam).Select(o => o.Value));
        var lookups = ProjectService.Lookups([], categories);
        Assert.Same(categories, lookups[ProjectDictionaries.WbsCategories]);
    }

    private static Dictionary<string, string?> Cat(string? category, string? description) =>
        new() { ["Cost Category"] = category, ["Opis"] = description };

    [Fact]
    public void ScheduleBudget_rules()
    {
        Assert.Empty(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-1", "600", "20 000,50", "2026-10-01", "2027-03-31")));
        Assert.True(HasError(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-9", "1", null)), "nie istnieje w słowniku „WP i CAM”"));
        Assert.True(HasError(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-1", "-1", null)), "nie może być ujemny"));
        Assert.True(HasError(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-1", "1", null, "2027-01-01", "2026-01-01")), "późniejszy"));
        Assert.True(HasError(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-1", "abc", null)), "oczekiwano liczby"));
        Assert.True(HasError(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-1", "1", null, "31.13.2026")), "oczekiwano daty"));
        Assert.Contains(Validate(ProjectDictionaries.ScheduleBudget, Plan("WP-2", null, null)), i => i.Level == CheckLevel.Warning && i.Message.Contains("bez budżetu"));
        // BAC – budżet kosztowy: ujemny – ERROR; sam BAC to budżet WP (bez ostrzeżenia).
        var bacOnly = Plan("WP-2", null, null);
        bacOnly["BAC"] = "15000";
        Assert.Empty(Validate(ProjectDictionaries.ScheduleBudget, bacOnly));
        bacOnly["BAC"] = "-1";
        Assert.True(HasError(Validate(ProjectDictionaries.ScheduleBudget, bacOnly), "BAC – budżet nie może być ujemny"));

        var spec = ProjectDictionaries.For(ProjectDictionaries.ScheduleBudget, Context with { Wps = null });
        Assert.Contains(DictionaryValidator.Validate(spec, [new DictRow(null, null, Plan("WP-9", "1", null))]),
            i => i.Level == CheckLevel.Warning && i.Message.Contains("Nie wczytano"));
    }

    [Fact]
    public void Exclusion_rules()
    {
        var valid = Validate(ProjectDictionaries.Exclusions, Excl("57100000", null, null, "rozliczenie"), Excl(null, null, "IC-TRANSFER", "IC"), Excl("57100000", "4D06WP.01", null, "para"));
        Assert.DoesNotContain(valid, i => i.Level == CheckLevel.Error);
        Assert.True(HasError(Validate(ProjectDictionaries.Exclusions, Excl(null, null, null, "opis")), "co najmniej jedno"));
        Assert.True(HasError(Validate(ProjectDictionaries.Exclusions, Excl("57100000", null, null, null)), "Opis: pole wymagane"));
        Assert.True(HasError(Validate(ProjectDictionaries.Exclusions, Excl("57100000", null, null, "a"), Excl("0057100000", null, null, "b")), "Duplikat klucza"));
    }

    [Fact]
    public void Project_cost_category_uses_global_rules()
    {
        var issues = Validate(GlobalDictionaries.CostCategory, new Dictionary<string, string?> { ["Numer elementu kosztowego"] = "123", ["Cost Category"] = null });
        Assert.Contains(issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("bez Cost Category"));
    }

    [Fact]
    public void Cas_rates_are_required_only_for_cas()
    {
        Assert.DoesNotContain(ProjectDictionaries.ForType(ProjectTypes.Sac), i => i.Code == ProjectDictionaries.CasRates);
        Assert.Contains(ProjectDictionaries.ForType(ProjectTypes.Cas), i => i.Code == ProjectDictionaries.CasRates && !i.Stored && i.IsRequired(ProjectTypes.Cas));
    }

    [Fact]
    public void Cost_category_for_editing_merges_global_rows_with_project_changes()
    {
        var spec = ProjectDictionaries.Base(GlobalDictionaries.CostCategory);
        static DictRow Row(long id, string element, string category) =>
            new(id, 1, new Dictionary<string, string?> { ["Numer elementu kosztowego"] = element, ["Opis"] = null, ["Obszar"] = null, ["Cost Category"] = category });
        var global = new[] { Row(1, "0057100000", "Material"), Row(2, "0061000000", "Labor") };
        var project = new[] { Row(7, "0061000000", "Subcontract"), Row(8, "0070000000", "Labor") };

        var rows = ProjectDictionaries.WithGlobal(spec, global, project);

        // Pozycja globalna zmieniona w projekcie – tylko wiersz projektu; pozostałe globalne – dziedziczone.
        Assert.Equal([(1L, true), (7L, false), (8L, false)], rows.Select(r => (r.Row.RowId!.Value, r.Inherited)));
    }
}
