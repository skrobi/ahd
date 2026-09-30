# Etap 1: pobranie plików RABIT i import do bazy

Wersja: 1.2 (rozwiązanie docelowe, MVP; 1.2: usunięta kompletność źródeł projektów – M21)

## Zasada

**Plik mówi, czym jest.** Zakres danych projektu wynika z jego elementów w drzewie P1S (M21) – import
nie zna projektów, a jedna paczka RABIT może obejmować wiele projektów.

```
RABIT → plik XLSX → PREFIKS → źródło → import do bazy → historia + hash
```

1. **Pobierz** – pliki z folderu RABIT na SharePoint są kopiowane przez **WebDAV** (jak „Otwórz
   w Eksploratorze”, konto Windows użytkownika) do wspólnego folderu `00_Global\RABIT\Do_importu`.
   Kopiowane są tylko pliki nowe i zmienione (rozmiar, data modyfikacji).
2. **Importuj** – źródło pliku rozpoznawane jest po **prefiksie nazwy** (np. `ACTUALS_PAF_01.xlsx` →
   `ACTUALS_PAF`). Importowany jest **każdy rozpoznany plik** samodzielnie, ale **tylko o nowej treści**
   (hash SHA-256 = tożsamość fizycznego pliku). Plik bez pasującego prefiksu nie jest importowany.
3. **Historia** – każdy widzi, kto, kiedy i co zaimportował.

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

## 3. Konfiguracja źródeł

> **Rozwiązanie przejściowe.** Docelowo prefiksy RABIT (M15) są
> konfiguracją w bazie słowników SQLite, edytowaną w aplikacji (D26, `docs/mapowanie-ces-p1s.md`).
> Plik CSV poniżej obowiązuje tylko w MVP etapu 1 – do czasu powstania bazy słowników z interfejsem.

Katalog `konfiguracja` (w repozytorium; inną lokalizację wskazuje `--konfiguracja` albo zmienna
`PZL_EV_KONFIGURACJA`, np. `\\serwer\udzial\PZL-EV\00_Global\Konfiguracja`). Plik CSV, separator `;`,
edycja w edytorze tekstu lub Excelu, wiersze z `#` pomijane. W repozytorium jest **przykład** do zastąpienia.

`zrodla_rabit.csv` – prefiks nazwy pliku → źródło (wygrywa najdłuższy pasujący prefiks, wielkość liter bez znaczenia):

```
Prefiks;KodZrodla;Opis
ACTUALS_PAF;ACTUALS_PAF;Koszty rzeczywiste PAF
FORECAST_PAF;FORECAST_PAF;Prognoza PAF
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

## 5. Historia importów

```bat
python -m pzl_ev.etap1 historia --landing "\\serwer\udzial\PZL-EV\01_LandingZone"
```

---

## 6. Co jest w bazie

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

## 7. Testy (developer)

```bat
python -m pytest -q tests
```

Testy działają na folderach lokalnych (WebDAV jest dla aplikacji zwykłym folderem UNC) i bazie SQLite.
Wariant MS SQL (`mssql://`) wymaga serwera w sieci PZL.
