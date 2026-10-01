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

Wymagane: **.NET 10 SDK** (tylko do budowy) i dostęp do pakietów NuGet z `src/PzlEv.Test.csproj`
(nuget.org albo wewnętrzny feed / cache offline PZL – wersje można dostosować w `.csproj`).

```bat
build.cmd
```

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
| błąd restore pakietu przy budowie | pakiet niedostępny w feedzie PZL | lista pakietów do zatwierdzenia / lokalny cache NuGet |

## Struktura

```
poc-wpf/
├── build.cmd                    publikacja self-contained single-file win-x64
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
