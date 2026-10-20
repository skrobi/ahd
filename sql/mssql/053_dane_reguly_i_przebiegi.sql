/* PZL-EV – migracja 053 (dane startowe): reguły walidacji i szablony przebiegów (docs/pipeline-fazy.md, rozdz. 4.1; tasks/F5.7).
   - „Reguły walidacji” (DICT_RunValidation): 22 reguły startowe aplikacji w SQL – 13 Walidacja ACTUALS, 8 Walidacja EV, przykładowe
     uzgodnienie (koszt brutto ≤ BAC × 1,1); reguła o tej samej nazwie już zapisana – bez zmian. Treść jak RunRules.Starters
     (test SqlPresetsTests porównuje).
   - Szablony przebiegów „Wewnętrzny” (domyślny dla wszystkich typów, w tym SAC) i „CAS” (domyślny dla CAS): 1 Import źródeł →
     2 Przypięcie stanu → 3 Walidacja ACTUALS (reguły ACTUALS) → 4 Walidacja EV (reguły EV) → 5 Zatwierdzenie analityka; szablon
     o tej nazwie już jest – bez zmian; aktywny, gdy dla jego typu nie ma innego aktywnego szablonu.
   - Elementy etapów „Uzgodnienie: …” (reguły uzgodnień sprzed reguł SQL) – usunięte (historia zostaje).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 053_dane_reguly_i_przebiegi.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = N'migracja 053 – reguły i szablony startowe';

-- Reguły walidacji (RunRules.Starters).
DECLARE @rules TABLE (Sort INT PRIMARY KEY, Name NVARCHAR(200) NOT NULL, Description NVARCHAR(1000) NULL, Level NVARCHAR(10) NOT NULL,
                      IsActive BIT NOT NULL, SqlText NVARCHAR(MAX) NOT NULL, ResultTemplate NVARCHAR(2000) NULL);
INSERT INTO @rules (Sort, Name, Description, Level, IsActive, SqlText, ResultTemplate) VALUES
    (1, N'Mniej wierszy niż w poprzednim przebiegu', N'Zrzut ACTUALS jest narastający – mniej wierszy niż w poprzednim przebiegu to brak danych.', N'ERROR', 1,
     N'SELECT b.Wiersze, p.Wiersze AS WierszePoprzednie, b.Wiersze - p.Wiersze AS Zmiana
FROM (SELECT ISNULL(SUM(Wiersze), 0) AS Wiersze FROM biezace) b
CROSS JOIN (SELECT ISNULL(SUM(Wiersze), 0) AS Wiersze FROM poprzednie) p
WHERE @JestPoprzedni = 1 AND b.Wiersze < p.Wiersze',
     N'Mniej wierszy danych niż w poprzednim przebiegu: {Wiersze} < {WierszePoprzednie} – niepełny zrzut ACTUALS albo zmiana zakresu'),
    (2, N'Koszt brutto mniejszy niż w poprzednim przebiegu', N'Koszt brutto (wiersze bez cost elementów rozliczeniowych) mniejszy o więcej niż 1 niż w poprzednim przebiegu.', N'WARNING', 1,
     N'SELECT b.Koszt, p.Koszt AS KosztPoprzedni, b.Koszt - p.Koszt AS Zmiana
FROM (SELECT ISNULL(SUM(Kwota), 0) AS Koszt FROM biezace WHERE Rozliczeniowy = 0) b
CROSS JOIN (SELECT ISNULL(SUM(Kwota), 0) AS Koszt FROM poprzednie WHERE Rozliczeniowy = 0) p
WHERE @JestPoprzedni = 1 AND b.Koszt < p.Koszt - 1',
     N'Koszt brutto mniejszy niż w poprzednim przebiegu: {Koszt} < {KosztPoprzedni} (zmiana {Zmiana})'),
    (3, N'Największe odchylenia raport do raportu', N'10 elementów WBS z największą zmianą salda (cała pula) od poprzedniego przebiegu.', N'INFO', 1,
     N'SELECT TOP (10) ElementWBS, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
FROM (SELECT ElementWBS, Kwota AS Biezaco, 0 AS Poprzednio FROM biezace
      UNION ALL
      SELECT ElementWBS, 0, Kwota FROM poprzednie) x
WHERE @JestPoprzedni = 1
GROUP BY ElementWBS
HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) >= 1
ORDER BY ABS(SUM(Biezaco) - SUM(Poprzednio)) DESC, ElementWBS',
     N'Element WBS {ElementWBS}: zmiana {Zmiana} ({Poprzednio} → {Biezaco})'),
    (4, N'Największe odchylenia raport do raportu – cost element', N'10 cost elementów z największą zmianą kwoty (cała pula) od poprzedniego przebiegu.', N'INFO', 1,
     N'SELECT TOP (10) CostElement, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
FROM (SELECT CostElement, Kwota AS Biezaco, 0 AS Poprzednio FROM biezace
      UNION ALL
      SELECT CostElement, 0, Kwota FROM poprzednie) x
WHERE @JestPoprzedni = 1
GROUP BY CostElement
HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) >= 1
ORDER BY ABS(SUM(Biezaco) - SUM(Poprzednio)) DESC, CostElement',
     N'Cost element {CostElement}: zmiana {Zmiana} ({Poprzednio} → {Biezaco})'),
    (5, N'Cost element spoza słownika Cost Category', N'Cost element z danych ACTUALS bez wpisu w słowniku Cost Category programu (globalny i zmiany programu).', N'ERROR', 1,
     N'SELECT b.CostElement, SUM(b.Wiersze) AS Wiersze, SUM(b.Kwota) AS Kwota
FROM biezace b
WHERE b.CostElement IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM cost_category c WHERE c.CostElement = b.CostElement)
GROUP BY b.CostElement
ORDER BY ABS(SUM(b.Kwota)) DESC, b.CostElement',
     N'Cost element {CostElement} spoza słownika Cost Category ({Wiersze} wierszy, kwota {Kwota}) – dodaj go (Słowniki → Cost Category → Uzupełnij z ACTUALS)'),
    (6, N'Cost element pierwszy raz w programie', N'Cost element ze słownika, którego w poprzednim przebiegu nie było (spoza słownika – reguła wyżej).', N'INFO', 1,
     N'SELECT b.CostElement, SUM(b.Kwota) AS Kwota
FROM biezace b
WHERE @JestPoprzedni = 1 AND b.CostElement IS NOT NULL
  AND EXISTS (SELECT 1 FROM cost_category c WHERE c.CostElement = b.CostElement)
  AND NOT EXISTS (SELECT 1 FROM poprzednie p WHERE p.CostElement = b.CostElement)
GROUP BY b.CostElement
ORDER BY ABS(SUM(b.Kwota)) DESC, b.CostElement',
     N'Cost element {CostElement} pierwszy raz w programie (kwota {Kwota})'),
    (7, N'Netowanie do 0 w okresach zamkniętych', N'Program nie netuje się do 0 w okresach zamkniętych (saldo programu > 1) – 10 elementów WBS z największym saldem i przyczyna.', N'ERROR', 1,
     N'WITH zamkniete AS (SELECT * FROM biezace WHERE OkresZamkniety = 1),
program AS (SELECT ISNULL(SUM(Kwota), 0) AS SaldoProgramu FROM zamkniete)
SELECT TOP (10) z.ElementWBS,
       SUM(z.Kwota) AS Saldo,
       SUM(CASE WHEN z.Rozliczeniowy = 0 THEN z.Kwota ELSE 0 END) AS Koszt,
       SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END) AS Rozliczenie,
       CASE WHEN SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END) = 0 THEN N''brak rozliczenia''
            WHEN ABS(SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END)) > ABS(SUM(CASE WHEN z.Rozliczeniowy = 0 THEN z.Kwota ELSE 0 END))
              THEN N''rozliczenie większe niż koszt''
            ELSE N''rozliczenie niepełne'' END AS Problem,
       CASE WHEN SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END) = 0
              THEN N''reguła rozliczenia, element statystyczny albo brak cost elementu „Rozliczeniowy” w słowniku''
            WHEN ABS(SUM(CASE WHEN z.Rozliczeniowy = 1 THEN z.Kwota ELSE 0 END)) > ABS(SUM(CASE WHEN z.Rozliczeniowy = 0 THEN z.Kwota ELSE 0 END))
              THEN N''storno kosztu po rozliczeniu?''
            ELSE N''sprawdź regułę rozliczenia elementu'' END AS Wskazowka,
       MAX(CASE WHEN z.Kwota <> 0 THEN z.RokOkres END) AS OstatniOkres,
       MAX(p.SaldoProgramu) AS SaldoProgramu
FROM zamkniete z
CROSS JOIN program p
WHERE ABS(p.SaldoProgramu) > 1
GROUP BY z.ElementWBS
HAVING ABS(SUM(z.Kwota)) > 1
ORDER BY ABS(SUM(z.Kwota)) DESC, z.ElementWBS',
     N'{ElementWBS}: saldo {Saldo} (koszt {Koszt}, rozliczenie {Rozliczenie}) – {Problem} ({Wskazowka}); ostatni okres z ruchem {OstatniOkres}; saldo programu {SaldoProgramu}'),
    (8, N'Koszt utworzony po rozliczeniu', N'Gdy program nie netuje się do 0 (saldo okresów zamkniętych > 1): koszt utworzony później niż ostatnie rozliczenie elementu w okresie.', N'WARNING', 1,
     N'WITH zamkniete AS (SELECT * FROM biezace WHERE OkresZamkniety = 1)
SELECT TOP (10) ElementWBS, RokOkres,
       MAX(CASE WHEN Rozliczeniowy = 0 THEN Utworzono END) AS KosztUtworzony,
       MAX(CASE WHEN Rozliczeniowy = 1 THEN Utworzono END) AS Rozliczono
FROM zamkniete
WHERE (SELECT ABS(ISNULL(SUM(Kwota), 0)) FROM zamkniete) > 1
GROUP BY ElementWBS, RokOkres
HAVING MAX(CASE WHEN Rozliczeniowy = 0 THEN Utworzono END) > MAX(CASE WHEN Rozliczeniowy = 1 THEN Utworzono END)
ORDER BY ElementWBS, RokOkres',
     N'{ElementWBS} {RokOkres}: koszt utworzony {KosztUtworzony}, po rozliczeniu z {Rozliczono} – wymaga ponownego rozliczenia'),
    (9, N'Zmiana salda okresu zamkniętego', N'Saldo elementu WBS w okresie zamkniętym inne niż w poprzednim przebiegu (10 największych zmian).', N'WARNING', 1,
     N'SELECT TOP (10) ElementWBS, RokOkres, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
FROM (SELECT ElementWBS, RokOkres, Kwota AS Biezaco, 0 AS Poprzednio FROM biezace WHERE OkresZamkniety = 1
      UNION ALL
      SELECT ElementWBS, RokOkres, 0, Kwota FROM poprzednie WHERE OkresZamkniety = 1) x
WHERE @JestPoprzedni = 1
GROUP BY ElementWBS, RokOkres
HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) > 1
ORDER BY ABS(SUM(Biezaco) - SUM(Poprzednio)) DESC, ElementWBS, RokOkres',
     N'{ElementWBS} {RokOkres}: saldo okresu zamkniętego zmieniło się o {Zmiana} od poprzedniego przebiegu ({Poprzednio} → {Biezaco})'),
    (10, N'Ujemny koszt elementu WBS', N'Element WBS z ujemnym saldem narastająco (cała pula: koszt i rozliczenie).', N'WARNING', 1,
     N'SELECT TOP (10) ElementWBS, SUM(Kwota) AS Saldo,
       SUM(CASE WHEN Rozliczeniowy = 0 THEN Kwota ELSE 0 END) AS Koszt,
       SUM(CASE WHEN Rozliczeniowy = 1 THEN Kwota ELSE 0 END) AS Rozliczenie
FROM biezace
GROUP BY ElementWBS
HAVING SUM(Kwota) < -1
ORDER BY ABS(SUM(Kwota)) DESC, ElementWBS',
     N'{ElementWBS}: ujemne saldo narastająco {Saldo} (koszt {Koszt}, rozliczenie {Rozliczenie}) – storno albo rozliczenie większe niż koszt?'),
    (11, N'Wiersze w okresach przyszłych', N'Wiersze z okresem po bieżącym okresie kalendarza.', N'WARNING', 1,
     N'SELECT SUM(Wiersze) AS Wiersze, SUM(Kwota) AS Kwota, MIN(RokOkres) AS OdOkresu, MAX(RokOkres) AS DoOkresu
FROM biezace
WHERE OkresPrzyszly = 1
HAVING COUNT(*) > 0',
     N'Wiersze w okresach przyszłych: {Wiersze} ({OdOkresu} – {DoOkresu}), kwota {Kwota}'),
    (12, N'Waluta inna niż waluta kosztów', N'Wiersze w walucie innej niż waluta pola kwoty ACTUALS (PLN) albo przeważająca w danych.', N'WARNING', 1,
     N'SELECT SUM(Wiersze) AS Wiersze, STRING_AGG(Waluta, N'', '') WITHIN GROUP (ORDER BY Waluta) AS Waluty, MAX(@WalutaKosztow) AS WalutaKosztow
FROM (SELECT Waluta, SUM(Wiersze) AS Wiersze FROM biezace
      WHERE Waluta <> N'''' AND Waluta <> @WalutaKosztow
      GROUP BY Waluta) w
HAVING COUNT(*) > 0',
     N'Waluta inna niż {WalutaKosztow}: {Waluty} ({Wiersze} wierszy) – saldo w różnych walutach'),
    (13, N'Wiersze bez elementu WBS', N'Wiersze bez elementu WBS (sieci, zlecenia przypisane do programu).', N'WARNING', 1,
     N'SELECT SUM(Wiersze) AS Wiersze, SUM(Kwota) AS Kwota
FROM biezace
WHERE ElementWBS IS NULL
HAVING COUNT(*) > 0',
     N'Wiersze bez elementu WBS: {Wiersze} (sieci, zlecenia przypisane do programu), kwota {Kwota}'),
    (14, N'Cost element bez Cost Category', N'Cost element kosztu ze słownika Cost Category programu z pustą kolumną Cost Category.', N'WARNING', 1,
     N'SELECT e.CostElement, c.Opis, SUM(e.Kwota) AS Kwota
FROM ev e
JOIN cost_category c ON c.CostElement = e.CostElement
WHERE c.CostCategory IS NULL
GROUP BY e.CostElement, c.Opis
ORDER BY e.CostElement',
     N'Cost element {CostElement} bez Cost Category – koszt {Kwota} poza raportem po kategoriach (Słowniki → Cost Category albo zmiana programu)'),
    (15, N'MPK bez stawki wydziału na rok', N'MPK WP ze słownika „WP i CAM” bez wiersza w „Stawki wydziałów” na rok bieżącego okresu.', N'ERROR', 1,
     N'SELECT w.MPK, CAST(LEFT(@BiezacyOkres, 4) AS INT) AS Rok, COUNT(*) AS LiczbaWP,
       STRING_AGG(CAST(w.WP AS NVARCHAR(MAX)), N'', '') WITHIN GROUP (ORDER BY w.WP) AS ListaWP
FROM (SELECT DISTINCT WP, MPK FROM wp_cam WHERE MPK IS NOT NULL) w
WHERE NOT EXISTS (SELECT 1 FROM stawki s WHERE s.MPK = w.MPK AND s.Rok = CAST(LEFT(@BiezacyOkres, 4) AS INT))
GROUP BY w.MPK
ORDER BY w.MPK',
     N'MPK {MPK} bez stawki w „Stawki wydziałów” na rok {Rok} – ETC Cost bez stawki; WP ({LiczbaWP}): {ListaWP}'),
    (16, N'WP bez MPK', N'WP ze słownika „WP i CAM” bez MPK.', N'WARNING', 1,
     N'SELECT WP
FROM wp_cam
GROUP BY WP
HAVING COUNT(MPK) = 0
ORDER BY WP',
     N'WP {WP} bez MPK w „WP i CAM” – ETC Cost bez stawki wydziału'),
    (17, N'WP bez budżetu', N'WP ze słownika „WP i CAM” bez BAC, BAC HOURS i BAC MATERIAL (puste albo 0) w „Harmonogram i budżet”.', N'WARNING', 1,
     N'SELECT DISTINCT w.WP
FROM wp_cam w
WHERE NOT EXISTS (SELECT 1 FROM harmonogram h
                  WHERE h.WP = w.WP AND (ISNULL(h.BAC, 0) <> 0 OR ISNULL(h.BacHours, 0) <> 0 OR ISNULL(h.BacMaterial, 0) <> 0))
ORDER BY w.WP',
     N'WP {WP} bez budżetu (BAC, BAC HOURS, BAC MATERIAL) w „Harmonogram i budżet” – WP bez podstawy BCWS i BCWP'),
    (18, N'Nowy rodzaj kosztu w programie', N'Koszt rodzaju (Hours / Materials / Other – Rodzaj kosztu w Cost Category, puste – Other), którego w poprzednim przebiegu nie było.', N'WARNING', 1,
     N'WITH teraz AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev GROUP BY RodzajKosztu),
wtedy AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev_poprzednie GROUP BY RodzajKosztu)
SELECT t.RodzajKosztu, t.Kwota, ISNULL(w.Kwota, 0) AS KwotaPoprzednia
FROM teraz t
LEFT JOIN wtedy w ON w.RodzajKosztu = t.RodzajKosztu
WHERE @JestPoprzedni = 1 AND ABS(t.Kwota) > 1 AND ABS(ISNULL(w.Kwota, 0)) <= 1
ORDER BY t.RodzajKosztu',
     N'Nowy rodzaj kosztu {RodzajKosztu}: {Kwota} (w poprzednim przebiegu {KwotaPoprzednia}) – sprawdź cost elementy rodzaju {RodzajKosztu}'),
    (19, N'Skok przyrostu kosztu', N'Przyrost rodzaju kosztu w bieżącym okresie większy niż 3 × średni przyrost z 6 poprzednich okresów (średnia ≤ 0 – bez wyniku).', N'WARNING', 1,
     N'SELECT RodzajKosztu, @BiezacyOkres AS Okres, Przyrost, Srednia, Przyrost / Srednia AS Krotnosc
FROM (SELECT RodzajKosztu,
             SUM(CASE WHEN OkresyWstecz = 0 THEN Kwota ELSE 0 END) AS Przyrost,
             SUM(CASE WHEN OkresyWstecz BETWEEN 1 AND 6 THEN Kwota ELSE 0 END) / 6 AS Srednia
      FROM ev
      GROUP BY RodzajKosztu) k
WHERE Srednia > 0 AND Przyrost > 3 * Srednia
ORDER BY RodzajKosztu',
     N'Skok przyrostu {RodzajKosztu} w okresie {Okres}: {Przyrost} – {Krotnosc} × średni przyrost {Srednia} z 6 poprzednich okresów (próg 3 ×)'),
    (20, N'Spadek kosztu narastająco w rodzaju', N'Koszt rodzaju narastająco mniejszy niż w poprzednim przebiegu o więcej niż 1.', N'WARNING', 1,
     N'WITH teraz AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev GROUP BY RodzajKosztu),
wtedy AS (SELECT RodzajKosztu, SUM(Kwota) AS Kwota FROM ev_poprzednie GROUP BY RodzajKosztu)
SELECT ISNULL(t.RodzajKosztu, w.RodzajKosztu) AS RodzajKosztu, ISNULL(t.Kwota, 0) AS Kwota, ISNULL(w.Kwota, 0) AS KwotaPoprzednia,
       ISNULL(t.Kwota, 0) - ISNULL(w.Kwota, 0) AS Zmiana
FROM teraz t
FULL JOIN wtedy w ON w.RodzajKosztu = t.RodzajKosztu
WHERE @JestPoprzedni = 1 AND ISNULL(t.Kwota, 0) < ISNULL(w.Kwota, 0) - 1
ORDER BY 1',
     N'Koszt {RodzajKosztu} narastająco mniejszy niż w poprzednim przebiegu: {Kwota} < {KwotaPoprzednia} (storno, przeksięgowanie, zmiana rodzaju w Cost Category?)'),
    (21, N'Zmiana rodzaju kosztu w zamkniętych okresach', N'Koszt rodzaju w okresach zamkniętych inny niż w poprzednim przebiegu o więcej niż 1.', N'WARNING', 1,
     N'SELECT RodzajKosztu, SUM(Poprzednio) AS Poprzednio, SUM(Biezaco) AS Biezaco, SUM(Biezaco) - SUM(Poprzednio) AS Zmiana
FROM (SELECT RodzajKosztu, Kwota AS Biezaco, 0 AS Poprzednio FROM ev WHERE OkresZamkniety = 1
      UNION ALL
      SELECT RodzajKosztu, 0, Kwota FROM ev_poprzednie WHERE OkresZamkniety = 1) x
WHERE @JestPoprzedni = 1
GROUP BY RodzajKosztu
HAVING ABS(SUM(Biezaco) - SUM(Poprzednio)) > 1
ORDER BY RodzajKosztu',
     N'Koszt {RodzajKosztu} w okresach zamkniętych zmienił się o {Zmiana} od poprzedniego przebiegu ({Poprzednio} → {Biezaco})'),
    (22, N'Koszt brutto nie większy niż BAC × 1,1', N'Przykład uzgodnienia: koszt brutto programu (ev – waluta programu) wobec sumy BAC z „Harmonogram i budżet” (waluta wpisów – sprawdź, czy ta sama).', N'WARNING', 1,
     N'SELECT k.Koszt, b.BAC, k.Koszt / b.BAC AS Relacja
FROM (SELECT ISNULL(SUM(Kwota), 0) AS Koszt FROM ev) k
CROSS JOIN (SELECT ISNULL(SUM(BAC), 0) AS BAC FROM harmonogram) b
WHERE b.BAC > 0 AND k.Koszt > b.BAC * 1.1',
     N'Koszt brutto {Koszt} większy niż BAC {BAC} × 1,1 (relacja {Relacja})');

INSERT INTO [$(Schema)].[$(Prefix)DICT_RunValidation] (RowId, Version, Project, Name, Description, Level, IsActive, SqlText, ResultTemplate, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId] OVER (ORDER BY r.Sort), 1, NULL, r.Name, r.Description, r.Level, r.IsActive, r.SqlText,
       r.ResultTemplate, @at, @by
FROM @rules r
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_RunValidation] v WHERE v.Project IS NULL AND v.SupersededAt IS NULL AND v.Name = r.Name)
ORDER BY r.Sort;   -- kolejność reguł na liście (Id) jak w aplikacji

-- Szablony przebiegów (RunDefinitions.Starters): etapy i reguły etapów jak definicja wbudowana.
DECLARE @templates TABLE (Code NVARCHAR(100) PRIMARY KEY, ProjectType VARCHAR(20) NOT NULL, Description NVARCHAR(400) NOT NULL);
INSERT INTO @templates (Code, ProjectType, Description) VALUES
    (N'Wewnętrzny', 'Wszystkie', N'Przebieg programów wewnętrznych i SAC.'),
    (N'CAS', 'CAS', N'Przebieg programów CAS (koszty po narzutach CAS).');
DECLARE @steps TABLE (StepNo INT PRIMARY KEY, Name NVARCHAR(100) NOT NULL, Action NVARCHAR(40) NOT NULL, WarningGate NVARCHAR(20) NOT NULL,
                      Approval BIT NOT NULL, Description NVARCHAR(400) NULL);
INSERT INTO @steps (StepNo, Name, Action, WarningGate, Approval, Description) VALUES
    (1, N'Import źródeł', N'Import źródeł', N'Informacja', 0, N'Import plików ze słownika programu „Źródła danych”.'),
    (2, N'Przypięcie stanu', N'Przypięcie stanu', N'Informacja', 0, N'Dane ACTUALS programu w chwili przebiegu; porównanie z poprzednim przebiegiem.'),
    (3, N'Walidacja ACTUALS', N'Kontrole', N'Potwierdzenie', 0, N'Spójność danych ACTUALS na całej puli (przed narzutami, bez Wykluczeń) – reguły walidacji w SQL (ekran Przebiegi).'),
    (4, N'Walidacja EV', N'Kontrole', N'Potwierdzenie', 0, N'Dane jak w strukturze (bez rozliczeniowych, po Wykluczeniach, CAS po narzutach) i słowniki programu – reguły walidacji w SQL.'),
    (5, N'Zatwierdzenie analityka', N'Zatwierdzenie', N'Potwierdzenie', 1, N'Analityk potwierdza wynik przebiegu (komentarz w dzienniku).');
DECLARE @elements TABLE (Step NVARCHAR(100) NOT NULL, Element NVARCHAR(250) NOT NULL);
INSERT INTO @elements (Step, Element) VALUES
    (N'Walidacja ACTUALS', N'Mniej wierszy niż w poprzednim przebiegu'),
    (N'Walidacja ACTUALS', N'Koszt brutto mniejszy niż w poprzednim przebiegu'),
    (N'Walidacja ACTUALS', N'Największe odchylenia raport do raportu'),
    (N'Walidacja ACTUALS', N'Największe odchylenia raport do raportu – cost element'),
    (N'Walidacja ACTUALS', N'Cost element spoza słownika Cost Category'),
    (N'Walidacja ACTUALS', N'Cost element pierwszy raz w programie'),
    (N'Walidacja ACTUALS', N'Netowanie do 0 w okresach zamkniętych'),
    (N'Walidacja ACTUALS', N'Koszt utworzony po rozliczeniu'),
    (N'Walidacja ACTUALS', N'Zmiana salda okresu zamkniętego'),
    (N'Walidacja ACTUALS', N'Ujemny koszt elementu WBS'),
    (N'Walidacja ACTUALS', N'Wiersze w okresach przyszłych'),
    (N'Walidacja ACTUALS', N'Waluta inna niż waluta kosztów'),
    (N'Walidacja ACTUALS', N'Wiersze bez elementu WBS'),
    (N'Walidacja EV', N'Cost element bez Cost Category'),
    (N'Walidacja EV', N'MPK bez stawki wydziału na rok'),
    (N'Walidacja EV', N'WP bez MPK'),
    (N'Walidacja EV', N'WP bez budżetu'),
    (N'Walidacja EV', N'Nowy rodzaj kosztu w programie'),
    (N'Walidacja EV', N'Skok przyrostu kosztu'),
    (N'Walidacja EV', N'Spadek kosztu narastająco w rodzaju'),
    (N'Walidacja EV', N'Zmiana rodzaju kosztu w zamkniętych okresach');

DECLARE @new TABLE (Code NVARCHAR(100) PRIMARY KEY);
INSERT INTO [$(Schema)].[$(Prefix)DICT_RunDefinition] (RowId, Version, Project, Code, ProjectType, ProgramCode, RunKind, IsActive, Description, RecordedAt, RecordedBy)
OUTPUT inserted.Code INTO @new (Code)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, t.Code, t.ProjectType, NULL, N'Każdy',
       CASE WHEN EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_RunDefinition] d
                         WHERE d.Project IS NULL AND d.SupersededAt IS NULL AND d.IsActive = 1 AND d.ProjectType = t.ProjectType) THEN 0 ELSE 1 END,
       t.Description, @at, @by
FROM @templates t
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_RunDefinition] d WHERE d.Project IS NULL AND d.SupersededAt IS NULL AND d.Code = t.Code);

INSERT INTO [$(Schema)].[$(Prefix)DICT_RunStep] (RowId, Version, Project, Definition, StepNo, Name, Action, WarningGate, Approval, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, n.Code, s.StepNo, s.Name, s.Action, s.WarningGate, s.Approval, s.Description, @at, @by
FROM @new n CROSS JOIN @steps s
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_RunStep] x WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.Definition = n.Code AND x.StepNo = s.StepNo);

INSERT INTO [$(Schema)].[$(Prefix)DICT_RunStepElement] (RowId, Version, Project, Definition, Step, Element, IsActive, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, n.Code, e.Step, e.Element, 1, NULL, @at, @by
FROM @new n CROSS JOIN @elements e
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_RunStepElement] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.Definition = n.Code AND x.Step = e.Step AND x.Element = e.Element);

-- Elementy „Uzgodnienie: …” (reguły uzgodnień usunięte migracjami 051 / 052) – usunięte z szablonów, historia zostaje.
UPDATE [$(Schema)].[$(Prefix)DICT_RunStepElement] SET SupersededAt = @at, SupersededBy = @by
WHERE SupersededAt IS NULL AND Element LIKE N'Uzgodnienie:%';
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 53)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (53, N'053_dane_reguly_i_przebiegi.sql', '0.59.0');
GO
