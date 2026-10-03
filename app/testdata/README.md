# Dane wzorcowe (F0.9)

Małe, syntetyczne pliki na wzór danych źródłowych – do ręcznego testu aplikacji i do testów automatycznych
(`tests/`). Zastąpią je zanonimizowane próbki z rzeczywistych źródeł, gdy będą dostępne. Oczekiwane wartości
służą do sprawdzenia przepływu danych (liczba wierszy i sumy kontrolne na każdym etapie).

## RABIT

| Plik | Źródło | Wiersze | Suma `Value in Obj. Crcy` (PLN) | Suma `Val.in rep.cur.` (USD) |
|---|---|---|---|---|
| `RABIT/ACTUALS_PAF_01.csv` | `ACTUALS_PAF` | 6 | 10 574,11 | 2 203,12 |

Format: CSV, separator `;`, UTF-8, liczby w formacie polskim (spacja tysięcy, przecinek dziesiętny, minus na
końcu dla korekty – zapis SAP). Układ kolumn `ACTUALS_*` z 2026-10 (23 kolumny, m.in. `Original Order Number`, `Item`,
`Purchase order number`, `Invoice Number`) – parser ACTUALS (migracja 004), `docs/zrodla-danych.md`, rozdz. 4.

**Ręczny test importu:** skopiuj plik do folderu `Do_importu` środowiska
(`<NetworkRoot>\00_Global\RABIT\Do_importu`; domyślnie `%LOCALAPPDATA%\PZL-EV\TEST-root\…`) i uruchom
**Importuj** na ekranie Import RABIT.
