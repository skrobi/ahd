# MVP – Etap 1: pobranie plików SAP

Wersja: 0.1 (test działania, nie gotowe rozwiązanie)

Moduł pobiera pliki z folderu SharePoint (np. raporty RABIT) albo z folderu lokalnego / sieciowego
do Landing Zone, liczy hash SHA-256, sprawdza pliki i zapisuje `manifest.json`.
Działa **lokalnie na komputerze użytkownika** – logowanie do SharePoint odbywa się kontem
zalogowanego użytkownika Windows (SSO), bez podawania hasła.

Nie ma jeszcze bazy danych ani interfejsu w przeglądarce – wynik jest w manifeście.

---

## 1. Instalacja (Windows, jednorazowo)

W katalogu repozytorium:

```bat
py -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```

Wymagany Python 3.10+.

---

## 2. Test połączenia

Link skopiowany z przeglądarki (widok folderu w bibliotece dokumentów):

```bat
python -m ahd.etap1 sprawdz --url "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx?...&RootFolder=%2fsites%2fRabbitReporting%2fShared%20Documents%2fE456659&..."
```

Oczekiwany wynik:

```
Witryna: https://lmsp4-intl.external.lmco.com/sites/RabbitReporting
Folder : /sites/RabbitReporting/Shared Documents/E456659
Zalogowano jako: i:0#.w|DOMENA\uzytkownik
W folderze: 12 plików, 0 podfolderów
```

Jeżeli się nie uda, komunikat mówi, co jest nie tak:

| Komunikat | Co zrobić |
|---|---|
| `Odmowa dostępu (HTTP 401) … metody logowania: Negotiate, NTLM` | SSO nie przeszło – spróbuj `--auth ntlm` (login i hasło wpisywane w konsoli, nie są zapisywane) |
| `Odmowa dostępu (HTTP 401) … metody logowania: brak` lub przekierowanie na stronę logowania | serwis wymaga logowania przez przeglądarkę (ADFS / karta) – wtedy użyj wariantu z folderem (pkt 5) |
| `Błąd certyfikatu TLS` | sprawdź, czy zainstalował się pakiet `truststore`; ewentualnie `--ca-bundle` |
| `Brak połączenia` | sieć / VPN / proxy |

---

## 3. Lista plików

```bat
python -m ahd.etap1 lista --url "<link>"
python -m ahd.etap1 lista --url "<link>" --filtr "*.xlsx" --filtr "*.csv"
```

---

## 4. Pobranie do Landing Zone

```bat
python -m ahd.etap1 pobierz --url "<link>" --cel C:\AHD_TEST\LandingZone --zakres F16 --filtr "*.xlsx"
```

- przebieg tygodniowy: domyślnie bieżący miesiąc i tydzień ISO (`--okres 2026-09 --tydzien 40`),
- zamknięcie miesiąca: `--zamkniecie --okres 2026-09`,
- `--dry-run` – tylko pokazuje, co zostałoby pobrane,
- `--rekurencyjnie` – także podfoldery,
- ponowne pobranie tego samego przebiegu wymaga `--nadpisz`.

Wynik:

```
C:\AHD_TEST\LandingZone\F16\2026-09\R-F16-2026-09-T40\
    CJI3_F16_cz1.csv
    CJI3_F16_cz2.csv
    ZRD_KKAJ_F16.xlsx
    manifest.json
```

`manifest.json` zawiera dla każdego pliku: źródło, rozmiar, datę modyfikacji, SHA-256,
status (`nowy`, `bez zmian`, `zmieniony` – względem wcześniejszych przebiegów zakresu),
oraz inspekcję (kolumny, liczba wierszy, kodowanie i separator CSV, arkusz Excela).
Pliki wieloczęściowe (`_cz1`, `_cz2`, `part 3`, `(2)`) są sprawdzane jako zestaw:
identyczne kolumny i łączna liczba wierszy. Niespójny zestaw kończy się kodem błędu.

---

## 5. Wariant: folder zamiast SharePoint

Jeśli bibliotekę da się zsynchronizować przez OneDrive albo pliki leżą na dysku sieciowym:

```bat
python -m ahd.etap1 pobierz --folder "C:\Users\<ja>\Lockheed Martin\RabbitReporting - E456659" --cel C:\AHD_TEST\LandingZone --zakres F16
python -m ahd.etap1 pobierz --folder "\\serwer\udzial\AHD\Zakresy\F16\SAP\2026-09\Tydz40" --cel ... --zakres F16
```

Pliki blokady Excela (`~$…`) są pomijane.

---

## 6. Co zgłosić po teście

- wynik `sprawdz` (bez danych z plików),
- lista nazw plików z `lista` – żeby ustalić nazewnictwo i zestawy wieloczęściowe,
- z `manifest.json` tylko sekcja `inspekcja` (kolumny, liczba wierszy) – bez zawartości plików.

Dane z plików nie powinny trafiać poza sieć firmową.

---

## 7. Testy (developer)

```bat
python -m pytest -q tests
```

Testy działają na symulowanym serwerze SharePoint (REST API), bez dostępu do sieci firmowej.
