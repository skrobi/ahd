using PzlEv.Modules.Cam.Data;
using PzlEv.Modules.Cam.Models;
using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Mail;
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

        public IReadOnlyList<CamPerson> Cams() => [new("0412", "Anna Nowak", "anna.nowak@pzl.pl")];

        public IReadOnlyList<CamWorkPackage> WorkPackages(string cam) => [CamEstimatesTests.Wp()];

        /// <summary>WP programów: PAF – WP-1 (wymagany), WP-2 zakończony (niewymagany); M28 – WP-3 innego CAM-a.</summary>
        public IReadOnlyList<CamWorkPackage> ProjectWorkPackages(string? project) =>
            [.. new[] { CamEstimatesTests.Wp(), CamEstimatesTests.Wp("WP-2") with { Finished = true }, CamEstimatesTests.Wp("WP-3", project: "M28") with { Cam = "0577" } }
                .Where(w => project is null || w.Project == project)];

        public List<(DateOnly? Start, DateOnly? Finish)> AppliedActual { get; } = [];

        public string? ApplyEstimate(string project, string wp, decimal etcHours, decimal materialEtc, string? currency, DateOnly? forecastStart, DateOnly? forecastFinish,
            DateOnly? actualStart = null, DateOnly? actualFinish = null)
        {
            if (Error is null)
            {
                Applied.Add((project, wp, etcHours, materialEtc, currency, forecastStart, forecastFinish));
                AppliedActual.Add((actualStart, actualFinish));
            }
            return Error;
        }
    }

    /// <summary>Atrapa poczty: wysłane wiadomości; Error – wysyłka kończy się wyjątkiem (np. brak Outlooka).</summary>
    private sealed class FakeMail : IMailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public string? Error { get; set; }

        public void Send(string to, string subject, string body)
        {
            if (Error is not null)
                throw new InvalidOperationException(Error);
            Sent.Add((to, subject, body));
        }
    }

    private readonly FakeMail _mail = new();

    private (CamService Service, SqlCamStore Store, AppServices App) Create()
    {
        var app = _services.App(_root, _database!.Sql);
        var store = new SqlCamStore(_database.Sql);
        return (new CamService(store, () => _packages, app, _mail), store, app);
    }

    /// <summary>
    /// Powiadomienia CAM: wiadomość do CAM-a z zaległymi wymaganymi estymacjami (lista WP, termin), zapis każdego powiadomienia, bez ponownej
    /// wysyłki w cyklu (chyba że resend), brak adresu i błąd wysyłki – zapisane z wynikiem, estymacja wysłana – bez wezwania.
    /// </summary>
    [SqlFact]
    public void Notify_sends_one_mail_per_cam_with_pending_work_packages_once_per_cycle()
    {
        var (service, store, app) = Create();
        var cycle = service.CycleOf("2026-10");

        var first = service.Notify("PAF", cycle, pinId: 7, resend: false);

        var sent = Assert.Single(first.Notifications);
        Assert.Equal(("0412", "Anna Nowak", "anna.nowak@pzl.pl", CamNotificationResults.Sent, "WP-1"), (sent.Cam, sent.Name, sent.Email, sent.Result, sent.Wps));
        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("anna.nowak@pzl.pl", mail.To);
        Assert.Equal($"PZL-EV: estymacje CAM PAF – okres 2026-10, termin {cycle.Deadline:yyyy-MM-dd}", mail.Subject);
        Assert.Contains("- WP-1 Montaż kadłuba: do uzupełnienia", mail.Body);                       // WP-2 zakończony – bez wezwania
        Assert.Contains(app.Journal.Recent(5), j => j.Message.StartsWith("Powiadomienia CAM PAF (cykl 2026-10): Anna Nowak – wysłano"));

        var again = service.Notify("PAF", cycle, pinId: 8, resend: false);
        Assert.Empty(again.Notifications);
        Assert.Equal(["0412"], again.AlreadyNotified);                                            // powiadomiony w cyklu – pominięty
        Assert.Single(_mail.Sent);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        _mail.Error = "Outlook niedostępny na tym stanowisku";
        var failed = Assert.Single(service.Notify("PAF", cycle, pinId: null, resend: true).Notifications);   // „Wyślij ponownie”
        Assert.Equal((CamNotificationResults.Failed, "Outlook niedostępny na tym stanowisku"), (failed.Result, failed.Message));
        Assert.Equal([CamNotificationResults.Failed, CamNotificationResults.Sent], service.Notifications("PAF", "2026-10").Select(n => n.Result));
        Assert.Equal("Anna Nowak", service.Notifications("PAF", "2026-10")[0].Name);

        // CAM bez adresu w słowniku Osoby (0577 – M28) – zapis „brak adresu”, bez wysyłki.
        _mail.Error = null;
        var noEmail = Assert.Single(service.Notify("M28", cycle, pinId: null, resend: false).Notifications);
        Assert.Equal(("0577", null, CamNotificationResults.NoEmail), (noEmail.Cam, noEmail.Email, noEmail.Result));
        Assert.Single(_mail.Sent);
        Assert.Single(service.Notify("M28", cycle, pinId: null, resend: false).Notifications);   // bez skutecznej wysyłki – próba w kolejnym przebiegu

        // Estymacja wysłana przez CAM-a – nie jest zaległa.
        var wp = CamEstimatesTests.Wp();
        Assert.True(service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)) with { Justification = "opóźnienie dostaw kooperacji" }, submit: true).Ok);
        Assert.Empty(service.Notify("PAF", cycle, pinId: null, resend: true).Notifications);
        Assert.Equal((2, 2), (store.Notifications("PAF", "2026-10").Count, store.Notifications("M28", "2026-10").Count));
    }

    /// <summary>Cykl tygodniowy: tydzień kalendarza okresów z terminem – koniec tygodnia; nowa estymacja tygodniowa – od ostatnio zatwierdzonej.</summary>
    [SqlFact]
    public void Weekly_cycle_comes_from_calendar_week_and_new_weekly_estimate_continues_last_approved()
    {
        var (service, store, _) = Create();
        var week = store.Cycle(new DateOnly(2026, 10, 7), weekly: true)!;
        Assert.True(week.Weekly);
        Assert.Matches(@"^2026-T\d{2}$", week.Period);
        Assert.True(week.Deadline >= new DateOnly(2026, 10, 7) && week.Deadline <= new DateOnly(2026, 10, 13), $"termin {week.Deadline}");
        Assert.Equal(week.Deadline, store.Deadline(week.Period));

        var wp = CamEstimatesTests.Wp();
        var monthly = service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)) with
        {
            Justification = "część poszycia przejmuje kooperant",
            Lines = [.. CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)).Lines.Select(l => l.Kind == CamLineKinds.Hours && l.Year == 2027 ? l with { Value = 1234 } : l)],
        }, submit: true).Estimate!;
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(service.Approve(store.Estimate("PAF", "WP-1", "2026-10")!).Ok);

        var item = Assert.Single(service.Worklist([wp with { WeeklyCycle = true }], service.CycleOf("2026-10"), week));

        Assert.Equal((week.Period, CamStatuses.ToDo, "część poszycia przejmuje kooperant"), (item.Estimate.Cycle, item.Estimate.Status, item.Estimate.Justification));
        Assert.Equal(1234m, item.Estimate.Lines.Single(l => l.Kind == CamLineKinds.Hours && l.Year == 2027).Value);   // wartość z zatwierdzonej
        var today = DateOnly.FromDateTime(_services.Clock.Now.LocalDateTime);
        Assert.Equal(CamEstimates.HoursProposal(wp, today).GetValueOrDefault(2027),
            item.Estimate.Lines.Single(l => l.Kind == CamLineKinds.Hours && l.Year == 2027).Proposal);               // propozycja systemu bez zmian
        Assert.NotEqual(1234m, monthly.Lines.Single(l => l.Kind == CamLineKinds.Hours && l.Year == 2027).Proposal);
        Assert.Equal(CamStatuses.Approved, Assert.Single(service.Worklist([wp], service.CycleOf("2026-10"), week)).Estimate.Status);   // program miesięczny
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

        // Zmiana względem propozycji (uzasadnienie opcjonalne) i błędne daty rzeczywiste – szkic tak, wysyłka nie.
        var changed = item.Estimate with
        {
            Lines = [.. item.Estimate.Lines.Select(l => l.Kind == CamLineKinds.Hours && l.Year == 2027 ? l with { Value = l.Value - 200 } : l)],
            ActualStart = new DateOnly(2025, 4, 1), ActualFinish = new DateOnly(2025, 3, 1),
        };
        var draft = service.Save(wp, changed, submit: false);
        Assert.True(draft.Ok, draft.Message);
        Assert.Equal((CamStatuses.Draft, new DateOnly(2025, 4, 1)), (draft.Estimate!.Status, draft.Estimate.ActualStart));
        Assert.Equal(new DateOnly(2025, 4, 1), store.Estimate("PAF", "WP-1", "2026-10")!.ActualStart);   // daty rzeczywiste w bazie
        var refused = service.Save(wp, draft.Estimate, submit: true);
        Assert.False(refused.Ok);
        Assert.Contains("rzeczywisty koniec przed rzeczywistym startem", refused.Errors!);
        draft = service.Save(wp, draft.Estimate with { ActualFinish = null }, submit: false);
        Assert.True(draft.Ok, draft.Message);

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
        Assert.Equal(((DateOnly?)new DateOnly(2025, 4, 1), (DateOnly?)null), Assert.Single(_packages.AppliedActual));   // rzeczywisty start – do struktury
        Assert.Equal(CamStatuses.Approved, store.Estimate("PAF", "WP-1", "2026-10")!.Status);

        var history = service.History(approved.Estimate!);
        Assert.Equal([CamStatuses.Approved, CamStatuses.Sent, CamStatuses.Returned, CamStatuses.Sent, CamStatuses.Draft, CamStatuses.Draft], history.Select(h => h.Status));
        Assert.StartsWith("ETC 2 920 h · materiały", history.Last().Summary.Replace('\u00A0', ' '));   // nowa – bez poprzedniej wersji
        Assert.EndsWith("rzeczywisty start 2025-04-01 · rzeczywisty koniec 2025-03-01", history.Last().Summary);
        Assert.Equal("rzeczywisty koniec —", history[^2].Summary);
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

    /// <summary>Kontrakt ICamReview (etap przebiegu „Estymacje CAM”, kolory struktury): wymagane WP wobec zatwierdzonych, decyzje z kontrolą wersji.</summary>
    [SqlFact]
    public void Review_of_program_counts_required_work_packages_and_decides_on_the_current_version_only()
    {
        var (service, store, _) = Create();
        var review = new CamReviewSource(service);
        var cycle = review.CycleOf("2026-10");
        Assert.Equal("2026-10", cycle.Period);
        Assert.NotNull(cycle.Deadline);                                                    // tydzień zamykający z kalendarza
        var wp = CamEstimatesTests.Wp();
        var finished = CamEstimatesTests.Wp("WP-2");
        Assert.True(service.Save(finished, CamEstimates.New(finished, "2026-10", new DateOnly(2026, 10, 20)), submit: false).Ok);   // szkic WP zakończonego

        var empty = review.Program("PAF", cycle);

        Assert.Equal(["WP-1", "WP-2"], empty.Items.Select(i => i.Wp));                    // tylko program PAF
        Assert.Equal((1, 0, 0, 0, 1), (empty.Required, empty.Approved, empty.Sent, empty.AtCam, empty.Missing));   // WP-2 zakończony – niewymagany
        var todo = empty.Items[0];
        Assert.Equal((CamStatuses.ToDo, (DateTimeOffset?)null), (todo.Status, todo.Version));
        Assert.Equal((0m, 0m), (todo.EtcHours, todo.Material));                          // bez estymacji – bez wartości (bez liczenia propozycji)
        Assert.False(review.Approve(todo).Ok);                                            // niezapisana – brak decyzji

        var sent = service.Save(wp, CamEstimates.New(wp, "2026-10", new DateOnly(2026, 10, 20)) with { Justification = "opóźnienie dostaw kooperacji" }, submit: true);
        Assert.True(sent.Ok, sent.Message);
        var listed = review.Program("PAF", cycle).Items[0];
        Assert.Equal((CamStatuses.Sent, 1), (listed.Status, review.Program("PAF", cycle).Sent));
        Assert.False(review.Return(listed, "za mało").Ok);                                // uwaga wymagana
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        var decided = review.Approve(listed);
        Assert.True(decided.Ok, decided.Message);
        Assert.Equal((CamStatuses.Approved, listed.Name, true), (decided.Item!.Status, decided.Item.Name, decided.Item.Required));   // pozycja po decyzji – bez odczytu programu
        Assert.True(review.Reopen(decided.Item, "korekta po decyzji z listy przebiegu").Ok);                                        // nowa wersja z wyniku decyzji
        var reopened = review.Program("PAF", cycle).Items[0];
        Assert.Equal(CamStatuses.Returned, reopened.Status);
        Assert.True(service.Save(wp, store.Estimate("PAF", "WP-1", "2026-10")!, submit: true).Ok);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(review.Approve(review.Program("PAF", cycle).Items[0]).Ok);
        Assert.False(review.Reopen(listed, "nieaktualny wiersz listy przebiegu").Ok);    // wersja sprzed zatwierdzenia
        var approved = review.Program("PAF", cycle);
        Assert.Equal((1, 1, 0), (approved.Required, approved.Approved, approved.Missing));
        Assert.StartsWith("Estymacje CAM 2026-10: zatwierdzone 1 z 1 wymaganych", approved.Summary);
        Assert.Equal(2, _packages.Applied.Count);
        Assert.Equal([(CamStatuses.Approved, false), (CamStatuses.Draft, false)],
            review.Saved("PAF", "2026-10").OrderBy(i => i.Wp).Select(i => (i.Status, i.Required)));   // zapisane – bez wymagalności

        // Postęp wszystkich programów (monitoring w module CAM): M28 – WP-3 bez estymacji.
        var progress = service.Progress(cycle);
        Assert.Equal((2, 1, 1), (progress.Required, progress.Approved, progress.Missing));
        Assert.Equal(["PAF", "M28"], progress.Items.Where(i => i.Required).Select(i => i.Project).Order(StringComparer.Ordinal).Reverse());
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
