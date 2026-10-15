# PZL-EV – Model danych

Zakres: warstwy danych, schematy i encje bazy MS SQL, historia i wersjonowanie oraz kontrakt przebiegu –
jak przebieg i rewizja wskazują dane, na których liczyły.

Powiązane: `docs/architektura.md` (technologia, podział logiki), `docs/pipeline-fazy.md` (kiedy dane powstają),
`docs/zrodla-danych.md` (znaczenie danych źródłowych), `docs/slowniki.md` (zawartość słowników).

---

## 1. Warstwy danych

Wszystkie dane PZL-EV są w centralnej bazie MS SQL. Oryginalne pliki źródłowe nie są przechowywane –
źródłem historii są dane zapisane w bazie.

```text
ŹRÓDŁA – RABIT (SAP CES), PZLPROD (struktura P1S, zaawansowanie z produkcji), pliki CAM
  ↓ import (pipeline G1), parser według definicji źródła – bez wierszy surowych (od migracji 007/008)
KANONICZNE – dane typowane według definicji źródła                                  can
  ↓
SŁOWNIKI, HARMONOGRAM I BUDŻET                                                      dict
  ↓ przypięcie stanu (P1)
PRZEBIEG – koszt per WP, zaawansowanie z pochodzeniem, problemy                     ev, meta
  ↓ silnik EVM (aplikacja)
WYNIK – rewizja wskaźników EV                                                       ev
  ↓
PLIKI (Excel, Cobra) i widoki dla BI
  ↓
ZAMROŻENIE – przebieg zamykający okres
```

---

## 2. Schematy

| Schemat | Zawartość |
|---|---|
| `meta` | importy, wersje plików (bez treści – migracja 008), definicje źródeł, parsery, projekty, przebiegi, etapy, rewizje, problemy, dziennik zdarzeń, role, wersja aplikacji i schematu |
| `can` | dane kanoniczne – jedna stała tabela `can.Row` ze slotami typowanymi dla wszystkich parserów (migracja 007) |
| `dict` | słowniki globalne i projektu – z historią |
| `ev` | koszt per WP, zaawansowanie, wyniki EV |

Struktura P1S nie jest kopiowana do bazy PZL-EV – aplikacja (docelowo procedury) czyta ją z `PZLPROD` (rozdz. 3.4).
Strona P1S projektu to `Legacy WBS` węzłów nakładki Performance Objectives i poddrzewa `LOG.WBS` pod nimi
(`docs/performance-objectives.md`, rozdz. 2).

**Nazwy w bazie:** warstwy z tabeli wyżej są częścią nazwy tabeli, a nie osobnymi schematami. Wszystkie obiekty są w
jednym schemacie z konfiguracji, z sygnaturą przed nazwą: `[<Schema>].[<Sygnatura><WARSTWA>_<Nazwa>]`, np.
`meta.ImportBatch` → `[FINOP].[PZLEV_META_ImportBatch]` (TEST: baza `PZLTEST`, schemat `FINOP`, sygnatura `PZLEV_`;
`docs/architektura.md`, rozdz. 8). Kolumny – nazwy angielskie jak w kodzie (słowa zastrzeżone SQL zastąpione:
`UserName`, `DataRows`, `CheckName`); tabele zapisu mają `DbLogin DEFAULT ORIGINAL_LOGIN()` – bez niej: `CAN_Row`,
`CAN_ActualsBalance`, `META_Run`, `META_RunPin`, `RUN_Balance`, `RUN_Issue` (kto – w imporcie albo w przebiegu:
`CreatedBy`, `StartedBy`); czas –
`DATETIMEOFFSET(7)`, kwoty – `DECIMAL(28,8)`; tabele z historią – `RecordedAt/By`, `SupersededAt/By`, unikalność
kluczy wśród bieżących wersji (indeksy filtrowane).

---

## 3. Historia i wersjonowanie

### 3.1 Zasada

Dane zapisane przez PZL-EV nie są nadpisywane ani usuwane. Zmiana tworzy nowy wpis, a poprzedni zostaje
w historii z informacją, kto i kiedy go zastąpił.

Wyjątek – migracje, które zmieniły wartości w miejscu, w całej historii: 033 (zaokrąglenie `Rate`, `PoolId`,
`BurdenGroup` → `BurdenGroupId` z usunięciem kolumny) i 038/039 (`CostKind`).

### 3.2 Dwie osie czasu

| Oś | Znaczenie | Kto ustala |
|---|---|---|
| **biznesowa** | `ValidFrom` / `ValidTo` – od kiedy do kiedy obowiązuje wartość (np. stawka na rok) | użytkownik |
| **techniczna** | moment zapisu i moment zastąpienia wpisu (kto, kiedy) | baza – tabele temporalne SQL Server albo kolumny zapisu / zastąpienia |

- Zmiana obowiązującej wartości = zamknięcie `ValidTo` starego wpisu i nowy wpis.
- „Usunięcie” = zamknięcie okresu obowiązywania.
- Przebieg czyta dane według osi technicznej (rozdz. 4) – dzięki temu późniejsza edycja, także wstecznej
  wartości na osi biznesowej, nie zmienia wyniku już obliczonego.

### 3.3 Co jest wersjonowane

| Dane | Sposób |
|---|---|
| Słowniki globalne i projektu, w tym harmonogram i budżet | historia na obu osiach (rozdz. 3.2) |
| Nakładka Performance Objectives, zakres projektu i definicje źródeł | historia na osi technicznej |
| Pliki źródłowe | każda nowa treść pliku (hash SHA-256) to nowa wersja z czasem importu; tylko dopisywanie |
| Dane kanoniczne | oznaczone wersją parsera; ponowne przetworzenie nowym parserem tworzy nową wersję danych z czasem zapisu |
| Zaawansowanie w przebiegu | każda zmiana wartości to nowy wpis z pochodzeniem (PRODUKCJA, ANALITYK, CAM), kto i kiedy |

### 3.4 Źródła zewnętrzne

- `PZLPROD.LOG.WBS` jest **przyrostowy** (wiersze tylko dochodzą) – czytany bezpośrednio, bez kopiowania.
- Zaawansowanie z produkcji jest pobierane w etapie P5 i zapisywane w przebiegu (`ev.Zaawansowanie`) –
  tylko dla WP projektu. Od tego momentu jest danymi przebiegu.

---

## 4. Przebieg i rewizja (kontrakt przebiegu)

### 4.1 Przebieg

- **Przebieg** = projekt + tydzień. Okres (miesiąc), do którego należy tydzień, i to, czy tydzień zamyka
  okres, wynikają z kalendarza okresów (`docs/slowniki.md`, rozdz. 2). Jeden przebieg na projekt i tydzień –
  wymuszane w bazie.
- Identyfikator: `R-<Projekt>-<RRRR-MM>-T<NN>` (numer tygodnia z kalendarza okresów).
- Przebieg jest trwałym obiektem w bazie – może trwać dni, a etapy mogą wykonywać różne osoby z różnych
  komputerów.

### 4.2 Znacznik stanu (przypięcie)

Znacznik stanu to moment zapisany w przebiegu w etapie P1. Przebieg czyta wszystkie dane wewnętrzne
**w stanie na ten moment** (oś techniczna):

- najnowszą wersję każdego pliku źródłowego (lokalizacja + nazwa) zaimportowaną do tego momentu, w wersji kanonicznej
  obowiązującej w tym momencie,
- słowniki globalne i słowniki projektu (w tym harmonogram i budżet),
- nakładkę Performance Objectives (zakres projektu, w tym `Legacy WBS`).

Ponowne przypięcie zapisuje nowy znacznik (`docs/pipeline-fazy.md`, rozdz. 5).

**Stan obecny (migracje 027/028):** przypięcie (`META_RunPin`) zapisuje tylko zagregowane dane ACTUALS projektu
(`RUN_Balance`); nakładkę i słowniki (w tym Cost Category) przebieg czyta w stanie bieżącym – bez zapisu stanu
słowników. Rewizji jeszcze nie ma (rozdz. 4.3 – stan docelowy, F8).

### 4.3 Rewizja

Rewizja to jedno obliczenie EV przebiegu (etap P8). Zapisuje:

| Element | Znaczenie |
|---|---|
| numer | R1, R2… w obrębie przebiegu |
| znacznik stanu | obowiązujący w przebiegu w chwili obliczenia |
| data stanu (Status Date) | data, na którą liczony jest wynik (`docs/ev-obliczenia.md`, rozdz. 2) |
| wersja silnika EVM i wersja aplikacji | silnik działa w aplikacji (`docs/architektura.md`, rozdz. 6) |
| zaawansowanie | stan wartości zaawansowania przebiegu w chwili obliczenia |
| kto, kiedy | konto AD |

- Ponowne obliczenie tworzy nową rewizję; poprzednie zostają.
- **Odtwarzalność:** ten sam znacznik stanu i ta sama wersja silnika dają ten sam wynik. Zmiana słowników,
  nakładki, budżetu czy harmonogramu po obliczeniu nie zmienia zapisanych rewizji.
- **Zamrożenie:** zatwierdzony przebieg zamykający okres jest tylko do odczytu; jego zatwierdzona rewizja jest
  formalnym wynikiem okresu i podstawą porównań kolejnych okresów.

### 4.4 Założenie o źródłach zewnętrznych

Odtwarzalność opiera się na przyrostowości `PZLPROD.LOG.WBS` (rozdz. 3.4).
Zmiana istniejących wierszy w `LOG.WBS` nie jest wykrywana (`docs/architektura.md`, rozdz. 10).

---

## 5. Główne encje

| Encja | Opis |
|---|---|
| `meta.ImportBatch` | uruchomienie importu: kto, komputer, wersja aplikacji, liczniki, status |
| `meta.SourceLocation` | lokalizacje RABIT: nazwa, ścieżka, aktywna (`docs/zrodla-danych.md`, rozdz. 3) |
| `meta.SourceFile` | wersja pliku (klucz SHA-256): lokalizacja, nazwa, kod źródła, data raportu (modyfikacja w RABIT), kolumny, sygnatura kolumn, liczba wierszy, import, stan danych kanonicznych (utworzone albo powód braku) |
| `meta.SourceFileSeen` | decyzja dla każdego pliku w każdym imporcie (`docs/pipeline-fazy.md`, G1) |
| `meta.SourceDefinition` | definicja źródła (`docs/zrodla-danych.md`, rozdz. 2) |
| `can.Row` | dane kanoniczne wszystkich parserów: wersja pliku, numer wiersza, parser i jego wersja, sloty typowane; pole parsera ma stały slot (`meta.Parser`). Koszty rzeczywiste – parser `ACTUALS` (wspólny dla źródeł `ACTUALS_*`, odróżnianych wersją pliku). Wszystkie wersje plików zostają: najnowsza – dane bieżące dla użytkowników, starsze – do porównań |
| `dict.*` | słowniki (`docs/slowniki.md`). Wiersz słownika ma identyfikator wiersza logicznego i kolejne wersje (kto i kiedy zapisał, kto i kiedy zastąpił); w MS SQL – osobna tabela z typowanymi kolumnami na słownik (rozdz. 5.1) |
| `meta.Projekt` | kod, nazwa, typ (SAC / CAS / wewnętrzny) |
| `meta.PerformanceObjective` | węzły nakładki kontraktu projektu: element WBS CES, poziom, rodzic, wirtualny węzeł, atrybuty, historia (`docs/performance-objectives.md`) – wyznacza zakres projektu |
| `meta.Przebieg` | projekt, tydzień, okres, czy zamykający, znacznik stanu, status, zamrożenie |
| `meta.EtapPrzebiegu` | przebieg, etap, status, kto, kiedy |
| `meta.Rewizja` | elementy z rozdz. 4.3 |
| `meta.Problem` | wynik kontroli: poziom, obszar, element, opis, stan (`docs/pipeline-fazy.md`, rozdz. 1.3) |
| `meta.Zdarzenie` | dziennik: przebieg, kto (AD), kiedy, co |
| `meta.Rola` | role i przypisane grupy AD (`docs/uprawnienia.md`) |
| `ev.KosztWP` | przebieg, WP, okres, kwoty, pochodzenie – wynik łączenia źródeł (P3) |
| `ev.Zaawansowanie` | przebieg, WP, wartość, pochodzenie, wartość z produkcji (podpowiedź dla CAM), kto, kiedy |
| `ev.Wynik` | rewizja, poziom (WP, CAM, `PROJORG`, projekt), wskaźniki EV (`docs/ev-obliczenia.md`) |

`meta.Przebieg`, `meta.EtapPrzebiegu`, `meta.Rewizja`, `meta.Rola` i `ev.*` – encje docelowe, jeszcze bez tabel;
przebieg dziś – `META_Run`, `META_RunPin` (rozdz. 5.1).

Schemat bazy powstaje skryptami migracyjnymi (`docs/architektura.md`, rozdz. 8). Każdy skrypt w jednej transakcji
wykonuje tylko aplikacja (Diagnostyka → Migracja, pytanie przy starcie). Ręcznie (sqlcmd) uruchamia się tylko skrypty
oczekujące (brak numeru w `META_SchemaVersion`) – ponowne wykonanie wszystkich odtworzyłoby tabele usunięte przez
późniejsze skrypty (np. zakładane w 001 i 005).

### 5.1 Tabele etapu 1 – migracja `sql/mssql/001_etap1_import_slowniki_projekty.sql`

Bez metodologii EV; kolejne tabele dochodzą kolejnymi migracjami.

| Tabela (sygnatura `PZLEV_`) | Zawartość |
|---|---|
| `META_SchemaVersion`, sekwencja `META_LogicalId` | wersja schematu i minimalna wersja aplikacji; identyfikatory wierszy logicznych |
| `META_SourceDefinition`, `META_SourceLocation` | definicje źródeł (kod, prefiks, typ raportu, parser, aktywna) i lokalizacje RABIT z historią |
| `META_Parser` (004) | parsery i ich pola (kolumna w pliku, typ, długość, wymagane, slot w `CAN_Row` – 007) z historią |
| `META_ImportBatch`, `META_SourceFile`, `META_SourceFileSeen` | importy, wersje plików (SHA-256), decyzje dla plików |
| `CAN_Row` (007) | dane kanoniczne wszystkich parserów: `FileId`, `RowNumber`, `ParserId`, `ParserVersion` i sloty T01–T40 (`NVARCHAR(400)`), L01–L05 (`NVARCHAR(4000)`), N01–N20 (`DECIMAL(28,8)`), I01–I10 (`INT`), D01–D10 (`DATE`); indeks klastrowy kolumnowy (clustered columnstore) – kompresja i szybkie grupowanie milionów wierszy |
| `CAN_ActualsBalance` (037) | zestawienie każdej wersji pliku ACTUALS: plik, Project definition, WBS element, Cost element (i nazwa), klasa i obiekt partnera, rok, okres, waluty; sumy `ValueObjCrcy`, `ValueRepCur`, `ValueTranCurr`, `TotalQuantity`, liczba wierszy, najpóźniejsze Created on; indeks klastrowy (Project definition, plik) – odczyty kosztów projektu bez czytania `CAN_Row` |
| `META_Journal`, `META_Problem` | dziennik zdarzeń (`meta.Zdarzenie`) i problemy (otwarte / rozwiązane: `ResolvedAt`, `ResolvedBy`, `Resolution` – migracja 006) |
| `META_Project`, `META_PerformanceObjective` | projekty (kod, nazwa, typ SAC / CAS / WEWNETRZNY) i nakładka Performance Objectives z historią |
| `DICT_Calendar`, `DICT_DepartmentRate`, `DICT_FxRate`, `DICT_CostCategory`, `DICT_Person`, `DICT_DjkRate`, `DICT_ProductionParameter` | słowniki globalne – tabela z typowanymi kolumnami na słownik; `Project` NULL = globalny (w Cost Category `Project` = zmiany w projekcie) |
| `DICT_WpCam`, `DICT_ScheduleBudget`, `DICT_WbsCategory`, `DICT_Exclusion` | słowniki projektu (F4.3; `DICT_WbsCategory` – Kategorie WBS, migracja 019); „Cost Category – zmiany w projekcie” – `DICT_CostCategory` z kodem projektu; Stawki CAS – po ustaleniu zawartości (O37) |
| `DICT_ProjectSource` (026), `DICT_ProjectSetting` (028) | słowniki projektu „Źródła danych” i „Parametry projektu” |
| `DICT_CasPool`, `DICT_CasBurdenGroup`, `DICT_CasBurdenRate`, `DICT_CasFormula` (029–034) | narzuty CAS: pule, grupy narzutowe, wskaźniki, formuły |
| `META_Run`, `META_RunPin`, `RUN_Balance`, `RUN_Issue` (027) | przebiegi (projekt + tydzień), uruchomienia (przypięcia), dane ACTUALS przypięte w uruchomieniu, wyniki kontroli przebiegu |

**Dane startowe – migracja `sql/mssql/002_dane_startowe.sql`:** definicje źródeł `ACTUALS_PAF` i `ACTUALS_CES`
(parser ACTUALS, układ kolumn – `docs/zrodla-danych.md`, rozdz. 4), aktywna lokalizacja RABIT E456659, kalendarz
okresów 2026–2027 (tygodnie ISO, okres według czwartku, ostatni tydzień okresu zamykający), Cost Category
(załącznik A, `docs/slowniki.md`). Skrypt dopisuje tylko brakujące wiersze i zapisuje wpis w dzienniku.

**Migracja `sql/mssql/003_usuniecie_plikow_niezgodnych.sql`:** usuwa wersje plików (`META_SourceFile`) zapisane do
wersji aplikacji 0.11 mimo niezgodności z definicją źródła (układ kolumn, typy wartości) wraz z ich wierszami
surowymi albo treścią i danymi kanonicznymi (gdy baza jest już po migracji 007); historia decyzji (`META_SourceFileSeen`) zostaje. Plik niezgodny z definicją źródła nie trafia do bazy
(`docs/zrodla-danych.md`, rozdz. 2).

**Migracja `sql/mssql/004_parsery.sql`:** tabela `META_Parser` (parser pilnuje układu pliku: kolumna w pliku, pole,
typ, wymagane), `CAN_Actuals` jako tabela parsera ACTUALS (kolumny pól dopuszczają NULL; nowe pola
`OriginalOrderNumber`, `Item`, `PurchaseOrderNumber`, `InvoiceNumber`) i parser ACTUALS w układzie raportu z 2026-10
(`docs/zrodla-danych.md`, rozdz. 4). Kolumny `Columns`, `Signature`, `ParserVersion` definicji źródła dopuszczają
NULL – definicja wskazuje parser. Tabelę `CAN_Actuals` usuwa migracja 007 (dane w `CAN_Row`).

**Migracja `sql/mssql/005_mapowanie_ces_p1s.sql`:** parser `MAPOWANIA` z tabelą `CAN_MappingReport` (raport mapowań
CES ↔ P1S z Excela) i tabela korekt mapowania `DICT_MappingCorrection`. Tabelę `CAN_MappingReport` usuwa migracja 007,
parser – 009, `DICT_MappingCorrection` – 025.

**Migracja `sql/mssql/006_problemy_rozwiazywanie.sql`:** kto, kiedy i jak rozwiązał problem (automatycznie – kolejny
import; ręcznie – Pulpit), indeks otwartych problemów; problemy importów wcześniejszych niż
ostatni zakończony import – rozwiązane (`docs/pipeline-fazy.md`, rozdz. 1.3).

**Migracja `sql/mssql/007_sloty_danych_kanonicznych.sql`:** dane kanoniczne w jednej stałej tabeli `CAN_Row`, bo
w PROD aplikacja nie może zakładać ani zmieniać tabel (nowy parser albo nowe pole to tylko zapis parsera). Pole
parsera dostaje slot swojego rodzaju (tekst do 400 znaków – T, dłuższy tekst – L, kwota / liczba – N, liczba
całkowita – I, data – D) przy zapisie parsera i zachowuje go we wszystkich wersjach; slot użyty przez jakąkolwiek
wersję parsera nie jest przydzielany innemu polu (zapisane dane nie zmieniają znaczenia), zmiana rodzaju pola jest
błędem (dodaje się nowe pole). Limity na parser: 40 / 5 / 20 / 10 / 10 pól. Migracja przydziela sloty polom
istniejących parserów (kolejność pól, osobno dla rodzaju), przenosi dane `CAN_<Tabela>` do `CAN_Row` (kontrola
liczby wierszy) i usuwa stare tabele; wiersze `STG_RawRow` przenosi do tabeli treści plików `META_SourceFileContent`
(usuniętej migracją 008) i usuwa tabelę. Wymaga SQL Server 2016+ i poziomu zgodności bazy co najmniej 130 – skrypt sprawdza go na początku
(Diagnostyka pokazuje wersję serwera i poziom zgodności). Zapytania raportów i grupowania czytają `CAN_Row` przez
sloty parsera (`SqlCanonical`; docelowo widoki – F10.2; z zewnątrz – procedura `CAN_LatestImport`, migracja 011).

**Migracja `sql/mssql/008_bez_tresci_plikow.sql`:** usuwa tabelę `META_SourceFileContent` z treścią plików.
Raport RABIT to zawsze pełne dane, a po udanym imporcie są one w `CAN_Row` – kopia pliku nie jest
potrzebna. Wersje plików (`META_SourceFile`) i dane kanoniczne zostają; wpis w dzienniku podaje liczbę i rozmiar
usuniętych treści.

**Migracja `sql/mssql/009_raport_mapowan_slownik.sql`:** tabela `DICT_MappingReport` (słownik globalny raportu mapowań
CES ↔ P1S); zamyka bieżące wersje definicji źródeł z parserem `MAPOWANIA` i samego parsera (historia zostaje).
Tabelę `DICT_MappingReport` usuwa migracja 025.

**Migracja `sql/mssql/010_stawki_mpk.sql`:** stawki wydziałów według MPK (`docs/slowniki.md`, rozdz. 2, 5.4) –
`DICT_DepartmentRate` dostaje kolumnę `CostCenter` (MPK, wymagana), `Department` (opis MPK) i `Overhead` dopuszczają
brak wartości, unikalny klucz bieżących wersji: `Project` + `CostCenter` + `Year`. Migracja przepisuje MPK
istniejących wierszy z kolumny `Department`; wpis w dzienniku podaje ich liczbę. Wymaga aplikacji 0.20.0.

**Migracja `sql/mssql/011_ostatni_import_parsera.sql`:** procedura `[<Schema>].[<Sygnatura>CAN_LatestImport]` – ostatni
import parsera dla raportów i narzędzi spoza aplikacji (Excel, Power BI, SQL). Zwraca dane kanoniczne parsera
z najnowszej wersji każdego pliku źródłowego (lokalizacja + nazwa; największa data raportu `ModifiedAt`, potem czas
importu; tylko wersje z utworzonymi danymi kanonicznymi): kolumny pliku `SourceCode`, `Location`, `FileName`,
`ModifiedAt`, `ImportedAt`, `BatchId`, `FileId`, `RowNumber`, `ParserVersion` i pola bieżącej wersji parsera pod
własnymi nazwami (sloty według `META_Parser.Fields`). Zapytanie składane jest przy wywołaniu, więc ta sama procedura
obsługuje każdy parser, także dodany później – bez zakładania widoków (aplikacja i użytkownicy nie wykonują DDL).
Parametry: `@Parser` (kod, np. `ACTUALS` – wszystkie jego źródła, `ACTUALS_PAF` i `ACTUALS_CES`), opcjonalnie
`@SourceCode` (jedno źródło) i `@AsOf` (stan na moment – pliki zaimportowane do tej chwili). Nocny import w kilku
partiach daje komplet: każdy plik w wersji z ostatniej partii, która go przyniosła; plik, którego RABIT nie
wygenerował ponownie, zostaje w poprzedniej wersji (O38); w trakcie importu wynik łączy pliki nowe i poprzednie
(kolumny `ImportedAt`, `BatchId`). Procedura działa z prawami wywołującego: potrzebny `EXECUTE` (migracja nadaje go roli
`pzl_ev_user`, gdy rola istnieje) i `SELECT` na `META_Parser`, `META_SourceDefinition`, `META_SourceFile`, `CAN_Row`
– jak aplikacja. `EXECUTE AS OWNER` nie jest używane: właścicielem schematu bywa grupa AD, której nie da się
„wykonać jako” (błąd 15517 przy migracji). Przykład:
`EXEC [FINOP].[PZLEV_CAN_LatestImport] @Parser = 'ACTUALS', @SourceCode = 'ACTUALS_CES';`. Wymaga aplikacji 0.21.0.

**Migracja `sql/mssql/012_ostatni_import_projektu.sql`:** `CAN_LatestImport` dostaje parametr `@Project` (kod
projektu PZL-EV) – tylko wiersze, których `Project definition` jest w nakładce Performance Objectives projektu
(bieżące wersje; parser musi mieć pole `ProjectDefinition`). Pełny zrzut ACTUALS (ok. 1,5 mln wierszy, 35 kolumn) to
minuty samego przesyłania i wyświetlania; dane jednego projektu – sekunda. Przykład:
`EXEC [FINOP].[PZLEV_CAN_LatestImport] @Parser = 'ACTUALS', @Project = 'M28';`. Wymaga aplikacji 0.23.0.

**Migracja `sql/mssql/013_raport_kosztow_projektu.sql`:** funkcja `CAN_LatestFiles(@Parser, @SourceCode, @AsOf)` –
najnowsza wersja każdego pliku parsera (wspólna dla `CAN_LatestImport` i raportów) – oraz procedura
**`REP_ProjectCosts @Project, @Value = 'ValueObjCrcy'`** – raport kosztów projektu z ostatniego importu ACTUALS:
- wiersz: `Project definition` z nakładki projektu × `Cost Element` – tylko elementy z kosztami projektu, bez wierszy
  pasujących do słownika projektu „Wykluczenia” (każde wypełnione pole wykluczenia musi się zgadzać);
- kolumny: `Grouping` (nazwa korzenia nakładki nad Project definition), `Project definition`, `Project definition
  description` (nazwa elementu nakładki = Project definition), `Cost Element`, `Cost Elem. Descr.` i `Cost grouping`
  (słownik Cost Category: zmiany projektu przed globalnym; opis bez wpisu – z danych), `Period MM/RRRR` (ostatni okres
  w danych ACTUALS), kolumny lat projektu od najnowszego;
- kwota: pole `@Value` parsera ACTUALS – domyślnie `ValueObjCrcy` (waluta obiektu, PLN), np. `ValueRepCur`.
Pomiar: 1,5 mln wierszy ACTUALS – raport projektu w ok. 0,2–0,7 s. Ekran Projekt, zakładka Raporty: „Raport kosztów
(Excel)”. Przykład: `EXEC [FINOP].[PZLEV_REP_ProjectCosts] @Project = 'M28';`.

**Migracja `sql/mssql/014_definicje_projektu.sql`:** Project definition projektu (`CAN_LatestImport @Project`,
`REP_ProjectCosts`) to Project definition **i WBS element** węzłów nakładki – element CES dodany ręcznie nie ma
Project definition, a najwyższy element CES to sam Project definition (np. `4D06WP`). Wymaga aplikacji 0.23.1.

**Migracja `sql/mssql/015_osoby_z_hr.sql`:** `DICT_Person` dostaje kolumny z HR (`FirstName`, `LastName`, `Email`,
`CostCenter`, `DepartmentShort`, `DepartmentName`, `Position`, `Division`, `IsManager`, `Pernr`); kluczem jest USRID
(kolumna `AdAccount`). Osoby dodaje się z PZLHRPROD (`HR.ORG`) – „Dodaj z HR” (`docs/funkcjonalnosc.md`, F03). Wymaga aplikacji 0.24.0.

**Migracja `sql/mssql/016_koszty_po_elementach.sql`:** procedura `REP_ProjectCostsByElement @Project, @Value =
'ValueObjCrcy'` – koszt rzeczywisty (ACWP) projektu po WBS elemencie CES: suma kwoty z ostatniego importu ACTUALS
(`CAN_LatestFiles`; zrzut zawsze zawiera całość, więc to koszt narastająco), Project definition projektu jak
w `REP_ProjectCosts`, bez wykluczeń projektu; wiersz bez WBS elementu – pod Project definition. Kolumna ACWP tabeli
struktury (`docs/performance-objectives.md`, rozdz. 4.2). Wymaga aplikacji 0.28.0.

**Migracja `sql/mssql/017_cost_element_rozliczeniowy.sql`:** `DICT_CostCategory.IsSettlement` – znacznik
„Rozliczeniowy” (cost element rozliczenia SAP, np. 0091902551 „PZL Invent Cost Set”). `REP_ProjectCostsByElement`
pomija rozliczenie wychodzące: wiersze z cost elementem rozliczeniowym (słownik efektywny projektu), których klasa
obiektu partnera nie jest „Profit analysis”; rozliczenie przychodzące („Profit analysis”) zostaje w ACWP. Bez tego
suma zrzutu na elemencie WBS rozliczanym co miesiąc wynosi 0. Wymaga aplikacji 0.29.0. Wyjątek „Profit analysis”
zniesiony w 043.

**Migracja `sql/mssql/018_cost_elementy_z_actuals.sql`:** procedura `CAN_ActualsCostElements @Value` – cost elementy
całego ostatniego importu ACTUALS (nazwa z danych, liczba wierszy, suma kwoty) dla „Uzupełnij z ACTUALS” w słowniku
Cost Category (`docs/slowniki.md`, rozdz. 6).

**Migracja `sql/mssql/019_kategorie_wbs.sql`:** tabela `DICT_WbsCategory` – słownik projektu „Kategorie WBS” (kategoria,
opis; klucz projekt + kategoria). Kolumna `DICT_WpCam.CostCategory` przyjmuje kategorię z tego słownika
(`docs/slowniki.md`, rozdz. 3). Wymaga aplikacji 0.31.0.

**Migracja `sql/mssql/020_bac_kosztowy.sql`:** kolumna `DICT_ScheduleBudget.Bac` – BAC, budżet kosztowy WP (kwota) obok
BAC HOURS i BAC MATERIAL. Wymaga aplikacji 0.32.0.

**Migracja `sql/mssql/021_cam_opcjonalny.sql`:** `DICT_WpCam.Cam` dopuszcza NULL – WP wskazuje się najpierw, CAM
uzupełnia później (gotowość projektu blokuje przebieg z WP bez CAM). Wymaga aplikacji 0.34.0.

**Migracje `sql/mssql/022_produkcja_slowniki.sql`, `023_dane_produkcji.sql`:** słowniki globalne „Wskaźniki DJK”
(`DICT_DjkRate`) i „Parametry produkcji” (`DICT_ProductionParameter`) z danymi startowymi raportu S70MR (023 – dane startowe,
dopisuje brakujące pozycje). Wymaga aplikacji 0.38.0.

**Migracja `sql/mssql/024_bez_slownika_elementow_wirtualnych.sql`:** usuwa pustą tabelę `DICT_VirtualP1s` (słownik
„Elementy wirtualne P1S”) – wydzielenia godzin to węzły wirtualne nakładki z kodem `ELEMENT_P1S.NAZWA.CPLGR`;
tabela z wpisami zostaje (do przeniesienia ręcznie). Wymaga aplikacji 0.40.0.

**Migracja `sql/mssql/025_bez_mapowania_ces_p1s.sql`:** usuwa tabele `DICT_MappingReport` i `DICT_MappingCorrection` –
strona P1S projektu to `Legacy WBS` z Performance Objectives i poddrzewa `LOG.WBS` (`docs/performance-objectives.md`,
rozdz. 2). Wymaga aplikacji 0.41.0.

**Migracja `sql/mssql/026_zrodla_projektu.sql`:** tabela `DICT_ProjectSource` – słownik projektu „Źródła danych”
(definicje źródeł importowane dla projektu, `docs/slowniki.md`, rozdz. 3). Wymaga aplikacji 0.42.0.

**Migracja `sql/mssql/027_przebiegi.sql`:** przebiegi projektu – `META_Run` (projekt + tydzień, kod
`R-<Projekt>-<RRRR-MM>-T<NN>`, jeden na projekt i tydzień), `META_RunPin` (kolejne uruchomienia: kto, kiedy, stan,
import, liczba wierszy, koszt brutto, saldo, błędy, ostrzeżenia), `RUN_Balance` (dane ACTUALS projektu w chwili
przypięcia: element WBS × cost element × rok × okres × rozliczenie wychodzące × waluta obiektu – wiersze, kwota,
najpóźniejsze `Created on`), `RUN_Issue` (wyniki kontroli) i procedura `RUN_PinBalance @Project, @PinId`
(`docs/pipeline-fazy.md`, rozdz. 4.1). Wymaga aplikacji 0.43.0.

**Migracja `sql/mssql/028_konfiguracja_projektu.sql`:** tabela `DICT_ProjectSetting` – słownik projektu „Parametry
projektu” (parametr → wartość; klucz projekt + parametr; `docs/slowniki.md`, rozdz. 3). Parametr „Pole kwoty ACTUALS”
podaje `@Value` procedur `REP_ProjectCosts`, `REP_ProjectCostsByElement` i `RUN_PinBalance` (brak – `ValueObjCrcy`).
`RUN_PinBalance @Project, @PinId, @Value` – kwota z pola `@Value`, waluta z pola waluty tej kwoty (`ObjectCurrency`,
`ReportCurrency`, `TransactionCurrency`); `META_RunPin.AmountField` – pole kwoty użyte w przypięciu. Wymaga aplikacji 0.44.0.

**Migracja `sql/mssql/029_narzuty_cas.sql`:** narzuty CAS raportu kosztów CES (`docs/slowniki.md`, rozdz. 6.1):
`DICT_CostCategory.BurdenGroup` – „Grupa narzutowa CAS” elementu kosztowego; `DICT_CasBurdenGroup` – „Grupy narzutowe
CAS” (od migracji 031 – grupa i opis); `DICT_CasBurdenRate` – „Wskaźniki narzutu CAS”
(pula, `ValidFrom`, `ValidTo`, `Rate DECIMAL(38,18)`; klucz projekt + pula + od). Raport liczy aplikacja
(`CasCostReport`) z wyniku `REP_ProjectCosts` – bez nowej procedury. Wymaga aplikacji 0.46.0.

**Migracja `sql/mssql/030_dane_narzuty_cas.sql`** (dane startowe; od 034 tylko zapis wersji): 10 grup narzutowych i wskaźniki 2025 i 2026 z arkusza
finansów; grupa narzutowa elementów kosztowych arkusza w globalnym Cost Category – nowa wersja pozycji bez grupy
(grupa ustawiona wcześniej zostaje).

**Migracja `sql/mssql/031_formuly_narzutow_cas.sql`:** formuły narzutów CAS jako dane (`docs/slowniki.md`, rozdz. 6.1):
`DICT_CasPool` – „Pule narzutu CAS” (pula, `SortOrder`, `Kind`: Koszt / FCCOM); `DICT_CasBurdenFormula` – „Formuły
narzutów CAS” (grupa, pula, `Basis` – składniki połączone „ + ”; klucz projekt + grupa + pula); z `DICT_CasBurdenGroup`
usunięte kolumny pul i `BaseInGsGa` (029) – zostają grupa i opis. Wymaga aplikacji 0.47.0.

**Migracja `sql/mssql/032_dane_formuly_narzutow_cas.sql`** (dane startowe): wcześniej pule i formuły z nazwami; od 034
tylko zapis wersji (dane startowe narzutów CAS – 034).

**Migracja `sql/mssql/033_narzuty_cas_identyfikatory.sql`:** narzuty CAS powiązane identyfikatorem (RowId), nie nazwą:
`DICT_CasBurdenRate.PoolId` zamiast `Pool`, `DICT_CostCategory.BurdenGroupId` zamiast `BurdenGroup` (nazwy przekształcone
na identyfikatory – nazwa szukana także w historii wierszy; wskaźnik puli spoza słownika – zamknięty), `DICT_CasFormula`
(grupa, pula, formuła z klocków: `base`, `p:<pula>`, `r:<pula>`, `+ - * / ( )`) zamiast `DICT_CasBurdenFormula`; wskaźniki
zaokrąglone do 15 cyfr znaczących (jak w Excelu). Wymaga aplikacji 0.48.0.

**Migracja `sql/mssql/034_dane_narzuty_cas.sql`** (dane startowe): 17 pul, 10 grup, wskaźniki 2025–2026, formuły grup
i grupa narzutowa elementów kosztowych z arkusza finansów – pule i grupy dopasowane po nazwie bieżącej albo z historii
(przemianowane przez użytkownika nie są dodawane ponownie); formuły grupy tylko, gdy grupa nie ma żadnej formuły.

**Migracja `sql/mssql/035_daty_rzeczywiste_wp.sql`:** kolumny `DICT_ScheduleBudget.ActualStart`, `ActualEnd` – daty
rzeczywiste WP („Actual Start”, „Actual Koniec” w „Harmonogram i budżet”) nadpisujące daty z raportu produkcyjnego
(`docs/performance-objectives.md`, rozdz. 4.2). Wymaga aplikacji 0.49.0.

**Migracja `sql/mssql/036_koszty_i_godziny_po_elementach.sql`:** `REP_ProjectCostsByElement @Project, @Value, @Hours` –
parametr `@Hours` (pole godzin parsera ACTUALS, np. `TotalQuantity`) dodaje kolumnę `Hours` (suma z tych samych wierszy
co kwota); struktura projektu czyta ACWP i Actual DLH jednym wywołaniem zamiast dwóch. Wymaga aplikacji 0.50.0.

**Migracja `sql/mssql/037_zestawienie_actuals.sql`:** zestawienie ACTUALS po Project definition – tabela
`CAN_ActualsBalance` i procedura `CAN_BuildActualsBalance @FileId` (grupuje tylko wiersze jednej wersji pliku). Import
liczy zestawienie po kontroli w bazie, w jednej transakcji ze zmianą stanu pliku na „utworzone” – plik jest widoczny dla
odczytów razem z zestawieniem; każdy import pisze tylko pod swoim `FileId`, więc równoległe importy (pełny i źródeł
projektów, o dowolnej porze) się nie blokują; wycofanie pliku usuwa też jego zestawienie. `REP_ProjectCostsByElement`,
`REP_ProjectCosts`, `RUN_PinBalance` i `CAN_ActualsCostElements` czytają zestawienie najnowszych wersji plików
(`CAN_LatestFiles`) po Project definition nakładki projektu – niezależnie od źródła i pliku (plik z wieloma projektami,
projekt na cudzym imporcie); `CAN_LatestImport @Project` (ACTUALS) czyta z `CAN_Row` tylko pliki z Project definition
projektu (stała lista `FileId` – serwer pomija rowgroupy innych plików i starszych wersji). Kwota: `ValueObjCrcy`,
`ValueRepCur` albo `ValueTranCurr`. Migracja liczy zestawienie plików już zaimportowanych (plik po pliku). Wymaga
aplikacji 0.51.0.

**Migracja `sql/mssql/038_evm_struktury.sql`:** dane EVM struktury projektu – `DICT_WpCam.Mpk` (MPK WP, słownik „Stawki
wydziałów”), `DICT_ScheduleBudget.ForecastStart`, `ForecastEnd`, `EtcHours`, `EtcMaterial` (prognoza i wartości do poniesienia
nadpisujące produkcję), `DICT_CostCategory.CostKind` (rodzaj kosztu Labor / Material – wstępnie wg Cost Category);
`REP_ProjectCostsByElement` zwraca też ACWP Labor i Material. Wymaga aplikacji 0.52.0.

**Migracja `sql/mssql/039_rodzaj_kosztu.sql`:** rodzaj kosztu Cost Category – wartości Hours / Materials / Other (Labor → Hours,
Material → Materials); `REP_ProjectCostsByElement` liczy ACWP Hours i Materials według nowych wartości (reszta – ACWP Other).
Wymaga aplikacji 0.53.0.

**Migracja `sql/mssql/040_dane_rodzaj_kosztu.sql` (dane startowe):** konta 0051105550, 0051102550, 0051103550 – rodzaj
Materials w globalnym Cost Category (pozycja bez rodzaju – nowa wersja; brakująca – dodana jako Direct Materials z grupą
narzutową jak 0051105550; rodzaj ustawiony wcześniej zostaje).

**Migracja `sql/mssql/041_waluta_budzetu.sql`:** `DICT_ScheduleBudget.Currency` – kod waluty projektu zapisywany przy
wpisaniu kwot „Harmonogram i budżet” (`BAC MATERIAL`, `BAC`, `ETC MATERIAL`); puste – waluta projektu. Wymaga aplikacji
0.54.0.

**Migracja `sql/mssql/042_bez_waluty_transakcji.sql`:** zestaw walutowy ACTUALS – tylko `ValueObjCrcy` i `ValueRepCur`:
`REP_ProjectCostsByElement`, `REP_ProjectCosts`, `RUN_PinBalance` odrzucają `ValueTranCurr` (THROW 50014),
`CAN_ActualsCostElements` go nie sumuje; „Parametry projektu” z `ValueTranCurr` – nowa wersja z `ValueObjCrcy`. Kolumna
`CAN_ActualsBalance.ValueTranCurr` zostaje (dane źródła). Wymaga aplikacji 0.54.0.

**Migracja `sql/mssql/043_rozliczeniowe_poza_kosztami.sql`:** cost element „Rozliczeniowy” (słownik efektywny projektu)
nie wchodzi do kosztów bez względu na klasę obiektu partnera – rozliczenie elementu WBS do segmentu rentowności (CO-PA)
ma partnera „Profit analysis” i zostawało w ACWP jako ujemny koszt (decyzja 2026-10-15): `REP_ProjectCostsByElement`
(ACWP, praca, materiał, Actual DLH struktury) i `REP_ProjectCosts` (raport kosztów CES – dotąd bez filtra rozliczenia)
pomijają te wiersze, `RUN_PinBalance` oznacza je `IsSettlement`. Wymaga aplikacji 0.55.0.

`META_SourceFile.CanonicalStatus`: „utworzone” – dane kanoniczne zapisane i sprawdzone (widoczne dla odczytów),
„w toku” – zapis wierszy trwa (odczyty pomijają plik; po awarii wpis i wiersze są usuwane), inna wartość – powód
braku danych (źródło bez parsera).

Aplikacja zapisuje do tabel importu, konfiguracji importu, słowników globalnych i projektu, projektów i nakładki
Performance Objectives, przebiegów, dziennika i problemów. Blokady importu – pliki na dysku sieciowym
(`docs/pipeline-fazy.md`, rozdz. 1.4).

