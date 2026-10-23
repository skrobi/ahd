namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Reguły startowe (docs/pipeline-fazy.md, rozdz. 4.1) – obowiązują, dopóki słownik „Reguły walidacji” jest pusty (zapis w edytorze
/// tworzy wiersze do zmiany). Walidacja ACTUALS – cała pula ACTUALS (biezace, poprzednie); Walidacja EV – dane jak w strukturze (ev)
/// i słowniki programu; przykładowe uzgodnienie (koszt brutto wobec BAC) – poza szablonami startowymi.
/// </summary>
public static partial class RunRules
{
    public static IReadOnlyList<RunRule> ActualsStarters { get; } =
    [
        new("Mniej wierszy niż w poprzednim przebiegu", CheckLevel.Error, true,
            """
            SELECT b.Wiersze, p.Wiersze AS WierszePoprzednie, b.Wiersze - p.Wiersze AS Zmiana
            FROM (SELECT ISNULL(SUM(Wiersze), 0) AS Wiersze FROM biezace) b
            CROSS JOIN (SELECT ISNULL(SUM(Wiersze), 0) AS Wiersze FROM poprzednie) p
            WHERE @JestPoprzedni = 1 AND b.Wiersze < p.Wiersze
            """,
            "Mniej wierszy danych niż w poprzednim przebiegu: {Wiersze} < {WierszePoprzednie} – niepełny zrzut ACTUALS albo zmiana zakresu",
            "Zrzut ACTUALS jest narastający – mniej wierszy niż w poprzednim przebiegu to brak danych."),
        new("Koszt brutto mniejszy niż w poprzednim przebiegu", CheckLevel.Warning, true,
            """
            SELECT b.Koszt, p.Koszt AS KosztPoprzedni, b.Koszt - p.Koszt AS Zmiana
            FROM (SELECT ISNULL(SUM(Kwota), 0) AS Koszt FROM biezace WHERE Rozliczeniowy = 0) b
            CROSS JOIN (SELECT ISNULL(SUM(Kwota), 0) AS Koszt FROM poprzednie WHERE Rozliczeniowy = 0) p
            WHERE @JestPoprzedni = 1 AND b.Koszt < p.Koszt - 1
            """,
            "Koszt brutto mniejszy niż w poprzednim przebiegu: {Koszt} < {KosztPoprzedni} (zmiana {Zmiana})",
            "Koszt brutto (wiersze bez cost elementów rozliczeniowych) mniejszy o więcej niż 1 niż w poprzednim przebiegu."),
        new("Największe odchylenia raport do raportu", CheckLevel.Pass, true,
            """
            SELECT TOP (10) ElementWBS, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
            FROM (SELECT ElementWBS, Kwota AS Biezaco, 0 AS Poprzednio FROM biezace
                  UNION ALL
                  SELECT ElementWBS, 0, Kwota FROM poprzednie) x
            WHERE @JestPoprzedni = 1
            GROUP BY ElementWBS
            HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) >= 1
            ORDER BY ABS(SUM(Biezaco) - SUM(Poprzednio)) DESC, ElementWBS
            """,
            "Element WBS {ElementWBS}: zmiana {Zmiana} ({Poprzednio} → {Biezaco})",
            "10 elementów WBS z największą zmianą salda (cała pula) od poprzedniego przebiegu."),
        new("Największe odchylenia raport do raportu – cost element", CheckLevel.Pass, true,
            """
            SELECT TOP (10) CostElement, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
            FROM (SELECT CostElement, Kwota AS Biezaco, 0 AS Poprzednio FROM biezace
                  UNION ALL
                  SELECT CostElement, 0, Kwota FROM poprzednie) x
            WHERE @JestPoprzedni = 1
            GROUP BY CostElement
            HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) >= 1
            ORDER BY ABS(SUM(Biezaco) - SUM(Poprzednio)) DESC, CostElement
            """,
            "Cost element {CostElement}: zmiana {Zmiana} ({Poprzednio} → {Biezaco})",
            "10 cost elementów z największą zmianą kwoty (cała pula) od poprzedniego przebiegu."),
        new("Cost element spoza słownika Cost Category", CheckLevel.Error, true,
            """
            SELECT b.CostElement, SUM(b.Wiersze) AS Wiersze, SUM(b.Kwota) AS Kwota, N'Cost Category' AS Slownik, b.CostElement AS Filtr
            FROM biezace b
            WHERE b.CostElement IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM cost_category c WHERE c.CostElement = b.CostElement)
            GROUP BY b.CostElement
            ORDER BY ABS(SUM(b.Kwota)) DESC, b.CostElement
            """,
            "Cost element {CostElement} spoza słownika Cost Category ({Wiersze} wierszy, kwota {Kwota}) – dodaj go (Słowniki → Cost Category → Uzupełnij z ACTUALS)",
            "Cost element z danych ACTUALS bez wpisu w słowniku Cost Category programu (globalny i zmiany programu)."),
        new("Cost element pierwszy raz w programie", CheckLevel.Pass, true,
            """
            SELECT b.CostElement, SUM(b.Kwota) AS Kwota
            FROM biezace b
            WHERE @JestPoprzedni = 1 AND b.CostElement IS NOT NULL
              AND EXISTS (SELECT 1 FROM cost_category c WHERE c.CostElement = b.CostElement)
              AND NOT EXISTS (SELECT 1 FROM poprzednie p WHERE p.CostElement = b.CostElement)
            GROUP BY b.CostElement
            ORDER BY ABS(SUM(b.Kwota)) DESC, b.CostElement
            """,
            "Cost element {CostElement} pierwszy raz w programie (kwota {Kwota})",
            "Cost element ze słownika, którego w poprzednim przebiegu nie było (spoza słownika – reguła wyżej)."),
        new("Netowanie do 0 w okresach zamkniętych", CheckLevel.Error, true,
            """
            WITH zamkniete AS (SELECT * FROM biezace WHERE OkresZamkniety = 1),
            program AS (SELECT ISNULL(SUM(Kwota), 0) AS SaldoProgramu FROM zamkniete)
            SELECT TOP (10) z.ElementWBS,
                   SUM(z.Kwota) AS Saldo,
                   SUM(CASE WHEN z.Rozliczeniowy = 0 THEN z.Kwota ELSE 0 END) AS Koszt,
                   SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END) AS Rozliczenie,
                   CASE WHEN SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END) = 0 THEN N'brak rozliczenia'
                        WHEN ABS(SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END)) > ABS(SUM(CASE WHEN z.Rozliczeniowy = 0 THEN z.Kwota ELSE 0 END))
                          THEN N'rozliczenie większe niż koszt'
                        ELSE N'rozliczenie niepełne' END AS Problem,
                   CASE WHEN SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END) = 0
                          THEN N'reguła rozliczenia, element statystyczny albo brak cost elementu „Rozliczeniowy” w słowniku'
                        WHEN ABS(SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END)) > ABS(SUM(CASE WHEN z.Rozliczeniowy = 0 THEN z.Kwota ELSE 0 END))
                          THEN N'storno kosztu po rozliczeniu?'
                        ELSE N'sprawdź regułę rozliczenia elementu' END AS Wskazowka,
                   MAX(CASE WHEN z.Kwota <> 0 THEN z.RokOkres END) AS OstatniOkres,
                   MAX(p.SaldoProgramu) AS SaldoProgramu
            FROM zamkniete z
            CROSS JOIN program p
            WHERE ABS(p.SaldoProgramu) > 1
            GROUP BY z.ElementWBS
            HAVING ABS(SUM(z.Kwota)) > 1
            ORDER BY ABS(SUM(z.Kwota)) DESC, z.ElementWBS
            """,
            "{ElementWBS}: saldo {Saldo} (koszt {Koszt}, rozliczenie {Rozliczenie}) – {Problem} ({Wskazowka}); ostatni okres z ruchem {OstatniOkres}; saldo programu {SaldoProgramu}",
            "Program nie netuje się do 0 w okresach zamkniętych (saldo programu > 1) – 10 elementów WBS z największym saldem i przyczyna."),
        new("Koszt utworzony po rozliczeniu", CheckLevel.Warning, true,
            """
            WITH zamkniete AS (SELECT * FROM biezace WHERE OkresZamkniety = 1)
            SELECT TOP (10) ElementWBS, RokOkres,
                   MAX(CASE WHEN Rozliczeniowy = 0 THEN Utworzono END) AS KosztUtworzony,
                   MAX(CASE WHEN Rozliczeniowy = 1 THEN Utworzono END) AS Rozliczono
            FROM zamkniete
            WHERE (SELECT ABS(ISNULL(SUM(Kwota), 0)) FROM zamkniete) > 1
            GROUP BY ElementWBS, RokOkres
            HAVING MAX(CASE WHEN Rozliczeniowy = 0 THEN Utworzono END) > MAX(CASE WHEN Rozliczeniowy = 1 THEN Utworzono END)
            ORDER BY ElementWBS, RokOkres
            """,
            "{ElementWBS} {RokOkres}: koszt utworzony {KosztUtworzony}, po rozliczeniu z {Rozliczono} – wymaga ponownego rozliczenia",
            "Gdy program nie netuje się do 0 (saldo okresów zamkniętych > 1): koszt utworzony później niż ostatnie rozliczenie elementu w okresie."),
        new("Zmiana salda okresu zamkniętego", CheckLevel.Warning, true,
            """
            SELECT TOP (10) ElementWBS, RokOkres, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
            FROM (SELECT ElementWBS, RokOkres, Kwota AS Biezaco, 0 AS Poprzednio FROM biezace WHERE OkresZamkniety = 1
                  UNION ALL
                  SELECT ElementWBS, RokOkres, 0, Kwota FROM poprzednie WHERE OkresZamkniety = 1) x
            WHERE @JestPoprzedni = 1
            GROUP BY ElementWBS, RokOkres
            HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) > 1
            ORDER BY ABS(SUM(Biezaco) - SUM(Poprzednio)) DESC, ElementWBS, RokOkres
            """,
            "{ElementWBS} {RokOkres}: saldo okresu zamkniętego zmieniło się o {Zmiana} od poprzedniego przebiegu ({Poprzednio} → {Biezaco})",
            "Saldo elementu WBS w okresie zamkniętym inne niż w poprzednim przebiegu (10 największych zmian)."),
        new("Ujemny koszt elementu WBS", CheckLevel.Warning, true,
            """
            SELECT TOP (10) ElementWBS, SUM(Kwota) AS Saldo,
                   SUM(CASE WHEN Rozliczeniowy = 0 THEN Kwota ELSE 0 END) AS Koszt,
                   SUM(CASE WHEN Rozliczeniowy = 1 THEN Kwota ELSE 0 END) AS Rozliczenie
            FROM biezace
            GROUP BY ElementWBS
            HAVING SUM(Kwota) < -1
            ORDER BY ABS(SUM(Kwota)) DESC, ElementWBS
            """,
            "{ElementWBS}: ujemne saldo narastająco {Saldo} (koszt {Koszt}, rozliczenie {Rozliczenie}) – storno albo rozliczenie większe niż koszt?",
            "Element WBS z ujemnym saldem narastająco (cała pula: koszt i rozliczenie)."),
        new("Wiersze w okresach przyszłych", CheckLevel.Warning, true,
            """
            SELECT SUM(Wiersze) AS Wiersze, SUM(Kwota) AS Kwota, MIN(RokOkres) AS OdOkresu, MAX(RokOkres) AS DoOkresu
            FROM biezace
            WHERE OkresPrzyszly = 1
            HAVING COUNT(*) > 0
            """,
            "Wiersze w okresach przyszłych: {Wiersze} ({OdOkresu} – {DoOkresu}), kwota {Kwota}",
            "Wiersze z okresem po bieżącym okresie kalendarza."),
        new("Waluta inna niż waluta kosztów", CheckLevel.Warning, true,
            """
            SELECT SUM(Wiersze) AS Wiersze, STRING_AGG(Waluta, N', ') WITHIN GROUP (ORDER BY Waluta) AS Waluty, MAX(@WalutaKosztow) AS WalutaKosztow
            FROM (SELECT Waluta, SUM(Wiersze) AS Wiersze FROM biezace
                  WHERE Waluta <> N'' AND Waluta <> @WalutaKosztow
                  GROUP BY Waluta) w
            HAVING COUNT(*) > 0
            """,
            "Waluta inna niż {WalutaKosztow}: {Waluty} ({Wiersze} wierszy) – saldo w różnych walutach",
            "Wiersze w walucie innej niż waluta pola kwoty ACTUALS (PLN) albo przeważająca w danych."),
        new("Wiersze bez elementu WBS", CheckLevel.Warning, true,
            """
            SELECT SUM(Wiersze) AS Wiersze, SUM(Kwota) AS Kwota
            FROM biezace
            WHERE ElementWBS IS NULL
            HAVING COUNT(*) > 0
            """,
            "Wiersze bez elementu WBS: {Wiersze} (sieci, zlecenia przypisane do programu), kwota {Kwota}",
            "Wiersze bez elementu WBS (sieci, zlecenia przypisane do programu)."),
    ];

    public static IReadOnlyList<RunRule> EvStarters { get; } =
    [
        new("Cost element bez Cost Category", CheckLevel.Warning, true,
            """
            SELECT e.CostElement, c.Opis, SUM(e.Kwota) AS Kwota, N'Cost Category' AS Slownik, e.CostElement AS Filtr
            FROM ev e
            JOIN cost_category c ON c.CostElement = e.CostElement
            WHERE c.CostCategory IS NULL
            GROUP BY e.CostElement, c.Opis
            ORDER BY e.CostElement
            """,
            "Cost element {CostElement} bez Cost Category – koszt {Kwota} poza raportem po kategoriach (Słowniki → Cost Category albo zmiana programu)",
            "Cost element kosztu ze słownika Cost Category programu z pustą kolumną Cost Category."),
        new("MPK bez stawki wydziału na rok", CheckLevel.Error, true,
            """
            SELECT w.MPK, CAST(LEFT(@BiezacyOkres, 4) AS INT) AS Rok, COUNT(*) AS LiczbaWP,
                   STRING_AGG(CAST(w.WP AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY w.WP) AS ListaWP,
                   N'Stawki wydziałów' AS Slownik, w.MPK AS Filtr
            FROM (SELECT DISTINCT WP, MPK FROM wp_cam WHERE MPK IS NOT NULL) w
            WHERE NOT EXISTS (SELECT 1 FROM stawki s WHERE s.MPK = w.MPK AND s.Rok = CAST(LEFT(@BiezacyOkres, 4) AS INT))
            GROUP BY w.MPK
            ORDER BY w.MPK
            """,
            "MPK {MPK} bez stawki w „Stawki wydziałów” na rok {Rok} – ETC Cost bez stawki; WP ({LiczbaWP}): {ListaWP}",
            "MPK WP ze słownika „WP i CAM” bez wiersza w „Stawki wydziałów” na rok bieżącego okresu."),
        new("WP bez MPK", CheckLevel.Warning, true,
            """
            SELECT WP, N'WP i CAM' AS Slownik, WP AS Filtr
            FROM wp_cam
            GROUP BY WP
            HAVING COUNT(MPK) = 0
            ORDER BY WP
            """,
            "WP {WP} bez MPK w „WP i CAM” – ETC Cost bez stawki wydziału",
            "WP ze słownika „WP i CAM” bez MPK."),
        new("WP bez budżetu", CheckLevel.Warning, true,
            """
            SELECT DISTINCT w.WP, N'Harmonogram i budżet' AS Slownik, w.WP AS Filtr
            FROM wp_cam w
            WHERE NOT EXISTS (SELECT 1 FROM harmonogram h
                              WHERE h.WP = w.WP AND (ISNULL(h.BAC, 0) <> 0 OR ISNULL(h.BacHours, 0) <> 0 OR ISNULL(h.BacMaterial, 0) <> 0))
            ORDER BY w.WP
            """,
            "WP {WP} bez budżetu (BAC, BAC HOURS, BAC MATERIAL) w „Harmonogram i budżet” – WP bez podstawy BCWS i BCWP",
            "WP ze słownika „WP i CAM” bez BAC, BAC HOURS i BAC MATERIAL (puste albo 0) w „Harmonogram i budżet”."),
        new("Nowy rodzaj kosztu w programie", CheckLevel.Warning, true,
            """
            WITH teraz AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev GROUP BY RodzajKosztu),
            wtedy AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev_poprzednie GROUP BY RodzajKosztu)
            SELECT t.RodzajKosztu, t.Kwota, ISNULL(w.Kwota, 0) AS KwotaPoprzednia
            FROM teraz t
            LEFT JOIN wtedy w ON w.RodzajKosztu = t.RodzajKosztu
            WHERE @JestPoprzedni = 1 AND ABS(t.Kwota) > 1 AND ABS(ISNULL(w.Kwota, 0)) <= 1
            ORDER BY t.RodzajKosztu
            """,
            "Nowy rodzaj kosztu {RodzajKosztu}: {Kwota} (w poprzednim przebiegu {KwotaPoprzednia}) – sprawdź cost elementy rodzaju {RodzajKosztu}",
            "Koszt rodzaju (Hours / Materials / Other – Rodzaj kosztu w Cost Category, puste – Other), którego w poprzednim przebiegu nie było."),
        new("Skok przyrostu kosztu", CheckLevel.Warning, true,
            """
            SELECT RodzajKosztu, @BiezacyOkres AS Okres, Przyrost, Srednia, Przyrost / Srednia AS Krotnosc
            FROM (SELECT RodzajKosztu,
                         SUM(CASE WHEN OkresyWstecz = 0 THEN Kwota ELSE 0 END) AS Przyrost,
                         SUM(CASE WHEN OkresyWstecz BETWEEN 1 AND 6 THEN Kwota ELSE 0 END) / 6 AS Srednia
                  FROM ev
                  GROUP BY RodzajKosztu) k
            WHERE Srednia > 0 AND Przyrost > 3 * Srednia
            ORDER BY RodzajKosztu
            """,
            "Skok przyrostu {RodzajKosztu} w okresie {Okres}: {Przyrost} – {Krotnosc} × średni przyrost {Srednia} z 6 poprzednich okresów (próg 3 ×)",
            "Przyrost rodzaju kosztu w bieżącym okresie większy niż 3 × średni przyrost z 6 poprzednich okresów (średnia ≤ 0 – bez wyniku)."),
        new("Spadek kosztu narastająco w rodzaju", CheckLevel.Warning, true,
            """
            WITH teraz AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev GROUP BY RodzajKosztu),
            wtedy AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev_poprzednie GROUP BY RodzajKosztu)
            SELECT ISNULL(t.RodzajKosztu, w.RodzajKosztu) AS RodzajKosztu, ISNULL(t.Kwota, 0) AS Kwota, ISNULL(w.Kwota, 0) AS KwotaPoprzednia,
                   ISNULL(t.Kwota, 0) - ISNULL(w.Kwota, 0) AS Zmiana
            FROM teraz t
            FULL JOIN wtedy w ON w.RodzajKosztu = t.RodzajKosztu
            WHERE @JestPoprzedni = 1 AND ISNULL(t.Kwota, 0) < ISNULL(w.Kwota, 0) - 1
            ORDER BY 1
            """,
            "Koszt {RodzajKosztu} narastająco mniejszy niż w poprzednim przebiegu: {Kwota} < {KwotaPoprzednia} (storno, przeksięgowanie, zmiana rodzaju w Cost Category?)",
            "Koszt rodzaju narastająco mniejszy niż w poprzednim przebiegu o więcej niż 1."),
        new("Zmiana rodzaju kosztu w zamkniętych okresach", CheckLevel.Warning, true,
            """
            SELECT RodzajKosztu, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
            FROM (SELECT RodzajKosztu, Kwota AS Biezaco, 0 AS Poprzednio FROM ev WHERE OkresZamkniety = 1
                  UNION ALL
                  SELECT RodzajKosztu, 0, Kwota FROM ev_poprzednie WHERE OkresZamkniety = 1) x
            WHERE @JestPoprzedni = 1
            GROUP BY RodzajKosztu
            HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) > 1
            ORDER BY RodzajKosztu
            """,
            "Koszt {RodzajKosztu} w okresach zamkniętych zmienił się o {Zmiana} od poprzedniego przebiegu ({Poprzednio} → {Biezaco})",
            "Koszt rodzaju w okresach zamkniętych inny niż w poprzednim przebiegu o więcej niż 1."),
    ];

    /// <summary>Przykładowe uzgodnienie (zamiast reguł uzgodnień z miarami) – w bibliotece reguł, poza szablonami startowymi.</summary>
    public static RunRule ReconciliationExample { get; } = new("Koszt brutto nie większy niż BAC × 1,1", CheckLevel.Warning, true,
        """
        SELECT k.Koszt, b.BAC, k.Koszt / b.BAC AS Relacja
        FROM (SELECT ISNULL(SUM(Kwota), 0) AS Koszt FROM ev) k
        CROSS JOIN (SELECT ISNULL(SUM(BAC), 0) AS BAC FROM harmonogram) b
        WHERE b.BAC > 0 AND k.Koszt > b.BAC * 1.1
        """,
        "Koszt brutto {Koszt} większy niż BAC {BAC} × 1,1 (relacja {Relacja})",
        "Przykład uzgodnienia: koszt brutto programu (ev – waluta programu) wobec sumy BAC z „Harmonogram i budżet” (waluta wpisów – sprawdź, czy ta sama).");

    /// <summary>Wszystkie reguły startowe w kolejności: ACTUALS, EV, uzgodnienie.</summary>
    public static IReadOnlyList<RunRule> Starters { get; } = [.. ActualsStarters, .. EvStarters, ReconciliationExample];

    public static RunRule? Starter(string name) => Starters.FirstOrDefault(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
