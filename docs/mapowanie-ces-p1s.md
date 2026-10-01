# PZL-EV – Mapowanie CES ↔ P1S

Zakres: przypisanie elementów WBS z SAP CES (koszty) do elementów WBS z SAP P1S (produkcja) – źródło prawdy,
dziedziczenie, korekty, kolejność rozstrzygania, statusy, model danych, widok i reguły walidacji.

Powiązane: `docs/zrodla-danych.md` (raport kosztów CES, struktura P1S i drzewo), `docs/pipeline-fazy.md`
(G2 – rozstrzyganie po imporcie, P1 i P3 – użycie w przebiegu), `docs/model-danych.md` (historia).

---

## 1. Cel i zakres

- Struktury CES i P1S są niezależne – nie zakładamy zgodności numeracji, nazw ani poziomów hierarchii.
- PZL-EV jest warstwą mapowania między systemami; danych w SAP nie modyfikuje.
- Mapowanie jest **częścią administracyjną**: przypisuje elementy CES do elementów P1S, bez kwot i bez
  drzewa kosztów. Koszty łączy przebieg (`docs/pipeline-fazy.md`, P3).
- Mapowanie jest **globalne i administracyjne** – niezależne od projektów PZL-EV i bez wpływu na nie. Zakres
  projektu wyznacza nakładka Performance Objectives (`docs/performance-objectives.md`), która przy budowie
  **czyta** mapowanie, żeby dołączyć stronę P1S jako dodatkowe zadania.

```text
SAP CES → projekt CES / WBS CES → mapowanie PZL-EV (globalne) → element P1S
```

---

## 2. Źródło prawdy – raport mapowań SAP↔CES

Powiązanie CES ↔ P1S pochodzi z **raportu mapowań SAP↔CES**, czytanego bezpośrednio z bazy `PZLPROD` (tylko
odczyt, źródło przyrostowe – `docs/model-danych.md`, rozdz. 3.4). Do czasu udostępnienia raportu w `PZLPROD`
źródłem pierwszego zasilenia jest jego ekstrakt CSV.

Kolumny:

`src, pspnr, pspnr_sap, pspnr_ces, pspnr_parent, project, project_sap, project_sap_org, project_ces, wbs,
wbs_sap, wbs_ces, wbs_desc(_sap/_ces), prctr(_sap/_ces), lvl(_sap/_ces), perf_obg, techs, sales_order_typ,
sales_order, sales_order_pos, matnr, network`

| Wiersz | Znaczenie | Przykład |
|---|---|---|
| `src` = SAP, wypełnione `pspnr_ces` | odpowiednik **1:1** elementu P1S i CES | `AC-LH8.1.01.01` ENG ↔ `4D02U8000001` |
| `src` = CES, wypełnione `pspnr_sap` | element **tylko w CES**, wskazany w SAP | korzeń projektu CES → `PROJORG`; `.RA` → zlecenie sprzedaży; `.02` → element `.03` |
| element tylko SAP | brak odpowiednika CES | np. „kontrakt” |

- **1 `PROJORG` = wiele projektów CES.** Projekt CES odpowiada poddrzewu zlecenia sprzedaży w SAP
  (`project_sap`, np. `4D02U8` ↔ `AC-LH8.1.01`).
- **Dopasowanie wyłącznie po `pspnr`** – poziomy SAP i CES się różnią, a opisy się powtarzają, więc ani
  poziom, ani opis nie są kluczem.
- Raport wskazuje element P1S dokładnie (nie zawsze `PROJORG`).
- Połączenie z drzewem P1S: `LOG.WBS.PSPNR` = `pspnr_sap` (`docs/zrodla-danych.md`, rozdz. 5).

---

## 3. WBS CES spoza raportu – dziedziczenie

WBS CES, którego nie ma w raporcie (np. nowy element z importu RABIT), **dziedziczy** odpowiednik swojego
projektu CES (`project_sap`). Dziedziczenie jest logiczne – nie powstają rekordy dla poszczególnych WBS.
Projekt CES elementu wynika z kolumny `Project Definition` raportu kosztów (`docs/zrodla-danych.md`, rozdz. 4).

---

## 4. Korekty

Korekty są **globalne** i mają **pierwszeństwo przed raportem**:

| Korekta | Działanie |
|---|---|
| **korekta elementu CES** | nowy cel P1S dla jednego elementu CES; może wskazać dowolny element P1S |
| **korekta projektu CES** | nowy cel dla **WBS spoza raportu** tego projektu CES (rozdz. 3); WBS z raportu – bez zmian (O20) |

- Korekta zapisuje się z historią: kto, kiedy, poprzedni i nowy cel, `valid_from` / `valid_to`.
- Zmiana względem raportu wymaga **uzasadnienia**.
- **Usunięcie korekty** (zamknięcie `valid_to`) przywraca przypisanie z raportu albo dziedziczenie.
- Przypisania z raportu **nie da się usunąć** – można je tylko skorygować.

---

## 5. Kolejność rozstrzygania i statusy

```text
element CES → 1. korekta elementu                                  → OVERRIDE
            → 2. raport mapowań (pspnr_ces / pspnr_sap)              → REPORT
            → 3. WBS spoza raportu: korekta projektu CES,
                 a bez niej odpowiednik projektu CES (project_sap)    → INHERITED
            → 4. brak                                                → UNMAPPED
```

| Status | Znaczenie |
|---|---|
| `OVERRIDE` | korekta elementu CES (z uzasadnieniem) |
| `REPORT` | przypisanie z raportu mapowań |
| `INHERITED` | WBS spoza raportu – cel projektu CES (z korekty projektu CES albo z raportu) |
| `UNMAPPED` | brak celu |

Statusy są **wyliczane**, nie zapisywane.

---

## 6. Krotność

- **Wiele elementów CES → jeden element P1S** – dozwolone.
- **Jeden element CES → wiele elementów P1S – niedozwolone.** Element CES ma w danym momencie co najwyżej
  jeden cel P1S, więc jego koszt jest pokazywany dokładnie raz – bez powielania i bez dzielenia.

---

## 7. Koszt bez przypisania

- Każdy koszt z raportów CES jest liczony. Koszt elementu `UNMAPPED` jest pokazywany, ale nie trafia do EV
  żadnego projektu.
- Postępowanie z takim kosztem w przebiegu – `docs/pipeline-fazy.md`, P3.
- Status „świadomie brak odpowiednika P1S” (`NO_P1S`) nie występuje – zostanie dodany, gdy pojawi się
  rzeczywisty przypadek. Element bez decyzji ma status `UNMAPPED`.

---

## 8. Model danych

Przypisania z raportu mapowań nie są kopiowane – czytane są z `PZLPROD`. W bazie PZL-EV (`dict`) zapisywane
są tylko **korekty**:

| Pole | Opis |
|---|---|
| `id` | identyfikator korekty |
| `typ` | `ELEMENT` (korekta elementu CES) / `PROJEKT` (korekta projektu CES) |
| `ces_pspnr` / `ces_project` | korygowany element CES albo projekt CES |
| `p1s_pspnr` | nowy cel P1S |
| `uzasadnienie` | obowiązkowe przy zmianie względem raportu |
| `valid_from`, `valid_to` | okres obowiązywania |
| `created_at/by`, `updated_at/by` | audyt |

Przebieg czyta korekty w stanie na swój znacznik stanu (`docs/model-danych.md`, rozdz. 4.2).

---

## 9. Po imporcie

Po każdym imporcie (`docs/pipeline-fazy.md`, G2) nowe elementy CES dostają wynik rozstrzygania i trafiają na
listę wymagających uwagi, np.:

```text
4D03GZ000041   → INHERITED: AC-I39
4D03H1000038   → UNMAPPED
```

Jedno przypisanie projektu CES (w raporcie albo korektą projektu CES) obsługuje wszystkie obecne i przyszłe
WBS tego projektu spoza raportu. Ręczna interwencja jest potrzebna tylko przy braku celu albo potrzebie zmiany
przypisania.

---

## 10. Reguły walidacji

Poziomy ERROR / WARNING – `docs/pipeline-fazy.md`, rozdz. 1.3.

| Reguła | Poziom |
|---|---|
| element CES ma co najwyżej jeden aktywny cel P1S | ERROR |
| korekta zmieniająca przypisanie z raportu ma uzasadnienie | ERROR |
| przypisania z raportu nie można usunąć – tylko skorygować | ERROR |
| cel korekty to element P1S nieaktywny albo usunięty (`Z_ACTIVE`, `LOEKZ`) | WARNING |

---

## 11. Widok (ekran Mapowanie CES ↔ P1S)

- **Dwa drzewa** CES | P1S w strukturze drzewa P1S (`docs/zrodla-danych.md`, rozdz. 5.3).
- Element CES jest umieszczony według elementu P1S, do którego jest przypisany – kategorię dostaje przez
  przypisanie (w mapowaniu kategorii się nie przypisuje).
- Elementy bez celu są w węźle **„Nieprzypisane”**; dla projektów CES spoza raportu – z propozycją celu.
- Statusy przy elementach; wyszukiwanie.
- Akcje: korekta elementu CES, korekta projektu CES (z uzasadnieniem), usunięcie korekty, historia.
- Bez kwot i drzewa kosztów.

---

## 12. Otwarte kwestie

| # | Kwestia |
|---|---|
| O20 | Czy korekta projektu CES ma przenosić także jego WBS z raportu mapowań? Dziś przenosi tylko WBS spoza raportu |
| O21 | Kategoria jest w każdym wierszu `LOG.WBS`, a drzewo grupuje `PROJORG` według jego wiersza (`STUFE` 1). Czy elementy jednego `PROJORG` mogą mieć różne kategorie? |
