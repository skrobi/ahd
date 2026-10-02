# PZL-EV – aplikacja testowa stosu (.NET 10 / WPF)

Cel: **zanim ruszy implementacja**, sprawdzić, czy założenia technologiczne pozwalają uruchomić
jeden `PZL-EV.exe` z dysku sieciowego na stanowisku PZL – bez instalacji .NET, bez lokalnego
serwera i bez lokalnej bazy – z pakietami dostępnymi w PZL.

Zakres: **Pulpit z prototypu** (`prototyp/pzl-ev-prototyp.html`, `vPulpit()`) i ekran **Diagnostyka**, dane
przykładowe, **bez połączenia z bazą**. Pozostałe pozycje menu pokazują ekran zastępczy modułu (dokumentacja
i etapy, które moduł przejmie).

Kod ma docelową strukturę modułową (`docs/architektura.md`, rozdz. 5.3) – po pozytywnym teście jest
rozwijany jako właściwa aplikacja.

## Co aplikacja sprawdza

| Założenie | Jak sprawdzane |
|---|---|
| C# / .NET 10, WPF + MVVM | powłoka i moduły: widoki (`…View.xaml`) związane z `…ViewModel` |
| self-contained, single-file, bez instalacji .NET | publikacja `build.cmd` → jeden `PZL-EV.exe`; uruchomienie na komputerze bez .NET |
| uruchomienie z dysku sieciowego | start `PZL-EV.exe` ze ścieżki UNC; ekran Diagnostyka pokazuje „Uruchomiono z” |
| Dapper, Microsoft.Data.SqlClient, ClosedXML (OpenXML) | pakiety są w projekcie; ekran **Diagnostyka** pokazuje wersje wczytane w runtime z bundla (bez łączenia z bazą) |
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
`poc-wpf/NuGet.config`, więc dodaj tylko poświadczenia do **konfiguracji użytkownika** (`%APPDATA%\NuGet\NuGet.Config`):

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

**Diagnostyka restore (PowerShell, w `poc-wpf/`):**

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

Nie uruchamiaj `dotnet nuget add/update source` w katalogu `poc-wpf/` bez `--configfile` – dopisałoby token do
wersjonowanego `NuGet.config`. Używaj konfiguracji użytkownika albo zmiennej środowiskowej.

**Tokenu nie wpisujemy do `poc-wpf/NuGet.config` ani nie commitujemy do repozytorium.**

Wynik: `publish\PZL-EV.exe` (ok. 70–100 MB – zawiera runtime .NET i WPF).

## Test (stanowisko użytkownika)

1. Skopiuj `publish\PZL-EV.exe` do folderu na dysku sieciowym, np. `\\serwer\udzial\PZL-EV\_test\`.
2. Na komputerze **bez zainstalowanego .NET** uruchom go ze ścieżki UNC (dwuklik albo
   `\\serwer\udzial\PZL-EV\_test\PZL-EV.exe`).
3. Sprawdź:
   - [ ] okno się otwiera, widać Pulpit (karty faz globalnych, 3 projekty, Wymaga uwagi, Ostatnie zdarzenia),
   - [ ] menu **Diagnostyka**: Runtime `.NET 10…`, 4 pakiety z wersjami, „Uruchomiono z” = ścieżka UNC,
   - [ ] konto Windows = Twoje konto AD,
   - [ ] w folderze `.exe` powstał `logs\pzl-ev-test-*.log` z wpisami startu, listą modułów i – po wejściu
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

Zasady i przepis „Nowy moduł” – `docs/architektura.md`, rozdz. 5.3.

```
poc-wpf/
├── build.cmd                    publikacja self-contained single-file win-x64
├── NuGet.config                 źródło pakietów = feed eFOSS (bez poświadczeń)
├── ArchitectureRules.targets    granice modułów sprawdzane przy każdej budowie (błąd PZLARCH)
└── src/
    ├── PzlEv.Test.csproj        net10.0-windows, WPF, pakiety z założeń
    ├── .editorconfig            przestrzeń nazw = folder (IDE0130)
    ├── App.xaml(.cs)            start, Serilog, obsługa wyjątków, zasoby wspólne
    ├── Shell/                   okno, menu, nawigacja; ModuleCatalog.cs – lista modułów
    ├── Modules/
    │   ├── Dashboard/           Pulpit – pełny ekran (Views, ViewModels, Models, Data)
    │   ├── Diagnostics/         wynik testu stosu (wersje pakietów wczytanych w runtime)
    │   └── Import/ Mapping/ …   pozostałe moduły: plik wejścia + etapy, ekran zastępczy
    └── Shared/
        ├── Utils/               MVVM, konwerter kolorów, kontrakt modułu i nawigacji
        ├── Models/              modele wspólne, klucze modułów, kontrakt etapu (Pipeline/)
        └── Views/
            ├── Templates/       Theme.xaml – paleta z prototypu, style, układ strony
            └── Partials/        pigułka statusu, kropka etapu, nagłówek ekranu, ekran zastępczy
```
