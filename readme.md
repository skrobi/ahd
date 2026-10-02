# PZL-EV – Earned Value Reporting Platform

**PZL-EV** to aplikacja do raportowania Earned Value (EV) w PZL Mielec, rozwijana w ramach zespołu / programu
**Unicorn-EV** (obejmującego także inne elementy poza tą aplikacją). Nazwa „AHD” jest zarezerwowana dla raportu
w operacjach i nie jest używana w tym projekcie – wyjątkiem są istniejące obiekty SQL w `PZLPROD.LOG`
(`vAHDD`, `uspUpdateAHDD`, `AHDD_*`), których nazwy pochodzą z systemu produkcyjnego.

Status: wstępny projekt, rozwijany iteracyjnie na podstawie warsztatów z użytkownikami, istniejących plików
Excel, szablonów raportowych, procedur finansowych i raportów SAP.

---

## Mapa dokumentacji

Każda reguła jest opisana w jednym dokumencie; pozostałe dokumenty do niego odsyłają. Otwarte kwestie
(`O<n>`) są w dokumencie, którego dotyczą.

| Dokument | Zawartość |
|---|---|
| `readme.md` | cel, kontekst biznesowy, pojęcia, mapa dokumentacji (ten plik) |
| `docs/architektura.md` | technologia, uruchomienie i dystrybucja, warstwy i moduły aplikacji, podział logiki między aplikację i bazę, dysk sieciowy, środowiska, wymagania niefunkcjonalne, ryzyka |
| `docs/model-danych.md` | warstwy danych, schematy i encje MS SQL, historia i wersjonowanie, przebieg, znacznik stanu i rewizja (kontrakt przebiegu) |
| `docs/zrodla-danych.md` | źródła danych i definicja źródła (kontrakt danych): RABIT, raport kosztów CES, struktura P1S i drzewo, zaawansowanie z produkcji, pliki CAM |
| `docs/slowniki.md` | słowniki globalne i projektu, typ projektu a słowniki, wymiana przez Excel, reguły walidacji, Cost Category |
| `docs/performance-objectives.md` | nakładka kontraktu CES wyznaczająca zakres projektu i oś raportowania wykonania |
| `docs/mapowanie-ces-p1s.md` | globalne przypisanie elementów CES do P1S: raport mapowań, dziedziczenie, korekty, statusy, widok |
| `docs/pipeline-fazy.md` | przepływ: fazy globalne i etapy przebiegu tygodniowego; statusy, problemy (ERROR / WARNING), współbieżność |
| `docs/ev-obliczenia.md` | silnik EVM (kontrakt EVM): dane wejściowe, wskaźniki, poziomy agregacji, kwestie do ustalenia z Finansami |
| `docs/uprawnienia.md` | konto AD, role, zakres danych (kontrakt RBAC) |
| `docs/funkcjonalnosc.md` | użytkownicy, ekrany, funkcje, pliki generowane, scenariusze, zakres wersji |
| `docs/mvp-etap1.md` | instrukcja narzędzia testowego w Pythonie (WebDAV, import) |
| `poc-wpf/` | aplikacja testowa stosu C#/.NET 10 + WPF (Pulpit z prototypu, bez bazy) – sprawdzenie uruchomienia `PZL-EV.exe` z dysku sieciowego (O6); kod w docelowej strukturze modułów (`docs/architektura.md`, rozdz. 5.3) |
| `pzl_ev/`, `tests/`, `konfiguracja/`, `sql/mssql/` | kod i testy narzędzia testowego, przykładowa konfiguracja prefiksów, skrypt tabel importu MS SQL |
| `prototyp/*.html` | klikalne prototypy na danych przykładowych; pokazują wcześniejszy stan koncepcji – obowiązuje dokumentacja |
| `dependencies.csv`, `resolved_objects.csv`, `export_log.txt` | eksport metadanych obiektów SQL z `splmcd03` (`PZLPROD.LOG`, `PZL_SAP`) |

---

## Pojęcia

| Pojęcie | Znaczenie | Szczegóły |
|---|---|---|
| Projekt | projekt PZL-EV, dla którego liczone są wskaźniki EV; typ SAC, CAS albo wewnętrzny | `docs/funkcjonalnosc.md`, F01 |
| Zakres projektu | nakładka Performance Objectives – struktura kontraktu CES wyznaczająca elementy projektu | `docs/performance-objectives.md` |
| Performance Objectives | nadrzędna struktura kontraktu CES, oś raportowania wykonania; wirtualne węzły | `docs/performance-objectives.md` |
| CES / P1S | SAP finansowy (koszty) / SAP produkcyjny (struktura, zaawansowanie) | `docs/zrodla-danych.md` |
| Projekt CES / `PROJORG` | projekt w SAP CES / projekt w SAP P1S (korzeń drzewa P1S) – nie mylić z projektem PZL-EV | `docs/mapowanie-ces-p1s.md` |
| RABIT | narzędzie SAP zrzucające wyniki raportów na SharePoint | `docs/zrodla-danych.md`, rozdz. 3 |
| Definicja źródła | formalny opis raportu źródłowego: schemat, ziarno, klucz, znaczenie kolumn i okresu, waluta, walidacja, wersja parsera | `docs/zrodla-danych.md`, rozdz. 2 |
| WP / CAM | Work Package / Cost Account Manager – osoba odpowiedzialna za WP | `docs/slowniki.md`, rozdz. 3 |
| Raport mapowań, korekta | przypisania CES ↔ P1S z `PZLPROD` / zmiana przypisania wprowadzona w PZL-EV | `docs/mapowanie-ces-p1s.md` |
| Przebieg | przetworzenie jednego projektu za jeden tydzień | `docs/pipeline-fazy.md`, rozdz. 4 |
| Przebieg zamykający | przebieg w tygodniu oznaczonym w kalendarzu okresów jako zamknięcie okresu: zaawansowanie od CAM, zamrożenie | `docs/pipeline-fazy.md`, rozdz. 4 |
| Znacznik stanu | moment, w którego stanie przebieg czyta dane i słowniki (przypięcie) | `docs/model-danych.md`, rozdz. 4.2 |
| Rewizja | jedno obliczenie EV przebiegu; kolejne obliczenia tworzą nowe rewizje | `docs/model-danych.md`, rozdz. 4.3 |
| Pochodzenie zaawansowania | `PRODUKCJA`, `ANALITYK` albo `CAM` | `docs/pipeline-fazy.md`, P5–P6 |
| Problem | wynik kontroli: ERROR (blokuje) albo WARNING | `docs/pipeline-fazy.md`, rozdz. 1.3 |

---

## Cel projektu

Wspólna platforma raportowa umożliwiająca:

- automatyczne pobieranie danych źródłowych,
- historyzację danych,
- standaryzację raportowania,
- ograniczenie pracy manualnej,
- automatyczne przygotowanie danych EV,
- automatyczne generowanie pakietów raportowych.

Docelowo rozwiązanie zmniejsza zależność od ręcznej obróbki danych w Excelu. W dłuższym horyzoncie PZL-EV ma
być centralnym Program Reporting Engine: pełna historia danych, standaryzacja EV, eliminacja ręcznego
przetwarzania, automatyczne raporty i pojedyncze źródło prawdy dla danych programowych.

---

## Aktualny stan

Proces jest wykonywany głównie manualnie. Każdy analityk pobiera dane samodzielnie, wykonuje własne
transformacje, utrzymuje własne pliki Excel, słowniki i formuły oraz wykonuje korekty ręczne.

W efekcie: duże ryzyko błędów, trudność odtworzenia procesu i wdrożenia nowej osoby, bardzo dużo czasu
poświęcanego na przygotowanie danych.

---

## Typy projektów EV

W PZL-EV wszystkie typy przechodzą ten sam przebieg; typ wyznacza zestaw słowników pobieranych do przebiegu
(`docs/slowniki.md`, rozdz. 4).

### 1. Projekty Sikorsky (SAC)

- Oficjalny proces EV prowadzi Sikorsky; Cobra służy do kalkulacji wskaźników i nadzorowania kosztów.
- PM Compass służy do wprowadzania zaawansowania kosztów materiałów oraz nakładu godzin.
- PZL dostarcza dane wejściowe (zaawansowanie wprowadzane przez CAM); Actual Cost pochodzi z SAP (koszty
  i godziny); ETC przygotowywane jest lokalnie; dane przekazywane są do Sikorsky.
- Roboczogodziny PZL są traktowane jako koszt dostawcy – przy wprowadzaniu zaawansowania godziny przeliczane
  są po bieżących stawkach na koszt w USD; raportowanie zgodnie z wymaganiami SAC.

```text
SAC – plik Cobra (budżet i harmonogram) → PZL Finanse → SAP → przetworzenie danych PZL → plik Cobra
→ Sikorsky → oficjalne EV
```

### 2. Projekty CAS Compliance

- Stosowane są stawki CAS; koszty przeliczane są według reguł CAS, nie wyłącznie według stawek SAP.
- Duża część logiki znajduje się obecnie w Excelach.

```text
SAP → przetworzenie danych PZL → przeliczenie CAS → kalkulacja EV
```

### 3. Projekty wewnętrzne

- Pełna odpowiedzialność po stronie PZL: własne szablony, raporty i kalkulacje EV.

```text
SAP → budżet → harmonogram → ETC → EV
```

---

## Główne problemy

| Problem | Opis |
|---|---|
| Dane | duże wolumeny: setki tysięcy rekordów, nawet 700 000+ wierszy dla pojedynczego projektu, wiele plików; limit wierszy Excela (dane jednego projektu w kilku plikach), wydajność, długi czas przygotowania danych; pobieranie częściowo rozwiązuje RABIT |
| Wiedza procesowa | rozproszona w plikach Excel, formułach, słownikach, tabelach pomocniczych i wiedzy konkretnych osób; brak centralnego repozytorium |
| Transformacje | duża liczba nieudokumentowanych mapowań, słowników, tabel translacyjnych, wyszukań (VLOOKUP) i ręcznych korekt |
| Standaryzacja | każdy analityk realizuje proces po swojemu; brak wspólnych definicji, słowników i warstwy danych |

---

## Otwarte pytania biznesowe

| # | Pytanie |
|---|---|
| O39 | Które pliki są krytyczne dla procesu? |
| O40 | Jakie raporty są generowane dla kierownictwa? |
| O41 | Jakie raporty są przekazywane do klienta? |
| O42 | Które elementy procesu są dziś najbardziej czasochłonne? |
