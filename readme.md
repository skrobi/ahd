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
- istniejących plików Excel,
- szablonów raportowych,
- logiki biznesowej,
- procedur finansowych,
- raportów SAP.

### Mapa dokumentacji

| Dokument | Zawartość |
|---|---|
| `readme.md` | kontekst biznesowy, cele, problemy, słowniki, otwarte pytania (ten plik) |
| `docs/architektura.md` | architektura rozwiązania: decyzje, komponenty, przepływ danych, baza, wdrożenia |
| `docs/pipeline-fazy.md` | koncepcja wszystkich faz pipeline: cel, wejście, przetwarzanie, efekt, przekazanie do kolejnej fazy |
| `docs/funkcjonalnosc.md` | specyfikacja funkcjonalna: ekrany, funkcje, etapy przebiegu, reguły |
| `prototyp/pzl-ev-prototyp.html` | klikalny prototyp aplikacji (dane przykładowe, otwierany w przeglądarce) |
| `docs/mvp-etap1.md`, `pzl_ev/`, `konfiguracja/` | etap 1: pobranie plików RABIT przez WebDAV, rozpoznanie źródła po prefiksie, import do bazy, kompletność źródeł projektów |
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
- **Centralna baza MS SQL** przechowuje cały stan: zakresy, przebiegi, wersje słowników,
  dane, wyniki EV i dziennik zdarzeń. Każdy widzi, co już przetworzono i przez kogo.
- **Logika biznesowa w bazie** (procedury i widoki SQL) – wynik nie zależy od wersji
  aplikacji na danym komputerze.
- **Pliki na dysku sieciowym** (ścieżki UNC): słowniki Excel, eksporty SAP, pliki dla finansów,
  pliki dla CAM, wyniki EV.
- **Praca w zakresach** – zakres to program lub pula małych projektów. Każdy przebieg
  dotyczy jednego zakresu, dzięki czemu np. F-16 nie czeka na pliki Cobra dla projektów SAC.
- **Przebieg tygodniowy** (poniedziałek) daje wstępne EV; **zamknięcie miesiąca** daje EV formalne,
  oparte wyłącznie na zaawansowaniu od CAM.

## Przebieg (pipeline) zakresu

```
Pobranie plików SAP (ręcznie z SharePoint lub automatycznie)
↓
Import słowników (nowa wersja tylko przy zmianie treści)
↓
Walidacja wszystkich importów
↓
Łączenie ze źródłami (logika do przedstawienia)
↓
Pliki pośrednie dla finansów → potwierdzenie w aplikacji
↓
Pliki do uzupełnienia przez CAM   (tydzień: zaawansowanie z produkcji + uzupełnienia)
↓
Import plików CAM
↓
Walidacja zaawansowania
↓
Generowanie EV (+ plik dla Cobra w projektach SAC)
```

## Warstwy danych

| Warstwa | Zawartość |
|---|---|
| Data Collection | SAP CES (CJI3, ZRD_KKAJ, Net Inv), dane produkcyjne P1S (np. `PZLPROD.LOG.vAHDD`), eksporty Cobra, słowniki Excel, pliki CAM, stawki wydziałów |
| Landing Zone | kopie oryginalnych plików (Excel, CSV, TXT) na dysku sieciowym, bez zmian; w bazie ścieżka i hash SHA-256 |
| Historical Repository | wszystkie importy, wersje słowników i snapshoty w bazie MS SQL (m.in. ImportBatch, SourceFile, SourceSystem) |
| Business Layer | widoki SQL, np. `vw_actual_cost`, `vw_budget`, `vw_etc`, `vw_ev`, `vw_cpi`, `vw_spi`, `vw_eac`, `vw_tcpi` |
| Report Layer | raporty EV, pliki dla Cobra, pakiety CAM, dane dla Power BI, szablony Excel |

---

# Założenia technologiczne

**Python** (aplikacja lokalna, interfejs w przeglądarce) odpowiada za:

- orkiestrację przebiegów,
- odczyt i kopiowanie plików (Excel, CSV, TXT),
- walidację struktury plików i słowników,
- ładowanie danych do bazy,
- generowanie plików Excel (dla finansów, dla CAM, raporty).

**SQL (MS SQL Server)** odpowiada za:

- logikę biznesową,
- agregacje,
- kalkulacje EV,
- wersjonowanie słowników i historię zmian,
- stan przebiegów i dziennik zdarzeń.

**Excel** odpowiada za:

- utrzymanie słowników (źródło prawdy),
- prezentację wyników i raport końcowy,
- pracę CAM (uzupełnianie zaawansowania w plikach).

Środowiska: developer pracuje na **TEST**; wdrożenia na **PROD** wykonuje administrator.
Logowanie do bazy przez konto AD (Windows Authentication).

---

# Słowniki biznesowe

## Cel

Słowniki biznesowe są kluczowym elementem procesu EV i zawierają wiedzę biznesową niezbędną
do przekształcania danych źródłowych w dane raportowe. Słowniki są utrzymywane w plikach Excel
i importowane do bazy danych przy każdym przebiegu (nowa wersja powstaje tylko przy zmianie treści).

Model:

```
Excel (Master)
↓
Import w przebiegu (kopia pliku + hash)
↓
Walidacja
↓
SQL Dictionary Tables (wersje + historia)
↓
Business Views
↓
EV Reports
```

---

## Zasada przechowywania

Excel jest źródłem prawdy (Source of Truth).

SQL przechowuje:

- aktualną wersję słownika,
- historię zmian (wszystkie wersje),
- datę importu,
- wersję słownika,
- numer przebiegu, w którym wersja powstała,
- użytkownika, który ją zaimportował.

Aktualizacja słownika odbywa się wyłącznie poprzez modyfikację pliku Excel.
Każdy przebieg zapamiętuje („przypina”) wersje słowników, na których liczył – dzięki temu
każdy raport EV można odtworzyć.

---

## Rodzaje słowników

- **globalne** – wspólne dla wszystkich zakresów (np. stawki wydziałów, kalendarz okresów, kursy USD/PLN),
- **zakresowe** – należą do jednego zakresu (np. struktura projektowa, harmonogram i budżet, stawki CAS).

---

## Przykładowe słowniki

### Struktura projektowa (zakresowy)

`P1S WBS | CAS WBS | Project Definition | Business Area | Program | Project | Customer | Cost Category | CAM | WP (yes/no)`

- łączy element WBS z **P1S** (system produkcyjny – zaawansowanie) z elementem WBS z **CES**
  (system finansowy – koszty); relacja nie jest stała: każdy wiersz to jedna para,
  ten sam element może wystąpić w kilku wierszach, jedna strona pary może być pusta,
- **wyznacza zakres** – projekty i elementy zakresu to wiersze tego słownika,
- kolumna **CAM** wyznacza listę CAM i podział plików do uzupełnienia,
- nowe elementy, które pojawią się w danych SAP po pobraniu, aplikacja wskazuje do dopisania.

### Finansowe (globalny)

`Department | Year | Labor Rate | Overhead`

### Harmonogramy i budżet (zakresowy)

`P1S WBS | CAS WBS | Project Definition | Budżet godzinowy | Budżet materiałowy | Bazowa planowana data rozpoczęcia | Bazowa planowana data zakończenia | Planowana data rozpoczęcia | Planowana data zakończenia | Rzeczywista data rozpoczęcia | Rzeczywista data zakończenia`

---

## Minimalna struktura słownika

Każdy słownik oprócz kolumn biznesowych zawiera:

- ValidFrom,
- ValidTo,
- Owner,
- Version,
- LastUpdate,
- Comments.

Zmiana obowiązującej wartości odbywa się przez zamknięcie wiersza (ValidTo) i dodanie nowego,
bez usuwania historii.

---

## Właściciel słownika

- słowniki zakresowe – osoba z finansów prowadząca zakres,
- słowniki globalne – wskazana osoba z finansów.

Dla każdego słownika określa się także lokalizację pliku i częstotliwość aktualizacji.
Nie ma podziału uprawnień: każda osoba z finansów może wykonać import pod nieobecność innej;
każda operacja jest zapisywana z nazwą użytkownika.

---

## Walidacja słowników

Każdy import słownika jest walidowany przed utworzeniem nowej wersji:

- plik i arkusz (istnieje, da się odczytać),
- struktura (wymagane kolumny, nagłówki),
- typy danych (daty, liczby, klucze WBS zapisane jako tekst),
- czystość danych (spacje, różne zapisy tej samej wartości),
- klucze i okresy ważności (duplikaty, nakładające się okresy),
- reguły biznesowe (np. WP = yes wymaga CAM),
- spójność między słownikami,
- wersjonowanie (zmiana treści wymaga podbicia wersji, wiersze się zamyka, a nie usuwa).

Błąd blokujący odrzuca nową wersję słownika – obowiązuje ostatnia poprawna.
Szczegóły: `docs/funkcjonalnosc.md`.

---

## Historia zmian

Każdy import słownika umożliwia:

- odtworzenie wcześniejszej wersji,
- porównanie zmian między wersjami,
- identyfikację osoby wprowadzającej zmianę.

---

## Założenie projektowe

Nie planuje się budowy dedykowanego formularza do utrzymania słowników. Pliki Excel pozostają
podstawowym narzędziem zarządzania słownikami, natomiast baza danych pełni rolę centralnego
repozytorium przetwarzania oraz historii zmian.

---

# Lista otwartych pytań

| # | Pytanie | Status |
|---|---|---|
| 1 | Jakie raporty SAP są wykorzystywane? | częściowo: CES – CJI3, ZRD_KKAJ, Net Inv; P1S – dane produkcyjne (`vAHDD`) |
| 2 | Które dane pochodzą z Cobra? | otwarte |
| 3 | Skąd pochodzi ETC? | otwarte |
| 4 | Jak liczony jest progress w poszczególnych programach? | częściowo: tydzień – produkcja + uzupełnienia; zamknięcie – CAM |
| 5 | Jakie słowniki istnieją obecnie? | otwarte (w tym `PZLPROD.LOG`: `Stanowiska`, `LearningCurve`, `PeriodDates`) |
| 6 | Jakie stawki CAS są wykorzystywane? | otwarte |
| 7 | Które pliki są krytyczne dla procesu? | otwarte |
| 8 | Jakie raporty są generowane dla kierownictwa? | otwarte |
| 9 | Jakie raporty są przekazywane do klienta? | otwarte |
| 10 | Które elementy procesu są dziś najbardziej czasochłonne? | otwarte |
| 11 | Logika łączenia źródeł (etap 4 przebiegu) | do przedstawienia |

Otwarte decyzje architektoniczne i funkcjonalne: `docs/architektura.md` (rozdz. 13)
i `docs/funkcjonalnosc.md` (rozdz. 10).

---

# Długoterminowy cel

Stworzenie centralnego Program Reporting Engine umożliwiającego:

- automatyczne pobieranie danych,
- pełną historię danych,
- standaryzację EV,
- eliminację ręcznego przetwarzania,
- automatyczne generowanie raportów,
- pojedyncze źródło prawdy dla danych programowych.
