# MVP – Etap 1: import plików SAP (RABIT)

Wersja: 0.3 (test działania, nie gotowe rozwiązanie)

## Zasada

Na etapie pobierania z RABIT **nie wiadomo, do którego zakresu należą pliki**. Dlatego import:

1. bierze **wszystkie pliki** z folderu (SharePoint albo folder lokalny / sieciowy),
2. importuje do bazy **tylko pliki, których jeszcze nie było** – decyduje odcisk treści (SHA-256),
   nie nazwa pliku,
3. pliku o tych samych metadanych co przy poprzednim imporcie (ścieżka, rozmiar, data modyfikacji)
   **nie pobiera ponownie** – to oszczędza czas przy dużych plikach,
4. ładuje wiersze do bazy w **postaci surowej** (wartości w kolejności kolumn + zapisane nagłówki),
5. zapisuje w bazie, kto, kiedy i co zaimportował – każdy widzi, co już zostało przetworzone.

Przypisanie wierszy do zakresów (F16, S70i…) odbywa się później, w przebiegu zakresu, na podstawie
słownika „Struktura projektowa” (element WBS → zakres).

Logowanie do SharePoint – kontem zalogowanego użytkownika Windows (SSO), bez podawania hasła.

---

## 1. Instalacja (Windows, jednorazowo)

```bat
py -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```

Wymagany Python 3.10+. Dla bazy MS SQL dodatkowo `pip install pyodbc` i sterownik
„ODBC Driver 18 for SQL Server”.

---

## 2. Test połączenia z SharePoint

```bat
python -m ahd.etap1 sprawdz --url "<link do folderu skopiowany z przeglądarki>"
```

Oczekiwany wynik:

```
Witryna: https://lmsp4-intl.external.lmco.com/sites/RabbitReporting
Folder : /sites/RabbitReporting/Shared Documents/E456659
Zalogowano jako: i:0#.w|DOMENA\uzytkownik
W folderze: 12 plików, 0 podfolderów
```

| Komunikat | Co zrobić |
|---|---|
| `Odmowa dostępu (HTTP 401) … metody logowania: Negotiate, NTLM` | SSO nie przeszło – spróbuj `--auth ntlm` (login i hasło w konsoli, nie są zapisywane) |
| `Odmowa dostępu … metody logowania: brak` / przekierowanie na stronę logowania | serwis wymaga logowania w przeglądarce (ADFS / karta) – użyj wariantu z folderem (pkt 5) |
| `SharePoint nie zwrócił danych w formacie JSON` + lista przekierowań | komunikat pokazuje, dokąd serwer przekierował (np. strona logowania ADFS) i co zrobić; przekaż ten wynik – nie zawiera danych z plików |
| `Błąd certyfikatu TLS` | sprawdź instalację pakietu `truststore`; ewentualnie `--ca-bundle` |
| `Brak połączenia` | sieć / VPN / proxy |

---

## 3. Lista plików

```bat
python -m ahd.etap1 lista --url "<link>"
```

---

## 4. Import

```bat
python -m ahd.etap1 import --url "<link>" --landing C:\AHD_TEST\LandingZone --dry-run
python -m ahd.etap1 import --url "<link>" --landing C:\AHD_TEST\LandingZone
```

Przykładowy wynik:

```
[1/4] CJI3_F16_cz1.csv: zaimportowany – 498 212 wierszy, typ CJI3
[2/4] CJI3_F16_cz2.csv: zaimportowany – 214 218 wierszy, typ CJI3
[3/4] ZRD_KKAJ_F16.xlsx: pominiety (metadane) – bez zmian od poprzedniego importu
[4/4] Kopia ZRD.xlsx: duplikat – treść już zaimportowana jako ZRD_KKAJ_F16.xlsx (jkowalski, 2026-09-28T08:31:02)
Zestaw CJI3_F16: 2 części, identyczny układ 34 kolumn (712 430 wierszy)
Import IMP-20260929-083104-a1f3: zaimportowane 2, bez zmian 1, duplikaty 1, błędy 0
```

Opcje:
- `--baza` – adres bazy; domyślnie plik SQLite `ahd_mvp.sqlite` w katalogu Landing Zone
  (MVP, do czasu ustalenia bazy docelowej). MS SQL: `--baza mssql://SERWER/AHD_TEST`
  (logowanie kontem Windows; tabele tworzy administrator skryptem `sql/mssql/001_etap1_import.sql`),
- `--filtr "*.csv"` – tylko wybrane pliki (można powtórzyć),
- `--rekurencyjnie` – także podfoldery,
- `--pelne-sprawdzenie` – pobiera i liczy hash także plików bez zmian w metadanych.

Pliki nowe trafiają do `LandingZone\<RRRR-MM-DD>\<IdImportu>\`. Duplikaty nie są przechowywane.
Uszkodzony plik jest oznaczany jako błąd i nie przerywa importu pozostałych.

---

## 3a. RABIT przez WebDAV – metoda podstawowa (sprawdzona 29.09.2026)

Test dostępu wykazał, że działa **WebDAV** (jak „Otwórz w Eksploratorze”) – logowanie kontem Windows,
bez synchronizacji i bez API. Moduł zamienia link do folderu na ścieżkę
`\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659`.

Skopiowanie wszystkich plików na dysk (kolejne uruchomienia kopiują tylko nowe i zmienione):

```bat
python -m ahd.etap1 pobierz --webdav "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659" --cel "\\serwer\udzial\AHD\00_Global\RABIT\Do_importu" --dry-run
python -m ahd.etap1 pobierz --webdav "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659" --cel "\\serwer\udzial\AHD\00_Global\RABIT\Do_importu"
```

Import z folderu na dysku (albo bezpośrednio z WebDAV: `import --webdav "<link>" --landing …`):

```bat
python -m ahd.etap1 import --folder "\\serwer\udzial\AHD\00_Global\RABIT\Do_importu" --landing "\\serwer\udzial\AHD\01_LandingZone"
```

Do testu lokalnie wystarczy `--cel C:\AHD_TEST\Do_importu` i `--landing C:\AHD_TEST\LandingZone`.

**Limit 50 MB:** usługa WebClient domyślnie nie pobiera plików większych niż ok. 50 MB. Taki plik
zostanie oznaczony błędem z podpowiedzią – administrator może zwiększyć `FileSizeLimitInBytes`
(`HKLM\SYSTEM\CurrentControlSet\Services\WebClient\Parameters`), a do tego czasu plik pobiera się ręcznie.

---

## 4a. Test dostępu do RABIT (które drogi działają)

Jednorazowy test – sprawdza cztery metody i zapisuje raport bez zawartości plików:

```bat
python narzedzia\test_dostepu_rabit.py --plik-url "<link do JEDNEGO pliku z folderu E456659>"
```

| Metoda | Co sprawdza |
|---|---|
| A | `owssvr.dll?XMLDATA=1` – to samo co „Eksport do Excela”, lista plików przez HTTP (SSO) |
| B | provider `Microsoft.Office.List.OLEDB.2.0` przez ADODB – jak połączenie w Excelu (definicja `<LIST>`) |
| C | WebDAV `\\host@SSL\DavWWWRoot\…` – jak „Otwórz w Eksploratorze” |
| D | bezpośrednie pobranie jednego pliku przez HTTP (SSO) |

Link do pliku: w bibliotece przy pliku „…” → „Kopiuj link” (albo prawy przycisk na nazwie → „Kopiuj adres linku”).
Definicję listy można podać z połączenia Excela (`--lista-xml plik.xml`) albo z pliku `.iqy` (`--iqy`);
domyślnie używana jest definicja folderu E456659.

Provider OLEDB (B) wymaga Pythona o tej samej bitowości co Office (np. Office 32-bit → Python 32-bit);
skrypt wypisuje bitowość. Na końcu skrypt podaje wniosek, a raport zapisuje w `raport_dostepu_rabit.json`
(nazwy plików – przejrzyj przed wysłaniem).

---

## 5. Wariant: ręczne pobranie z przeglądarki (gdy SharePoint wymaga logowania w przeglądarce)

Gdy `sprawdz` pokazuje przekierowanie na stronę logowania, a synchronizacja nie jest dostępna:

1. Folder na pobrane pliki – docelowo wspólny na dysku sieciowym: `\\serwer\udzial\AHD\00_Global\RABIT\Do_importu`
   (do testu wystarczy lokalny, np. `C:\AHD_TEST\Do_importu`).
2. Otwórz folder RABIT w przeglądarce, zaznacz wszystkie pliki (pole wyboru w nagłówku listy) i kliknij
   **Pobierz**. Przy kilku plikach SharePoint zapisze jeden plik ZIP (np. `OneDrive_1_29-09-2026.zip`).
3. Przenieś ZIP (albo pojedyncze pliki) do `C:\AHD_TEST\Do_importu`. Rozpakowywać nie trzeba.
   Jeśli pobranie kilku plików naraz (ZIP) jest zablokowane – pobieraj pliki pojedynczo; import można
   też wskazać na jeden plik: `--folder C:\AHD_TEST\Do_importu\CJI3_F16.xlsx`.
4. Uruchom import:

```bat
python -m ahd.etap1 import --folder C:\AHD_TEST\Do_importu --landing C:\AHD_TEST\LandingZone --dry-run
python -m ahd.etap1 import --folder C:\AHD_TEST\Do_importu --landing C:\AHD_TEST\LandingZone
```

Co tydzień można pobrać **wszystkie** pliki ponownie – zaimportowane zostaną tylko pliki o nowej treści,
reszta będzie oznaczona jako duplikat lub „bez zmian”. Stare ZIP-y można zostawić w folderze albo usunąć.
Można też wskazać bezpośrednio plik ZIP: `--folder C:\Users\<ja>\Downloads\OneDrive_1_29-09-2026.zip`.

Ten sam wariant działa dla dysku sieciowego (`--folder "\\serwer\udzial\RABIT"`).
Pliki blokady Excela (`~$…`) są pomijane.

---

## 6. Historia importów

```bat
python -m ahd.etap1 historia --landing C:\AHD_TEST\LandingZone
```

Pokazuje ostatnie importy (kto, ile nowych / pominiętych / błędów) i ostatnio zaimportowane pliki
(kto, typ raportu, liczba wierszy, hash).

---

## 7. Co jest w bazie

| Tabela | Zawartość |
|---|---|
| `meta.ImportBatch` | każde uruchomienie importu: kto, komputer, wersja aplikacji, liczniki, status |
| `meta.SourceFile` | każdy unikalny plik (klucz: SHA-256): nazwa, źródło, ścieżka w Landing Zone, kolumny, sygnatura kolumn, typ raportu, liczba wierszy |
| `meta.SourceFileSeen` | każdy plik widziany w każdym imporcie i decyzja: zaimportowany / duplikat / pominięty / błąd |
| `stg.RawRow` | surowe wiersze: hash pliku, numer wiersza, wartości (JSON) |

**Typ raportu** jest na razie rozpoznawany po nazwie pliku (CJI3, ZRD_KKAJ, NET_INV, inaczej „nieznany”).
**Sygnatura kolumn** (odcisk układu nagłówków) pozwoli rozpoznawać typ niezależnie od nazwy –
po teście na prawdziwych plikach zrobimy słownik sygnatur.

Wydajność (test): plik CSV 700 000 wierszy / 85 MB – import do SQLite ok. 13 s.

---

## 8. Co zgłosić po teście

- wynik `sprawdz` (bez danych z plików),
- listę nazw plików z `lista`,
- wynik `historia` (nazwy, typ raportu, liczba wierszy),
- nagłówki kolumn plików (np. z `meta.SourceFile.Kolumny`) – bez zawartości.

Dane z plików nie powinny trafiać poza sieć firmową.

---

## 9. Testy (developer)

```bat
python -m pytest -q tests
```

Testy działają na symulowanym serwerze SharePoint (REST API) i bazie SQLite.
Wariant MS SQL (`mssql://`) nie był testowany automatycznie – wymaga serwera w sieci PZL.
