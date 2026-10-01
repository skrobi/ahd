# PZL-EV – aplikacja testowa stosu (.NET 10 / WPF)

Cel: **zanim ruszy implementacja**, sprawdzić, czy założenia technologiczne pozwalają uruchomić
jeden `PZL-EV.exe` z dysku sieciowego na stanowisku PZL – bez instalacji .NET, bez lokalnego
serwera i bez lokalnej bazy – z pakietami dostępnymi w PZL.

Zakres: **tylko Pulpit z prototypu** (`prototyp/pzl-ev-prototyp.html`, `vPulpit()`), dane przykładowe,
**bez połączenia z bazą**. Pozostałe pozycje menu pokazują ekran zastępczy.

## Co aplikacja sprawdza

| Założenie | Jak sprawdzane |
|---|---|
| C# / .NET 10, WPF + MVVM | aplikacja jest WPF z widokiem związanym z `MainViewModel` |
| self-contained, single-file, bez instalacji .NET | publikacja `build.cmd` → jeden `PZL-EV.exe`; uruchomienie na komputerze bez .NET |
| uruchomienie z dysku sieciowego | start `PZL-EV.exe` ze ścieżki UNC; panel pokazuje „Uruchomiono z” |
| Dapper, Microsoft.Data.SqlClient, ClosedXML (OpenXML) | pakiety są w projekcie; panel **Diagnostyka środowiska** pokazuje wersje wczytane w runtime z bundla (bez łączenia z bazą) |
| Serilog | log w `logs\pzl-ev-test-RRRRMMDD.log` obok `.exe` (sprawdza też prawo zapisu w tym miejscu) |
| konto Windows / AD | panel pokazuje `DOMENA\użytkownik` |

## Budowa (maszyna developera, Windows)

Wymagane: **.NET 10 SDK** (tylko do budowy) i dostęp do pakietów NuGet z `src/PzlEv.Test.csproj`.
Źródło pakietów ustawia wersjonowany `poc-wpf/NuGet.config` (feed **eFOSS**, nie nuget.org); wersje
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
- **Proxy (jeśli wymagane w sieci):** `HTTP_PROXY=http://proxy-lmi.global.lmco.com:80` – ustaw w konfiguracji
  użytkownika / zmiennej środowiskowej, nie w pliku w repo (zależne od stanowiska).

**Tokenu nie wpisujemy do `poc-wpf/NuGet.config` ani nie commitujemy do repozytorium.**

Wynik: `publish\PZL-EV.exe` (ok. 70–100 MB – zawiera runtime .NET i WPF).

## Test (stanowisko użytkownika)

1. Skopiuj `publish\PZL-EV.exe` do folderu na dysku sieciowym, np. `\\serwer\udzial\PZL-EV\_test\`.
2. Na komputerze **bez zainstalowanego .NET** uruchom go ze ścieżki UNC (dwuklik albo
   `\\serwer\udzial\PZL-EV\_test\PZL-EV.exe`).
3. Sprawdź:
   - [ ] okno się otwiera, widać Pulpit (karty faz globalnych, 3 projekty, Wymaga uwagi, Ostatnie zdarzenia),
   - [ ] **Diagnostyka środowiska**: Runtime `.NET 10…`, 4 pakiety z wersjami, „Uruchomiono z” = ścieżka UNC,
   - [ ] konto Windows = Twoje konto AD,
   - [ ] w folderze `.exe` powstał `logs\pzl-ev-test-*.log` z wpisami startu i pakietów
     (jeśli folder jest tylko do odczytu – aplikacja się nie uruchomi; to też wynik testu),
   - [ ] menu po lewej przełącza na ekran zastępczy i z powrotem.
4. Powtórz na 2–3 stanowiskach (różni użytkownicy, różne polityki).

## Możliwe blokady (co oznaczają)

| Objaw | Prawdopodobna przyczyna | Kierunek |
|---|---|---|
| „Ta aplikacja została zablokowana” / AppLocker / WDAC | polityka IT blokuje `.exe` spoza `Program Files` lub z sieci | wpis do listy dozwolonych (ścieżka UNC albo podpis cyfrowy) – decyzja IT |
| SmartScreen „Nieznany wydawca” | brak podpisu kodu | podpis certyfikatem firmowym |
| długi pierwszy start | single-file rozpakowuje biblioteki natywne do `%TEMP%\.net` | normalne przy pierwszym uruchomieniu; kolejne szybsze |
| brak pliku logu | brak prawa zapisu w folderze `.exe` | docelowo log w `%LOCALAPPDATA%` albo w osobnym folderze sieciowym |
| błąd restore pakietu przy budowie | brak poświadczeń do eFOSS, zły/wygasły token, proxy albo pakiet niedostępny w feedzie | sprawdź token i proxy (sekcja „Pakiety NuGet… eFOSS”); jeśli pakietu nie ma w feedzie – lista do zatwierdzenia / lokalny cache |

## Struktura

```
poc-wpf/
├── build.cmd                    publikacja self-contained single-file win-x64
├── NuGet.config                 źródło pakietów = feed eFOSS (bez poświadczeń)
└── src/
    ├── PzlEv.Test.csproj        net10.0-windows, WPF, pakiety z założeń
    ├── App.xaml(.cs)            start, Serilog, obsługa wyjątków
    ├── MainWindow.xaml(.cs)     powłoka (pasek środowiska, menu) + Pulpit
    ├── Theme.xaml               paleta z prototypu
    ├── ViewModels/MainViewModel.cs
    ├── Models/Models.cs
    ├── Mvvm/                    ObservableObject, RelayCommand, konwerter kolorów
    ├── SampleData.cs            dane Pulpitu przeliczone z prototypu
    └── PackageDiagnostics.cs    wersje pakietów wczytanych w runtime
```

To jest aplikacja testowa – nie jest początkiem właściwego kodu (modułów, dostępu do danych, ról).
