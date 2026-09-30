# PZL-EV – Earned Value Reporting Platform

**PZL-EV** to oprogramowanie do raportowania Earned Value w PZL Mielec, rozwijane w ramach zespołu /
programu **Unicorn-EV** (obejmującego także inne elementy poza tym oprogramowaniem).
Nazwa „AHD” jest zarezerwowana dla raportu w operacjach i nie jest używana w tym projekcie –
wyjątkiem są istniejące obiekty SQL w `PZLPROD.LOG` (`vAHDD`, `uspUpdateAHDD`, `AHDD_*`),
których nazwy pochodzą z systemu produkcyjnego.

## Status dokumentu

Wersja: 0.2 (wstępny projekt)

Dokument roboczy opisujący docelowe rozwiązanie wspierające Program Reporting oraz
Earned Value Management (EVM) w PZL Mielec.

Dokument jest rozwijany iteracyjnie na podstawie:
- warsztatów z użytkownikami,
- istniejących plików Excel (stan obecny),
- szablonów raportowych,
- logiki biznesowej,
- procedur finansowych,
- raportów SAP.

### Mapa dokumentacji

| Dokument | Zawartość |
|---|---|
| `readme.md` | kontekst biznesowy, cele, problemy, słowniki, otwarte pytania (ten plik) |
| `docs/architektura.md` | architektura rozwiązania: decyzje, komponenty, przepływ danych, baza, wdrożenia |
| `docs/pipeline-fazy.md` | **jedyny opis etapów**: fazy globalne i etapy przebiegu – cel, wejście, bramki, działanie w aplikacji, kontrole, efekt, statusy |
| `docs/mapowanie-ces-p1s.md` | założenia mapowania struktur WBS CES ↔ P1S i analiza spójności z dokumentacją |
| `docs/funkcjonalnosc.md` | specyfikacja funkcjonalna: ekrany, funkcje, etapy przebiegu, reguły |
| `prototyp/pzl-ev-prototyp.html` | klikalny prototyp aplikacji (dane przykładowe, otwierany w przeglądarce) |
| `prototyp/PZL-EV Pipeline v3.html` | klikalny prototyp v3 – ilustracja ustaleń M16–M26 (mapowanie z raportu mapowań, kategoryzacja WBS, kreator projektu, Cost Category); źródłem prawdy jest dokumentacja |
| `docs/mvp-etap1.md`, `pzl_ev/`, `konfiguracja/` | etap 1: pobranie plików RABIT przez WebDAV, rozpoznanie źródła po prefiksie, import do bazy |
| `dependencies.csv`, `resolved_objects.csv`, `export_log.txt` | eksport metadanych obiektów SQL z `splmcd03` (`PZLPROD.LOG`, `PZL_SAP`) |

---

# Cel projektu

Stworzenie wspólnej platformy raportowej umożliwiającej:

- automatyczne pobieranie danych źródłowych,
- historyzację danych,
- standaryzację raportowania,
- ograniczenie pracy manualnej,
- automatyczne przygotowanie danych EV,
- automatyczne generowanie pakietów raportowych.

Docelowo rozwiązanie ma zmniejszyć zależność od ręcznej obróbki danych w Excelu.

---

# Aktualny stan

Proces jest wykonywany głównie manualnie.

Każdy analityk:

- pobiera dane samodzielnie,
- wykonuje własne transformacje,
- utrzymuje własne pliki Excel,
- utrzymuje własne słowniki,
- wykonuje korekty ręczne,
- utrzymuje własne formuły.

W efekcie:

- występuje duże ryzyko błędów,
- trudno odtworzyć proces,
- trudno wdrożyć nową osobę,
- bardzo dużo czasu poświęcane jest na przygotowanie danych.

---

# Typy projektów EV

## 1. Projekty Sikorsky (SAC)

Charakterystyka:

- oficjalny proces EV prowadzony jest przez Sikorsky,
- Cobra jest wykorzystywana do kalkulacji wskaźników i nadzorowania kosztów,
- PM Compass jest wykorzystywany do wprowadzania zaawansowania kosztów materiałów oraz nakładu godzin,
- PZL dostarcza dane wejściowe (zaawansowanie wprowadzane przez CAM – Cost Account Manager),
- Actual Cost pochodzi z SAP (koszty i godziny),
- ETC przygotowywane jest lokalnie,
- dane przekazywane są do Sikorsky.

Specyfika:

- roboczogodziny PZL traktowane są jako koszt dostawcy, dlatego podczas wprowadzania
  zaawansowania godziny przeliczane są po obecnych stawkach na koszt w USD,
- koszt pracy konwertowany jest do USD,
- raportowanie odbywa się zgodnie z wymaganiami SAC.

Model:

```
SAC – plik Cobra (zawiera budżet i harmonogram)
↓
PZL Finanse
↓
SAP
↓
Przetworzenie danych PZL (kolekcja danych)
↓
Plik Cobra
↓
Sikorsky
↓
Oficjalne EV
```

---

## 2. Projekty CAS Compliance

Charakterystyka:

- stosowane są stawki CAS,
- koszty przeliczane są według reguł CAS,
- koszty nie opierają się wyłącznie na stawkach SAP,
- duża część logiki znajduje się obecnie w Excelach.

Model:

```
SAP
↓
Przetworzenie danych PZL (kolekcja danych)
↓
Przeliczenie CAS
↓
Kalkulacja EV
```

---

## 3. Projekty wewnętrzne

Charakterystyka:

- pełna odpowiedzialność po stronie PZL,
- własne szablony,
- własne raporty,
- własne kalkulacje EV.

Model:

```
SAP
↓
Budżet
↓
Harmonogram
↓
ETC
↓
EV
```

---

# Główne problemy

## Problem 1 – Dane

Duże wolumeny danych.

Przykłady:

- setki tysięcy rekordów,
- nawet 700 000+ wierszy dla pojedynczego projektu,
- wiele plików dla różnych projektów.

Skutki:

- czas pobierania danych (częściowo rozwiązany przez RABIT – narzędzie w SAP do pobierania danych),
- problemy z Excelem (m.in. limit wierszy – dane jednego projektu bywają w kilku plikach),
- problemy z wydajnością,
- długi czas przygotowania danych.

---

## Problem 2 – Wiedza procesowa

Wiedza jest rozproszona. Znajduje się w:

- plikach Excel,
- formułach,
- słownikach,
- tabelach pomocniczych,
- wiedzy konkretnych osób.

Brak centralnego repozytorium wiedzy.

---

## Problem 3 – Transformacje

Występuje duża liczba:

- mapowań,
- słowników,
- tabel translacyjnych,
- wyszukań pionowych (VLOOKUP),
- ręcznych korekt.

Te transformacje nie są obecnie udokumentowane.

---

## Problem 4 – Standaryzacja

Każdy analityk często realizuje proces własnym sposobem. Brakuje:

- wspólnych definicji,
- wspólnych słowników,
- wspólnej warstwy danych.

---

# Wizja rozwiązania (skrót)

Szczegóły: `docs/architektura.md` i `docs/funkcjonalnosc.md`.

- **Aplikacja w Pythonie uruchamiana lokalnie**, obsługiwana w przeglądarce. Każda osoba
  z finansów uruchamia ją u siebie; nie jest potrzebny serwer aplikacyjny.
- **Centralna baza MS SQL** przechowuje dane: importy, przebiegi, wyniki EV i dziennik zdarzeń.
  Każdy widzi, co już przetworzono i przez kogo.
- **Słowniki, przypisania i konfiguracja** (korekty mapowania CES↔P1S, WP i CAM, harmonogramy, budżety, Cost Category,
  stawki, kalendarz, kursy, prefiksy RABIT) – w **lekkiej bazie SQLite na dysku sieciowym**, edytowane w interfejsie
  aplikacji (CRUD + drzewo). **Excel nie jest źródłem słowników** – słowniki projektu i Cost Category można
  pobrać do Excela i wczytać z walidacją (M24, M26). Przypisania CES↔P1S – z raportu mapowań SAP↔CES (M16).
- **Logika biznesowa w bazie** (procedury i widoki SQL) – wynik nie zależy od wersji
  aplikacji na danym komputerze.
- **Pliki na dysku sieciowym** (ścieżki UNC): baza słowników (SQLite), eksporty SAP (RABIT), pliki dla
  finansów, pliki dla CAM, wyniki EV.
- **Praca w projektach** – projekt (dawniej „zakres”, M22) obejmuje węzły drzewa P1S wybrane w kreatorze
  (grupy kategorii i pojedyncze `PROJORG`, M23). Każdy przebieg
  dotyczy jednego projektu, dzięki czemu np. F-16 nie czeka na pliki Cobra dla projektów SAC.

## Warstwy danych

| Warstwa | Zawartość |
|---|---|
| Data Collection | SAP CES (CJI3, ZRD_KKAJ, Net Inv), dane produkcyjne P1S (np. `PZLPROD.LOG.vAHDD`), eksporty Cobra, pliki CAM; słowniki i przypisania – baza słowników (SQLite) |
| Landing Zone | kopie oryginalnych plików (Excel, CSV, TXT) na dysku sieciowym, bez zmian; w bazie ścieżka i hash SHA-256 |
| Historical Repository | wszystkie importy, wersje słowników i snapshoty w bazie MS SQL (m.in. ImportBatch, SourceFile, SourceSystem) |
| Business Layer | widoki SQL, np. `vw_actual_cost`, `vw_budget`, `vw_etc`, `vw_ev`, `vw_cpi`, `vw_spi`, `vw_eac`, `vw_tcpi` |
| Report Layer | raporty EV, pliki dla Cobra, pakiety CAM, dane dla Power BI, szablony Excel |

---

# Założenia technologiczne

**Python** (aplikacja lokalna, interfejs w przeglądarce) odpowiada za:

- orkiestrację przebiegów,
- odczyt i kopiowanie plików (Excel, CSV, TXT),
- walidację plików i danych wprowadzanych w interfejsie,
- ładowanie danych do bazy,
- generowanie plików Excel (dla finansów, dla CAM, raporty).

**SQL (MS SQL Server)** odpowiada za:

- logikę biznesową,
- agregacje,
- kalkulacje EV,
- wersjonowanie słowników i historię zmian,
- stan przebiegów i dziennik zdarzeń.

**SQLite (dysk sieciowy)** odpowiada za:

- słowniki, przypisania i konfigurację (edycja w interfejsie aplikacji, historia zmian).

**Excel** odpowiada wyłącznie za **wymianę plików** z ludźmi i systemami:

- raporty RABIT (wejście),
- pliki dla finansów, pliki dla CAM i raport końcowy (wyjście),
- pracę CAM (uzupełnianie zaawansowania w plikach).

Środowiska: developer pracuje na **TEST**; wdrożenia na **PROD** wykonuje administrator.
Logowanie do bazy przez konto AD (Windows Authentication).

---

# Słowniki i przypisania

## Cel

Słowniki i przypisania zawierają wiedzę biznesową potrzebną do przekształcenia danych źródłowych
w dane raportowe: powiązanie CES↔P1S, przypisanie do WP i CAM, harmonogramy, budżety, stawki.

## Zasada przechowywania

- **Źródłem prawdy jest baza słowników PZL-EV** – lekka baza SQLite w repozytorium PZL-EV na dysku
  sieciowym (`docs/mapowanie-ces-p1s.md`, M1, M13).
- Edycja wyłącznie w **interfejsie aplikacji** (dodawanie, zmiana, dezaktywacja, drzewo, wyszukiwanie).
  Excel nie jest źródłem słowników; ~~eksport do Excela – tylko do podglądu~~ *(zmienione: M24, M26 – słowniki
  projektu i Cost Category: pobranie do Excela i wczytanie z walidacją i podglądem różnic)*.
- Każda zmiana zapisuje: kto, kiedy, poprzednią i nową wartość, okres obowiązywania
  (`ValidFrom` / `ValidTo`). Zmiana obowiązującej wartości = zamknięcie okresu starej i nowy wpis.
- Przebieg przypina stan słowników i przypisań, na którym liczył – każdy raport EV można odtworzyć.
- Dane importów (miliony wierszy) są w MS SQL, nie w bazie słowników.

## Zawartość bazy słowników

| Obszar | Zawartość |
|---|---|
| Mapowanie CES ↔ P1S | korekty elementu / projektu CES względem raportu mapowań SAP↔CES, historia (M16–M18, `docs/mapowanie-ces-p1s.md`); ~~reguły projektu CES → `PROJORG` P1S, wyjątki WBS~~ |
| Słowniki projektu | zakres P1S projektu (M23); WP i CAM (element P1S → WP, CAM, Cost Category), Harmonogram i budżet, Stawki CAS, Cost Category – zmiany w projekcie (M24, M26) |
| Cost Category | numer elementu kosztowego → Opis, Obszar, Cost Category (M26) |
| Finansowe | stawki wydziałów (`Department | Year | Labor Rate | Overhead`), stawki CAS, kursy walut |
| Kalendarz | okresy rozliczeniowe |
| Konfiguracja importu | prefiksy plików RABIT → źródło (M21) |

Struktury źródłowe (tylko odczyt): WBS CES z importów RABIT (`Project Definition`, `WBS Element`),
WBS P1S i kategoryzacja z `PZLPROD.LOG.WBS` i `LOG.WBS_DIC` (M20), raport mapowań SAP↔CES z `PZLPROD` (M16).

## Walidacja

Walidacja odbywa się **przy zapisie w interfejsie** (a nie przy imporcie pliku): wymagane pola, typy,
unikalność, nakładające się okresy ważności, odwołania (np. CAM, WP, element P1S istnieje), reguły biznesowe
(np. WP = yes wymaga CAM, data startu ≤ data końca), blokada relacji CES → wiele P1S. Szczegóły:
`docs/funkcjonalnosc.md`, rozdz. 5.

---

# Lista otwartych pytań

| # | Pytanie | Status |
|---|---|---|
| 1 | Jakie raporty SAP są wykorzystywane? | częściowo: CES – CJI3, ZRD_KKAJ, Net Inv; P1S – dane produkcyjne (`vAHDD`) |
| 2 | Które dane pochodzą z Cobra? | otwarte |
| 3 | Skąd pochodzi ETC? | otwarte |
| 4 | Jak liczony jest progress w poszczególnych programach? | częściowo: tydzień – produkcja + uzupełnienia; zamknięcie – CAM |
| 5 | Jakie słowniki istnieją obecnie? | częściowo: przenoszone do bazy słowników; w `PZLPROD.LOG`: `WBS`, `WBS_DIC` (źródło P1S), `Stanowiska`, `LearningCurve`, `PeriodDates` |
| 6 | Jakie stawki CAS są wykorzystywane? | otwarte |
| 7 | Które pliki są krytyczne dla procesu? | otwarte |
| 8 | Jakie raporty są generowane dla kierownictwa? | otwarte |
| 9 | Jakie raporty są przekazywane do klienta? | otwarte |
| 10 | Które elementy procesu są dziś najbardziej czasochłonne? | otwarte |
| 11 | Logika łączenia źródeł (etap 4 przebiegu) | do przedstawienia |

Otwarte decyzje architektoniczne i funkcjonalne: `docs/architektura.md` (rozdz. 13)
i `docs/funkcjonalnosc.md` (rozdz. 9).

---

# Długoterminowy cel

Stworzenie centralnego Program Reporting Engine umożliwiającego:

- automatyczne pobieranie danych,
- pełną historię danych,
- standaryzację EV,
- eliminację ręcznego przetwarzania,
- automatyczne generowanie raportów,
- pojedyncze źródło prawdy dla danych programowych.
