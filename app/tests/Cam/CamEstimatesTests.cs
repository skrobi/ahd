using PzlEv.Modules.Cam.Models;
using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using Xunit;

namespace PzlEv.Tests.Cam;

/// <summary>Moduł CAM bez bazy (CamEstimates, CamResults): propozycje w latach, stawki, wyliczenia dla Finansów, walidacja, wyniki EV.</summary>
public sealed class CamEstimatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    internal static CamWorkPackage Wp(string wp = "WP-1", bool production = true, decimal? productionEtc = 3120, decimal bacHours = 6400, decimal? actualHours = 3980,
        string? finish = "2028-01-31", string? forecastFinish = "2028-03-31", decimal bacMaterial = 180000, decimal acwpMaterial = 96400,
        IReadOnlyDictionary<int, decimal>? undelivered = null, IReadOnlyDictionary<int, decimal>? rates = null, string project = "PAF",
        decimal bac = 1152800, decimal? bcws = 697800, decimal? bcwp = 654800, decimal acwp = 701360, decimal? etcCost = 529970, decimal? materialEtc = 69750,
        decimal? toPln = 1, decimal? toUsd = 0.27m, string currency = "PLN", IReadOnlyDictionary<int, decimal>? etcByYear = null,
        string? forecastStart = "2025-03-03", string? planStart = null, string? planFinish = null) =>
        new(project, project + " – program", wp, "Montaż kadłuba", "0412", "4100", currency, "2025-03-03", finish, forecastStart, forecastFinish,
            bacHours, bacMaterial, bac, bcws, bcwp, acwp, acwp - acwpMaterial, acwpMaterial, actualHours, production, productionEtc, 2950, materialEtc, etcCost,
            undelivered ?? new Dictionary<int, decimal>(), rates ?? new Dictionary<int, decimal> { [2026] = 152, [2027] = 158 }, toPln, toUsd, null,
            etcByYear, planStart, planFinish);

    [Fact]
    public void Split_spreads_over_working_days_by_year_and_keeps_the_total()
    {
        var split = CamEstimates.Split(1000, Today, new DateOnly(2027, 12, 31), 0);

        var all = WorkingDays.Between(Today, new DateOnly(2027, 12, 31));
        Assert.Equal(Math.Round(1000m * WorkingDays.Between(Today, new DateOnly(2026, 12, 31)) / all, 0), split[2026]);
        Assert.Equal(1000m, split.Values.Sum());
        Assert.Equal([2026, 2027], split.Keys.Order());
        Assert.Equal(new Dictionary<int, decimal> { [2026] = 500m }, CamEstimates.Split(500, Today, new DateOnly(2026, 1, 31), 0));   // koniec minął – rok bieżący
        Assert.Empty(CamEstimates.Split(0, Today, new DateOnly(2027, 1, 1), 0));
    }

    [Fact]
    public void Hours_proposal_comes_from_production_etc_and_without_production_from_budget_minus_actuals()
    {
        Assert.Equal(3120m, CamEstimates.HoursProposal(Wp(), Today).Values.Sum());
        Assert.Equal([2026, 2027, 2028], CamEstimates.HoursProposal(Wp(), Today).Keys.Order());                     // do prognozowanego końca 2028-03-31
        Assert.Equal(420m, CamEstimates.HoursProposal(Wp(production: false, productionEtc: null, bacHours: 2400, actualHours: 1980), Today).Values.Sum());
        Assert.Empty(CamEstimates.HoursProposal(Wp(production: false, bacHours: 100, actualHours: 150), Today));      // przekroczony – 0, nie ujemne
        Assert.Equal([2026, 2027], CamEstimates.HoursProposal(Wp(forecastFinish: null, finish: "2027-06-30"), Today).Keys.Order());   // bez prognozy – baseline
    }

    [Fact]
    public void Hours_proposal_of_production_wp_follows_planned_year_of_ahd()
    {
        // R z AHD: zaległe (2025) – rok bieżący; bez roku (0) – rozkład do końca WP; lata planu, nie liniowo.
        var byYear = CamEstimates.HoursProposal(Wp(forecastFinish: "2026-12-31", etcByYear: new Dictionary<int, decimal>
            { [2025] = 10.4m, [2026] = 100, [2028] = 500.6m, [0] = 50 }), Today);

        Assert.Equal([(2026, 160m), (2028, 501m)], byYear.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)));
        Assert.Equal(3120m, CamEstimates.HoursProposal(Wp(etcByYear: new Dictionary<int, decimal>()), Today).Values.Sum());   // bez lat – liniowo
        Assert.Equal(420m, CamEstimates.HoursProposal(Wp(production: false, productionEtc: null, bacHours: 2400, actualHours: 1980,
            etcByYear: new Dictionary<int, decimal> { [2030] = 999 }), Today).Values.Sum());                                      // bez produkcji – z budżetu
    }

    [Fact]
    public void Forecast_dates_are_the_last_entered_else_planned_dates_of_orders_else_baseline()
    {
        var planned = Wp(forecastStart: null, forecastFinish: null, planStart: "2026-02-10", planFinish: "2027-03-01");

        Assert.Equal((new DateOnly(2026, 2, 10), new DateOnly(2027, 3, 1)), (CamEstimates.Start(planned), CamEstimates.Finish(planned)));
        Assert.Equal((new DateOnly(2025, 3, 3), new DateOnly(2028, 3, 31)),
            (CamEstimates.Start(Wp(planStart: "2026-02-10", planFinish: "2027-03-01")), CamEstimates.Finish(Wp(planFinish: "2027-03-01"))));   // wpisana prognoza wygrywa
        Assert.Equal((new DateOnly(2025, 3, 3), new DateOnly(2028, 1, 31)), (CamEstimates.Start(Wp(forecastStart: null, forecastFinish: null)),
            CamEstimates.Finish(Wp(forecastStart: null, forecastFinish: null))));                                                                   // bez planu – baseline
        var estimate = CamEstimates.New(planned, "2026-10", Today);
        Assert.Equal((new DateOnly(2026, 2, 10), new DateOnly(2027, 3, 1)), (estimate.ForecastStart, estimate.ForecastFinish));
    }

    [Fact]
    public void Material_proposal_takes_undelivered_by_delivery_year_else_budget_minus_actuals()
    {
        var byYear = CamEstimates.MaterialProposal(Wp(undelivered: new Dictionary<int, decimal> { [0] = 70, [2025] = 10, [2026] = 200, [2027] = 1000.555m }), Today);

        Assert.Equal([(2026, 280m), (2027, 1000.56m)], byYear.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)));   // bez daty i zaległe – rok bieżący
        Assert.Equal(83600m, CamEstimates.MaterialProposal(Wp(), Today).Values.Sum());                               // 180 000 − 96 400 rozłożone
        Assert.Empty(CamEstimates.MaterialProposal(Wp(bacMaterial: 0, acwpMaterial: 0), Today));
    }

    [Fact]
    public void New_estimate_starts_from_proposals_for_each_year_to_the_finish()
    {
        var estimate = CamEstimates.New(Wp(undelivered: new Dictionary<int, decimal> { [2027] = 5000 }), "2026-10", Today);

        Assert.Equal((CamStatuses.ToDo, "2026-10", (long?)null), (estimate.Status, estimate.Cycle, estimate.EstimateId));
        Assert.Equal([2026, 2027, 2028], estimate.Of(CamLineKinds.Hours).Select(l => l.Year));
        Assert.All(estimate.Lines, l => Assert.Equal(l.Proposal ?? 0, l.Value));
        Assert.Equal((3120m, 5000m), (estimate.Total(CamLineKinds.Hours), estimate.Total(CamLineKinds.Material)));
        Assert.Equal((new DateOnly(2025, 3, 3), new DateOnly(2028, 3, 31)), (estimate.ForecastStart, estimate.ForecastFinish));
        Assert.False(CamEstimates.Changed(Wp(finish: "2028-03-31"), estimate));
    }

    [Fact]
    public void Rate_of_year_falls_back_to_last_earlier_rate()
    {
        Assert.Equal(new CamRate(152, null), CamEstimates.Rate(Wp(), 2026));
        Assert.Equal(new CamRate(158, 2027), CamEstimates.Rate(Wp(), 2029));
        Assert.Null(CamEstimates.Rate(Wp(), 2025));
    }

    [Fact]
    public void Summary_gives_finance_values_and_eac_credibility()
    {
        var estimate = Estimate(Wp(), [(2026, 980), (2027, 1620), (2028, 350)], [(2026, 22940), (2027, 46810)]);

        var summary = CamEstimates.Summary(Wp(), estimate);

        Assert.Equal(2950m, summary.EtcHours);
        Assert.Equal(980 * 152m + 1620 * 158m + 350 * 158m, summary.LaborCost);   // 2028 – stawka 2027
        Assert.Equal(69750m, summary.Material);
        Assert.Equal(701360m + 460220m + 69750m, summary.Eac);
        Assert.Equal(1152800m - 1231330m, summary.Vac);
        Assert.Equal(Math.Round(701360m + (1152800m - 654800m) / (654800m / 701360m), 0), Math.Round(summary.IeacCpi!.Value, 0));
        Assert.Equal(Math.Round((1152800m - 654800m) / 529970m, 4), Math.Round(summary.Tcpi!.Value, 4));
        Assert.False(summary.TcpiGap);
        var noRates = CamEstimates.Summary(Wp(rates: new Dictionary<int, decimal>()), estimate);
        Assert.Equal([2026, 2027, 2028], noRates.MissingRates);
        Assert.Equal(((decimal?)null, (decimal?)null), (noRates.LaborCost, noRates.Eac));
    }

    [Fact]
    public void Validate_requires_justification_for_changes_and_no_values_after_the_finish()
    {
        var wp = Wp(finish: "2028-01-31");
        var changed = Estimate(wp, [(2026, 980), (2027, 1620), (2028, 350)], []);

        Assert.Contains(CamEstimates.Validate(wp, changed), e => e.StartsWith("uzasadnienie zmian względem propozycji i końca po baseline"));
        Assert.Empty(CamEstimates.Validate(wp, changed with { Justification = "kooperant przejmuje część prac" }));
        Assert.Contains(CamEstimates.Validate(wp, changed with { Justification = "kooperant przejmuje", ForecastFinish = new DateOnly(2027, 12, 31) }),
            e => e.Contains("po prognozowanym końcu (2028)"));
        Assert.Contains(CamEstimates.Validate(wp, Estimate(wp, [(2026, -1)], []) with { Justification = "korekta ujemna test" }), e => e.Contains("ujemne"));
        Assert.Equal(["Daty", "Godziny", "Materiały", "Uzasadnienie"], CamEstimates.Checklist(wp, changed).Select(c => c.Item));
        Assert.Equal(["Daty", "Godziny"], CamEstimates.Checklist(Wp(bacMaterial: 0, acwpMaterial: 0, finish: "2028-03-31"), CamEstimates.New(Wp(bacMaterial: 0, acwpMaterial: 0, finish: "2028-03-31"), "2026-10", Today))
            .Select(c => c.Item));   // bez materiałów i bez zmian – bez uzasadnienia
    }

    [Fact]
    public void Results_sum_in_one_currency_with_indices_from_sums_and_scopes()
    {
        CamWorkPackage[] wps = [Wp(), Wp("WP-2", project: "S70", currency: "USD", bac: 1000, bcws: 500, bcwp: 400, acwp: 500, etcCost: 600, bacMaterial: 0, acwpMaterial: 0,
            materialEtc: null, toPln: 3.7m, toUsd: 1), Wp("WP-3", toPln: null, toUsd: null)];

        var (pln, skipped) = CamResults.Total(wps, CamScope.All, CamCurrency.Pln);

        Assert.Equal(1, skipped);   // WP-3 bez kursu – poza sumą
        Assert.Equal((1152800m + 3700m, 701360m + 1850m), (pln.Bac, pln.Acwp));
        Assert.Equal((654800m + 1480m) / (701360m + 1850m), pln.Cpi);              // wskaźnik z sum
        Assert.False(CamResults.SingleCurrency(wps));
        var labor = CamResults.Of(wps[0], CamScope.Labor, 1)!;
        Assert.Equal((972800m, 604960m, 460220m, (decimal?)null), (labor.Bac, labor.Acwp, labor.Etc, labor.Cpi));
        Assert.Null(CamResults.Of(wps[1], CamScope.Material, 1));                    // WP bez materiałów
        Assert.Equal(("ok", "warn", "crit", (string?)null), (CamResults.Tone(0.97m), CamResults.Tone(0.93m), CamResults.Tone(0.89m), CamResults.Tone(null)));
    }

    private static CamEstimate Estimate(CamWorkPackage wp, (int Year, decimal Value)[] hours, (int Year, decimal Value)[] materials)
    {
        var proposal = CamEstimates.New(wp, "2026-10", Today);
        var lines = proposal.Of(CamLineKinds.Hours).Select(l => l with { Value = hours.FirstOrDefault(h => h.Year == l.Year).Value })
            .Concat(proposal.Of(CamLineKinds.Material).Select(l => l with { Value = materials.FirstOrDefault(m => m.Year == l.Year).Value }))
            .Concat(hours.Where(h => proposal.Of(CamLineKinds.Hours).All(l => l.Year != h.Year)).Select(h => new CamEstimateLine(CamLineKinds.Hours, h.Year, null, h.Value)))
            .ToList();
        return proposal with { Lines = lines };
    }
}
