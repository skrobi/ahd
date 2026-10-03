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
ŹRÓDŁA – RABIT (SAP CES), PZLPROD (struktura P1S, raport mapowań, zaawansowanie z produkcji), pliki CAM
  ↓ import (pipeline G1)
SUROWE – wiersze plików bez zmian, z pochodzeniem                                   stg
  ↓ parser według definicji źródła
KANONICZNE – dane typowane według definicji źródła                                  can
  ↓
SŁOWNIKI, MAPOWANIE, HARMONOGRAM I BUDŻET                                           dict
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
| `meta` | importy i wersje plików, definicje źródeł, projekty, przebiegi, etapy, rewizje, problemy, dziennik zdarzeń, role, wersja aplikacji i schematu |
| `stg` | surowe wiersze plików źródłowych |
| `can` | dane kanoniczne – osobna tabela typowana na każde źródło |
| `dict` | słowniki globalne i projektu, korekty mapowania CES ↔ P1S – z historią |
| `ev` | koszt per WP, zaawansowanie, wyniki EV |

Struktura P1S nie jest kopiowana do bazy PZL-EV – aplikacja (docelowo procedury) czyta ją z `PZLPROD` (rozdz. 3.4).
Raport mapowań SAP↔CES trafia do bazy importem pliku Excel (parser `MAPOWANIA`, tabela `can.MappingReport`) – każda
wersja pliku zostaje (`docs/mapowanie-ces-p1s.md`, rozdz. 2).

**Nazwy w bazie:** warstwy z tabeli wyżej są częścią nazwy tabeli, a nie osobnymi schematami. Wszystkie obiekty są w
jednym schemacie z konfiguracji, z sygnaturą przed nazwą: `[<Schema>].[<Sygnatura><WARSTWA>_<Nazwa>]`, np.
`meta.ImportBatch` → `[FINOP].[PZLEV_META_ImportBatch]` (TEST: baza `PZLTEST`, schemat `FINOP`, sygnatura `PZLEV_`;
`docs/architektura.md`, rozdz. 8). Kolumny – nazwy angielskie jak w kodzie (słowa zastrzeżone SQL zastąpione:
`UserName`, `DataRows`, `CheckName`); każda tabela zapisu ma `DbLogin DEFAULT ORIGINAL_LOGIN()`; czas –
`DATETIMEOFFSET(7)`, kwoty – `DECIMAL(28,8)`; tabele z historią – `RecordedAt/By`, `SupersededAt/By`, unikalność
kluczy wśród bieżących wersji (indeksy filtrowane).

---

## 3. Historia i wersjonowanie

### 3.1 Zasada

Dane zapisane przez PZL-EV nie są nadpisywane ani usuwane. Zmiana tworzy nowy wpis, a poprzedni zostaje
w historii z informacją, kto i kiedy go zastąpił.

### 3.2 Dwie osie czasu

| Oś | Znaczenie | Kto ustala |
|---|---|---|
| **biznesowa** | `ValidFrom` / `ValidTo` – od kiedy do kiedy obowiązuje wartość (np. stawka na rok, korekta mapowania) | użytkownik |
| **techniczna** | moment zapisu i moment zastąpienia wpisu (kto, kiedy) | baza – tabele temporalne SQL Server albo kolumny zapisu / zastąpienia |

- Zmiana obowiązującej wartości = zamknięcie `ValidTo` starego wpisu i nowy wpis.
- „Usunięcie” = zamknięcie okresu obowiązywania.
- Przebieg czyta dane według osi technicznej (rozdz. 4) – dzięki temu późniejsza edycja, także wstecznej
  wartości na osi biznesowej, nie zmienia wyniku już obliczonego.

### 3.3 Co jest wersjonowane

| Dane | Sposób |
|---|---|
| Słowniki globalne i projektu, w tym harmonogram i budżet | historia na obu osiach (rozdz. 3.2) |
| Korekty mapowania CES ↔ P1S | historia na obu osiach (`docs/mapowanie-ces-p1s.md`, rozdz. 8) |
| Nakładka Performance Objectives, zakres projektu i definicje źródeł | historia na osi technicznej |
| Pliki źródłowe | każda nowa treść pliku (hash SHA-256) to nowa wersja z czasem importu; tylko dopisywanie |
| Dane kanoniczne | oznaczone wersją parsera; ponowne przetworzenie nowym parserem tworzy nową wersję danych z czasem zapisu |
| Zaawansowanie w przebiegu | każda zmiana wartości to nowy wpis z pochodzeniem (PRODUKCJA, ANALITYK, CAM), kto i kiedy |

### 3.4 Źródła zewnętrzne

- `PZLPROD.LOG.WBS` jest **przyrostowy** (wiersze tylko dochodzą) – czytany bezpośrednio, bez kopiowania. Raport
  mapowań SAP↔CES jest importowany jako wersje pliku (historia w bazie, mapowanie czyta najnowszą).
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
- nakładkę Performance Objectives (zakres projektu),
- korekty mapowania CES ↔ P1S.

Ponowne przypięcie zapisuje nowy znacznik (`docs/pipeline-fazy.md`, rozdz. 5).

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
  mapowania, budżetu czy harmonogramu po obliczeniu nie zmienia zapisanych rewizji.
- **Zamrożenie:** zatwierdzony przebieg zamykający okres jest tylko do odczytu; jego zatwierdzona rewizja jest
  formalnym wynikiem okresu i podstawą porównań kolejnych okresów.

### 4.4 Założenie o źródłach zewnętrznych

Odtwarzalność opiera się na przyrostowości `PZLPROD.LOG.WBS` (rozdz. 3.4) i wersjach plików raportu mapowań.
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
| `stg.RawRow` | surowe wiersze: wersja pliku, numer wiersza, wartości |
| `can.<Źródło>` | dane kanoniczne źródła: wersja pliku, wersja parsera, kolumny typowane; dla kosztów rzeczywistych – `can.Actuals` (wspólna dla źródeł `ACTUALS_*`, odróżnianych wersją pliku) |
| `dict.*` | słowniki (`docs/slowniki.md`) i korekty mapowania (`docs/mapowanie-ces-p1s.md`, rozdz. 8). Wiersz słownika ma identyfikator wiersza logicznego i kolejne wersje (kto i kiedy zapisał, kto i kiedy zastąpił); w MS SQL – osobna tabela z typowanymi kolumnami na słownik (rozdz. 5.1) |
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

Schemat bazy powstaje skryptami migracyjnymi (`docs/architektura.md`, rozdz. 8).

### 5.1 Tabele etapu 1 – migracja `sql/mssql/001_etap1_import_slowniki_projekty.sql`

Bez metodologii EV; kolejne tabele dochodzą kolejnymi migracjami.

| Tabela (sygnatura `PZLEV_`) | Zawartość |
|---|---|
| `META_SchemaVersion`, sekwencja `META_LogicalId` | wersja schematu i minimalna wersja aplikacji; identyfikatory wierszy logicznych |
| `META_SourceDefinition`, `META_SourceLocation` | definicje źródeł (kod, prefiks, typ raportu, parser, aktywna) i lokalizacje RABIT z historią |
| `META_Parser` (004) | parsery: tabela danych kanonicznych `CAN_<Tabela>` i jej pola (kolumna w pliku, typ, długość, wymagane) z historią |
| `CAN_MappingReport` (005) | tabela parsera MAPOWANIA – wiersze raportu mapowań SAP↔CES z importu Excela |
| `DICT_MappingCorrection` (005) | korekty mapowania CES ↔ P1S (elementu i projektu CES): cel P1S, poprzednie przypisanie, uzasadnienie, `ValidFrom` / `ValidTo`, historia wersji (`docs/mapowanie-ces-p1s.md`, rozdz. 8) |
| `META_ImportBatch`, `META_SourceFile`, `META_SourceFileSeen` | importy, wersje plików (SHA-256), decyzje dla plików |
| `STG_RawRow` | wiersze surowe (JSON wartości), kompresja PAGE |
| `CAN_Actuals` | tabela parsera ACTUALS – koszty rzeczywiste `ACTUALS_*` (kolumny typowane, NULL dozwolony – wymagane wskazuje parser), kompresja PAGE; kolejne parsery mają własne tabele `CAN_<Tabela>` zakładane z aplikacji |
| `META_Journal`, `META_Problem` | dziennik zdarzeń (`meta.Zdarzenie`) i problemy |
| `META_Project`, `META_PerformanceObjective` | projekty (kod, nazwa, typ SAC / CAS / WEWNETRZNY) i nakładka Performance Objectives z historią |
| `DICT_Calendar`, `DICT_DepartmentRate`, `DICT_FxRate`, `DICT_CostCategory`, `DICT_Person` | słowniki globalne – tabela z typowanymi kolumnami na słownik; `Project` NULL = globalny (w Cost Category `Project` = zmiany w projekcie) |
| `DICT_WpCam`, `DICT_ScheduleBudget`, `DICT_Exclusion` | słowniki projektu (F4.3); Stawki CAS – po ustaleniu zawartości (O37) |

**Dane startowe – migracja `sql/mssql/002_dane_startowe.sql`:** definicje źródeł `ACTUALS_PAF` i `ACTUALS_CES`
(parser ACTUALS, układ kolumn – `docs/zrodla-danych.md`, rozdz. 4), aktywna lokalizacja RABIT E456659, kalendarz
okresów 2026–2027 (tygodnie ISO, okres według czwartku, ostatni tydzień okresu zamykający), Cost Category
(załącznik A, `docs/slowniki.md`). Skrypt dopisuje tylko brakujące wiersze i zapisuje wpis w dzienniku.

**Migracja `sql/mssql/003_usuniecie_plikow_niezgodnych.sql`:** usuwa wersje plików (`META_SourceFile`) zapisane do
wersji aplikacji 0.11 mimo niezgodności z definicją źródła (układ kolumn, typy wartości) wraz z ich wierszami
surowymi; historia decyzji (`META_SourceFileSeen`) zostaje. Od wersji 0.12 taki plik nie trafia do bazy
(`docs/zrodla-danych.md`, rozdz. 2).

**Migracja `sql/mssql/004_parsery.sql`:** tabela `META_Parser` (parser pilnuje układu pliku: kolumna w pliku, pole,
typ, wymagane), `CAN_Actuals` jako tabela parsera ACTUALS (kolumny pól dopuszczają NULL; nowe pola
`OriginalOrderNumber`, `Item`, `PurchaseOrderNumber`, `InvoiceNumber`) i parser ACTUALS w układzie raportu z 2026-10
(`docs/zrodla-danych.md`, rozdz. 4). Kolumny `Columns`, `Signature`, `ParserVersion` definicji źródła dopuszczają
NULL i nie są już używane – definicja wskazuje tylko parser. Zapis parsera
w aplikacji zakłada albo rozszerza tabelę `CAN_<Tabela>` (`SqlCanonical`) – poza migracjami, w jednej transakcji
z wersją parsera.

**Migracja `sql/mssql/005_mapowanie_ces_p1s.sql`:** parser `MAPOWANIA` z tabelą `CAN_MappingReport` (raport
mapowań importowany z Excela; definicję źródła z prefiksem nazwy pliku dodaje się w Administracji) i tabela korekt
`DICT_MappingCorrection` (bieżąca korekta elementu albo projektu CES – unikalna wśród wersji bez `SupersededAt`
i `ValidTo`; usunięcie korekty to nowa wersja z `ValidTo`).

Aplikacja zapisuje dziś do tabel importu, konfiguracji importu, słowników globalnych, korekt mapowania, dziennika i problemów;
tabele projektów i słowników projektu czekają na moduły F4. Blokada importu – plik na dysku sieciowym
(`docs/pipeline-fazy.md`, rozdz. 1.3), `sp_getapplock` razem z procedurami (F10.2).

---

## 6. Otwarte kwestie

| # | Kwestia |
|---|---|
| O32 | Retencja wierszy surowych i kanonicznych (wolumen: setki tysięcy wierszy × raporty × tygodnie) |
