# PZL-EV – Architektura

Zakres: technologia, uruchomienie i dystrybucja, warstwy i moduły aplikacji, podział logiki między aplikację
i bazę, dysk sieciowy, środowiska, wymagania niefunkcjonalne i ryzyka.

Powiązane: `docs/model-danych.md` (dane i wersjonowanie), `docs/pipeline-fazy.md` (przepływ),
`docs/uprawnienia.md` (role), `docs/funkcjonalnosc.md` (ekrany i funkcje).

---

## 1. Cele architektury

1. **Jedno źródło stanu** – wszystkie dane w centralnej bazie MS SQL; każdy widzi, co przetworzono, kto
   i na jakich danych.
2. **Odtwarzalność** – każdy wynik EV można odtworzyć (`docs/model-danych.md`, rozdz. 4).
3. **Kontrola jakości danych** – dane są sprawdzane, zanim zostaną użyte (`docs/pipeline-fazy.md`, rozdz. 1.3).
4. **Niezależność projektów** – przebieg dotyczy jednego projektu, projekty nie czekają na siebie.
5. **Prostota wdrożenia i utrzymania** – jeden plik aplikacji, bez serwera aplikacyjnego i bez lokalnej bazy.

---

## 2. Technologia

Kierunek **wstępny** – do potwierdzenia testem na stanowisku PZL (O6, aplikacja `app/`).

**Bez SQLite.** Wszystkie słowniki, mapowania, konfiguracja, definicje źródeł, wersje reguł, użytkownicy i role,
zakresy uprawnień oraz historia zmian są w bazie MS SQL; aplikacja nie ma lokalnej bazy ani bazy plikowej
na dysku sieciowym.

| Obszar | Rozwiązanie |
|---|---|
| Język i platforma | C#, .NET 10 LTS |
| Interfejs | WPF, wzorzec MVVM |
| Baza danych | MS SQL Server – centralna baza `PZL_EV`, osobna dla TEST i PROD |
| Dostęp do bazy | Microsoft.Data.SqlClient i Dapper (wywołania procedur, odczyt widoków); SqlBulkCopy – ładowanie wierszy importu |
| Excel | ClosedXML – generowanie plików i wymiana słowników; OpenXML SDK – strumieniowy odczyt dużych plików RABIT |
| Źródła plikowe | WebDAV (SharePoint RABIT przez usługę WebClient Windows), SMB (dysk sieciowy) |
| Log techniczny | Serilog; zdarzenia biznesowe (kto, co, kiedy) – dziennik w bazie |
| Uwierzytelnienie | konto Windows / AD (Windows Authentication do MS SQL) |

---

## 3. Uruchomienie i dystrybucja

- **`PZL-EV.exe`** – jedna aplikacja dla wszystkich ról, publikowana jako *self-contained, single-file*:
  nie wymaga instalacji .NET, nie uruchamia lokalnego serwera i nie ma lokalnej bazy. Działa na komputerze
  użytkownika i łączy się bezpośrednio z bazą MS SQL oraz źródłami plikowymi.
- Funkcje dostępne w aplikacji wynikają z roli użytkownika (`docs/uprawnienia.md`).
- Profil środowiska (TEST / PROD) określa serwer bazy i korzeń folderów; konfiguracja nie zawiera haseł.
  Nagłówek aplikacji pokazuje środowisko, użytkownika, rolę oraz wersję aplikacji i schematu.
- Przy starcie aplikacja sprawdza w bazie **minimalną wymaganą wersję aplikacji** i **wersję schematu**;
  niezgodna wersja odmawia pracy i informuje, co zaktualizować. Dzięki temu wszyscy liczą EV tą samą wersją
  silnika.
- Długie operacje (import, łączenie źródeł) działają w tle z widocznym postępem.
- Forma dystrybucji pliku wymaga potwierdzenia testem na stanowisku PZL (O6).

---

## 4. Widok ogólny

```text
 Stanowisko użytkownika                               Centralnie
 ┌──────────────────────────────┐            ┌────────────────────────────────────┐
 │ PZL-EV.exe (WPF)             │  konto AD  │ MS SQL – baza PZL_EV (TEST / PROD) │
 │  – ekrany według roli        │◄──────────►│  procedury i widoki                │
 │  – orkiestracja etapów       │            │  importy, słowniki, mapowanie,     │
 │  – import i walidacja        │            │  przebiegi, rewizje, wyniki,       │
 │  – silnik EVM                │            │  problemy, dziennik                │
 │  – pliki Excel               │            └─────────────────┬──────────────────┘
 └──────┬──────────────┬────────┘                              │ odczyt
        │ WebDAV       │ SMB                 ┌─────────────────▼──────────────────┐
        ▼              ▼                     │ splmcd03: PZLPROD.LOG, PZL_SAP     │
 SharePoint RABIT   Dysk sieciowy            │ struktura P1S,                     │
 (raporty SAP CES)  (pliki dla finansów,     │ zaawansowanie z produkcji          │
                     CAM, wyniki EV)         └────────────────────────────────────┘
```

Źródła i ich znaczenie – `docs/zrodla-danych.md`.

---

## 5. Warstwy i moduły

Aplikacja jest jednym plikiem wykonywalnym, wewnętrznie podzielonym na warstwy i moduły. Nie ma mikroserwisów
ani osobnych aplikacji.

### 5.1 Warstwy

| Warstwa | Odpowiedzialność |
|---|---|
| UI (WPF) | ekrany i widoki według roli; bez logiki biznesowej |
| Application | przypadki użycia: uruchamianie etapów, sprawdzanie bramek i roli, obsługa długich operacji |
| Domain | pojęcia i reguły: projekt, przebieg, rewizja, słowniki, walidacja przy zapisie |
| Processing | parsery źródeł (wersjonowane) – przekształcenie wierszy pliku do postaci kanonicznej |
| EVM | silnik obliczeń EV (`docs/ev-obliczenia.md`) – bez zależności od pozostałych warstw poza Domain |
| Authorization | rola użytkownika z grup AD, dostęp do funkcji (`docs/uprawnienia.md`) |
| Excel | odczyt i generowanie plików: słowniki, pliki dla finansów i CAM, wyniki |
| File Connectors | WebDAV, SMB |
| SQL Access | wywołania procedur, odczyt widoków, ładowanie wsadowe |

### 5.2 Moduły funkcjonalne

Każdy moduł jest folderem `Modules/<Folder>/` i właścicielem swoich ekranów oraz etapów przebiegu
(`docs/pipeline-fazy.md`). Moduł bez własnego ekranu pokazuje panele swoich etapów na ekranie Przebiegu.

| Moduł | Folder | Zakres | Etapy | Opis |
|---|---|---|---|---|
| Shell (powłoka, nie moduł) | `Shell/` | okno, nagłówek, menu, nawigacja, lista modułów | – | `docs/funkcjonalnosc.md`, rozdz. 2 |
| Pulpit | `Dashboard` | pulpit, „Wymaga uwagi”, ostatnie zdarzenia (F07) | – | `docs/funkcjonalnosc.md` |
| Import | `Import` | pobranie i załadowanie plików, parsery źródeł (F05) | G1 | `docs/zrodla-danych.md` |
| Mapping | `Mapping` | ekran Mapowanie CES ↔ P1S, korekty, drzewo P1S (F04); rozstrzyganie mapowania jest wspólne (`Shared/Utils/Mapping`) | G2 | `docs/mapowanie-ces-p1s.md` |
| Master Data | `MasterData` | ekran Słowniki – słowniki globalne (F03); mechanizm słowników jest wspólny (`Shared/Utils/Dictionaries`) | G3 | `docs/slowniki.md` |
| Projects | `Projects` | projekty, kreator, Performance Objectives, słowniki projektu, gotowość, foldery projektu (F01, F02) | – | `docs/performance-objectives.md` |
| Runs | `Runs` | przebiegi, ekran przebiegu, przypięcie stanu, walidacja, zamknięcie (F06) | P0, P1, P2, Z | `docs/pipeline-fazy.md`, `docs/model-danych.md` |
| Reconciliation | `Reconciliation` | łączenie źródeł, pliki dla finansów | P3, P4 | `docs/pipeline-fazy.md` |
| CAM / Progress | `Progress` | zaawansowanie: produkcja, uzupełnienia, pliki CAM | P5, P6, P7 | `docs/pipeline-fazy.md` |
| EVM Engine | `Evm` | obliczenia EV | P8 | `docs/ev-obliczenia.md` |
| Export | `Export` | pliki wynikowe, plik dla Cobra | P9 | `docs/funkcjonalnosc.md`, rozdz. 4 |
| Administration | `Administration` | definicje źródeł, lokalizacje RABIT, role (F08) | – | `docs/zrodla-danych.md`, `docs/uprawnienia.md` |

Kontrola jakości (`meta.Problem`) i dziennik zdarzeń nie są modułami – to mechanizmy wspólne (`Shared`),
z których korzysta każdy moduł; pokazują je Pulpit i ekran Przebiegu.

### 5.3 Struktura kodu

Jeden projekt, moduły jako foldery; przestrzeń nazw odpowiada ścieżce folderu (`PzlEv.Modules.Dashboard.ViewModels`
↔ `Modules/Dashboard/ViewModels/`). Wzorzec pokazuje kod `app/src/` – moduły z pełnym ekranem
(Pulpit, Słowniki, Import, Projekty, Administracja) i ekrany zastępcze z przypisanymi etapami dla pozostałych.

```text
App.xaml(.cs)                      start: konfiguracja → usługi wspólne → moduły → powłoka; logowanie
Shell/                             okno, menu, nawigacja; ModuleCatalog.cs – lista modułów
Modules/<Moduł>/
  <Moduł>Module.cs                 plik wejścia: klucz, menu, dokumentacja, etapy, utworzenie ekranu
  Views/ ViewModels/               ekran i jego logika prezentacji (WPF)
  Models/                          modele modułu
  Data/                            magazyn danych modułu: I<Moduł>Store (kontrakt) + Sql<Moduł>Store
  Services/ Stages/                przypadki użycia, walidacja, parsery, implementacje etapów (IStage)
Shared/
  Utils/Ui/                        MVVM (w tym BusyState – operacja w tle), konwertery, okna wyboru pliku, kontrakt modułu i nawigacji (WPF)
  Utils/Config/                    konfiguracja pzl-ev.json
  Utils/Data/                      baza MS SQL (Sql/: połączenie, migracje, dziennik, problemy), czas, użytkownik, blokady operacji, usługi wspólne
  Utils/Files/                     Excel, CSV/TXT, liczby polskie, daty, sygnatura kolumn, SHA-256
  Utils/Mapping/                   rozstrzyganie mapowania CES ↔ P1S (reguły, klucze, magazyn raportu i korekt) – używają go moduły Mapowanie i Projekty, później przebiegi (P1, P3)
  Utils/Dictionaries/              mechanizm słowników (walidacja, zapis z historią, wymiana przez Excel, magazyn SQL) i słowniki globalne – używają go moduły Słowniki i Projekty
  Models/                          modele wspólne, klucze modułów, wynik kontroli (Issue), kontrakt etapu (Pipeline), opisy słowników (Dictionaries), modele mapowania (Mapping)
  Models/Db/                       wiersze tabel schematu (meta, stg, can, dict) – wspólne jak schemat bazy
  Models/Sources/                  parsery i układy kolumn źródeł (kontrakt danych)
  Views/Templates/                 wygląd całej aplikacji: paleta, style, tabele, układ strony
  Views/Partials/                  fragmenty wielokrotnego użytku: pigułka statusu, wynik kontroli, nagłówek ekranu, pasek „Trwa: …” (BusyBar), ekran zastępczy
```

Podfolder modułu powstaje dopiero, gdy ma zawartość. Warstwy z rozdz. 5.1 mieszczą się w tym układzie:
UI – `Views`, `ViewModels`, `Shared/Views`; Application i Processing – `Services`, `Stages`; Domain – `Models`
modułu i `Shared/Models`; Excel, File Connectors, SQL Access – `Shared/Utils` (wspólne) albo `Data` (modułu).

**Zależności** – sprawdzane przy każdej budowie (`app/ArchitectureRules.targets`, błąd `PZLARCH`):

- moduł korzysta tylko z `Shared`; nie zna innego modułu ani `Shell`,
- `Shared` nie zna modułów ani `Shell`,
- `Shell` zna konkretne moduły tylko w `ModuleCatalog.cs`; poza nim – wyłącznie kontrakt `IModule`,
- moduły komunikują się przez bazę (etapy – `docs/pipeline-fazy.md`, rozdz. 1.1), nawigację po kluczu
  (`INavigator`, `ModuleKeys`) albo kontrakt w `Shared`,
- silnik EVM zależy wyłącznie od modeli (rozdz. 5.1).

**Warstwa danych.** Aplikacja pracuje wyłącznie na bazie MS SQL środowiska (`pzl-ev.json`, rozdz. 8). Moduł korzysta
z danych przez swój magazyn `Data/I<Moduł>Store` (kontrakt odpowiadający procedurom i widokom) w implementacji
`Data/Sql<Moduł>Store`; wiersze tabel – `Shared/Models/Db`. Moduły wymieniają dane przez wspólne tabele, nie przez
swoje typy. Wersji danych w pamięci nie ma (usunięta 2026-10-02 z aplikacji i z testów); Pulpit także czyta
wyłącznie z bazy (`Dashboard/Data/SqlDashboardData`).

**Testy** (`app/tests`, xUnit, `net10.0`): projekt kompiluje pliki logiki aplikacji – wszystko poza `Views`,
`ViewModels`, `Shell`, `Shared/Utils/Ui` i plikami `*Module.cs` – więc logika nie może używać typów WPF i testy
działają także poza Windows. Testy magazynów i serwisów działają na bazie SQL (`TestDatabase`, atrybut `[SqlFact]`);
baza to sekcja TEST z `pzl-ev.json` (konto AD, jak aplikacja) albo zmienna `PZLEV_TEST_SQL`; bazy niedostępnej – pomijane z powodem. Dane wzorcowe z oczekiwanymi sumami – `app/testdata`.
`build.cmd` uruchamia testy przed publikacją.

**Konwencje:** jeden typ w pliku, nazwa pliku = nazwa typu; sufiksy `…Module`, `…View`, `…ViewModel`,
`I…Store`, `Sql…Store`, `…Service`; kolory i style wyłącznie z `Shared/Views/Templates`; fragment trafia do
`Shared/Views/Partials`, gdy używa go drugi moduł; log z kontekstem modułu (`Log.ForContext("Module", …)`).

**Responsywność:** odczyt i zapis w bazie, PZLPROD, pliki Excel i dysk sieciowy nie wykonują się w wątku okna.
Ekran uruchamia je przez `BusyState` (`Shared/Utils/Ui/Mvvm`) – praca w tle, pasek „Trwa: … (n s)” (`BusyBar`),
kursor „praca w tle”, polecenia ekranu nieaktywne do końca operacji; wynik wraca do wątku okna. Dane duże i rzadko
zmieniane (raport mapowań, LOG.WBS) są czytane raz na sesję, z przyciskiem odświeżenia; kontrole wywoływane przy
każdej edycji działają na danych w pamięci (zapis sprawdza je ponownie w bazie).

**Kontrakt etapu** (`Shared/Models/Pipeline`): `StageDescriptor` (kod, nazwa, krok, zakres, czy wymaga
decyzji człowieka) deklarowany przez moduł-właściciela i `IStage` (bramka wejścia, akcja z bramką wyjścia,
wynik z kontrolami ERROR / WARNING / PASS). Ten sam kontrakt obsługuje uruchomienie etapu z ekranu przez
analityka i późniejsze łączenie etapów bez udziału człowieka – łańcuch zatrzymuje się na etapie wymagającym
decyzji (P4, P6, Z) albo z wynikiem ERROR.

**Nowy moduł:**

1. Wiersz w tabeli 5.2 (folder, zakres, etapy, dokument).
2. Klucz w `Shared/Models/ModuleKeys.cs`.
3. `Modules/<Moduł>/<Moduł>Module.cs` implementujący `IModule`; do czasu implementacji ekran zastępczy
   (`PlaceholderView.Create`).
4. Dane: tabele w nowej migracji `sql/mssql/NNN_*.sql` (dane startowe – osobna migracja `NNN_dane_*.sql`), `Data/I<Moduł>Store` + `Data/Sql<Moduł>Store` (wiersze w
   `Shared/Models/Db`) i testy `[SqlFact]` na bazie testowej;
   ekran: `Views/<Moduł>View.xaml`, `ViewModels/<Moduł>ViewModel.cs`.
5. Jedna linia w `Shell/ModuleCatalog.cs`.
6. Budowa (`build.cmd`) – reguły zależności i przestrzeni nazw oraz testy muszą przejść.

---

## 6. Podział logiki między aplikację i bazę

| Baza (procedury i widoki) | Aplikacja |
|---|---|
| wybór danych projektu według znacznika stanu | orkiestracja etapów przebiegu (każdy etap uruchamia użytkownik) |
| łączenie źródeł i agregacje dużych wolumenów | import plików: odczyt, rozpoznanie, parsowanie |
| reguły procesu: jeden przebieg na projekt i tydzień, bramki etapów, zamrożenie | walidacja przy zapisie słowników |
| historia zmian i dziennik | silnik EVM |
| widoki dla raportów BI | generowanie plików Excel i pliku dla Cobra |

- Aplikacja korzysta z bazy wyłącznie przez procedury i widoki. Użytkownicy nie mają praw do tabel –
  mają prawo wykonywania procedur i odczytu widoków. Wyjątkiem jest ładowanie wsadowe wierszy importu
  (SqlBulkCopy) do tabeli przyjęć, na której rola ma wyłącznie prawo INSERT.
- Reguły procesu są wymuszane w procedurach – aplikacja działa na stanowisku użytkownika i nie może być jedyną
  kontrolą.
- Każda procedura zapisuje użytkownika AD (`ORIGINAL_LOGIN()`) w dzienniku.

---

## 7. Dysk sieciowy

```text
\\serwer\udział\PZL-EV\                 korzeń środowiska (osobny dla TEST i PROD)
├── 00_Global\RABIT\Do_importu\         pliki RABIT pobrane ręcznie (powyżej limitu WebDAV)
├── 00_Global\RABIT\import.lock         blokada importu (docelowo sp_getapplock – F10.2)
└── Projekty\<Projekt>\
    ├── Finanse\<RRRR-MM>\              pliki dla finansów (P4)
    ├── CAM\<RRRR-MM>\Wyslane\          pliki dla CAM (P6, przebieg zamykający)
    ├── CAM\<RRRR-MM>\Zwrocone\         pliki zwrócone przez CAM
    └── EV\<RRRR-MM>\                   wyniki EV i plik dla Cobra (P9)
```

- Ścieżki UNC (nie litery dysków); w bazie zapisywane są ścieżki **względne** od korzenia środowiska.
- Aplikacja sprawdza strukturę folderów przy otwarciu projektu.
- Uprawnienia do folderów – `docs/uprawnienia.md`, rozdz. 5.
- Oryginalne pliki RABIT nie są archiwizowane ani przechowywane w bazie – w bazie są ich wersje (SHA-256, kolumny, liczba wierszy) i dane kanoniczne (`docs/model-danych.md`, rozdz. 5.1).

---

## 8. Środowiska i wdrożenia

- **Konfiguracja środowiska:** plik `pzl-ev.json` obok `PZL-EV.exe` – przełącznik `Env` (`TEST` / `PROD`) i sekcja
  `Environments.<Env>`: `NetworkRoot` (korzeń folderów środowiska, rozdz. 7), `Sql` (serwer, baza, schemat,
  sygnatura tabel – logowanie kontem AD) i `PzlProd` (struktura P1S: serwer `splmcd03`, baza `PZLPROD`, schemat
  `LOG` – tylko odczyt kontem AD; sekcja opcjonalna – bez niej ekran Mapowanie pokazuje, czego brakuje). Plik jest wymagany – wzór z opisem ustawień `app/pzl-ev.json` kopiowany
  obok exe przy budowie; brak pliku albo pola = komunikat przy starcie (`app/README.md`). TEST: `pzltestdb.intl.lmco.com`, baza `PZLTEST`, schemat `FINOP`, sygnatura `PZLEV_`.
- **TEST** – developer; osobna baza i osobny korzeń folderów.
- **PROD** – wdraża administrator (IT).
- Zmiany bazy jako numerowane, idempotentne skrypty migracyjne w repozytorium (`sql/mssql/NNN_*.sql`, zmienne
  sqlcmd `$(Schema)` i `$(Prefix)`); baza przechowuje wersję schematu i minimalną wymaganą wersję aplikacji
  (`META_SchemaVersion`). Skrypty są wbudowane w exe; brakujące wykonuje przycisk Diagnostyka → Migracja (wykonane
  pomija, `sp_getapplock` chroni przed równoczesnym uruchomieniem) albo pytanie przy starcie. Dane startowe (presety)
  – osobne migracje `NNN_dane_*.sql` (`002_dane_startowe.sql`); aplikacja nie dopisuje danych z kodu.
  Aplikacja nie zmienia schematu poza migracjami (w PROD użytkownicy nie mają prawa tworzenia i zmiany tabel):
  dane kanoniczne wszystkich parserów są w jednej stałej tabeli `CAN_Row` ze slotami typowanymi (migracja 007,
  `docs/model-danych.md`, rozdz. 5.1) – nowy parser albo nowe pole to tylko zapis parsera. Migracje uruchamia osoba
  z prawem zmiany schematu (Diagnostyka → Migracja albo sqlcmd).
- **Serwer SQL:** SQL Server 2016 lub nowszy, poziom zgodności bazy co najmniej 130 (indeks kolumnowy, `OPENJSON`,
  `COMPRESS`); Diagnostyka pokazuje wersję serwera, edycję i poziom zgodności bazy.
- Paczka wdrożeniowa: skrypty bazy, plik `PZL-EV.exe`, instrukcja dla administratora.
- **Pakiety (NuGet):** zależności (Dapper, Microsoft.Data.SqlClient, ClosedXML/OpenXML, Serilog) przywracane
  są z firmowego proxy **eFOSS (Nexus)** – `https://nexus.global.lmco.com/repository/nuget-proxy-v3/index.json`,
  nie z nuget.org; połączenie bezpośrednie, bez proxy (wymagana sieć LM / VPN). Źródło ustawia wersjonowany
  `NuGet.config`; logowanie: NTID +
  token dostępu eFOSS (generowany w `efoss.global.lmco.com/accesstoken`, wymaga charge number i CAM).
  Token trzymany poza repozytorium (Windows Credential Manager albo zmienna środowiskowa budowy); nigdy nie
  jest commitowany.
  Konfiguracja i rozwiązywanie problemów – `app/README.md`.

---

## 9. Wymagania niefunkcjonalne

| Obszar | Wymaganie |
|---|---|
| Wydajność | import plików ACTUALS z milionami wierszy w czasie akceptowalnym dla przebiegu tygodniowego – plik pobierany na dysk lokalny, odczyt strumieniowy (wiersz po wierszu, także Excel – bez modelu dokumentu), jeden przebieg z parsowaniem równolegle z ładowaniem wsadowym do `CAN_Row` (indeks kolumnowy), bez podglądu danych; pamięć stała (ok. 0,3 GB niezależnie od rozmiaru pliku). Pomiar bez pobierania (SQL Server 2022 lokalnie): CSV 87 MB / 560 tys. wierszy – 10 s; Excel 67 MB / 1 mln wierszy – 21 s (wcześniejszy odczyt ClosedXML: 102 s i 5,9 GB pamięci samego odczytu) |
| Wydajność – ekran projektu | edycja struktury zapisywana wsadowo (jeden zapis na słownik niezależnie od liczby wierszy; zamknięcie wersji jednym `UPDATE … IN`, identyfikatory nowych wierszy jednym `sp_sequence_get_range`, wielowierszowy `INSERT`); odświeżenie po zapisie czyta każdy słownik projektu raz, dane referencyjne (osoby, nakładki innych projektów, foldery na dysku sieciowym) – raz na otwarcie ekranu i przy „Odśwież” (`docs/performance-objectives.md`, rozdz. 4.2) |
| Odtwarzalność | każdy wynik EV odtwarzalny ze znacznika stanu i wersji silnika (`docs/model-danych.md`, rozdz. 4) |
| Audyt | każda akcja z użytkownikiem AD i czasem; historia słowników i korekt |
| Spójność | ta sama wersja silnika EV u wszystkich (kontrola minimalnej wersji aplikacji) |
| Odporność | przerwana operacja nie zostawia częściowych danych (transakcje, idempotentny import) |
| Utrzymanie | definicje źródeł, lokalizacje RABIT, reguły walidacji źródeł i kalendarz okresów konfigurowane w aplikacji, bez zmiany kodu |

---

## 10. Ryzyka

| # | Ryzyko | Ograniczenie |
|---|---|---|
| 1 | Różne wersje aplikacji u użytkowników dają różne wyniki EV | kontrola minimalnej wersji w bazie; wersja silnika w rewizji |
| 2 | Uruchamianie pliku exe zablokowane (AppLocker, antywirus) | test przed decyzją o formie dystrybucji (O6); podpis kodu; alternatywnie instalacja zarządzana przez IT |
| 3 | Przebiegi trwające dni | trwały stan w bazie, kontynuacja przez inną osobę, unieważnianie etapów |
| 4 | Zmiana słowników lub nowe importy w trakcie przebiegu | znacznik stanu, decyzja „kontynuuj / przypnij ponownie” w dzienniku |
| 5 | Zmiana istniejących wierszy w `PZLPROD` (założenie przyrostowości) | odczyt bez kopiowania; zmiana `LOG.WBS` nie jest wykrywana i może zmienić wynik odtworzenia rewizji; raport mapowań jest słownikiem z historią wierszy |
| 6 | Nieaktualne dane produkcyjne (`vAHDD`) | kontrola świeżości w P0 |
| 7 | Mieszanie źródeł zaawansowania | zapis pochodzenia; w przebiegu zamykającym wyłącznie CAM |
| 8 | Nowe elementy SAP bez przypisania | wykrywanie po imporcie i w P3; blokada w przebiegu zamykającym |
| 9 | Pliki CAM zmienione poza polami lub z innego przebiegu | identyfikator i blokady w szablonie, kontrola przy imporcie |
| 10 | Excel zmienia typy (WBS jako liczba, daty, zera wiodące) | walidacja typów; CSV z RABIT, jeśli dostępny |
| 11 | Reguły procesu omijane przez bezpośrednie połączenie z bazą | reguły w procedurach, brak praw do tabel |
| 12 | Różne litery dysków | ścieżki UNC, w bazie ścieżki względne |
| 13 | Etykiety poufności / szyfrowanie plików | do weryfikacji z IT |
| 14 | Przyrost danych w bazie (wiersze importów co tydzień – wszystko zostaje, O32) | kompresja kolumnowa `CAN_Row`; sam plik nie jest przechowywany (`docs/model-danych.md`, rozdz. 5.1) |
| 15 | Wsparcie .NET 10 LTS kończy się w listopadzie 2028 | przejście na kolejną wersję LTS przed tym terminem |

---

## 11. Otwarte kwestie

| # | Kwestia |
|---|---|
| O5 | Serwer bazy `PZL_EV` – na instancji z `PZLPROD` (odczyt w procedurach między bazami) czy osobny serwer (serwer połączony) |
| O6 | Forma dystrybucji `PZL-EV.exe`. Test na stanowisku PZL: (1) uruchomienie pliku z dysku lokalnego i z udziału sieciowego, potrzeba podpisu kodu; (2) połączenie z MS SQL TEST kontem Windows; (3) odczyt lokalizacji RABIT przez WebDAV; (4) czas załadowania pliku RABIT ok. 85 MB do bazy. Aplikacja `app/` (lista kontrolna testu stosu w `app/README.md`) sprawdza punkt (1) i pakiety z rozdz. 2; punkty (2)–(4) wymagają osobnego testu |
