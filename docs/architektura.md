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
 SharePoint RABIT   Dysk sieciowy            │ struktura P1S, raport mapowań,     │
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
| Processing | parsery źródeł (wersjonowane) – przekształcenie wierszy surowych do postaci kanonicznej |
| EVM | silnik obliczeń EV (`docs/ev-obliczenia.md`) – bez zależności od pozostałych warstw poza Domain |
| Authorization | rola użytkownika z grup AD, dostęp do funkcji (`docs/uprawnienia.md`) |
| Excel | odczyt i generowanie plików: słowniki, pliki dla finansów i CAM, wyniki |
| File Connectors | WebDAV, SMB |
| SQL Access | wywołania procedur, odczyt widoków, ładowanie wsadowe |

### 5.2 Moduły funkcjonalne

| Moduł | Zakres | Opis |
|---|---|---|
| Shell / UI | nawigacja, nagłówek, pulpit | `docs/funkcjonalnosc.md` |
| Source Management | definicje źródeł | `docs/zrodla-danych.md`, rozdz. 2 |
| Import | pobranie i załadowanie plików | `docs/pipeline-fazy.md`, G1 |
| Data Quality | kontrole i problemy (ERROR / WARNING) | `docs/pipeline-fazy.md`, rozdz. 1.3 |
| Master Data | słowniki globalne i projektu | `docs/slowniki.md` |
| Mapping | mapowanie CES ↔ P1S | `docs/mapowanie-ces-p1s.md` |
| Project Run | projekty, przebiegi, etapy, rewizje | `docs/pipeline-fazy.md`, `docs/model-danych.md` |
| CAM / Progress | zaawansowanie: produkcja, uzupełnienia, pliki CAM | `docs/pipeline-fazy.md`, P5–P7 |
| Reconciliation | łączenie źródeł | `docs/pipeline-fazy.md`, P3 |
| EVM Engine | obliczenia EV | `docs/ev-obliczenia.md` |
| Export | pliki wynikowe, plik dla Cobra | `docs/funkcjonalnosc.md`, rozdz. 4 |
| Audit | dziennik zdarzeń, historia | `docs/model-danych.md`, rozdz. 3 |
| Administration | role, konfiguracja | `docs/uprawnienia.md`, `docs/funkcjonalnosc.md` |

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
└── Projekty\<Projekt>\
    ├── Finanse\<RRRR-MM>\              pliki dla finansów (P4)
    ├── CAM\<RRRR-MM>\Wyslane\          pliki dla CAM (P6, przebieg zamykający)
    ├── CAM\<RRRR-MM>\Zwrocone\         pliki zwrócone przez CAM
    └── EV\<RRRR-MM>\                   wyniki EV i plik dla Cobra (P9)
```

- Ścieżki UNC (nie litery dysków); w bazie zapisywane są ścieżki **względne** od korzenia środowiska.
- Aplikacja sprawdza strukturę folderów przy otwarciu projektu.
- Uprawnienia do folderów – `docs/uprawnienia.md`, rozdz. 5.
- Oryginalne pliki RABIT nie są archiwizowane – ich treść i hash są w bazie (`docs/model-danych.md`, rozdz. 1).

---

## 8. Środowiska i wdrożenia

- **TEST** – developer; osobna baza i osobny korzeń folderów.
- **PROD** – wdraża administrator (IT).
- Zmiany bazy jako numerowane, idempotentne skrypty migracyjne w repozytorium (`sql/mssql/`); baza przechowuje
  wersję schematu i minimalną wymaganą wersję aplikacji.
- Paczka wdrożeniowa: skrypty bazy, plik `PZL-EV.exe`, instrukcja dla administratora.
- **Pakiety (NuGet):** zależności (Dapper, Microsoft.Data.SqlClient, ClosedXML/OpenXML, Serilog) przywracane
  są z firmowego proxy **eFOSS (Nexus)** – `https://nexus.global.lmco.com/repository/nuget-proxy-v3/index.json`,
  nie z nuget.org. Źródło ustawia wersjonowany `NuGet.config`; logowanie: NTID + token dostępu eFOSS
  (generowany w `efoss.global.lmco.com/accesstoken`, wymaga charge number i CAM). Token trzymany poza
  repozytorium (Windows Credential Manager albo zmienna środowiskowa budowy); nigdy nie jest commitowany.
  Konfiguracja i rozwiązywanie problemów – `poc-wpf/README.md`.

---

## 9. Wymagania niefunkcjonalne

| Obszar | Wymaganie |
|---|---|
| Wydajność | import 700 000+ wierszy (plik ok. 85 MB) w czasie akceptowalnym dla przebiegu tygodniowego – ładowanie wsadowe, bez podglądu danych |
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
| 5 | Zmiana istniejących wierszy w `PZLPROD` (założenie przyrostowości) | odczyt bez kopiowania; zmiana raportu mapowań lub `LOG.WBS` nie jest wykrywana i może zmienić wynik odtworzenia rewizji |
| 6 | Nieaktualne dane produkcyjne (`vAHDD`) | kontrola świeżości w P0 |
| 7 | Mieszanie źródeł zaawansowania | zapis pochodzenia; w przebiegu zamykającym wyłącznie CAM |
| 8 | Nowe elementy SAP bez przypisania | wykrywanie po imporcie i w P3; blokada w przebiegu zamykającym |
| 9 | Pliki CAM zmienione poza polami lub z innego przebiegu | identyfikator i blokady w szablonie, kontrola przy imporcie |
| 10 | Excel zmienia typy (WBS jako liczba, daty, zera wiodące) | walidacja typów; CSV z RABIT, jeśli dostępny |
| 11 | Reguły procesu omijane przez bezpośrednie połączenie z bazą | reguły w procedurach, brak praw do tabel |
| 12 | Różne litery dysków | ścieżki UNC, w bazie ścieżki względne |
| 13 | Etykiety poufności / szyfrowanie plików | do weryfikacji z IT |
| 14 | Przyrost danych w bazie (wiersze importów co tydzień) | retencja (`docs/model-danych.md`, O32) |
| 15 | Wsparcie .NET 10 LTS kończy się w listopadzie 2028 | przejście na kolejną wersję LTS przed tym terminem |

---

## 11. Otwarte kwestie

| # | Kwestia |
|---|---|
| O5 | Serwer bazy `PZL_EV` – na instancji z `PZLPROD` (odczyt w procedurach między bazami) czy osobny serwer (serwer połączony) |
| O6 | Forma dystrybucji `PZL-EV.exe`. Test na stanowisku PZL: (1) uruchomienie pliku z dysku lokalnego i z udziału sieciowego, potrzeba podpisu kodu; (2) połączenie z MS SQL TEST kontem Windows; (3) odczyt lokalizacji RABIT przez WebDAV; (4) czas załadowania pliku RABIT ok. 85 MB do bazy |
