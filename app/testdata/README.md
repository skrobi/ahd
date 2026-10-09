# Dane wzorcowe (F0.9)

Małe, syntetyczne pliki na wzór danych źródłowych – do ręcznego testu aplikacji i do testów automatycznych
(`tests/`). Zastąpią je zanonimizowane próbki z rzeczywistych źródeł, gdy będą dostępne. Oczekiwane wartości
służą do sprawdzenia przepływu danych (liczba wierszy i sumy kontrolne na każdym etapie).

## RABIT

| Plik | Źródło | Wiersze | Suma `Value in Obj. Crcy` (PLN) | Suma `Val.in rep.cur.` (USD) |
|---|---|---|---|---|
| `RABIT/ACTUALS_PAF_01.csv` | `ACTUALS_PAF` | 6 | 10 574,11 | 2 203,12 |

## Mapowanie

| Plik | Zawartość | Oczekiwane |
|---|---|---|
| `Mapowanie/Raport_mapowan.csv` | raport mapowań SAP↔CES w pełnym układzie (28 kolumn, `docs/mapowanie-ces-p1s.md`, rozdz. 2) – słownik „Raport mapowań CES ↔ P1S” | 27 wierszy (20 SAP, 7 CES), bez błędów i ostrzeżeń; projekty CES `4D02U8`, `4D02UH`, `4D02UR` → `AC-LH8.1.01`, `.1.02`, `.1.03`; np. `4D02U8000001` → PSPNR `14217226`, `4D02U8.RA` → `14217225` |

## Projekty

| Plik | Zawartość | Oczekiwane |
|---|---|---|
| `Projekty/PO_M28.xlsx` | eksport struktury WBS z SAP (układ `docs/performance-objectives.md`, rozdz. 5), projekt CES `4D06WP` | 8 elementów, 4 poziomy: `4D06WP` → `4D06WP.RA` → `4D06WP.01` (`…000001`, `…000002`), `4D06WP.02` (`…000003`, `…000004`) |
| `Projekty/Slowniki_M28.xlsx` | słowniki projektu M28: arkusze „WP i CAM”, „Harmonogram i budżet”, „Wykluczenia”, „Cost Category projektu” | 4 elementy P1S, 3 WP, 2 CAM; BAC HOURS 2 250, BAC MATERIAL 75 000,50; 2 wykluczenia; 1 zmiana Cost Category (`0057100000`) |

Format plików RABIT: CSV, separator `;`, UTF-8, liczby w formacie polskim (spacja tysięcy, przecinek dziesiętny, minus na
końcu dla korekty – zapis SAP). Układ kolumn `ACTUALS_*` z 2026-10 (23 kolumny, m.in. `Original Order Number`, `Item`,
`Purchase order number`, `Invoice Number`) – parser ACTUALS (migracja 004), `docs/zrodla-danych.md`, rozdz. 4.

**Ręczny test importu:** skopiuj plik do folderu `Do_importu` środowiska
(`<NetworkRoot>\00_Global\RABIT\Do_importu`; domyślnie `%LOCALAPPDATA%\PZL-EV\TEST-root\…`) i uruchom
**Importuj** na ekranie Import RABIT.
