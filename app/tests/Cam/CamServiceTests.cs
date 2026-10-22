using PzlEv.Modules.Cam.Data;
using PzlEv.Modules.Cam.Models;
using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Cam;

/// <summary>Moduł CAM na bazie (migracja 056): cykl z kalendarza, szkic, wysyłka z walidacją, zatwierdzenie i zwrot, historia, konflikt zapisu.</summary>
public sealed class CamServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pzlev-" + Guid.NewGuid().ToString("N"));
    private readonly TestDatabase? _database;
    private readonly FakeWorkPackages _packages = new();

    public CamServiceTests()
    {
        Directory.CreateDirectory(_root);
        if (TestDatabase.ConnectionString is not null)
            _database = new TestDatabase(presets: true);   // kalendarz okresów 2026–2027 (dane startowe)
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeWorkPackages : ICamWorkPackages
    {
        public List<(string Project, string Wp, decimal Hours, decimal Material, string? Currency, DateOnly? Start, DateOnly? Finish)> Applied { get; } = [];

        public string? Error { get; set; }

        public IReadOnlyList<CamPerson> Cams() => [new("0412", "Anna Nowak")];

        public IReadOnlyList<CamWorkPackage> WorkPackages(string cam) => [CamEstimatesTests.Wp()];

        public string? ApplyEstimate(string project, string wp, decimal etcHours, decimal materialEtc, string? currency, DateOnly? forecastStart, DateOnly? forecastFinish)
        {
            if (Error is null)
                Applied.Add((project, wp, etcHours, materialEtc, currency, forecastStart, forecastFinish));
            return Error;
        }
    }

    private (CamService Service, SqlCamStore Store, AppServices App) Create()
    {
        var app = _services.App(_root, _database!.Sql);
        var store = new SqlCamStore(_database.Sql);
        return (new CamService(store, () => _packages, app), store, app);
    }

    [SqlFact]
    public void Cycle_comes_from_calendar_with_closing_week_as_deadline()
    {
        var (_, store, _) = Create();

        var cycle = store.Cycle(new DateOnly(2026, 10, 7))!;

        Assert.Equal("2026-10", cycle.Period);
        Assert.True(cycle.Deadline >= new DateOnly(2026, 10, 1) && cycle.Deadline <= new DateOnly(2026, 11, 8), $"termin {cycle.Deadline}");
        Assert.Null(store.Cycle(new DateOnly(2035, 1, 1)));
    }

    [SqlFact]
    public void Draft_submit_approve_and_return_follow_the_workflow_with_history()
    {
        var (service, store, app) = Create();
        var cycle = new CamCycle("2026-10", new DateOnly(2026, 10, 30));
        var wp = CamEstimatesTests.Wp();
        var item = Assert.Single(service.Worklist(service.WorkPackages("0412"), cycle));
        Assert.Equal(CamStatuses.ToDo, item.Estimate.Status);

        // Zmiana względem propozycji bez uzasadnienia – szkic tak, wysyłka nie.
        var changed = item.Estimate with { Lines = [.. item.Estimate.Lines.Select(l => l.Kind == CamLineKinds.Hours && l.Year == 2027 ? l with { Value = l.Value - 200 } : l)] };
        var draft = service.Save(wp, changed, submit: false);
        Assert.True(draft.Ok, draft.Message);
        Assert.Equal(CamStatuses.Draft, draft.Estimate!.Status);
        var refused = service.Save(wp, draft.Estimate, submit: true);
        Assert.False(refused.Ok);
        Assert.Contains(refused.Errors!, e => e.StartsWith("uzasadnienie"));

        var sent = service.Save(wp, draft.Estimate with { Justification = "część poszycia przejmuje kooperant" }, submit: true);
        Assert.True(sent.Ok, sent.Message);
        Assert.Equal((CamStatuses.Sent, app.User.Account), (sent.Estimate!.Status, sent.Estimate.SubmittedBy));
        Assert.False(service.Save(wp, sent.Estimate, submit: false).Ok);   // wysłana – edycja zablokowana
        Assert.Equal([sent.Estimate.Wp], service.Pending(cycle).Select(e => e.Wp));

        // Zwrot wymaga uwagi; zwrócona wraca do CAM-a.
        Assert.False(service.Return(sent.Estimate, "za mało").Ok);
        var returned = service.Return(store.Estimate("PAF", "WP-1", "2026-10")!, "SPI 0,87 – dodaj wyjaśnienie odchylenia");
        Assert.True(returned.Ok, returned.Message);
        Assert.Equal((CamStatuses.Returned, "SPI 0,87 – dodaj wyjaśnienie odchylenia"), (returned.Estimate!.Status, returned.Estimate.ReviewNote));
        Assert.Equal(CamStatuses.Returned, Assert.Single(service.Worklist([wp], cycle)).Estimate.Status);

        var resent = service.Save(wp, returned.Estimate, submit: true);
        Assert.True(resent.Ok, resent.Message);
        _packages.Error = "brak kursu EUR";
        Assert.False(service.Approve(resent.Estimate!).Ok);   // błąd zapisu słownika – zostaje wysłana
        Assert.Equal(CamStatuses.Sent, store.Estimate("PAF", "WP-1", "2026-10")!.Status);
        _packages.Error = null;
        var approved = service.Approve(store.Estimate("PAF", "WP-1", "2026-10")!);
        Assert.True(approved.Ok, approved.Message);
        var applied = Assert.Single(_packages.Applied);
        Assert.Equal(("PAF", "WP-1", 2920m, resent.Estimate!.Total(CamLineKinds.Material), "PLN", new DateOnly(2028, 3, 31)),
            (applied.Project, applied.Wp, applied.Hours, applied.Material, applied.Currency, applied.Finish));
        Assert.Equal(CamStatuses.Approved, store.Estimate("PAF", "WP-1", "2026-10")!.Status);

        var history = service.History(approved.Estimate!);
        Assert.Equal([CamStatuses.Approved, CamStatuses.Sent, CamStatuses.Returned, CamStatuses.Sent, CamStatuses.Draft], history.Select(h => h.Status));
        Assert.StartsWith("ETC 2 920 h · materiały", history.Single(h => h.Status == CamStatuses.Draft).Summary.Replace('\u00A0', ' '));   // nowa – bez poprzedniej wersji
        Assert.Equal(("zwrócona do CAM-a", "SPI 0,87 – dodaj wyjaśnienie odchylenia"),
            (history.Single(h => h.Status == CamStatuses.Returned).Summary, history.Single(h => h.Status == CamStatuses.Returned).Comment));
        Assert.Equal("bez zmian wartości", history.Last(h => h.Status == CamStatuses.Sent).Summary);   // ponowne wysłanie po zwrocie
        Assert.Contains(app.Journal.Recent(10), j => j.Message.Contains("zatwierdzona"));
        Assert.Equal(CamStatuses.Approved, service.LastApproved(approved.Estimate! with { Cycle = "2026-11" })!.Status);
    }

    [SqlFact]
    public void Approval_of_an_estimate_changed_since_it_was_read_is_refused_before_writing_the_schedule()
    {
        var (service, store, _) = Create();
        var wp = CamEstimatesTests.Wp();
        var first = service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)) with { Justification = "opóźnienie dostaw kooperacji" }, submit: true);
        Assert.True(first.Ok, first.Message);
        var sent = first.Estimate!;
        var seenByAnalyst = store.Estimate("PAF", "WP-1", "2026-10")!;   // lista „Zatwierdzanie” wczytana

        // Inny analityk zwraca, CAM poprawia i wysyła ponownie – obiekt na liście pierwszego analityka jest nieaktualny.
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(service.Return(seenByAnalyst, "zmień rozkład godzin na lata").Ok);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        var changed = store.Estimate("PAF", "WP-1", "2026-10")!;
        var resent = service.Save(wp, changed with
        {
            Justification = "przesunięcie montażu na 2027",
            Lines = [.. changed.Lines.Select(l => l.Kind == CamLineKinds.Hours && l.Year == 2026 ? l with { Value = 0 } : l)],
        }, submit: true);
        Assert.True(resent.Ok, resent.Message);

        var stale = service.Approve(seenByAnalyst);

        Assert.False(stale.Ok);
        Assert.Contains("zmieniono w międzyczasie", stale.Message);
        Assert.Empty(_packages.Applied);                                     // „Harmonogram i budżet” bez nieaktualnych wartości
        Assert.True(service.Approve(store.Estimate("PAF", "WP-1", "2026-10")!).Ok);
        Assert.Equal(resent.Estimate!.Total(CamLineKinds.Hours), Assert.Single(_packages.Applied).Hours);
        Assert.NotEqual(sent.Total(CamLineKinds.Hours), _packages.Applied[0].Hours);
    }

    [SqlFact]
    public void Approved_estimate_reopened_with_comment_goes_back_to_cam_and_schedule_waits_for_new_approval()
    {
        var (service, store, app) = Create();
        var wp = CamEstimatesTests.Wp();
        var cycle = new CamCycle("2026-10", new DateOnly(2026, 10, 30));
        var sent = service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)) with { Justification = "opóźnienie dostaw kooperacji" }, submit: true);
        Assert.True(sent.Ok, sent.Message);
        Assert.False(service.Reopen(sent.Estimate!, "otwarcie wysłanej estymacji").Ok);   // tylko zatwierdzona
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        var approved = service.Approve(store.Estimate("PAF", "WP-1", "2026-10")!).Estimate!;
        Assert.Equal([approved.Wp], service.Approved(cycle).Select(e => e.Wp));

        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(service.Reopen(approved, "za mało").Ok);                              // komentarz wymagany
        var reopened = service.Reopen(approved, "Finanse: przesunięcie zakupu materiałów na 2027");

        Assert.True(reopened.Ok, reopened.Message);
        Assert.Equal((CamStatuses.Returned, "Finanse: przesunięcie zakupu materiałów na 2027"), (reopened.Estimate!.Status, reopened.Estimate.ReviewNote));
        Assert.Empty(service.Approved(cycle));
        Assert.Single(_packages.Applied);                                                  // „Harmonogram i budżet” – wartości ostatniego zatwierdzenia
        Assert.False(service.Reopen(approved, "drugi raz ze starego wiersza listy").Ok);  // nieaktualny wiersz – konflikt
        var history = service.History(reopened.Estimate);
        Assert.StartsWith("otwarta ponownie", history[0].Summary);
        Assert.Equal(("Finanse: przesunięcie zakupu materiałów na 2027", CamStatuses.Returned), (history[0].Comment, history[0].Status));
        Assert.Contains(app.Journal.Recent(10), j => j.Message.Contains("otwarta ponownie"));

        // CAM poprawia i wysyła; nowe zatwierdzenie zapisuje nowe wartości.
        var item = Assert.Single(service.Worklist([wp], cycle));
        Assert.Equal(CamStatuses.Returned, item.Estimate.Status);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        var corrected = service.Save(wp, item.Estimate with
        {
            Lines = [.. item.Estimate.Lines.Select(l => l.Kind == CamLineKinds.Material && l.Year == 2026 ? l with { Value = 0 } : l)],
        }, submit: true);
        Assert.True(corrected.Ok, corrected.Message);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(service.Approve(store.Estimate("PAF", "WP-1", "2026-10")!).Ok);
        Assert.Equal(2, _packages.Applied.Count);
        Assert.Equal(corrected.Estimate!.Total(CamLineKinds.Material), _packages.Applied[1].Material);
    }

    [SqlFact]
    public void Save_of_an_estimate_changed_meanwhile_is_a_conflict()
    {
        var (service, store, _) = Create();
        var wp = CamEstimatesTests.Wp();
        var first = service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)), submit: false).Estimate!;
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(service.Save(wp, first, submit: false).Ok);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));

        var stale = service.Save(wp, first, submit: false);   // UpdatedAt sprzed drugiego zapisu
        var duplicate = service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)), submit: false);

        Assert.False(stale.Ok);
        Assert.Contains("zmienił w międzyczasie", stale.Message);
        Assert.False(duplicate.Ok);
        Assert.Equal(2, store.Events(first.EstimateId!.Value).Count);
    }
}
