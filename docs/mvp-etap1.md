# Etap 1: pobranie plików RABIT i import do bazy

Wersja: 1.1 (rozwiązanie docelowe, MVP)

## Zasada

**Plik mówi, czym jest – projekt mówi, czego potrzebuje.**

```
RABIT → plik XLSX → PREFIKS → źródło → import do bazy → historia + hash → PROJEKT → wymagane źródła
```

1. **Pobierz** – pliki z folderu RABIT na SharePoint są kopiowane przez **WebDAV** (jak „Otwórz
   w Eksploratorze”, konto Windows użytkownika) do wspólnego folderu `00_Global\RABIT\Do_importu`.
   Kopiowane są tylko pliki nowe i zmienione (rozmiar, data modyfikacji).
2. **Importuj** – źródło pliku rozpoznawane jest po **prefiksie nazwy** (np. `ACTUALS_PAF_01.xlsx` →
   `ACTUALS_PAF`). Importowany jest **każdy rozpoznany plik** samodzielnie, ale **tylko o nowej treści**
   (hash SHA-256 = tożsamość fizycznego pliku). Plik bez pasującego prefiksu nie jest importowany.
3. **Kompletność** – każdy projekt ma listę wymaganych źródeł; PZL-EV pokazuje, czego brakuje.
4. **Historia** – każdy widzi, kto, kiedy i co zaimportował.

---

## 1. Instalacja (Windows, jednorazowo)

```bat
py -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```

Wymagany Python 3.10+. Dla bazy MS SQL dodatkowo `pip install pyodbc` i sterownik
„ODBC Driver 18 for SQL Server”.

Usługa Windows **WebClient** musi działać (standardowo uruchamia się przy pierwszym użyciu WebDAV).

---

## 2. Pobranie plików RABIT

```bat
python -m pzl_ev.etap1 pobierz --webdav "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659" --cel "\\serwer\udzial\PZL-EV\00_Global\RABIT\Do_importu" --dry-run
python -m pzl_ev.etap1 pobierz --webdav "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659" --cel "\\serwer\udzial\PZL-EV\00_Global\RABIT\Do_importu"
```

- `--webdav` przyjmuje link do folderu skopiowany z przeglądarki (także widok `AllItems.aspx?RootFolder=…`)
  albo gotową ścieżkę `\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659`.
- Status każdego pliku: **skopiowany**, **bez zmian**, **błąd**.
- `--filtr "*.xlsx"` – tylko wybrane pliki; `--dry-run` – tylko podgląd.
- `lista --webdav "<link>"` – same nazwy, rozmiary i daty plików.

**Limit 50 MB:** usługa WebClient domyślnie nie pobiera plików większych niż ok. 50 MB. Taki plik dostanie
status błędu z podpowiedzią. Administrator może zwiększyć `FileSizeLimitInBytes`
(`HKLM\SYSTEM\CurrentControlSet\Services\WebClient\Parameters`); do tego czasu plik pobiera się ręcznie
w przeglądarce i zapisuje w `Do_importu` – import potraktuje go tak samo.

---

## 3. Konfiguracja źródeł i projektów

> **Rozwiązanie przejściowe.** Docelowo prefiksy RABIT (M15) i wymagane źródła projektów (M10 – zastąpione przez M21: usunięte) są
> konfiguracją w bazie słowników SQLite, edytowaną w aplikacji (D26, `docs/mapowanie-ces-p1s.md`).
> Pliki CSV poniżej obowiązują tylko w MVP etapu 1 – do czasu powstania bazy słowników z interfejsem.

Katalog `konfiguracja` (w repozytorium; inną lokalizację wskazuje `--konfiguracja` albo zmienna
`PZL_EV_KONFIGURACJA`, np. `\\serwer\udzial\PZL-EV\00_Global\Konfiguracja`). Pliki CSV, separator `;`,
edycja w edytorze tekstu lub Excelu, wiersze z `#` pomijane. W repozytorium są **przykłady** do zastąpienia.

`zrodla_rabit.csv` – prefiks nazwy pliku → źródło (wygrywa najdłuższy pasujący prefiks, wielkość liter bez znaczenia):

```
Prefiks;KodZrodla;Opis
ACTUALS_PAF;ACTUALS_PAF;Koszty rzeczywiste PAF
FORECAST_PAF;FORECAST_PAF;Prognoza PAF
```

`projekty_zrodla.csv` – projekt → wymagane źródła (jeden wiersz = jedno źródło):

```
Projekt;KodZrodla
PAF-001;ACTUALS_PAF
PAF-001;FORECAST_PAF
ABC-002;ACTUALS_PAF
```

`lista --webdav "<link>"` albo `lista --folder …` pokazuje, jakie źródło zostanie rozpoznane dla każdego pliku.

---

## 4. Import do bazy

```bat
python -m pzl_ev.etap1 import --folder "\\serwer\udzial\PZL-EV\00_Global\RABIT\Do_importu" --landing "\\serwer\udzial\PZL-EV\01_LandingZone"
```

Decyzja dla każdego pliku:

| Decyzja | Znaczenie |
|---|---|
| zaimportowany | rozpoznany plik o nowej treści – kopia w Landing Zone (`<RRRR-MM-DD>\<IdImportu>\`), wiersze w bazie z kodem źródła |
| pominiety (metadane) | ten sam plik (ścieżka, rozmiar, data) co przy poprzednim imporcie – nie jest nawet czytany |
| duplikat | ten fizyczny plik (hash) już jest w bazie, także pod inną nazwą – komunikat mówi, kto i kiedy go zaimportował |
| nierozpoznany | brak pasującego prefiksu w `zrodla_rabit.csv` – plik nie jest importowany; po dopisaniu prefiksu zaimportuje się przy kolejnym uruchomieniu |
| blad | np. uszkodzony plik – pozostałe pliki importują się dalej |

RABIT nadpisuje plik tą samą nazwą: każda nowa treść to nowa wersja w historii (poprzednie zostają).
Kilka plików jednego źródła (`ACTUALS_PAF_01`, `_02`, `_03`) importuje się niezależnie, nawet jeśli
mają różne kolumny.

Opcje:
- `--baza` – adres bazy; domyślnie plik SQLite `pzl_ev_mvp.sqlite` w katalogu Landing Zone (do czasu
  ustalenia bazy docelowej). MS SQL: `--baza mssql://SERWER/PZL_EV_TEST` (konto Windows; tabele tworzy
  administrator skryptem `sql/mssql/001_etap1_import.sql`),
- `--folder` może wskazywać także pojedynczy plik,
- `--pelne-sprawdzenie` – liczy hash także plików bez zmian w metadanych,
- `--webdav "<link>"` zamiast `--folder` – import bezpośrednio z SharePoint, bez kopii w `Do_importu`.

---

## 5. Kompletność źródeł projektów

> **Docelowo usunięte (M21, `docs/mapowanie-ces-p1s.md`).** Słownik „Wymagane źródła projektów” i kontrola
> kompletności nie wchodzą do aplikacji: zakres danych projektu wynika z jego węzłów w drzewie P1S, a przebieg
> przypina wszystkie zaimportowane pliki. Komenda `kompletnosc` i `projekty_zrodla.csv` zostają w kodzie MVP
> bez zmian.

```bat
python -m pzl_ev.etap1 kompletnosc --landing "\\serwer\udzial\PZL-EV\01_LandingZone" --maks-wiek-dni 7
```

```
PAF-001: NIEKOMPLETNY
  ✓ ACTUALS_PAF            ostatni import 2026-09-29T08:10 (0 dni), raport z 2026-09-29T05:12, plików 3, ostatni: ACTUALS_PAF_03.xlsx
  ✓ FORECAST_PAF           ostatni import 2026-09-29T08:10 (0 dni), raport z 2026-09-28T22:00, plików 1, ostatni: FORECAST_PAF.xlsx
  ✗ ETC_PAF                brak importu
ABC-002: komplet
  …
Projektów: 2, niekompletnych: 1
```

✗ – brak importu, ⚠ – ostatni import starszy niż `--maks-wiek-dni`. Kod wyjścia 1, gdy którykolwiek
projekt jest niekompletny. Sprawdzenie, czy import obejmuje bieżący okres – w kolejnym kroku.

---

## 6. Historia importów

```bat
python -m pzl_ev.etap1 historia --landing "\\serwer\udzial\PZL-EV\01_LandingZone"
```

---

## 7. Co jest w bazie

| Tabela | Zawartość |
|---|---|
| `meta.ImportBatch` | każde uruchomienie importu: kto, komputer, wersja aplikacji, liczniki, status |
| `meta.SourceFile` | każdy unikalny plik (klucz: SHA-256): nazwa, **kod źródła**, ścieżka w Landing Zone, data raportu, kolumny, sygnatura kolumn, liczba wierszy |
| `meta.SourceFileSeen` | każdy plik widziany w każdym imporcie i decyzja |
| `stg.RawRow` | surowe wiersze: hash pliku, numer wiersza, wartości (JSON); widok `stg.vRawRowZrodlo` (MS SQL) dokłada kod źródła i import |

**Sygnatura kolumn** (odcisk układu nagłówków) pozwala zauważyć, że RABIT/SAP zmienił układ raportu.
Tabele typowane per źródło (mapowanie kolumn) powstaną, gdy będzie wiadomo, co zawierają raporty.

Baza SQLite z wcześniejszej wersji MVP jest aktualizowana automatycznie (kolumna `TypRaportu` → `KodZrodla`).

Wydajność (test): plik CSV 700 000 wierszy / 85 MB – import do SQLite ok. 13 s.

---

## 8. Testy (developer)

```bat
python -m pytest -q tests
```

Testy działają na folderach lokalnych (WebDAV jest dla aplikacji zwykłym folderem UNC) i bazie SQLite.
Wariant MS SQL (`mssql://`) wymaga serwera w sieci PZL.
