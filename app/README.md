# PZL-EV – aplikacja (.NET 10 / WPF)

Aplikacja PZL-EV rozwijana według planu w `tasks/` (`tasks/README.md`). Powstała z aplikacji testowej stosu
i nadal służy do sprawdzenia uruchomienia jednego `PZL-EV.exe` z dysku sieciowego (O6, sekcja „Test stosu”).

Stan: **Słowniki** (słowniki globalne – F1), **Import RABIT** i **Administracja** (import plików, definicje
źródeł, parsery, lokalizacje – F2), **Mapowanie** (F3), **Projekty** (kreator, Performance Objectives, słowniki
projektu, gotowość, foldery – F4), **Pulpit** (stan z bazy, otwarte problemy) i **Diagnostyka**.
Pozostałe pozycje menu pokazują ekran
zastępczy modułu (dokumentacja i etapy, które moduł przejmie). Dane są w bazie MS SQL środowiska
(sekcja „Konfiguracja i dane”).

Kod ma strukturę modułową (`docs/architektura.md`, rozdz. 5.3).

## Konfiguracja i dane

**Plik `pzl-ev.json` jest wymagany** – leży obok `PZL-EV.exe`. Wzór z opisem każdego ustawienia to `app\pzl-ev.json`;
`build.cmd` kopiuje go do `app\publish` (zmiany zrobione w `app\publish` nie są nadpisywane, dopóki wzór się nie
zmieni). Brak pliku albo błąd w nim = komunikat przy starcie z nazwą pola – bez cichych wartości domyślnych.
`Env` wybiera środowisko (`TEST` / `PROD`), a sekcja `Environments.<Env>` – jego ustawienia:

| Ustawienie | Znaczenie |
|---|---|
| `Env` | `TEST` albo `PROD`; nazwa środowiska w nagłówku |
| `NetworkRoot` | korzeń folderów środowiska (`docs/architektura.md`, rozdz. 7): `00_Global\RABIT\Do_importu`, blokada importu; docelowo wspólny folder sieciowy; zmienne (np. `%LOCALAPPDATA%`) są rozwijane |
| `Sql.Server`, `Sql.Database` | serwer i baza (TEST: `pzltestdb.intl.lmco.com`, `PZLTEST`); logowanie kontem AD użytkownika, bez hasła w pliku |
| `Sql.Schema`, `Sql.TablePrefix` | schemat i sygnatura tabel: `[FINOP].[PZLEV_META_ImportBatch]` (`docs/model-danych.md`, rozdz. 2); tylko litery, cyfry i `_`; sygnatura domyślnie `PZLEV_` |
| `Sql.TrustServerCertificate` | `true` tylko gdy połączenie zgłasza niezaufany certyfikat serwera |
| `PzlProd.Server`, `PzlProd.Database`, `PzlProd.Schema` | baza ze strukturą P1S (`splmcd03`, `PZLPROD`, schemat `LOG`: tabele `WBS`, `WBS_DIC`) – tylko odczyt kontem AD; sekcja opcjonalna – bez niej ekran Mapowanie pokazuje, czego brakuje; **Diagnostyka → Sprawdź PZLPROD** testuje połączenie |

**Baza danych – migracje:** skrypty `sql/mssql/NNN_*.sql` są wbudowane w exe:
- `001_etap1_import_slowniki_projekty.sql` – tabele etapu 1;
- `002_dane_startowe.sql` – presety: definicje źródeł `ACTUALS_PAF` i `ACTUALS_CES`, aktywna lokalizacja RABIT
  E456659, kalendarz okresów 2026–2027, Cost Category (załącznik A);
- `003_usuniecie_plikow_niezgodnych.sql` – usuwa pliki zapisane do wersji 0.11 mimo niezgodności z definicją
  (układ kolumn, typy wartości) wraz z wierszami surowymi (albo treścią i danymi kanonicznymi); kolejny import
  pobierze je ponownie;
- `004_parsery.sql` – parsery w bazie (Administracja → Parsery): parser pilnuje układu pliku (kolumna w pliku → pole,
  typ, wymagane), definicja źródła tylko go wskazuje; parser ACTUALS w układzie raportu z 2026-10 (nowe pola
  `Original Order Number`, `Item`, `Purchase order number`, `Invoice Number`);
- `005_mapowanie_ces_p1s.sql` – parser `MAPOWANIA` (raport mapowań SAP↔CES z Excela)
  i korekty mapowania (`DICT_MappingCorrection`);
- `006_problemy_rozwiazywanie.sql` – rozwiązywanie problemów (kto, kiedy, jak); problemy importów wcześniejszych
  niż ostatni zakończony – rozwiązane;
- `007_sloty_danych_kanonicznych.sql` – dane kanoniczne wszystkich parserów w jednej stałej tabeli `CAN_Row`
  (sloty typowane, indeks kolumnowy) i treść plików (`META_SourceFileContent`, usunięta w 008); przenosi dane
  z `CAN_Actuals`, `CAN_MappingReport`, `STG_RawRow` i usuwa te tabele. Wymaga SQL Server 2016+ i poziomu zgodności
  bazy co najmniej 130 (Diagnostyka → **Serwer SQL**); po niej aplikacja nie zmienia tabel – nowy parser i nowe
  pole to tylko zapis parsera (`docs/model-danych.md`, rozdz. 5.1);
- `008_bez_tresci_plikow.sql` – usuwa przechowywaną treść plików (`META_SourceFileContent`): w bazie zostają wersje
  plików i dane kanoniczne, sam plik nie jest przechowywany;
- `009_raport_mapowan_slownik.sql` – raport mapowań SAP↔CES jako słownik globalny (`DICT_MappingReport`); usuwa
  definicje źródeł z parserem `MAPOWANIA` i sam parser – raport wczytuje się raz do słownika (Słowniki).

Migracje wykonuje przycisk **Diagnostyka → Migracja** – uruchamia po kolei skrypty, których numeru nie ma
w `META_SchemaVersion`, a wykonane pomija. Lista na tym ekranie pokazuje, które skrypty są wykonane (kiedy, kto)
i które czekają. Przy starcie aplikacja pyta o brakujące migracje: **Tak** – wykonuje je od razu, **Nie** – startuje
bez nich. Migracje uruchamia konto AD z prawem tworzenia i zmiany tabel w schemacie; pozostali użytkownicy
takiego prawa nie potrzebują. Dwie osoby uruchamiające migrację jednocześnie nie
wykonają skryptu dwa razy (`sp_getapplock`). Skrypty można też uruchomić ręcznie, po kolei:
`sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i sql\mssql\001_etap1_import_slowniki_projekty.sql`
(i tak samo kolejne; `-f 65001` – skrypty są w UTF-8). Dane startowe są tylko w migracji – aplikacja sama nic nie dopisuje;
usunięta definicja nie wraca.

Ustawienia, baza (także wersja SQL Server, edycja i poziom zgodności bazy – wiersz **Serwer SQL**) i ścieżki widać
na ekranie **Diagnostyka** i w stopce okna.

**Ręczny test F1 / F2:**

1. **Słowniki** – kalendarz okresów i Cost Category mają dane startowe (migracja 002). Zmień wartość, **Zapisz**; zaznacz wiersz –
   historia pokazuje poprzednią wersję. **Pobierz do Excela** → zmień / dodaj / usuń wiersz → **Wczytaj z Excela** –
   podgląd różnic (+/~/−), **Zatwierdź wczytanie**. Błędna wartość (np. tekst w liczbie) – wynik walidacji, brak zapisu.
2. **Import RABIT** – skopiuj `testdata/RABIT/ACTUALS_PAF_01.csv` do folderu `Do_importu` (ścieżka na ekranie),
   **Importuj** – w trakcie status pliku pokazuje etap (1/4 pobieranie na dysk, 2/4 sprawdzanie, 3/4 odczyt i zapis
   wierszy, 4/4 kontrola w bazie) z postępem i czasem; potem „zaimportowany”, 6 wierszy, sumy jak
   w `testdata/README.md`; ponowny import – „pominięty”;
   ta sama treść pod inną nazwą – „duplikat”; plik o nieznanym prefiksie – „nierozpoznany”.
3. **Administracja** – definicje `ACTUALS_PAF`, `ACTUALS_CES` (kod, prefiks, typ raportu, parser ACTUALS, aktywna);
   lokalizacja RABIT E456659 (migracja 002 – aktywna). Nowe źródło o znanym układzie: **Nowa definicja** → kod,
   prefiks, parser → **Zapisz definicję**. Nowy układ raportu: **Parsery** → wybierz parser → **Kolumny z pliku…**
   (nowe kolumny dochodzą jako pola tekstowe, brakujące są oznaczone, siatka pokazuje przykładowe wartości) →
   popraw pole w bazie, typ, **Wymagane** → **Zapisz parser** (nowe pola dostają sloty w `CAN_Row` – kolumna **Slot**;
   bez zmian tabel); import utworzy dane kanoniczne według parsera. Zmiana typu istniejącego pola (np. kwota → tekst)
   jest odrzucana – dodaj nowe pole.
4. **Import z SharePoint (brama F5)** – **Importuj**: okno logowania do SharePoint jak w Office (przy ważnej sesji
   zamyka się samo) → lista plików pasujących do definicji z datami, „zostanie zaimportowany” → import, status
   każdego pliku zmienia się na bieżąco; w historii od razu wpis „w toku”. Druga osoba w tym czasie widzi „Trwa
   import: kto, od kiedy” i nie uruchomi importu. **Sprawdź źródła** – to samo bez importu (**Pokaż wszystkie
   pliki** – także nierozpoznane). Import zapisuje dane w bazie, nie kopiuje plików do `Do_importu`.
5. **Import nie widzi plików** – **Sprawdź źródła** pokazuje dla każdej lokalizacji czytaną ścieżkę (WebDAV), dostęp
   albo pełny błąd, liczbę plików i podfoldery (import czyta tylko główny folder lokalizacji). Szczegóły, decyzja dla
   każdego pliku importu i pełna treść wyjątków – `logs\pzl-ev-RRRRMMDD.log` obok `.exe`.
6. **Administracja** – definicję źródła można usunąć (**Usuń definicję** → **Potwierdź usunięcie**); historia zostaje.
7. **Mapowanie CES ↔ P1S** – **Diagnostyka → Sprawdź PZLPROD**: liczba elementów `LOG.WBS`, grup `WBS_DIC`
   i faktyczne wartości `Z_ACTIVE` / `LOEKZ` (aplikacja przyjmuje: `LOEKZ` niepuste = usunięty; `Z_ACTIVE` puste,
   `0` albo `N` = nieaktywny). **Słowniki** → „Raport mapowań CES ↔ P1S” → **Wczytaj z Excela** (np.
   `testdata/Mapowanie/Raport_mapowan.csv` albo eksport z `PZLPROD`) → podgląd różnic → **Zatwierdź wczytanie**;
   ponowne wczytanie innego pliku zastępuje cały raport (wiersze spoza pliku – „−”, historia zostaje).
   **Mapowanie**: statusy elementów CES z raportów ACTUALS (REPORT, INHERITED, OVERRIDE, UNMAPPED), drzewo P1S
   z elementami CES pod celami i węzłem „Nieprzypisane” (z propozycją). Korekta: wybierz element CES → cel w drzewie
   albo w wyszukiwaniu → uzasadnienie (wymagane przy zmianie przypisania z raportu) → **Zapisz korektę**; status
   OVERRIDE, historia; **Usuń korektę** – wraca przypisanie z raportu.
8. **Pulpit → Wymaga uwagi** – otwarte problemy wszystkich obszarów (import, mapowanie), błędy najpierw. Plik z błędem
   → import (problem) → poprawiony plik → import: problem poprzedniego importu znika sam. Korekta elementu CES
   z problemem G2 → **Mapowanie → Odśwież**: problem znika. **Rozwiązane** przy problemie – zamknięcie ręczne
   (wpis w dzienniku).
9. **Projekty** – **+ Nowy projekt**: kod `M28`, nazwa, typ → **Performance Objectives**: **Wczytaj Excel**
   `testdata/Projekty/PO_M28.xlsx` (8 elementów; kolumna „P1S z mapowania” – status i cel z mapowania CES ↔ P1S,
   gdy słownik raportu mapowań nie jest pusty), dodaj węzeł wirtualny i przeciągnij do niego elementy →
   **Słowniki projektu**: **Pobierz szablon Excel** (arkusz „WP i CAM” z elementami P1S z zakresu) albo
   **Wczytaj skoroszyt** `testdata/Projekty/Slowniki_M28.xlsx` → **Foldery** → **Podsumowanie**: baza analityczna
   (3 WP, 2 250 h, 75 000,50 materiałów) → **Utwórz projekt**. Bez słowników projekt powstaje, ale jest niegotowy
   (ERROR „WP i CAM”); po wczytaniu słowników na ekranie projektu – gotowy. Drugi projekt z tym samym kodem albo z tym
   samym elementem CES – odrzucony.

## Test stosu – co aplikacja sprawdza

| Założenie | Jak sprawdzane |
|---|---|
| C# / .NET 10, WPF + MVVM | powłoka i moduły: widoki (`…View.xaml`) związane z `…ViewModel` |
| self-contained, single-file, bez instalacji .NET | publikacja `build.cmd` → jeden `PZL-EV.exe`; uruchomienie na komputerze bez .NET |
| uruchomienie z dysku sieciowego | start `PZL-EV.exe` ze ścieżki UNC; ekran Diagnostyka pokazuje „Uruchomiono z” |
| Dapper, Microsoft.Data.SqlClient, ClosedXML (OpenXML) | pakiety są w projekcie; ekran **Diagnostyka** pokazuje wersje wczytane w runtime z bundla (bez łączenia z bazą) |
| Serilog | log w `logs\pzl-ev-RRRRMMDD.log` obok `.exe` (sprawdza też prawo zapisu w tym miejscu) |
| konto Windows / AD | panel pokazuje `DOMENA\użytkownik` |

## Budowa (maszyna developera, Windows)

Wymagane: **.NET 10 SDK** (tylko do budowy) i dostęp do pakietów NuGet z `src/PzlEv.csproj`.
Źródło pakietów ustawia wersjonowany `app/NuGet.config` (feed **eFOSS**, nie nuget.org); wersje
pakietów można dostosować w `.csproj`.

```bat
build.cmd
```

### Pakiety NuGet w środowisku LM (eFOSS)

W LM pakiety pobiera się z proxy **eFOSS (Nexus)**, nie z nuget.org:

- **Źródło:** `https://nexus.global.lmco.com/repository/nuget-proxy-v3/index.json` (ustawione w `NuGet.config`).
- **Token dostępu:** wygeneruj w `https://efoss.global.lmco.com/accesstoken` (wymaga charge number i CAM;
  CAM znajdziesz w `https://ces-lookup.us.lmco.com`). Token zapisz u siebie – pokazywany jest raz.
- **Logowanie:** użytkownik = **NTID**, hasło = **token dostępu**.
- **Gdzie trzymać poświadczenia (nie w repozytorium):**
  - Visual Studio: przy pierwszym użyciu feedu eFOSS zaznacz „zapamiętaj” – trafią do **Windows Credential Manager**;
  - budowa z wiersza poleceń (`build.cmd`, CI): zmienna środowiskowa sesji
    `NuGetPackageSourceCredentials_eFOSS=Username=<NTID>;Password=<token>`;
  - albo użytkownikowy `NuGet.config` (poza repo) z sekcją `<packageSourceCredentials>`.
- **Bez proxy:** `nexus.global.lmco.com` łączy się **bezpośrednio**, bez proxy (dokumentacja eFOSS, Quick start
  pkt 4); wymagana sieć LM (w biurze albo VPN). `build.cmd` ustawia `NO_PROXY=.lmco.com`, więc ewentualne
  `HTTP_PROXY` stanowiska nie obejmuje nexusa. Jeśli wcześniej ustawiono proxy w konfiguracji użytkownika, usuń je:

  ```powershell
  dotnet nuget config unset http_proxy --configfile "$env:APPDATA\NuGet\NuGet.Config"
  ```

**Token dla `build.cmd` (zalecane).** Utwórz obok `build.cmd` plik `eFOSS.local.cmd` (jest w `.gitignore`,
nie trafi do repozytorium) z jedną linią – `build.cmd` wczyta go sam:

```bat
set NuGetPackageSourceCredentials_eFOSS=Username=<NTID>;Password=<TOKEN>
```

**Poświadczenia z wiersza poleceń (na maszynie developera, nie w repo).** Źródło `eFOSS` jest już w
`app/NuGet.config`, więc dodaj tylko poświadczenia do **konfiguracji użytkownika** (`%APPDATA%\NuGet\NuGet.Config`):

```bat
dotnet nuget disable source nuget.org
dotnet nuget update source eFOSS -u <NTID> -p <TOKEN> --store-password-in-clear-text --configfile "%APPDATA%\NuGet\NuGet.Config"
```

Bezpieczniej (token nie ląduje w żadnym pliku) – zmienna środowiskowa sesji budowy:

```bat
set NuGetPackageSourceCredentials_eFOSS=Username=<NTID>;Password=<TOKEN>
build.cmd
```

To samo w PowerShell:

```powershell
$env:NuGetPackageSourceCredentials_eFOSS = "Username=<NTID>;Password=<TOKEN>"
.\build.cmd
```

**Diagnostyka restore (PowerShell, w `app/`):**

```powershell
dotnet nuget list source                  # oczekiwane: tylko eFOSS (włączone)
curl.exe -s -o NUL -w "%{http_code}`n" --noproxy "*" https://nexus.global.lmco.com/repository/nuget-proxy-v3/index.json
git diff -- NuGet.config                  # token nie może trafić do pliku w repo
```

| Wynik | Znaczenie |
|---|---|
| NU1301 „Żądana nazwa jest prawidłowa, ale dane żądanego typu nie zostały znalezione” (`nexus.global.lmco.com:443`) | stanowisko nie widzi nexusa – sprawdź sieć LM / VPN (`curl` poniżej) |
| NU1301 z nazwą proxy (np. `proxy-lmi…`) | NuGet idzie przez proxy, a nie powinien – usuń `http_proxy` z konfiguracji użytkownika (wyżej) |
| `curl` → `200` albo `401` | połączenie z nexusem działa (`401` – feed wymaga logowania) |
| `curl` → `000` / błąd połączenia | brak połączenia z nexusem – sprawdź VPN |
| NU1301 z `401 Unauthorized` | brak poświadczeń albo nieważny token – wygeneruj nowy token |
| `407 Proxy Authentication Required` | proxy wymaga logowania – zgłoszenie do IT / konfiguracja `http_proxy.user` w konfiguracji użytkownika |

Nie uruchamiaj `dotnet nuget add/update source` w katalogu `app/` bez `--configfile` – dopisałoby token do
wersjonowanego `NuGet.config`. Używaj konfiguracji użytkownika albo zmiennej środowiskowej.

**Tokenu nie wpisujemy do `app/NuGet.config` ani nie commitujemy do repozytorium.**

Wynik: `publish\PZL-EV.exe` (ok. 70–100 MB – zawiera runtime .NET i WPF).

**Testy** (`tests/PzlEv.Tests.csproj`, xUnit): `build.cmd` uruchamia je przed publikacją; ręcznie – `dotnet test tests\PzlEv.Tests.csproj`.
Testy magazynów i serwisów (import, administracja, słowniki, Pulpit, migracje) działają tylko na bazie SQL – nie ma
wersji w pamięci. Wymagają zmiennej środowiskowej `PZLEV_TEST_SQL` z ciągiem połączenia (np.
`Server=pzltestdb.intl.lmco.com;Database=PZLTEST;Integrated Security=True;Encrypt=True`); bez niej są pomijane
(`build.cmd` wypisuje ostrzeżenie). Każdy test zakłada w schemacie `FINOP` tabele z losową sygnaturą (`T…_`)
i usuwa je po sobie – nie dotyka tabel aplikacji (`PZLEV_*`).
Test wydajności importu (`ImportPerformanceTests`, plik ACTUALS z powtórzonych wierszy wzorcowych) działa tylko ze
zmienną `PZLEV_PERF_ROWS` (liczba wierszy, np. `1000000`; `PZLEV_PERF_FORMAT=xlsx` – plik Excel, domyślnie CSV):
`dotnet test tests\PzlEv.Tests.csproj --filter ImportPerformanceTests --logger "console;verbosity=detailed"` – wypisuje
czas i pamięć procesu w czasie importu. Plik jest generowany raz (`%TEMP%\pzl-ev-perf`) – pamięć mierzy dopiero kolejne
uruchomienie. Pomiar (SQL Server 2022 lokalnie): CSV 87 MB / 560 tys. wierszy – 10 s; Excel 67 MB / 1 mln wierszy – 21 s;
pamięć ok. 0,3 GB.
Pakiety testowe (xUnit) przy pierwszym pobraniu z eFOSS trafiają do zatwierdzenia – do tego czasu `build.cmd`
pomija testy z ostrzeżeniem.

## Test stosu (stanowisko użytkownika)

1. Skopiuj `publish\PZL-EV.exe` do folderu na dysku sieciowym, np. `\\serwer\udzial\PZL-EV\_test\`.
2. Na komputerze **bez zainstalowanego .NET** uruchom go ze ścieżki UNC (dwuklik albo
   `\\serwer\udzial\PZL-EV\_test\PZL-EV.exe`).
3. Sprawdź:
   - [ ] okno się otwiera, widać Pulpit z danymi z bazy: ostatni import, źródła importu, liczba wierszy słowników
     globalnych, projekty, ostrzeżenia ostatniego importu i dziennik (na pustej bazie – „Brak importów w bazie”,
     „Brak projektów w bazie”),
   - [ ] menu **Diagnostyka**: Runtime `.NET 10…`, 4 pakiety z wersjami, „Uruchomiono z” = ścieżka UNC,
   - [ ] konto Windows = Twoje konto AD,
   - [ ] w folderze `.exe` powstał `logs\pzl-ev-*.log` z wpisami startu, listą modułów i – po wejściu
     w Diagnostykę – pakietów (jeśli folder jest tylko do odczytu – aplikacja się nie uruchomi; to też wynik testu),
   - [ ] pozostałe pozycje menu pokazują ekran zastępczy z dokumentacją i etapami modułu; „Wróć do Pulpitu” wraca.
4. Powtórz na 2–3 stanowiskach (różni użytkownicy, różne polityki).

## Możliwe blokady (co oznaczają)

| Objaw | Prawdopodobna przyczyna | Kierunek |
|---|---|---|
| „Ta aplikacja została zablokowana” / AppLocker / WDAC | polityka IT blokuje `.exe` spoza `Program Files` lub z sieci | wpis do listy dozwolonych (ścieżka UNC albo podpis cyfrowy) – decyzja IT |
| SmartScreen „Nieznany wydawca” | brak podpisu kodu | podpis certyfikatem firmowym |
| długi pierwszy start | single-file rozpakowuje biblioteki natywne do `%TEMP%\.net` | normalne przy pierwszym uruchomieniu; kolejne szybsze |
| brak pliku logu | brak prawa zapisu w folderze `.exe` | docelowo log w `%LOCALAPPDATA%` albo w osobnym folderze sieciowym |
| import: „…was deadlocked on lock resources with another process…” | zapis pliku zakleszczył się z inną sesją bazy (np. ekran Mapowanie / Projekty innej osoby, testy na tej samej bazie, zadania serwera na indeksie kolumnowym) | od 0.18.1 import powtarza zapis pliku sam (do 3 prób, wpis w logu); gdy błąd zostaje – graf zakleszczenia z `system_health` (zapytanie w `docs/zrodla-danych.md`, rozdz. 2) pokaże drugą sesję |
| błąd restore pakietu przy budowie | brak sieci LM / VPN, brak poświadczeń do eFOSS, zły/wygasły token albo pakiet niedostępny w feedzie | diagnostyka w sekcji „Pakiety NuGet… eFOSS”; jeśli pakietu nie ma w feedzie – lista do zatwierdzenia / lokalny cache |

## Struktura

Zasady, warstwa danych przejściowa, testy i przepis „Nowy moduł” – `docs/architektura.md`, rozdz. 5.3.

```
app/
├── build.cmd                    restore, testy, publikacja self-contained single-file win-x64
├── NuGet.config                 źródło pakietów = feed eFOSS (bez poświadczeń)
├── ArchitectureRules.targets    granice modułów sprawdzane przy każdej budowie (błąd PZLARCH)
├── testdata/                    dane wzorcowe z oczekiwanymi sumami (F0.9)
├── tests/PzlEv.Tests.csproj     testy logiki (xUnit, net10.0 – bez WPF), testy kontraktu magazynów
└── src/
    ├── PzlEv.csproj             net10.0-windows, WPF, pakiety z założeń
    ├── .editorconfig            przestrzeń nazw = folder (IDE0130)
    ├── App.xaml(.cs)            start: konfiguracja → usługi wspólne → moduły → powłoka
    ├── Shell/                   okno, menu, nawigacja; ModuleCatalog.cs – lista modułów
    ├── Modules/
    │   ├── MasterData/          Słowniki globalne (F1)
    │   ├── Import/              Import RABIT (F2)
    │   ├── Administration/      definicje źródeł, lokalizacje RABIT (F2)
    │   ├── Dashboard/           Pulpit: import, źródła, słowniki, otwarte problemy, dziennik (z bazy)
    │   ├── Diagnostics/         test stosu, konfiguracja środowiska, migracje, sprawdzenie PZLPROD
    │   ├── Mapping/             Mapowanie CES ↔ P1S (F3): rozstrzyganie, drzewo P1S, korekty
    │   └── Runs/ Projects/ …    pozostałe moduły: plik wejścia + etapy, ekran zastępczy
    └── Shared/
        ├── Utils/Ui/            MVVM, konwertery, okna wyboru pliku, kontrakt modułu (WPF)
        ├── Utils/Config|Data|Files/  konfiguracja, baza MS SQL (Data/Sql), dziennik, problemy, Excel/CSV
        ├── Models/              modele wspólne; Db/ – tabele schematu; Sources/ – układy źródeł; Pipeline/ – kontrakt etapu
        └── Views/
            ├── Templates/       Theme.xaml – paleta z prototypu, style, tabele, układ strony
            └── Partials/        pigułka statusu, wynik kontroli, nagłówek ekranu, ekran zastępczy
```
