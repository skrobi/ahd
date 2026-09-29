# Etap 1: pobranie plików RABIT i import do bazy

Wersja: 1.0 (rozwiązanie docelowe, MVP)

## Zasada

1. **Pobierz** – pliki z folderu RABIT na SharePoint są kopiowane przez **WebDAV** (jak „Otwórz
   w Eksploratorze”, konto Windows użytkownika) do wspólnego folderu `00_Global\RABIT\Do_importu`.
   Kopiowane są tylko pliki nowe i zmienione (rozmiar, data modyfikacji).
2. **Importuj** – wszystkie pliki z `Do_importu` trafiają do bazy, ale **tylko te o nowej treści**
   (odcisk SHA-256). Na etapie importu **nie wiadomo, do którego zakresu** należy plik – wiersze są
   zapisywane w postaci surowej, a przypisanie do zakresów odbywa się w przebiegu zakresu (słownik
   „Struktura projektowa”).
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
python -m ahd.etap1 pobierz --webdav "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659" --cel "\\serwer\udzial\AHD\00_Global\RABIT\Do_importu" --dry-run
python -m ahd.etap1 pobierz --webdav "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659" --cel "\\serwer\udzial\AHD\00_Global\RABIT\Do_importu"
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

## 3. Import do bazy

```bat
python -m ahd.etap1 import --folder "\\serwer\udzial\AHD\00_Global\RABIT\Do_importu" --landing "\\serwer\udzial\AHD\01_LandingZone"
```

Decyzja dla każdego pliku:

| Decyzja | Znaczenie |
|---|---|
| zaimportowany | nowa treść – kopia w Landing Zone (`<RRRR-MM-DD>\<IdImportu>\`), wiersze w bazie |
| pominiety (metadane) | ten sam plik (ścieżka, rozmiar, data) co przy poprzednim imporcie – nie jest nawet czytany |
| duplikat | treść już jest w bazie (także pod inną nazwą) – komunikat mówi, kto i kiedy ją zaimportował |
| blad | np. uszkodzony plik – pozostałe pliki importują się dalej |

Opcje:
- `--baza` – adres bazy; domyślnie plik SQLite `ahd_mvp.sqlite` w katalogu Landing Zone (do czasu
  ustalenia bazy docelowej). MS SQL: `--baza mssql://SERWER/AHD_TEST` (konto Windows; tabele tworzy
  administrator skryptem `sql/mssql/001_etap1_import.sql`),
- `--folder` może wskazywać także pojedynczy plik,
- `--pelne-sprawdzenie` – liczy hash także plików bez zmian w metadanych,
- `--webdav "<link>"` zamiast `--folder` – import bezpośrednio z SharePoint, bez kopii w `Do_importu`.

Pliki wieloczęściowe (`_cz1`, `_cz2`, `part 3`, `(2)`) są sprawdzane jako zestaw: te same kolumny,
łączna liczba wierszy.

---

## 4. Historia importów

```bat
python -m ahd.etap1 historia --landing "\\serwer\udzial\AHD\01_LandingZone"
```

---

## 5. Co jest w bazie

| Tabela | Zawartość |
|---|---|
| `meta.ImportBatch` | każde uruchomienie importu: kto, komputer, wersja aplikacji, liczniki, status |
| `meta.SourceFile` | każdy unikalny plik (klucz: SHA-256): nazwa, źródło, ścieżka w Landing Zone, kolumny, sygnatura kolumn, typ raportu, liczba wierszy |
| `meta.SourceFileSeen` | każdy plik widziany w każdym imporcie i decyzja |
| `stg.RawRow` | surowe wiersze: hash pliku, numer wiersza, wartości (JSON) |

**Typ raportu** jest wstępnie rozpoznawany po nazwie (CJI3, ZRD_KKAJ, NET_INV, inaczej „nieznany”).
Pliki RABIT mają nazwy robocze (np. „B6 AC1-2”, „PAF2 hedge Status”), dlatego typ będzie rozpoznawany
po **sygnaturze kolumn** – słownik sygnatur powstanie na podstawie pierwszych importów.

Wydajność (test): plik CSV 700 000 wierszy / 85 MB – import do SQLite ok. 13 s.

---

## 6. Testy (developer)

```bat
python -m pytest -q tests
```

Testy działają na folderach lokalnych (WebDAV jest dla aplikacji zwykłym folderem UNC) i bazie SQLite.
Wariant MS SQL (`mssql://`) wymaga serwera w sieci PZL.
