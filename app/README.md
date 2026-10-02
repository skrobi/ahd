# PZL-EV – aplikacja (.NET 10 / WPF)

Aplikacja PZL-EV rozwijana według planu w `tasks/` (`tasks/README.md`). Powstała z aplikacji testowej stosu
i nadal służy do sprawdzenia uruchomienia jednego `PZL-EV.exe` z dysku sieciowego (O6, sekcja „Test stosu”).

Stan: **Słowniki** (słowniki globalne – F1), **Import RABIT** i **Administracja** (import plików, definicje
źródeł, lokalizacje – F2), **Pulpit** (dane przykładowe) i **Diagnostyka**. Pozostałe pozycje menu pokazują ekran
zastępczy modułu (dokumentacja i etapy, które moduł przejmie). Do czasu bazy TEST dane są **w pamięci**
(tryb przejściowy – sekcja „Konfiguracja i dane”).

Kod ma strukturę modułową (`docs/architektura.md`, rozdz. 5.3).

## Konfiguracja i dane

Plik `pzl-ev.json` obok `PZL-EV.exe` (opcjonalny – brak pliku = wartości domyślne). `Env` wybiera środowisko
(`TEST` / `PROD`), a sekcja `Environments.<Env>` – jego ustawienia:

```json
{
  "Env": "TEST",
  "Environments": {
    "TEST": {
      "NetworkRoot": "\\\\serwer\\udzial\\PZL-EV-TEST",
      "DataMode": "Sql",
      "Sql": { "Server": "pzltestdb.intl.lmco.com", "Database": "PZLTEST", "Schema": "FINOP", "TablePrefix": "PZLEV_" }
    },
    "PROD": { "...": "przy uruchomieniu PROD" }
  }
}
```

| Ustawienie | Domyślnie | Znaczenie |
|---|---|---|
| `Env` | `TEST` | wybór sekcji `Environments`; nazwa środowiska w nagłówku |
| `NetworkRoot` | `%LOCALAPPDATA%\PZL-EV\TEST-root` | korzeń folderów środowiska (`docs/architektura.md`, rozdz. 7); import czyta `00_Global\RABIT\Do_importu` |
| `DataMode` | `InMemory` | `Sql` – baza MS SQL środowiska; `InMemory` – dane w pamięci (tryb zapasowy, lokalny dla stanowiska) |
| `Sql.Server`, `Sql.Database` | – | serwer i baza; logowanie kontem AD użytkownika (bez hasła w pliku) |
| `Sql.Schema`, `Sql.TablePrefix` | –, `PZLEV_` | schemat i sygnatura tabel: `[FINOP].[PZLEV_META_ImportBatch]` (`docs/model-danych.md`, rozdz. 2); tylko litery, cyfry i `_` |
| `Sql.TrustServerCertificate` | `false` | `true` tylko gdy certyfikat serwera nie jest zaufany na stanowisku |
| `InMemoryStatePath` | `%LOCALAPPDATA%\PZL-EV\inmemory-state.json` | plik stanu trybu `InMemory` |

Wcześniejszy układ płaski (`Environment`, `NetworkRoot`, `DataMode`, `InMemoryStatePath`) nadal działa.

**Baza danych:** przy starcie aplikacja sprawdza wersję schematu (`META_SchemaVersion`). Gdy brakuje tabel albo
migracji, pyta o zgodę i wykonuje skrypty `sql/mssql/NNN_*.sql` (wbudowane w exe) – konto AD musi mieć prawo
tworzenia tabel w schemacie. Te same skrypty można uruchomić ręcznie:
`sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -v Schema=FINOP Prefix=PZLEV_ -i sql\mssql\001_etap1_import_slowniki_projekty.sql`.
Przy pierwszym starcie na pustej bazie moduły zapisują dane startowe (kalendarz, Cost Category, definicje
`ACTUALS_*`, lokalizacja E456659).

Ustawienia, baza i ścieżki widać na ekranie **Diagnostyka** i w stopce okna. Tryb w pamięci jest lokalny dla
stanowiska (bez współdzielenia danych) i przeznaczony do testów na mniejszych plikach.

**Ręczny test F1 / F2:**

1. **Słowniki** – kalendarz okresów i Cost Category mają dane startowe. Zmień wartość, **Zapisz**; zaznacz wiersz –
   historia pokazuje poprzednią wersję. **Pobierz do Excela** → zmień / dodaj / usuń wiersz → **Wczytaj z Excela** –
   podgląd różnic (+/~/−), **Zatwierdź wczytanie**. Błędna wartość (np. tekst w liczbie) – wynik walidacji, brak zapisu.
2. **Import RABIT** – skopiuj `testdata/RABIT/ACTUALS_PAF_01.csv` do folderu `Do_importu` (ścieżka na ekranie),
   **Importuj** – plik „zaimportowany”, 6 wierszy, sumy jak w `testdata/README.md`; ponowny import – „pominięty”;
   ta sama treść pod inną nazwą – „duplikat”; plik o nieznanym prefiksie – „nierozpoznany”.
3. **Administracja** – definicje `ACTUALS_PAF`, `ACTUALS_CES`; lokalizacja RABIT E456659 jest nieaktywna – włącz ją
   na stanowisku z dostępem do SharePoint (WebDAV) i uruchom import.
4. **Import z SharePoint (brama F5)** – **Importuj**: okno logowania do SharePoint jak w Office (przy ważnej sesji
   zamyka się samo) → lista plików pasujących do definicji z datami, „zostanie zaimportowany” → import, status
   każdego pliku zmienia się na bieżąco; w historii od razu wpis „w toku”. Druga osoba w tym czasie widzi „Trwa
   import: kto, od kiedy” i nie uruchomi importu. **Sprawdź źródła** – to samo bez importu (**Pokaż wszystkie
   pliki** – także nierozpoznane). Import zapisuje dane w bazie, nie kopiuje plików do `Do_importu`.
5. **Import nie widzi plików** – **Sprawdź źródła** pokazuje dla każdej lokalizacji czytaną ścieżkę (WebDAV), dostęp
   albo pełny błąd, liczbę plików i podfoldery (import czyta tylko główny folder lokalizacji). Szczegóły, decyzja dla
   każdego pliku importu i pełna treść wyjątków – `logs\pzl-ev-RRRRMMDD.log` obok `.exe`.
6. **Administracja** – definicję źródła można usunąć (**Usuń definicję** → **Potwierdź usunięcie**); historia zostaje.

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
Testy magazynów (import, administracja, słowniki, migracje) działają też na bazie SQL, gdy zmienna środowiskowa
`PZLEV_TEST_SQL` zawiera ciąg połączenia (np. `Server=pzltestdb.intl.lmco.com;Database=PZLTEST;Integrated Security=True;Encrypt=True`):
każdy test zakłada w schemacie `FINOP` tabele z losową sygnaturą (`T…_`) i usuwa je po sobie.
Pakiety testowe (xUnit) przy pierwszym pobraniu z eFOSS trafiają do zatwierdzenia – do tego czasu `build.cmd`
pomija testy z ostrzeżeniem.

## Test stosu (stanowisko użytkownika)

1. Skopiuj `publish\PZL-EV.exe` do folderu na dysku sieciowym, np. `\\serwer\udzial\PZL-EV\_test\`.
2. Na komputerze **bez zainstalowanego .NET** uruchom go ze ścieżki UNC (dwuklik albo
   `\\serwer\udzial\PZL-EV\_test\PZL-EV.exe`).
3. Sprawdź:
   - [ ] okno się otwiera, widać Pulpit (karty faz globalnych, 3 projekty, Wymaga uwagi, Ostatnie zdarzenia),
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
    │   ├── Dashboard/           Pulpit (dane przykładowe)
    │   ├── Diagnostics/         test stosu i konfiguracja środowiska
    │   └── Mapping/ Runs/ …     pozostałe moduły: plik wejścia + etapy, ekran zastępczy
    └── Shared/
        ├── Utils/Ui/            MVVM, konwertery, okna wyboru pliku, kontrakt modułu (WPF)
        ├── Utils/Config|Data|Files/  konfiguracja, dane w pamięci, dziennik, problemy, Excel/CSV
        ├── Models/              modele wspólne; Db/ – tabele schematu; Sources/ – układy źródeł; Pipeline/ – kontrakt etapu
        └── Views/
            ├── Templates/       Theme.xaml – paleta z prototypu, style, tabele, układ strony
            └── Partials/        pigułka statusu, wynik kontroli, nagłówek ekranu, ekran zastępczy
```
