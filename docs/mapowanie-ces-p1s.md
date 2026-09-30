# PZL-EV – Mapowanie CES ↔ P1S (założenia)

Wersja: 0.7 (założenia + decyzje M1–M26 – bez implementacji; 0.7: mapowanie z raportu mapowań SAP↔CES
z korektami – M16–M19, kategoryzacja WBS – M20, import RABIT bez wymaganych źródeł – M21, „projekt” zamiast
„zakresu” i kreator projektu – M22–M25, słownik Cost Category – M26)
Powiązane: `docs/architektura.md` (D25), `docs/pipeline-fazy.md` (G3), `docs/funkcjonalnosc.md` (F01, F21–F25),
`prototyp/PZL-EV Pipeline v3.html` (ilustracja M16–M26 na danych przykładowych; źródłem prawdy jest ten dokument).

> W specyfikacji źródłowej występuje nazwa „AHD” – w projekcie oznacza ona **PZL-EV**.

> **Od wersji 0.7 obowiązuje rozdz. 2a (M16–M19).** Rozdz. 2.1–2.3, 3 i 5 opisują wcześniejszy model
> (reguła projektu CES → `PROJORG`, wyjątki, `include_children`, propozycje) – zostają jako historia
> ustaleń, oznaczone jako zastąpione. Bez zmian obowiązują: 2.4 (M3) i 2.5 (M14).

---

## 1. Cel

PZL-EV umożliwia mapowanie struktury WBS z **SAP CES** (koszty) na strukturę WBS z **SAP P1S**
(produkcja). Struktury są niezależne – nie zakładamy zgodności numeracji, nazw ani poziomów hierarchii.
PZL-EV jest **warstwą mapowania** między systemami; mapowanie jest przechowywane w bazie PZL-EV i używane
automatycznie przy kolejnych importach.

```text
SAP CES → CES Project / CES WBS → mapowanie PZL-EV → P1S Project / P1S WBS → SAP P1S
```

Danych źródłowych SAP nie modyfikujemy. Źródłem prawdy o powiązaniu jest **zatwierdzona reguła
mapowania**. *(Zastąpione przez M16: źródłem prawdy jest raport mapowań SAP↔CES, a korekty PZL-EV mają
przed nim pierwszeństwo – rozdz. 2a.)*

Mapowanie jest **częścią administracyjną** (M16): służy wyłącznie do przypisania elementów CES do elementów
P1S. Nie pokazuje kosztów – kwoty i drzewo kosztów to inny etap.

---

## 2a. Mapowanie z raportu mapowań SAP↔CES (M16–M19)

### 2a.1 Źródło prawdy – raport mapowań (M16)

Powiązanie CES ↔ P1S pochodzi z **raportu mapowań SAP↔CES**, czytanego zapytaniem do bazy `PZLPROD`
(tylko odczyt). Kolumny:

`src, pspnr, pspnr_sap, pspnr_ces, pspnr_parent, project, project_sap, project_sap_org, project_ces, wbs,
wbs_sap, wbs_ces, wbs_desc(_sap/_ces), prctr(_sap/_ces), lvl(_sap/_ces), perf_obg, techs, sales_order_typ,
sales_order, sales_order_pos, matnr, network`

Znaczenie wierszy:

| Wiersz | Znaczenie | Przykład |
|---|---|---|
| `src` = SAP, wypełnione `pspnr_ces` | odpowiednik **1:1** elementu SAP (P1S) i CES | `AC-LH8.1.01.01` ENG ↔ `4D02U8000001` |
| `src` = CES, wypełnione `pspnr_sap` | element **tylko w CES**, wskazany w SAP | korzeń projektu CES → `PROJORG`; `.RA` → zlecenie sprzedaży; `.02` → element `.03` |
| element tylko SAP | brak odpowiednika CES | np. „kontrakt” |

- **1 `PROJORG` = wiele projektów CES.** Projekt CES odpowiada poddrzewu zlecenia sprzedaży w SAP
  (`project_sap`, np. `4D02U8` ↔ `AC-LH8.1.01`).
- **Dopasowanie wyłącznie po `pspnr`** – poziomy SAP i CES się różnią, a opisy (np. SWBS) się powtarzają,
  więc ani poziom, ani opis nie są kluczem.
- Raport wskazuje element P1S **dokładnie** (nie zawsze `PROJORG`) – zastępuje M7.
- Połączenie z kategoryzacją P1S: `PSPNR` z `LOG.WBS` = `pspnr_sap` (M20, rozdz. 4a).

### 2a.2 WBS CES spoza raportu – dziedziczenie po projekcie CES (M17)

WBS CES, którego nie ma w raporcie (np. nowy element z importu RABIT), **automatycznie dziedziczy**
odpowiednik swojego projektu CES (`project_sap`). Dziedziczenie jest logiczne – bez zapisywania rekordów
dla każdego WBS (jak dotychczas w 2.1).

### 2a.3 Korekty (M18)

Korekty są **globalne** (na potrzeby finansów, niezależne od projektu PZL-EV):

| Korekta | Działanie |
|---|---|
| **korekta elementu CES** | nowy cel P1S dla jednego elementu CES; może wskazać dowolny element P1S |
| **korekta projektu CES** | nowy cel dla **WBS spoza raportu** tego projektu CES (dziedziczenie z 2a.2); WBS z raportu – bez zmian (zob. O20) |

- Korekta ma **pierwszeństwo przed raportem** i zapisuje się **z historią** (kto, kiedy, poprzedni i nowy cel,
  `valid_from/valid_to`).
- Zmiana względem raportu wymaga **uzasadnienia** (pole obowiązkowe).
- **Usunięcie korekty** przywraca przypisanie z raportu albo dziedziczenie.
- Przypisania z raportu **nie da się usunąć** – można je tylko skorygować.
- M3 obowiązuje: element CES ma w danym momencie co najwyżej jeden cel P1S.

### 2a.4 Kolejność rozstrzygania i statusy (M18)

```text
element CES → 1. korekta elementu                              → OVERRIDE
            → 2. raport mapowań (pspnr_ces / pspnr_sap)          → REPORT
            → 3. WBS spoza raportu: korekta projektu CES,
                 a bez niej odpowiednik projektu CES (project_sap) → INHERITED
            → 4. brak                                            → UNMAPPED
```

| Status | Znaczenie |
|---|---|
| `OVERRIDE` | korekta elementu CES (z uzasadnieniem) |
| `REPORT` | przypisanie z raportu mapowań |
| `INHERITED` | WBS spoza raportu – cel projektu CES (z raportu albo z korekty projektu CES) |
| `UNMAPPED` | brak celu; koszt jest liczony, ale nie trafia do EV (M9) |
| ~~`NO_P1S`~~ | odłożony (M14) |

Statusy `MAPPED` i dotychczasowe znaczenie `INHERITED` (reguła projektu) z rozdz. 3 – zastąpione.

### 2a.5 Widok (M19)

- **Dwa drzewa w tej samej strukturze kategoryzacji** (M20): CES | P1S.
- Element CES jest umieszczony w drzewie według elementu P1S, do którego jest przypisany (kategoria
  przechodzi na element CES przez przypisanie – w mapowaniu kategorii się nie przypisuje).
- Elementy nieprzypisane są w jednym węźle **„Nieprzypisane”**; dla projektów CES spoza raportu – z
  **propozycją celu**.
- Akcje: korekta elementu CES, korekta projektu CES (z uzasadnieniem), usunięcie korekty, historia.
  Bez kwot i drzewa kosztów.

---

## 2. Reguły mapowania *(model wcześniejszy – zastąpiony przez rozdz. 2a z wyjątkiem 2.4 i 2.5)*

### 2.1 Reguła nadrzędna – projekt CES → projekt P1S *(zastąpione: M16, M17)*

```text
4D03GZ → XYZ      (MAPPING_TYPE = PROJECT, STATUS = CONFIRMED)
```

Wszystkie WBS projektu CES `4D03GZ` – obecne i **przyszłe** – są przypisane do projektu P1S `XYZ`
bez ponownego zatwierdzania. Dziedziczenie jest **logiczne**: nie tworzymy rekordów
`4D03GZ000001 → XYZ`, `4D03GZ000002 → XYZ` … Ewentualny wynik materializowany (wydajność) jest
wyłącznie cache/widokiem, nie źródłem prawdy.

### 2.2 Wyjątek – WBS CES → WBS P1S *(zastąpione: M18 – korekta elementu CES)*

```text
4D03GZ000038 → XYZ.007
```

Wyjątek ma pierwszeństwo przed regułą projektu.

### 2.3 Gałąź P1S (`include_children`) *(zastąpione: M16 – raport i korekta wskazują element P1S; zasięg węzła wynika z drzewa)*

Reguła może wskazywać węzeł P1S wraz z potomkami (`include_children = true`) – obejmuje też potomków
dodanych później, dopóki reguła jest aktywna.

### 2.4 Krotność (decyzja M3) *(obowiązuje – potwierdzone 30.09.2026)*

- **wiele CES → jeden P1S** – obsługiwane,
- **jeden CES → wiele P1S – niedozwolone.** Koszt CES występujący raz jest **raz pokazywany**
  (bez powielania i bez dzielenia). Element CES ma w danym momencie **co najwyżej jeden** cel P1S.
- Reguła projektu i wyjątek nie tworzą 1:wiele – wyjątek **zastępuje** regułę projektu dla danego WBS.
- `include_children` nie powiela kosztu na potomków: określa zasięg węzła P1S (np. przy prezentacji
  i agregacji), a koszt CES jest przypisany raz – do wskazanego węzła.

### 2.5 Brak odpowiednika – `NO_P1S` *(odłożone – M14)*

> **M14:** status `NO_P1S` odkładamy do czasu, aż pojawi się rzeczywisty przypadek. Do tego czasu
> element bez decyzji ma status `UNMAPPED`, a jego koszt jest liczony (M9). Poniższy opis zostaje
> jako założenie na przyszłość.

Świadomie potwierdzony brak elementu P1S. Różni się od `UNMAPPED` (decyzja jeszcze nie podjęta).
Element nie jest przypisywany do „przypadkowego” WBS.

---

## 3. Kolejność rozstrzygania *(zastąpione: M18 – rozdz. 2a.4)*

```text
CES WBS → ustal Project Definition
        → 1. dokładne mapowanie WBS CES → WBS P1S (wyjątek)
        → 2. reguła projektu CES → projekt P1S (dziedziczenie)
        → 3. automatyczna propozycja (NIE jest zatwierdzeniem)
        → 4. UNMAPPED
```

### Statusy

| Status | Znaczenie |
|---|---|
| `UNMAPPED` | brak zatwierdzonego mapowania (może mieć propozycję z poziomu 3) |
| `MAPPED` | bezpośrednie, zatwierdzone mapowanie WBS (bez reguły projektu) |
| `INHERITED` | mapowanie wynika z reguły projektu |
| `OVERRIDE` | własne mapowanie WBS nadpisujące regułę projektu |
| ~~`NO_P1S`~~ | potwierdzony brak odpowiednika P1S – *odłożony (M14), nie występuje w wersji 1* |

### Automatyczne propozycje (poziom 3) *(zastąpione: M16 – dopasowanie tylko po `pspnr`; propozycja = cel projektu CES, 2a.5)*

Kryteria: identyfikator projektu, materiał, numer seryjny, zlecenie, model, klient, dane zamówienia,
istniejące relacje P1S, struktura hierarchiczna; nazwy i opisy – tylko pomocniczo. Propozycja wymaga
zatwierdzenia przez użytkownika, chyba że później zostanie zdefiniowana jednoznaczna reguła
automatycznego zatwierdzania.

---

## 4. Model danych (logiczny)

### 4.0 Korekty mapowania (M18)

Przypisania z raportu mapowań **nie są kopiowane** do bazy słowników – czytane są z `PZLPROD` (M16).
W bazie słowników (SQLite) zapisywane są tylko **korekty**:

| Pole | Opis |
|---|---|
| `id` | identyfikator korekty |
| `typ` | `ELEMENT` (korekta elementu CES) / `PROJEKT` (korekta projektu CES – cel WBS spoza raportu) |
| `ces_pspnr` / `ces_project` | korygowany element CES albo projekt CES |
| `p1s_pspnr` | nowy cel P1S (dowolny element P1S) |
| `uzasadnienie` | obowiązkowe przy zmianie względem raportu |
| `valid_from`, `valid_to` | okres obowiązywania; usunięcie korekty = zamknięcie `valid_to` (wraca raport / dziedziczenie) |
| `created_at/by`, `updated_at/by` | audyt |

Statusy (`OVERRIDE`, `REPORT`, `INHERITED`, `UNMAPPED`) są **wyliczane**, nie zapisywane.

### 4.0a Model wcześniejszy *(zastąpiony przez 4.0 – M16, M18)*

`CES_P1S_MAPPING`

| Pole | Opis |
|---|---|
| `id` | identyfikator reguły |
| `mapping_type` | `PROJECT` / `WBS` |
| `ces_project`, `ces_wbs` | strona CES (`ces_wbs` puste dla reguły projektu) |
| `p1s_project`, `p1s_wbs` | strona P1S (`p1s_wbs` puste = cały projekt); **jedna aktywna reguła WBS na element CES** w danym okresie |
| `include_children` | czy obejmuje potomków węzła P1S |
| `mapping_status` | `MAPPED` / `OVERRIDE` (`NO_P1S` odłożony – M14; statusy `INHERITED` i `UNMAPPED` są wyliczane, nie zapisywane) |
| `mapping_method` | np. ręczne / z zatwierdzonej propozycji |
| `active`, `valid_from`, `valid_to` | aktywność i okres obowiązywania (raportowanie historyczne) |
| `created_at/by`, `updated_at/by` | audyt |

**Historia zmian:** kto i kiedy utworzył, kto i kiedy zmienił, poprzednia i nowa wartość.

Dodatkowo potrzebny **rejestr elementów CES i P1S** (kod, projekt, rodzic, opis, pierwsze/ostatnie
wystąpienie) – na nim działa drzewo, wykrywanie nowych elementów i `include_children`.
- elementy **CES** – z importów RABIT (G2),
- elementy **P1S** – z przetworzonej tabeli **`PZLPROD.LOG.WBS`** (decyzja M2; bez sięgania do
  `PZL_SAP.dbo.Z_R3_PRPS_TBL`).

### 4.1 Przechowywanie (decyzja M1)

Reguły mapowania, słowniki, rejestry elementów i historia zmian są w **lekkiej bazie plikowej (SQLite)**
w repozytorium PZL-EV na dysku sieciowym, np. `\\serwer\udzial\PZL-EV\00_Global\Baza\pzl_ev.sqlite`.
Zarządzanie przez **narzędzie CRUD z drzewem** (dwa drzewa CES | P1S) – bez Excela jako źródła prawdy
dla przypisań.

Ryzyka SQLite na udziale sieciowym i sposób ich ograniczenia:

| Ryzyko | Ograniczenie |
|---|---|
| blokady plików przez SMB bywają zawodne; tryb WAL nie działa na udziale sieciowym | tryb dziennika `DELETE`, krótkie transakcje, jeden zapis naraz (blokada aplikacyjna + `busy_timeout`) |
| jednoczesny zapis kilku osób | zapis tylko w krótkich operacjach CRUD/importu; odczyt równoległy bez ograniczeń |
| uszkodzenie pliku przy zerwaniu połączenia | automatyczna kopia pliku przed każdym importem / sesją edycji; kopie dzienne |
| rozmiar | nie dotyczy: w SQLite są **tylko słowniki i przypisania** (M4); dane importów (miliony wierszy) pozostają w MS SQL |

---

## 4a. Źródła struktur (M5, M6, M16, M20)

### Raport mapowań SAP↔CES (M16)

Zapytanie do `PZLPROD`, tylko odczyt – kolumny i znaczenie wierszy w rozdz. 2a.1. Łączenie z kategoryzacją:
`LOG.WBS.PSPNR` = `pspnr_sap`.

### CES – z importów RABIT (G2)

Raporty CES zawierają kolumny:

| Kolumna CES | Znaczenie w mapowaniu |
|---|---|
| `Project Definition` | **CES Project** (np. `4D03GZ`) – bez wyliczania z prefiksu (M5) |
| `WBS Element` | **CES WBS** (np. `4D03GZ000001`) |

Pełny układ raportu kosztów CES (zrzut RABIT):

`Project Definition | WBS Element | Cost Element | Cost element descr. | Cost element name | CO object name |
Transaction Currency | Value TranCurr | Object Currency | Value in Obj. Crcy | Report currency | Val.in rep.cur. |
Total Quantity | Partner-CCtr | Source object name | Partner Object Class | Partner object | Original material |
Original material description | Fiscal Year | Created on | Period`

Przykład: `2DI473 | 2DI473001001 | 51105550 | PZL Material Consumption | … | PAF2 - materiały pod RTS batch 6 |
USD | 261,54 | PLN | 1 254,51 | USD | 261,54 | 0,000 | … | 2026 | 2026-03-29 | 3`.
Liczby w formacie polskim (spacja tysięcy, przecinek dziesiętny); trzy waluty: transakcji, obiektu (PLN),
raportowa (USD). Dla mapowania znaczenie mają tylko `Project Definition`, `WBS Element` i opis obiektu
`CO object name` (M12).

Struktura CES jest **płaska**: projekt → lista WBS (numeracja ciągła z lukami, np. brak `…000028`,
`…000031`). Drzewo CES w narzędziu ma więc dwa poziomy. *(Od M19 drzewo CES jest układane według
przypisanego elementu P1S w strukturze kategoryzacji – rozdz. 2a.5; import RABIT pozostaje źródłem WBS
spoza raportu mapowań, M17.)*

### P1S – z `PZLPROD.LOG.WBS` i `PZLPROD.LOG.WBS_DIC` (MS SQL, `splmcd03`)

`LOG.WBS` – wszystkie WBS P1S (nadrzędne i szczegółowe), z danymi do grupowania:

| Kolumna | Użycie w mapowaniu |
|---|---|
| `PSPNR` | identyfikator techniczny elementu (klucz) |
| `PARENT` | `PSPNR` elementu nadrzędnego – **budowa drzewa P1S** i `include_children` |
| `STUFE` | poziom w hierarchii (1 = korzeń projektu) |
| `WBS_ELEMENT` | kod WBS P1S (np. `MC-00.001.0001.001`) – **wartość `p1s_wbs` w regułach** |
| `PROJORG` | projekt SAP P1S – **zawsze nadrzędny trzon** (korzeń drzewa P1S); **cel reguły projektu** (M7) |
| `PROJECT` | grupa raportowa wg `WBS_DIC`; często = `PROJORG` (np. `AC-I39`), ale nie zawsze (np. `MC-00.001` pod `MC-00`) – filtr / grupowanie, nie cel reguły projektu |
| `PROJNAME`, `LTXA1`, `Z_OPIS` | opisy (wyszukiwanie, podpowiedzi) |
| `PRCTR` | profit center (grupowanie wg `WBS_DIC`, gdy `Z_GRP = PRCTR`) |
| `Z_KAT_ZBIORCZA`, `Z_KATEGORIA` | kategorie (filtry w drzewie) |
| `Z_MODEL`, `MATNR_LO`, `SERNR_LO`, `KDAUF`/`KDPOS`, `KUNNR`, `BSTNK`, `MATNR`, `AUFNR` | model, materiał i nr seryjny, zlecenie sprzedaży, klient, zamówienie klienta, materiał, zlecenie – **kryteria propozycji (poziom 3)** |
| `Z_ACTIVE`, `LOEKZ` | aktywność / znacznik usunięcia – elementy nieaktywne i usunięte wyszarzone; mapowanie na nie → ostrzeżenie |
| `ERDAT`, `AEDAT` | daty utworzenia i zmiany – wykrywanie nowych elementów P1S |

`LOG.WBS_DIC` – słownik grupujący projekty P1S:

| Kolumna | Użycie |
|---|---|
| `Z_PROJECT` + `Z_GRP` | klucz grupy: `PROJECT` (grupa po projekcie/WBS) albo `PRCTR` (grupa po profit center) |
| `Z_OPIS`, `Z_KATEGORIA`, `Z_KAT_ZBIORCZA`, `Z_INFO` | opis i kategorie grupy (np. „Internal Work”, „Spares & Services”) |
| `ERDAT`, `Z_USER` | kto i kiedy dopisał grupę |

Drzewo P1S w narzędziu: korzeń = `PROJORG`, dalej `PARENT → PSPNR` (poziomy wg `STUFE`), np.

```text
AC-I39                    (PROJORG = PROJECT = AC-I39, PRCTR PIDS70OM)
└─ AC-I39.1
   └─ AC-I39.1.01
      ├─ AC-I39.1.01.01
      ├─ AC-I39.1.01.02
      └─ …
```

Filtrowanie / grupowanie po `PROJECT` (grupa raportowa), kategoriach i profit center. Dane P1S są **czytane** z MS SQL (bez zmian w SAP ani
w `LOG.WBS`); do SQLite trafiają tylko reguły mapowania i ewentualny podręczny rejestr elementów.

### Kategoryzacja WBS – `PZLPROD.LOG.WBS` (M20)

Kolumny istotne dla modelu: `PSPNR`, `PROJECT`, `PROJORG`, `WBS_ELEMENT`, `PRCTR`, `STUFE`, `PARENT`,
`Z_KAT_ZBIORCZA`, `Z_KATEGORIA`, `Z_OPIS`, `LTXA1`, `KDAUF`, `KDPOS`, `KUNNR`, `MATNR`, `MAKTX`, `TECHS`.

- Łączenie z raportem mapowań: `PSPNR` = `pspnr_sap`.
- Hierarchia elementów: według `PARENT`.
- Drzewa (mapowanie – rozdz. 2a.5, kreator projektu – `docs/funkcjonalnosc.md` F01) mają poziomy:

```text
Z_KAT_ZBIORCZA
└─ Z_KATEGORIA
   └─ Z_OPIS
      └─ PROJORG
         └─ elementy WBS/PSP (wg PARENT)
```

- Kategoria **nie jest przypisywana w mapowaniu** – element CES dostaje ją przez przypisanie do elementu P1S.
- `PROJORG` bez kategorii trafia do grupy **„Bez kategorii w WBS”**.
- Drzewo grupuje `PROJORG` według kategorii jego wiersza (`STUFE` 1); czy elementy jednego `PROJORG` mogą
  mieć różne kategorie – O21.
- Zastępuje wcześniejszy układ drzewa P1S (korzeń = `PROJORG`, grupowanie z `LOG.WBS_DIC` – M6 w części
  dotyczącej grupowania).

## 5. Interfejs wizualny *(zastąpione: M19 – rozdz. 2a.5)*

Dwa drzewa obok siebie (CES | P1S). Użytkownik może: rozwijać/zwijać, wyszukiwać, wybrać węzeł CES
i P1S, utworzyć mapowanie, regułę projektu, wyjątek, usunąć/dezaktywować,
zobaczyć istniejące mapowania. Drag & drop – tylko jako dodatek.

Formularze: **reguła projektu** (cały projekt / pojedynczy WBS) i **wyjątek** (z podglądem reguły
nadrzędnej, którą nadpisuje).

**Kolejność implementacji:** model danych → logika priorytetów → dziedziczenie → dopiero UI.

---

## 6. Miejsce w pipeline

> Od M16–M18: rozstrzyganie wg rozdz. 2a.4 (korekta → raport → dziedziczenie po projekcie CES → `UNMAPPED`);
> nowy WBS CES z importu bez wpisu w raporcie dostaje `INHERITED` (cel projektu CES) albo `UNMAPPED`.
> Poniższy opis (reguła projektu, wyjątek, propozycja) – model wcześniejszy.

```text
RABIT / SAP CES → import (G2) → CES staging → Project Definition
   → mapowanie (wyjątek WBS / reguła projektu / propozycja / UNMAPPED)
   → struktura P1S → wspólny poziom raportowania → godziny / koszty / wartości → EV / CAM / raporty
```

Po każdym imporcie CES: lista nowych elementów wymagających uwagi, np.

```text
4D03GZ000041   → odziedziczono: XYZ
4D03H1000038   → UNMAPPED
```

**Kontrola kompletności projektu CES:** lista WBS ze statusami (`INHERITED`, `OVERRIDE → XYZ.002`,
`UNMAPPED`).

**Wymaganie kluczowe:** jedno zatwierdzenie `4D03GZ → XYZ` obsługuje wszystkie obecne i przyszłe WBS
projektu. Ręczna interwencja tylko przy wyjątku, braku reguły projektu lub braku
jednoznaczności.

---

## 7. Kryteria ukończenia (skrót)

Od M16–M19: odczyt raportu mapowań (tylko odczyt, dopasowanie po `pspnr`); dziedziczenie po projekcie CES
dla WBS spoza raportu; korekty elementu i projektu CES z uzasadnieniem, historią i pierwszeństwem przed
raportem; usunięcie korekty przywraca raport / dziedziczenie; przypisania z raportu nieusuwalne; statusy
`OVERRIDE/REPORT/INHERITED/UNMAPPED`; dwa drzewa w strukturze kategoryzacji z węzłem „Nieprzypisane”;
bez kosztów w widoku mapowania.

*Wcześniejsze kryteria (model reguł projektu):*

Drzewa CES i P1S z wyszukiwaniem; reguła projektu obejmuje obecne i nowe WBS; wyjątek z pierwszeństwem;
wiele:1; **blokada 1:wiele** (M3); `include_children`; statusy `INHERITED/OVERRIDE/UNMAPPED` (`NO_P1S` odłożony – M14);
mapowania w bazie i używane automatycznie przy importach; historia zmian; brak mapowania nie gubi
danych CES; elementy bez odpowiednika P1S wykazywalne; źródłem prawdy jest reguła, nie rekordy pochodne.

---

## 8. Spójność z dotychczasową dokumentacją

### 8.1 Zgodne

| Założenie mapowania | Dotychczasowa dokumentacja |
|---|---|
| Mapowanie w bazie, historia zmian, `valid_from/valid_to` | wersjonowanie i dwie osie czasu (arch. 6.3), audyt AD (arch. 8.3) |
| Propozycja ≠ zatwierdzenie | wcześniejsza rekomendacja: prefiks/klucz tylko jako podpowiedź (K6) |
| Wiele CES → jeden P1S; bez 1:wiele (M3) | D19 dopuszczała dowolną krotność – M3 ją zawęża (koszt pokazywany raz) |
| Brak mapowania nie gubi danych CES | G2: surowe dane zawsze w bazie; D20: zamknięcie wymaga przypisania kosztów |
| Każdy z finansów może zmieniać mapowania, audyt w historii | D9 (bez podziału uprawnień) |
| UI w przeglądarce | D2 |
| Nowe elementy wykrywane po imporcie, bez wyprzedzania | D17, odpowiedź „dopiero po ściągnięciu” |

### 8.2 Rozbieżności do rozstrzygnięcia

| # | Rozbieżność | Dotychczas | Nowe założenie | Propozycja | Stan |
|---|---|---|---|---|---|
| S1 | **Gdzie żyje powiązanie CES↔P1S** | D19, arch. 6.4, spec. 6.2, readme: para `P1S WBS | CAS WBS` w wierszu słownika Excel „Struktura projektowa” | reguły w bazie PZL-EV, zarządzane w UI | powiązanie CES↔P1S **wyłącznie** w warstwie mapowania (D25); słownik „Struktura projektowa” opisuje już tylko **P1S WBS** (Program, Project, CAM, WP, Cost Category…) | ✅ **M1:** przypisania w lekkiej bazie (SQLite) na dysku sieciowym, zarządzane narzędziem CRUD + drzewo |
| S2 | **Zasada „Excel jest źródłem prawdy, bez formularza”** (readme – słowniki) | wszystkie słowniki w Excelu | mapowanie edytowane w aplikacji | mapowanie CES↔P1S to **wyjątek od zasady** – nie jest słownikiem Excel; ewentualnie eksport/import reguł do Excela jako wygoda | ✅ **M13:** zasada odwrócona – **żaden** słownik nie jest w Excelu; wszystkie słowniki, przypisania i konfiguracja w SQLite z interfejsem |
| S3 | **Definicja zakresu** (D17) | zakres = wiersze słownika struktury (pary P1S/CES) | wspólny poziom raportowania = struktura P1S | zakres = zbiór **projektów P1S** (ze słownika struktury); dane CES trafiają do zakresu przez mapowanie | ✅ **M8** → zastąpione: **M22, M23** (projekt = węzły drzewa P1S) |
| S4 | **Wykrywanie nowych elementów** (D17, arch. 6.4, spec. F08) | eksport brakujących elementów do `Slowniki\Propozycje` i dopisanie wiersza w Excelu | nowe WBS dziedziczą regułę projektu; uwagi wymagają tylko `UNMAPPED` | G3 = mapowanie; eksport „Propozycje” dotyczy już tylko nowych elementów **P1S** do słownika struktury | ✅ **M8** |
| S5 | **Źródło struktury P1S** | „dopiero po ściągnięciu – nie wybiegamy przed szereg” (P1S tylko z danych z zaawansowaniem) | pełne drzewo P1S w UI, `include_children` obejmuje nowych potomków | potrzebne źródło hierarchii P1S (raport RABIT z P1S albo `PZL_SAP.Z_R3_PRPS_TBL`) – **do decyzji** | ✅ **M2:** hierarchia P1S z przetworzonej tabeli `PZLPROD.LOG.WBS` (bez `Z_R3_PRPS_TBL`) |
| S6 | **Nazwa kolumny CES** | readme/słownik: „CAS WBS” | „CES WBS” | ujednolicić na **CES WBS** (CAS = typ projektu Compliance, CES = system) | otwarte |
| S7 | **Wartości przy 1:wiele** | brak | nie dzielimy bez reguły podziału | do ustalenia, gdzie w EV trafia koszt CES z relacją 1:wiele bez reguły (blokada zamknięcia? poziom projektu?) | ✅ **M3:** 1:wiele niedozwolone – koszt CES pokazywany dokładnie raz |
| S8 | **Koszty `NO_P1S` w EV** | D20: koszty muszą być przypisane na zamknięciu | `NO_P1S` = świadomy brak odpowiednika | do ustalenia: czy `NO_P1S` przechodzi bramkę zamknięcia i gdzie raportujemy ten koszt | ✅ **M9** / **M14:** każdy koszt CES liczony; `NO_P1S` odłożony |
| S9 | **Odtwarzalność przebiegu** | przebieg przypina wersje słowników i dane (P1) | mapowanie ma `valid_from/valid_to` | przebieg przypina również **stan mapowania** (znacznik czasu); zmiana mapowania w trakcie przebiegu → decyzja „kontynuuj / przelicz” jak przy słownikach | otwarte: słowniki – przypinane (D27, także Cost Category – M26); raport mapowań – **O19** |
| S10 | **„Projekt” w `projekty_zrodla.csv`** (D24) | nieokreślone | rozróżnienie projekt CES / projekt P1S | doprecyzować, czy chodzi o projekt P1S (raportowy), czy CES | ✅ **M10** → zastąpione: **M21** (bez wymaganych źródeł) |
| S11 | **Ustalenie Project Definition dla WBS CES** | brak | krok obowiązkowy rozstrzygania | z kolumny raportu CES (która?) czy z prefiksu kodu WBS (np. 6 znaków) | ✅ **M5:** kolumna `Project Definition` |

---

## 9. Decyzje (30.09.2026)

| # | Decyzja | Skutek |
|---|---|---|
| M1 | Przypisania (reguły mapowania, rejestry elementów, historia) w **lekkiej bazie SQLite na dysku sieciowym**, zarządzane **narzędziem CRUD z drzewem** | rozwiązuje S1, S2; mapowanie nie jest w Excelu |
| M2 | Struktura P1S z **`PZLPROD.LOG.WBS`** (dane już przetworzone), bez `PZL_SAP.dbo.Z_R3_PRPS_TBL` | rozwiązuje S5; potrzebny opis kolumn `LOG.WBS` (pytanie P2) |
| M3 | **Brak relacji 1:wiele** – koszt CES występujący raz jest raz pokazywany | rozwiązuje S7; walidacja blokuje drugi aktywny cel dla tego samego elementu CES |
| M4 | W SQLite na dysku sieciowym są **tylko słowniki i przypisania**. Dane importów (miliony wierszy w kolejnych cyklach tygodniowych) pozostają w **MS SQL** | zamyka P1; D3 (MS SQL) obowiązuje dla danych |
| M5 | CES Project = kolumna **`Project Definition`**, CES WBS = **`WBS Element`** z raportu CES; struktura CES płaska | zamyka P5 / S11 |
| M7 | *(zastąpione: M16, M18)* ~~**`PROJORG` = nadrzędny trzon P1S.** Reguła projektu CES → P1S wskazuje `PROJORG` (np. `4D03GZ → AC-I39`); wyjątki i węzły wskazują `WBS_ELEMENT`; `PROJECT` służy do grupowania (często równy `PROJORG`, nie zawsze)~~ – raport mapowań wskazuje element dokładnie; korekta może wskazać dowolny element P1S | zamyka P7 |
| M6 | *(uzupełnione: M20 – poziomy drzewa z kategoryzacji `LOG.WBS`)* Drzewo P1S z **`LOG.WBS`** (`PSPNR`/`PARENT`/`STUFE`, kod `WBS_ELEMENT`), grupowanie z **`LOG.WBS_DIC`** | zamyka P2 |
| M8 | *(zastąpione: M22, M23)* ~~**Zakres = konkretny program albo program indywidualny.** Na każdym zakresie są przypisania do **WP**; **harmonogramy, budżet i CAM** też trafiają do mapy przypisań (narzędzie CRUD + drzewo, SQLite)~~ – zakres projektu z drzewa P1S (M23), słowniki projektu (M24) | zamyka P3 / S3, S4; słownik Excel „Struktura projektowa” oraz „Harmonogram i budżet” zastępuje narzędzie przypisań |
| M9 | **Wszystko, co jest w zrzutach CES, wchodzi do przeliczenia kosztów i wskaźników** – także elementy `NO_P1S` | zamyka P4 / S8; `NO_P1S` nie blokuje zamknięcia; koszt nie jest pomijany |
| M10 | *(zastąpione: M21)* ~~Lista raportów (źródeł) importowanych dla projektu to **konfiguracja w lokalnej bazie** (SQLite), nie plik CSV~~ – słownik „Wymagane źródła projektów” usunięty | zamyka P6 / S10; dotyczy `projekty_zrodla.csv` (D24) |
| M11 | Elementy P1S nieaktywne / usunięte (`Z_ACTIVE`, `LOEKZ`) **zostają** w drzewie i mogą być celem mapowania – mogą mieć koszty | zamyka P8 |
| M13 | *(zmienione w części: M24, M26 – Excel także jako format wczytania / pobrania słowników, z walidacją; źródłem prawdy pozostaje baza)* **Wszystkie słowniki, przypisania i konfiguracja w bazie SQLite z interfejsem** (mapowanie, WP, CAM, harmonogram, budżet, stawki wydziałów, stawki CAS, kalendarz okresów, kursy walut, konfiguracja źródeł). **Excel nie jest źródłem słowników** – służy tylko do wymiany plików (RABIT na wejściu, pliki dla finansów i CAM na wyjściu) | zamyka P12 / S2; znoszą się D17–D19 i przepływ „plik Excel → walidacja → wersja”; walidacja przy zapisie w UI; przebieg przypina stan i kopiuje migawkę do MS SQL (D27) |
| M14 | Status **`NO_P1S` odłożony** – wrócimy, gdy pojawi się rzeczywisty przypadek | zamyka P11 (odłożone); w wersji 1 elementy bez decyzji mają status `UNMAPPED` |
| M15 | Konfiguracja prefiksów plików RABIT (`zrodla_rabit.csv`) – **do SQLite**, tak jak M10 | zamyka P13; w MVP etap 1 CSV pozostaje rozwiązaniem przejściowym |
| M12 | *(zastąpione: M16 – dopasowanie wyłącznie po `pspnr`, bez propozycji z opisów)* ~~Raport CES ma tylko informacje o elemencie WBS (bez zlecenia, materiału końcowego, klienta) – **automatyczne propozycje** ograniczone do zgodności kodów i opisów (`CO object name` ↔ opisy P1S)~~ | zamyka P9 |
| M16 | **Mapowanie CES ↔ P1S = część administracyjna** (tylko przypisanie elementów CES do P1S, bez kosztów). **Źródłem prawdy jest raport mapowań SAP↔CES** z `PZLPROD` (tylko odczyt); wiersz SAP z `pspnr_ces` = odpowiednik 1:1, wiersz CES z `pspnr_sap` = element tylko w CES wskazany w SAP; elementy tylko SAP nie mają CES. **Dopasowanie wyłącznie po `pspnr`** | rozdz. 2a.1; zastępuje M7, M12 oraz reguły 2.1–2.3 i propozycje (rozdz. 3) |
| M17 | **1 `PROJORG` = wiele projektów CES**; projekt CES odpowiada poddrzewu zlecenia sprzedaży w SAP (`project_sap`). **WBS CES spoza raportu dziedziczy** odpowiednik swojego projektu CES | rozdz. 2a.2 |
| M18 | **Korekty globalne:** korekta elementu CES albo korekta projektu CES (cel dla jego WBS spoza raportu); pierwszeństwo przed raportem, historia, uzasadnienie przy zmianie względem raportu; usunięcie korekty przywraca raport / dziedziczenie; przypisań z raportu nie da się usunąć. Rozstrzyganie: `OVERRIDE` → `REPORT` → `INHERITED` → `UNMAPPED` | rozdz. 2a.3–2a.4, 4.0; zastępuje wyjątki (2.2) i statusy z rozdz. 3 |
| M19 | **Widok mapowania:** dwa drzewa CES \| P1S w tej samej strukturze kategoryzacji (M20); CES umieszczony wg przypisanego elementu P1S; węzeł „Nieprzypisane” z propozycją celu dla projektów CES spoza raportu | rozdz. 2a.5; zastępuje rozdz. 5 |
| M20 | **Kategoryzacja WBS z `PZLPROD.LOG.WBS`**: łączenie `PSPNR` = `pspnr_sap`, hierarchia wg `PARENT`, poziomy drzew `Z_KAT_ZBIORCZA → Z_KATEGORIA → Z_OPIS → PROJORG → elementy WBS/PSP`; kategoria nie jest przypisywana w mapowaniu; `PROJORG` bez kategorii → „Bez kategorii w WBS” | rozdz. 4a; uzupełnia M6 |
| M21 | **Import RABIT bez słownika „Wymagane źródła projektów”** – usunięty wraz z kontrolą kompletności źródeł (Import, Pulpit, gotowość projektu, nowy przebieg). Zakres danych projektu wynika z jego węzłów w drzewie P1S, a jedna paczka RABIT może obejmować wiele projektów (np. całe PWC). Przebieg przypina **wszystkie zaimportowane pliki** i wybiera z nich wiersze elementów projektu. Konfiguracja importu = tylko „Prefiksy plików RABIT” (M15) | zastępuje M10 i część D24 (wymagane źródła); faza G2b i F05b usunięte |
| M22 | **„Zakres” zastąpiony pojęciem „projekt”** (projekt PZL-EV – budowany do przeliczania wskaźników). Projekt ≠ projekt CES ≠ `PROJORG` (projekt P1S) | nazewnictwo w dokumentacji, ekranach i folderach (`Projekty\<Projekt>\`); zastępuje definicję z M8 |
| M23 | **Kreator projektu:** Podstawowe → Projekty P1S → Słowniki projektu → Foldery → Podsumowanie (krok „CAM” usunięty – CAM w słowniku „WP i CAM”). **Zakres projektu z drzewa P1S** (rozwijanego w dół, checkbox na każdym poziomie): kilka grup z różnych poziomów i pojedyncze `PROJORG`; grupa obejmuje wszystkie swoje `PROJORG` i ich elementy WBS/PSP. Zapis: `sel` (ścieżki grup) + `p1s` (pojedyncze `PROJORG`). `PROJORG` należący do innego projektu jest pomijany; pojedynczo wskazany `PROJORG` ma pierwszeństwo przed grupą; wśród grup wygrywa projekt utworzony wcześniej | zastępuje M8 („program indywidualny / pula”); `docs/funkcjonalnosc.md` F01 |
| M24 | **Słowniki projektu z Excela**: WP i CAM, Harmonogram i budżet, w CAS także Stawki CAS, opcjonalnie „Cost Category – zmiany w projekcie”. Jeden plik z arkuszami (szablon do pobrania z elementami P1S z zakresu) albo osobne pliki / CSV; kolumny rozpoznawane po nagłówkach. Walidacja jak przy zapisie w aplikacji; słownik z błędami nie zostaje zapisany. Po utworzeniu projektu – pobranie do Excela i ponowne wczytanie z podglądem różnic (+nowe / ~zmienione / −usunięte), zapis z historią | zmienia w części M13 (Excel jako format wymiany słowników; źródłem prawdy pozostaje baza); `docs/funkcjonalnosc.md` F01, F03, 6.3 |
| M25 | **Podsumowanie kreatora = baza analityczna**: drzewo (kategoria → `PROJORG` → element P1S) połączone z WP, CAM, BAC, datami i liczbą elementów CES z mapowania; braki (element z kosztami CES albo zaawansowaniem bez WP, WP bez budżetu); zestawienie według CAM; eksport do xlsx | `docs/funkcjonalnosc.md` F01 krok 5; na stronie projektu – O23 |
| M26 | **Słownik Cost Category:** globalny (numer elementu kosztowego z `ACTUALS_CES` → Opis, Obszar, Cost Category) + w każdym projekcie opcjonalny „Cost Category – zmiany w projekcie” (zmienia i dodaje pozycje, pierwszeństwo przed globalnym); słownik efektywny = globalny + zmiany projektu. Edycja w aplikacji z historią i Excel (pobierz / wczytaj z podglądem różnic); numer zapisany w Excelu jako liczba uzupełniany zerami do 10 znaków. Przebieg przypina oba. Numer z kosztów projektu bez wpisu w żadnym słowniku – błąd blokujący (naprawa: dodanie w słowniku projektu); numer bez kategorii – ostrzeżenie | `docs/funkcjonalnosc.md` rozdz. 6.6 i załącznik A |

**Potwierdzone bez zmian (30.09.2026, prototyp v3):** M3 (WBS ma jeden cel, koszt liczony raz), M9 (koszt bez
przypisania jest liczony, ale nie trafia do EV), M14 (`NO_P1S` odłożone), D27 (migawka słowników kopiowana
do MS SQL).

## 10. Pytania otwarte

| # | Pytanie |
|---|---|
| ~~P1~~ | ✅ M4 – w SQLite tylko słowniki i przypisania, dane importów w MS SQL |
| ~~P2~~ | ✅ M6 – kolumny opisane w rozdz. 4a |
| ~~P3~~ | ✅ M8 – zakres = program; WP, harmonogram, budżet, CAM w mapie przypisań *(M8 zastąpione: M22, M23)* |
| ~~P4~~ | ✅ M9 – wszystkie koszty CES liczone, `NO_P1S` nie blokuje |
| ~~P5~~ | ✅ M5 – kolumna `Project Definition` |
| ~~P6~~ | ✅ M10 – lista raportów projektu jako konfiguracja w SQLite *(M10 zastąpione: M21 – bez wymaganych źródeł)* |
| ~~P7~~ | ✅ M7 – reguła projektu wskazuje `PROJORG` (nadrzędny trzon); `PROJECT` = grupowanie *(M7 zastąpione: M16, M18)* |
| ~~P8~~ | ✅ M11 – elementy nieaktywne/usunięte zostają |
| ~~P9~~ | ✅ M12 – propozycje tylko z kodów i opisów *(M12 zastąpione: M16)* |
| ~~P11~~ | ⏸ M14 – odłożone razem z `NO_P1S` |
| ~~P12~~ | ✅ M13 – nic nie zostaje w Excelu; wszystkie słowniki w SQLite z interfejsem *(zmienione w części: M24, M26 – Excel jako format wymiany)* |
| ~~P13~~ | ✅ M15 – prefiksy RABIT w SQLite, jak M10 |

### Otwarte

| # | Kwestia |
|---|---|
| P10 | **UNMAPPED a zamknięcie miesiąca.** Wg M9 każdy koszt CES jest liczony. Czy koszt `UNMAPPED` (brak decyzji) liczymy na poziomie projektu CES / projektu PZL-EV i dopuszczamy zamknięcie, czy – jak w D20 – wymagamy mapowania przed zamknięciem? Rekomendacja: liczony zawsze, ale zamknięcie wymaga zmapowania (przy odłożonym `NO_P1S` – M14 – jedyną decyzją jest mapowanie). |
| O19–O22 | Kwestie z prototypu v3 dotyczące mapowania i kategoryzacji (przypinanie raportu mapowań, zasięg korekty projektu CES, kategorie w obrębie `PROJORG`, nowe `PROJORG` w zaznaczonej grupie) – `docs/funkcjonalnosc.md`, rozdz. 10 |
