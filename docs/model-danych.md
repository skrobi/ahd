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

Struktura P1S i raport mapowań nie są kopiowane do bazy PZL-EV – procedury czytają je z `PZLPROD`
(rozdz. 3.4).

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

- `PZLPROD.LOG.WBS` i raport mapowań SAP↔CES są **przyrostowe** (wiersze tylko dochodzą). Są czytane
  bezpośrednio, bez kopiowania.
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

Odtwarzalność opiera się na przyrostowości `PZLPROD.LOG.WBS` i raportu mapowań (rozdz. 3.4). Zmiana
istniejących wierszy w tych źródłach nie jest wykrywana (`docs/architektura.md`, rozdz. 10).

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
| `dict.*` | słowniki (`docs/slowniki.md`) i korekty mapowania (`docs/mapowanie-ces-p1s.md`, rozdz. 8). Wiersz słownika ma identyfikator wiersza logicznego i kolejne wersje (kto i kiedy zapisał, kto i kiedy zastąpił); w warstwie danych w pamięci – jedna tabela `dict.Entry` z wartościami kolumn według opisu słownika; układ tabel MS SQL – F10.1 |
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

Schemat bazy powstaje skryptami migracyjnymi (`docs/architektura.md`, rozdz. 8). Istniejący skrypt
`sql/mssql/001_etap1_import.sql` tworzy tabele importu (`meta.ImportBatch`, `meta.SourceFile`,
`meta.SourceFileSeen`, `stg.RawRow`).

---

## 6. Otwarte kwestie

| # | Kwestia |
|---|---|
| O32 | Retencja wierszy surowych i kanonicznych (wolumen: setki tysięcy wierszy × raporty × tygodnie) |
