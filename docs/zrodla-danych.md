# PZL-EV – Źródła danych

Zakres: źródła danych PZL-EV i ich znaczenie (kontrakt danych) – definicja źródła, raporty RABIT, struktura P1S,
zaawansowanie z produkcji, pliki CAM, reguły walidacji źródeł.

Powiązane: `docs/pipeline-fazy.md` (G1 – przebieg importu, P5–P6 – pobranie zaawansowania),
`docs/model-danych.md` (warstwy i wersjonowanie), `docs/mapowanie-ces-p1s.md` (raport mapowań SAP↔CES).

---

## 1. Lista źródeł

| Źródło | System | Dane | Pozyskanie |
|---|---|---|---|
| Raporty CES (CJI3, ZRD_KKAJ, Net Inv) | SAP CES przez RABIT | koszty rzeczywiste, zobowiązania | import plików z lokalizacji RABIT na SharePoint przez WebDAV (rozdz. 3) |
| Struktura P1S i kategoryzacja | `PZLPROD.LOG.WBS`, `LOG.WBS_DIC` (`splmcd03`) | drzewo elementów P1S, kategorie | odczyt bezpośredni, źródło przyrostowe (rozdz. 5) |
| Raport mapowań SAP↔CES | `PZLPROD` | przypisania elementów CES do P1S | odczyt bezpośredni, źródło przyrostowe (`docs/mapowanie-ces-p1s.md`, rozdz. 2) |
| Zaawansowanie z produkcji | `PZLPROD.LOG.vAHDD` (O10) | zaawansowanie godzin i materiałów | odczyt w etapie P5 (rozdz. 6) |
| Pliki CAM | Excel na dysku sieciowym | zaawansowanie od CAM w przebiegu zamykającym | import w etapie P6 (rozdz. 7) |
| Cobra | Sikorsky (projekty SAC) | budżet i harmonogram SAC | do ustalenia (O28) |

Danych w systemach źródłowych PZL-EV nie modyfikuje.

---

## 2. Definicja źródła

Każdy raport importowany do PZL-EV ma formalną definicję zapisaną w bazie (`meta.SourceDefinition`, z historią)
i utrzymywaną na ekranie Administracja (`docs/funkcjonalnosc.md`, F08).

| Element definicji | Znaczenie |
|---|---|
| Kod źródła | identyfikator, np. `ACTUALS_CES` |
| Rozpoznanie pliku | prefiks nazwy pliku; wygrywa najdłuższy pasujący prefiks, bez rozróżniania wielkości liter |
| Typ raportu | np. koszty rzeczywiste, zobowiązania, prognoza |
| Oczekiwany schemat | kolumny i ich typy; sygnatura kolumn (odcisk układu nagłówków) |
| Ziarno | co oznacza jeden wiersz (np. pozycja kosztowa elementu WBS i elementu kosztowego w okresie) |
| Klucz | kolumny jednoznacznie identyfikujące wiersz |
| Znaczenie kolumn | która kolumna to element WBS, element kosztowy, kwota, ilość, okres |
| Znaczenie okresu | koszt okresu albo narastająco; kolumna wyznaczająca okres |
| Waluta i jednostki | waluta każdej kwoty, jednostki ilości |
| Interpretacja wartości | format liczb (np. polski: spacja tysięcy, przecinek dziesiętny), znak, puste wartości |
| Reguły walidacji | kontrole wiersza i pliku z poziomem ERROR / WARNING (rozdz. 8) |
| Wersja parsera | wersja przekształcenia wierszy surowych do postaci kanonicznej |

- Prefiks służy wyłącznie do rozpoznania pliku. Znaczenie danych wynika z definicji, nie z nazwy pliku.
- Każdy prefiks to **osobne źródło** z własnym kodem i definicją, nawet gdy kilka źródeł ma identyczny układ
  kolumn (np. wszystkie `ACTUALS_*` – rozdz. 4). Układy nie są łączone w jedno źródło.
- Plik bez pasującego prefiksu nie jest importowany; po dodaniu definicji zostanie zaimportowany przy kolejnym
  imporcie.
- Wiersze surowe są zapisywane zawsze. Dane kanoniczne powstają tylko z pliku zgodnego ze schematem definicji.
- Definicje powstają dla źródeł w miarę ustalania ich zawartości (O27).

---

## 3. Pliki RABIT

- RABIT automatycznie (także pod nieobecność pracownika) zrzuca wyniki raportów SAP na SharePoint i przy
  każdym uruchomieniu **nadpisuje ten sam plik**. Historia wersji jest w bazie (wersje plików wg hash SHA-256).
- Nazwy plików są proste, bez projektu, dat i wersji (np. `ACTUALS_PAF_01.xlsx`).
- Raporty jednego zrzutu dotyczą jednego okresu.
- Jeden plik może obejmować wiele projektów (np. całe PWC). Import nie zna projektów – dane projektu wybiera
  przebieg według zakresu projektu (`docs/pipeline-fazy.md`, P1).
- Duże raporty jednego źródła są dzielone na części (`_01`, `_02`, `_03`) – każda część to osobny plik tego
  samego źródła, o układzie zgodnym z definicją. Pliki o innym prefiksie to osobne źródła (rozdz. 2).
- **Lokalizacje RABIT:** raporty mogą trafiać do wielu folderów na SharePoint. Lista lokalizacji jest
  konfiguracją importu w bazie (`meta.SourceLocation`, ekran Administracja – `docs/funkcjonalnosc.md`, F08):
  nazwa, ścieżka, aktywna. Przykład ścieżki:
  `\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659`.
- Plik jest identyfikowany przez **lokalizację i nazwę** – pliki o tej samej nazwie w różnych lokalizacjach to
  różne pliki. Rozpoznanie źródła po prefiksie (rozdz. 2) nie zależy od lokalizacji.
- **Dostęp:** lokalizacje są czytane przez WebDAV (usługa WebClient Windows, konto użytkownika). Konto
  uruchamiające import musi mieć dostęp do wszystkich aktywnych lokalizacji. Usługa WebClient ma limit
  rozmiaru pliku (domyślnie ok. 50 MB, `FileSizeLimitInBytes` zmienia administrator);
  większy plik pobiera się ręcznie w przeglądarce do folderu `00_Global\RABIT\Do_importu`
  (`docs/architektura.md`, rozdz. 7) i importuje tak samo.
- Niedostępne dla użytkownika: API REST SharePoint, synchronizacja OneDrive, eksport listy do Excela,
  pobieranie ZIP.
- Format: jeśli RABIT pozwala – CSV/TXT (brak limitu wierszy i konwersji typów przez Excel; O7).

---

## 4. Raporty kosztów rzeczywistych CES (`ACTUALS_*`)

Pliki z prefiksem `ACTUALS_` to zrzuty kosztów rzeczywistych pobierane z SAP CES przez RABIT, w formacie Excel
(`.xlsx`). Każdy taki prefiks (np. `ACTUALS_PAF`, `ACTUALS_CES`) to **osobne źródło** (rozdz. 2); wszystkie mają
wspólny układ kolumn:

`Project Definition | WBS Element | Cost Element | Cost element descr. | Cost element name | CO object name |
Transaction Currency | Value TranCurr | Object Currency | Value in Obj. Crcy | Report currency | Val.in rep.cur. |
Total Quantity | Partner-CCtr | Source object name | Partner Object Class | Partner object | Original material |
Original material description | Fiscal Year | Created on | Period`

Przykład: `2DI473 | 2DI473001001 | 51105550 | PZL Material Consumption | … | PAF2 - materiały pod RTS batch 6 |
USD | 261,54 | PLN | 1 254,51 | USD | 261,54 | 0,000 | … | 2026 | 2026-03-29 | 3`.

| Kolumna | Znaczenie |
|---|---|
| `Project Definition` | projekt CES (np. `4D03GZ`) |
| `WBS Element` | element WBS CES (np. `4D03GZ000001`) – klucz mapowania CES ↔ P1S |
| `Cost Element` | numer elementu kosztowego – klucz słownika Cost Category (`docs/slowniki.md`, rozdz. 6) |
| `CO object name` | opis obiektu (wyszukiwanie) |
| `Value TranCurr`, `Value in Obj. Crcy`, `Val.in rep.cur.` | kwota w walucie transakcji, obiektu (PLN) i raportowej (USD) |
| `Total Quantity` | ilość |
| `Fiscal Year`, `Period` | rok i okres księgowy |

- Liczby w formacie polskim (spacja tysięcy, przecinek dziesiętny).
- Struktura CES jest **płaska**: projekt → lista elementów WBS (numeracja ciągła z lukami).

---

## 5. Struktura P1S i kategoryzacja

### 5.1 `PZLPROD.LOG.WBS`

Wszystkie elementy WBS P1S (nadrzędne i szczegółowe), czytane bez zmian.

| Kolumna | Użycie |
|---|---|
| `PSPNR` | identyfikator elementu; łączenie z raportem mapowań (`PSPNR` = `pspnr_sap`) |
| `PARENT` | `PSPNR` elementu nadrzędnego – hierarchia |
| `STUFE` | poziom w hierarchii (1 = korzeń `PROJORG`) |
| `WBS_ELEMENT` | kod WBS P1S (np. `MC-00.001.0001.001`) |
| `PROJORG` | projekt SAP P1S – korzeń drzewa elementów |
| `PROJECT` | grupa raportowa wg `WBS_DIC`; często równa `PROJORG` (np. `AC-I39`), nie zawsze (np. `MC-00.001` pod `MC-00`) |
| `PROJNAME`, `LTXA1`, `Z_OPIS` | opisy (wyszukiwanie) |
| `PRCTR` | profit center |
| `Z_KAT_ZBIORCZA`, `Z_KATEGORIA` | kategorie – poziomy drzewa (rozdz. 5.3) |
| `Z_MODEL`, `MATNR_LO`, `SERNR_LO`, `KDAUF`/`KDPOS`, `KUNNR`, `BSTNK`, `MATNR`, `MAKTX`, `AUFNR`, `TECHS` | atrybuty opisowe: model, materiał i numer seryjny, zlecenie sprzedaży, klient, zamówienie klienta, materiał, zlecenie |
| `Z_ACTIVE`, `LOEKZ` | aktywność i znacznik usunięcia |
| `ERDAT`, `AEDAT` | daty utworzenia i zmiany – wykrywanie nowych elementów |

### 5.2 `PZLPROD.LOG.WBS_DIC`

Słownik grupujący projekty P1S.

| Kolumna | Użycie |
|---|---|
| `Z_PROJECT` + `Z_GRP` | klucz grupy: `PROJECT` (grupa po projekcie / WBS) albo `PRCTR` (grupa po profit center) |
| `Z_OPIS`, `Z_KATEGORIA`, `Z_KAT_ZBIORCZA`, `Z_INFO` | opis i kategorie grupy (np. „Internal Work”, „Spares & Services”) |
| `ERDAT`, `Z_USER` | kto i kiedy dopisał grupę |

### 5.3 Drzewo P1S

Jedno drzewo P1S jest używane przy budowie nakładki Performance Objectives (`docs/performance-objectives.md`) i w widoku mapowania
(`docs/mapowanie-ces-p1s.md`, rozdz. 11):

```text
Z_KAT_ZBIORCZA
└─ Z_KATEGORIA
   └─ Z_OPIS
      └─ PROJORG
         └─ elementy WBS/PSP (hierarchia wg PARENT)
```

- `PROJORG` jest umieszczany według kategorii swojego wiersza (`STUFE` = 1); `PROJORG` bez kategorii trafia
  do grupy **„Bez kategorii w WBS”**.
- Elementy nieaktywne i usunięte (`Z_ACTIVE`, `LOEKZ`) **zostają** w drzewie (wyszarzone) – mogą mieć koszty.
- Przykład gałęzi: `AC-I39` (`PROJORG` = `PROJECT`, `PRCTR` PIDS70OM) → `AC-I39.1` → `AC-I39.1.01` →
  `AC-I39.1.01.01`, `AC-I39.1.01.02`, …

---

## 6. Zaawansowanie z produkcji

- Źródło: `PZLPROD.LOG.vAHDD` (do potwierdzenia – O10) – zaawansowanie godzin i materiałów według elementów
  P1S. Pobierane w etapie P5 dla elementów WBS projektu.
- `vAHDD` jest aktualny dopiero po przebiegu procedury `uspUpdateAHDD`, która uruchamia `uspUpdateAHDD_ORDER`,
  `_PSPNR` i `_VORNR` (ta ostatnia wywołuje `uspUpdateZMTO`). Status odświeżenia i błędy zapisują
  `StatusAktualizacjiRaportow`, `ReportErrorInfo` i `TableList` – na tej podstawie etap P0 sprawdza świeżość
  danych produkcyjnych.

### 6.1 Istniejące obiekty na `splmcd03`

Według eksportu metadanych (`dependencies.csv`, `resolved_objects.csv`, `export_log.txt`):

- Dane SAP P1S są replikowane do `PZL_SAP` (`Z_R3_PRPS_TBL`, `AUFK`, `AFKO`, `AFPO`, `AFVC/AFVV`, `JEST`…).
  Struktura P1S jest czytana z przetworzonej tabeli `LOG.WBS`, bez sięgania do `Z_R3_PRPS_TBL`.
- Tabel kosztowych CES (podstawa CJI3) w replikacji nie ma – koszty pochodzą z plików RABIT.
- `Stanowiska`, `LearningCurve`, `PeriodDates`, `EmployeesHist` to istniejące tabele słownikowe
  (`docs/slowniki.md`, O9).
- Eksport zawiera tylko metadane; do szczegółowego projektu potrzebne są definicje `vAHDD`, `WBS`,
  `uspUpdateAHDD_VORNR` i kolumny tabel.

---

## 7. Pliki CAM

Plik CAM generuje PZL-EV w przebiegu zamykającym okres (`docs/pipeline-fazy.md`, P6); CAM uzupełnia go
i odkłada do folderu `Zwrocone`.

- Jeden plik na CAM w ramach projektu (O3), z WP danego CAM ze słownika „WP i CAM”.
- Ukryty arkusz: identyfikator przebiegu, projekt, okres, CAM, wersja szablonu.
- Wartość zaawansowania z produkcji wpisana jako **podpowiedź**; CAM potwierdza ją albo zmienia.
- Komórki poza polami do uzupełnienia są zablokowane. Pola uzupełniane przez CAM – O17.
- Plik jest odrzucany przy imporcie, gdy: brak identyfikatora, plik z innego przebiegu lub okresu, zmiana
  poza polami do uzupełnienia, wartości poza zakresem.

---

## 8. Reguły walidacji źródeł

Poziomy ERROR / WARNING – `docs/pipeline-fazy.md`, rozdz. 1.3.

| Reguła | Poziom | Kiedy |
|---|---|---|
| lokalizacja RABIT niedostępna (brak dostępu, błąd WebDAV) | ERROR dla lokalizacji – pozostałe lokalizacje importują się dalej | import |
| plik bez pasującego prefiksu | WARNING (plik nierozpoznany, nieimportowany) | import |
| sygnatura kolumn niezgodna z definicją (zmiana układu raportu) | ERROR dla wersji pliku – dane kanoniczne nie powstają do czasu aktualizacji definicji | import |
| wartość niezgodna z typem kolumny | ERROR | import |
| duplikat klucza w pliku albo między częściami jednego źródła | ERROR | import / P2 |
| reguły szczegółowe źródła | wg definicji | import |

Kontrole danych względem okresu przebiegu i słowników – `docs/pipeline-fazy.md`, P2.

---

## 9. Otwarte kwestie

| # | Kwestia |
|---|---|
| O7 | Czy RABIT może eksportować CSV/TXT? (CSV preferowany) |
| O10 | Źródło zaawansowania z produkcji (`vAHDD`) – potwierdzenie i definicja widoku |
| O27 | Zawartość pozostałych raportów RABIT (zobowiązania, „PZL roll”, „hedge”, „workaround”), ich prefiksy i definicje źródeł; układ `ACTUALS_*` – rozdz. 4 |
| O28 | Które dane pochodzą z Cobra i w jakiej formie |
| O38 | Wycofanie pliku, którego RABIT już nie generuje (dziś jego ostatnia wersja pozostaje najnowsza) |
