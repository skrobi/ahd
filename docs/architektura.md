# PZL-EV – Architektura rozwiązania

Wersja: 1.0 (wstępny projekt)
Status: **do akceptacji** – dokument nie zawiera implementacji.

Powiązane dokumenty: `readme.md` (kontekst biznesowy), `docs/funkcjonalnosc.md` (specyfikacja
funkcjonalna), `prototyp/pzl-ev-prototyp.html` (klikalny prototyp).

---

## 1. Kontekst i cele architektury

Proces EV w PZL Mielec jest dziś wykonywany ręcznie w Excelu przez każdego analityka osobno.
Architektura ma zapewnić:

1. **Jedno źródło stanu** – każdy widzi, co zostało przetworzone, przez kogo i na jakich danych.
2. **Odtwarzalność** – każdy wynik EV można odtworzyć (wersje słowników i danych są zapamiętane).
3. **Kontrolę jakości danych** – walidacja słowników i plików przed użyciem.
4. **Niezależność zakresów** – programy nie czekają na siebie.
5. **Wdrażalność w PZL** – bez serwera aplikacyjnego, z podziałem TEST (developer) / PROD (admin).

---

## 2. Decyzje architektoniczne

| # | Decyzja | Uzasadnienie |
|---|---|---|
| D1 | Aplikacja w **Pythonie**, uruchamiana **lokalnie** przez użytkownika | brak konieczności uruchamiania na serwerze; każdy może mieć własną instancję |
| D2 | Interfejs **przeglądarkowy** (lokalny serwer na `localhost`) | wygoda; ta sama aplikacja może później trafić na serwer |
| D3 | **Centralna, zdalna baza MS SQL** dla danych (importy, przebiegi, wyniki EV); słowniki i przypisania – SQLite (D26) | każdy widzi, co już przetworzono, kto i kiedy |
| D4 | **Logika biznesowa w bazie** (procedury, widoki); Python = orkiestracja, pliki, walidacja struktury, generowanie Excel | wynik nie zależy od wersji aplikacji na danym komputerze |
| D5 | Logowanie do bazy **po AD** (Windows Authentication) | brak haseł na stanowiskach, audyt „kto co zrobił” |
| D6 | Developer pracuje na **TEST**, wdrożenia na **PROD** wykonuje admin | wymóg organizacyjny PZL |
| D7 | Przebiegi **co tydzień (poniedziałek)**, okres rozliczeniowy **miesięczny** | rytm pracy zespołu |
| D8 | Wolumen danych przez sieć nie stanowi problemu | ładowanie z aplikacji lokalnej do bazy zdalnej |
| D9 | **Brak podziału uprawnień** w finansach – każda osoba z finansów może prowadzić każdy zakres | zastępstwa, ciągłość pracy; AD służy do audytu |
| D10 | Pliki dla finansów wymagają **formalnego potwierdzenia w aplikacji** (może je wykonać osoba prowadząca przebieg) | kontrola przed wysłaniem plików do CAM; zapis kto/kiedy |
| D11 | Dopuszczalne ponowne przeliczenie EV | każde przeliczenie = nowa rewizja, poprzednie zostają |
| D12 | Oryginały plików w centralnym folderze (Landing Zone), w bazie ścieżka + hash | odtwarzalność bez przyrostu bazy |
| D13 | **Wszystko per zakres** (program / pula projektów); przebieg przypina stan słowników i przypisań (D27) | brak pipeline globalnego i przekazywania pracy, prosta współbieżność |
| D14 | Tydzień: zaawansowanie z **produkcji** + uzupełnienia; **zamknięcie miesiąca: zaawansowanie wyłącznie od CAM** | bieżąca informacja co tydzień, formalne dane na zamknięcie |
| D15 | Korzeń folderów na **dysku sieciowym** (UNC) | wspólna ścieżka dla wszystkich |
| D16 | **CAM pracują wyłącznie na plikach** | bez wdrażania aplikacji u CAM |
| D17 | *(zastąpione: M8, M13)* ~~**Zakres wyznacza jego słownik „Struktura projektowa”**; nowe elementy wykrywane po pobraniu danych~~ | elementy pojawiają się między przebiegami |
| D18 | *(zastąpione: M8)* ~~**Lista CAM z kolumny CAM słownika struktury** – bez osobnego słownika CAM~~ | jedno miejsce zarządzania projektem |
| D19 | *(zastąpione: D25, M1–M3)* ~~Relacja **P1S ↔ CES definiowana w słowniku** (wiersz = para, dowolna krotność)~~ | brak stałej relacji między systemami |
| D20 | ⚠ *(M9: wszystkie koszty CES liczone, `NO_P1S` odłożony – M14; UNMAPPED – P10)* Zamknięcie miesiąca wymaga przypisania elementów z kosztem | kompletność EV formalnego |
| D23 | **Rozwiązanie docelowe: pliki RABIT kopiowane przez WebDAV** (`\\host@SSL\DavWWWRoot\…`, konto Windows użytkownika) do `00_Global\RABIT\Do_importu` komendą `pobierz` – tylko nowe i zmienione; następnie `import` do bazy | test 29.09.2026: WebDAV działa; API REST, synchronizacja i eksport do Excela nie są dostępne |
| D22 | Awaryjnie (np. plik > 50 MB – limit usługi WebClient): pojedynczy plik pobrany ręcznie w przeglądarce do `00_Global\RABIT\Do_importu` | import traktuje go tak samo |
| D27 | **Snapshot słowników w MS SQL:** przy przypięciu (start przebiegu / przeliczenie) aplikacja kopiuje stan bazy słowników do schematu `dict` w MS SQL z identyfikatorem wersji – procedury EV (D4) czytają słowniki z MS SQL, a przebieg pozostaje odtwarzalny | logika EV w MS SQL nie może czytać pliku SQLite |
| D26 | **Lekka baza plikowa (SQLite) w repozytorium PZL-EV na dysku sieciowym** dla **wszystkich słowników, przypisań i konfiguracji** (mapowanie CES ↔ P1S, mapa przypisań WP/CAM, harmonogramy, budżety, stawki, kalendarz, kursy, źródła RABIT), edytowanych w interfejsie aplikacji (CRUD + drzewo); **Excel nie jest źródłem słowników** (M13, M15); dane importów (miliony wierszy) pozostają w MS SQL (M4). Przebieg kopiuje przypięty stan słowników do MS SQL (D27). Ryzyka SMB (blokady, brak WAL) ograniczane: krótkie transakcje, jeden zapis naraz, kopie pliku | brak konieczności serwera bazy; proste wdrożenie |
| D25 | **Mapowanie CES ↔ P1S jako warstwa w bazie PZL-EV** *(przyjęte: M1–M3 – SQLite na dysku sieciowym, P1S z `PZLPROD.LOG.WBS`, bez 1:wiele)*: reguła projektu CES → projekt P1S (dziedziczona logicznie przez wszystkie obecne i przyszłe WBS), wyjątki WBS → WBS (pierwszeństwo), `include_children`, `NO_P1S`, relacje wiele:1 (**1:wiele niedozwolone** – koszt CES pokazywany raz), historia i `valid_from/valid_to`; zarządzanie w UI (dwa drzewa). Szczegóły i analiza spójności: `docs/mapowanie-ces-p1s.md` | struktury CES i P1S są niezależne; jedno zatwierdzenie projektu zamiast mapowania każdego WBS |
| D24 | *(M10, M15: prefiksy i źródła projektów jako konfiguracja w bazie słowników; CSV tylko w MVP)* **Plik mówi, czym jest – projekt mówi, czego potrzebuje.** Źródło pliku RABIT rozpoznawane po **prefiksie nazwy** (`konfiguracja/zrodla_rabit.csv`, wygrywa najdłuższy prefiks); importowany jest **każdy rozpoznany plik** samodzielnie (np. `ACTUALS_PAF_01/_02/_03`), bez kontroli „zestawów”; plik nierozpoznany nie jest importowany. Projekt ma listę **wymaganych źródeł** (`konfiguracja/projekty_zrodla.csv`), a PZL-EV sprawdza ich kompletność i aktualność | proste nazwy plików RABIT, różne potrzeby projektów, automatyczna kontrola zamiast ręcznej |
| D21 | **Import plików SAP (RABIT) jest globalny, bez zakresu**: wszystkie pliki z folderu, import tylko nowych (SHA-256), wiersze w postaci surowej; przebieg zakresu wybiera swoje dane po elementach WBS ze słownika struktury | przy pobieraniu nie wiadomo, do którego zakresu należy plik |

---

## 3. Widok ogólny

```
 Stanowisko osoby z finansów                          Centralnie
 ┌─────────────────────────────────────┐     ┌──────────────────────────────────┐
 │ Przeglądarka  ⇄  PZL-EV (Python)    │     │ MS SQL – baza PZL_EV (TEST / PROD)│
 │                  - orkiestracja     │◄───►│  meta  – zakresy, przebiegi,     │
 │                  - odczyt plików    │ AD  │          etapy, dziennik, blokady│
 │                  - walidacja        │     │  stg   – surowe dane z plików    │
 │                  - generowanie xlsx │     │  dict  – snapshot słowników (D27)│
 └──────────────┬──────────────────────┘     │  hist  – snapshoty danych        │
                │ uprawnienia użytkownika    │  ev    – wyniki i rewizje EV     │
 ┌──────────────▼──────────────────────┐     │  procedury = logika biznesowa    │
 │ Dysk sieciowy \\serwer\udział\PZL-EV│     └──────────────┬───────────────────┘
 │  baza słowników (SQLite), RABIT,    │                    │ odczyt
 │  Landing Zone                        │     ┌──────────────▼───────────────────┐
 └──────────────▲──────────────────────┘     │ splmcd03: PZLPROD.LOG, PZL_SAP   │
                │ pliki                       │ (dane produkcyjne P1S, vAHDD)    │
      CAM (Excel) · SAP CES (RABIT / ręcznie) └──────────────────────────────────┘
```

---

## 4. Komponenty

### 4.1 Aplikacja PZL-EV (Python, lokalnie)

- Lokalny serwer WWW + przeglądarka. Aplikacja jest **bezstanowa** – cały stan jest w bazie.
- Odpowiada za: orkiestrację etapów, odczyt i kopiowanie plików, walidację struktury i typów,
  ładowanie danych do `stg`, generowanie plików Excel, wywołanie procedur SQL.
- Długie operacje (np. import 700 000 wierszy) w tle, z postępem.
- Przy starcie sprawdza zgodność z bazą: **minimalna wymagana wersja aplikacji** i **wersja schematu**
  zapisane w bazie; niezgodna aplikacja odmawia pracy.
- Profile konfiguracji TEST / PROD (serwer bazy, korzeń folderów); brak haseł w konfiguracji.

### 4.2 Baza PZL-EV (MS SQL)

- Jedyne źródło stanu i historii. Logika biznesowa w procedurach i widokach.
- Szczegóły w rozdz. 8.

### 4.3 Dysk sieciowy

- Magazyn plików wejściowych i wyjściowych oraz archiwum oryginałów (Landing Zone).
- Szczegóły w rozdz. 7.

### 4.4 Źródła danych

| Źródło | System | Dane | Sposób pozyskania |
|---|---|---|---|
| CJI3, ZRD_KKAJ, Net Inv | SAP **CES** (finansowy) | koszty rzeczywiste, zobowiązania | eksport RABIT na SharePoint, kopiowany przez WebDAV (D23), pliki na projekt (także wieloczęściowe) |
| Dane produkcyjne | SAP **P1S** (produkcyjny) | zaawansowanie godzin i materiałów | np. `PZLPROD.LOG.vAHDD` na `splmcd03` (do potwierdzenia, O10) |
| Słowniki i przypisania | baza słowników PZL-EV (SQLite, dysk sieciowy) | mapowanie CES↔P1S, WP, CAM, harmonogram, budżet, stawki, kalendarz, kursy, konfiguracja | interfejs aplikacji (D26) |
| Struktura P1S | MS SQL `splmcd03` | `PZLPROD.LOG.WBS`, `LOG.WBS_DIC` | odczyt (M6, M7) |
| Pliki CAM | Excel | zaawansowanie od CAM | pliki zwrócone przez CAM |
| Cobra | Sikorsky | budżet i harmonogram (SAC) | do ustalenia |

---

## 5. Model przetwarzania

### 5.1 Zakres

- **Zakres** = program albo pula małych projektów, raportowane razem.
- Typ zakresu: **SAC**, **CAS**, **Wewnętrzny** – wyznacza szablon etapów (np. plik Cobra tylko w SAC)
  i wymagane słowniki (np. stawki CAS tylko w CAS).
- Zawartość zakresu (elementy P1S, WP, CAM, harmonogram, budżet) wynika z mapy przypisań w bazie słowników (M8, rozdz. 6.4).
- Zakres tworzy się w aplikacji (kreator): rejestracja w bazie, foldery, mapa przypisań.

### 5.2 Przebieg

- **Przebieg** (`Run`) dotyczy jednego zakresu i jednego okresu:
  - **tygodniowy** – poniedziałek w trakcie miesiąca, EV wstępne (nieformalne),
  - **zamknięcie miesiąca** – EV formalne, po zatwierdzeniu zamrażane.
- Identyfikator: `R-<Zakres>-<RRRR-MM>-T<tydzień>` lub `R-<Zakres>-<RRRR-MM>-Z`.
- Przebieg jest trwałym obiektem w bazie – może trwać dni (oczekiwanie na CAM); aplikację można
  zamknąć, a przebieg może kontynuować dowolna osoba z finansów (D9).

### 5.3 Etapy

| # | Etap (tydzień) | Etap (zamknięcie) | Bramka wyjścia |
|---|---|---|---|
| 1 | Dane SAP | Dane SAP | import nowych plików RABIT (globalny, D21) zakończony; wybór danych zakresu po WBS; zgodny okres |
| 2 | Słowniki | Słowniki | przypięcie stanu bazy słowników i przypisań, snapshot w MS SQL (D27) |
| 3 | Walidacja | Walidacja | brak błędów blokujących |
| 4 | Łączenie źródeł | Łączenie źródeł | kontrole pokrycia; nowe elementy przypisane lub (tydzień) świadomie pominięte |
| 5 | Pliki dla finansów | Pliki dla finansów | potwierdzenie w aplikacji (D10) |
| 6 | Zaawansowanie z produkcji | Pliki dla CAM | pobrane / wygenerowane |
| 7 | Uzupełnienie braków | Import plików CAM | braki uzupełnione / pliki wszystkich CAM zaimportowane |
| 8 | Walidacja zaawansowania | Walidacja zaawansowania | zamknięcie: 100% wartości od CAM |
| 9 | Generowanie EV | Generowanie EV | rewizja zapisana z wersjami wejść |
| 10 | Plik dla Cobra (SAC) | Plik dla Cobra (SAC) | – |

Zamknięcie okresu zatwierdza się po etapie EV; przebieg jest wtedy zamrażany.

### 5.4 Maszyna stanów etapu

Statusy: `Oczekuje` → `Do wykonania` → `W toku` → `Zakończony`, oraz `Wymaga akcji`, `Błąd`,
`Nieaktualny`.

- Zakończenie etapu udostępnia następny.
- Ponowne wykonanie etapu (albo zmiana jego danych wejściowych) oznacza etapy późniejsze jako
  **Nieaktualne** – trzeba je wykonać ponownie.
- Każda zmiana statusu trafia do dziennika (kto, kiedy, co).

### 5.5 Przypinanie stanu słowników

- Przy starcie przebieg przypina **stan bazy słowników i przypisań** (wersja / znacznik czasu) i kopiuje go
  do MS SQL (`dict`, D27) – procedury EV liczą na tej kopii.
- Zmiana słownika lub przypisania w interfejsie w trakcie przebiegu: aplikacja pyta, czy kontynuować na
  przypiętym stanie, czy **przeliczyć od etapu 3**. Decyzja trafia do dziennika.

### 5.6 Pochodzenie wartości zaawansowania

Każda wartość zaawansowania ma zapisane pochodzenie: `CAM`, `PRODUKCJA` albo `ANALITYK`
(wpis osoby z finansów: kto, kiedy, metoda). Wpis ręczny nie nadpisuje wartości od CAM bez śladu.
Raporty pokazują udział wartości spoza CAM. Na zamknięciu dozwolone jest wyłącznie `CAM` (D14).

### 5.7 Współbieżność

- Jeden przebieg na zakres i tydzień (lub zamknięcie okresu) – wymuszane w bazie.
- Operacja w toku (np. import) zakłada krótką blokadę przebiegu (`sp_getapplock`), żeby dwie osoby
  nie wykonały tej samej operacji jednocześnie.
- Zapis w bazie słowników (SQLite na dysku sieciowym) – krótkie transakcje, jeden zapis naraz (D26).
- Import pliku jest idempotentny: ten sam hash nie tworzy nowej wersji ani duplikatu danych.

---

## 6. Słowniki, przypisania i wersjonowanie

### 6.1 Zawartość i miejsce (D26, M13)

| Obszar | Przykłady | Zasięg |
|---|---|---|
| Mapowanie CES ↔ P1S | reguła projektu CES → `PROJORG`, wyjątki WBS | globalny (`docs/mapowanie-ces-p1s.md`) |
| Mapa przypisań zakresu | element P1S → WP, CAM, Cost Category; harmonogram, budżet | zakres (program) |
| Finansowe | stawki wydziałów, stawki CAS, kursy walut | globalny / zakres |
| Kalendarz | okresy rozliczeniowe | globalny |
| Konfiguracja importu | prefiksy plików RABIT → źródło; źródła wymagane przez projekt | globalny |

Wszystko w bazie słowników (SQLite na dysku sieciowym), edycja w interfejsie aplikacji. Excel nie jest
źródłem słowników (M13).

### 6.2 Przepływ

```
Interfejs (CRUD + drzewo) → walidacja przy zapisie → nowy wpis z historią (SCD2: ValidFrom / ValidTo,
kto, kiedy, poprzednia i nowa wartość) → baza słowników (SQLite)
   → przypięcie przez przebieg → snapshot w MS SQL `dict` (D27) → procedury EV
```

### 6.3 Dwie osie czasu

- **biznesowa** – ValidFrom / ValidTo wpisane w interfejsie (od kiedy obowiązuje wartość),
- **techniczna** – wersja stanu słowników i przebieg, który ją przypiął.

### 6.4 Mapa przypisań zakresu (M8)

- Zakres = konkretny program albo program indywidualny.
- Elementy P1S zakresu (drzewo z `LOG.WBS`, korzeń `PROJORG`) przypisane do **WP**, **CAM**, kategorii;
  na tej samej mapie **harmonogram** i **budżet**.
- Koszty CES trafiają do zakresu przez mapowanie CES ↔ P1S (`docs/mapowanie-ces-p1s.md`).
- Ten sam element nie może należeć do dwóch zakresów w nakładających się okresach.
- CAM z mapy przypisań wyznacza listę CAM i podział plików CAM.
- Nowe elementy (CES po imporcie, P1S z `LOG.WBS`) pojawiają się w interfejsie jako wymagające uwagi –
  bez eksportu do plików.

### 6.5 Walidacja

Walidacja **przy zapisie w interfejsie** (reguły deklaratywne per słownik): wymagane pola, typy,
unikalność, nakładające się okresy, odwołania między słownikami, reguły biznesowe. Błędny zapis jest
odrzucany z komunikatem – nie powstaje błędna wersja. Szczegóły: `docs/funkcjonalnosc.md`, rozdz. 6.

---

## 7. Pliki i foldery

### 7.1 Struktura (D15)

```
\\serwer\udział\PZL-EV\                 korzeń środowiska (osobny dla TEST i PROD)
├── 00_Global\Baza\                 baza słowników i przypisań (SQLite, D26) + kopie
├── 00_Global\RABIT\Do_importu\     kopie plików RABIT (WebDAV, D23)
├── 01_LandingZone\<RRRR-MM-DD>\<IdImportu>\   archiwum oryginałów (bez podziału na zakresy)
└── Zakresy\<Zakres>\
    ├── Finanse\<RRRR-MM>\           pliki pośrednie dla finansów
    ├── CAM\<RRRR-MM>\Wyslane\       pliki do uzupełnienia przez CAM
    ├── CAM\<RRRR-MM>\Zwrocone\      pliki zwrócone przez CAM
    └── EV\<RRRR-MM>\                wyniki EV, plik dla Cobra
```

- Ścieżki UNC (nie litery dysków); w bazie ścieżki **względne** od korzenia środowiska.
- Aplikacja sprawdza strukturę folderów przy otwarciu zakresu.
- Dostęp: finanse – zapis w całym `PZL-EV`; CAM – zapis w `CAM\…\Zwrocone`, odczyt w `Wyslane`
  swojego zakresu (nadaje IT).

### 7.2 Landing Zone i integralność

- Każdy importowany plik jest najpierw kopiowany do Landing Zone; przetwarzana jest kopia
  (plik źródłowy może być otwarty w Excelu).
- Hash SHA-256 zapisany w bazie. Aplikacja działa na uprawnieniach użytkownika, więc Landing Zone
  nie jest chroniona uprawnieniami – zmianę pliku po imporcie wykrywa hash.

### 7.3 Pliki SAP (D21)

- Źródło (D23): folder RABIT na SharePoint czytany przez **WebDAV** (usługa WebClient Windows, konto
  użytkownika), np. `\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659`.
  `pobierz` kopiuje nowe i zmienione pliki do `00_Global\RABIT\Do_importu`, `import` ładuje je do bazy.
- Limit usługi WebClient: domyślnie ok. 50 MB na plik (`FileSizeLimitInBytes`, zmienia administrator);
  większy plik pobiera się ręcznie w przeglądarce do `Do_importu` (D22).
- Niedostępne dla użytkownika (sprawdzone 29.09.2026) i usunięte z kodu: API REST SharePoint, synchronizacja
  OneDrive, eksport listy do Excela (Office List OLEDB / owssvr), pobieranie ZIP.
- Import obejmuje **wszystkie** pliki; zakres nie jest znany. Tożsamość pliku = SHA-256 treści:
  nowy hash → import; znany hash → duplikat; te same metadane (ścieżka, rozmiar, data) co wcześniej →
  pominięcie bez pobierania.
- **Rozpoznanie źródła (D24):** prefiks nazwy pliku → kod źródła wg konfiguracji źródeł (docelowo w bazie słowników, M15; w MVP `konfiguracja/zrodla_rabit.csv`)
  (np. `ACTUALS_PAF_01.xlsx` → `ACTUALS_PAF`). Nazwy plików pozostają proste – bez projektu, dat, wersji.
  Plik bez pasującego prefiksu → decyzja „nierozpoznany”, nie jest importowany (po dopisaniu prefiksu
  zostanie zaimportowany przy kolejnym uruchomieniu).
- **Każdy rozpoznany plik importowany samodzielnie** – także kilka plików jednego źródła
  (`_01`, `_02`, `_03`) o różnych układach kolumn; brak kontroli „zestawów”.
- **Hash = tożsamość fizycznego pliku:** ponowne pobranie tego samego pliku nie jest traktowane jako nowe.
  RABIT nadpisuje plik tą samą nazwą – każda nowa treść to nowa wersja w historii (`meta.SourceFile`).
- Wiersze ładowane w postaci surowej (`stg.RawRow`, widok `stg.vRawRowZrodlo` z kodem źródła i importem);
  zapisywana jest też sygnatura kolumn (zmiana układu raportu). Tabele typowane per źródło – gdy
  zostanie zdefiniowana zawartość raportów.
- **Kompletność projektu (D24):** źródła wymagane przez projekt (docelowo w bazie słowników, M10; w MVP `konfiguracja/projekty_zrodla.csv`);
  `kompletnosc` pokazuje dla projektu: ✓ źródło zaimportowane (ostatni import, data raportu, plik),
  ✗ brak importu, ⚠ import starszy niż zadany próg. Sprawdzenie pokrycia okresu – później.
- Każde uruchomienie importu i decyzja dla każdego pliku są zapisane w bazie (`meta.ImportBatch`,
  `meta.SourceFile`, `meta.SourceFileSeen`) – widać, kto i kiedy zaimportował dane.
- Kod: `pzl_ev/etap1` (`pobierz`, `import`, `kompletnosc`, `historia`), konfiguracja `konfiguracja/`, instrukcja `docs/mvp-etap1.md`, DDL `sql/mssql/001_etap1_import.sql`.
- Jeśli RABIT pozwala – eksport do CSV/TXT (brak limitu wierszy, brak konwersji typów przez Excel).

### 7.4 Pliki CAM

- Jeden plik na CAM w ramach zakresu (rekomendacja, O3).
- Ukryty arkusz: ID przebiegu, zakres, okres, CAM, wersja szablonu. Komórki poza polami do
  uzupełnienia zablokowane.
- Import odrzuca plik: bez identyfikatora, z innego przebiegu lub okresu, zmieniony poza polami.

---

## 8. Baza danych

### 8.1 Schematy

| Schemat | Zawartość |
|---|---|
| `meta` | zakresy, szablony etapów, przebiegi, etapy, zdarzenia (dziennik), blokady, pliki (ścieżka, hash), wersja aplikacji i schematu |
| `stg` | surowe dane z plików (per przebieg i plik) |
| `dict` | **snapshoty** słowników i przypisań przypięte przez przebiegi (kopia z bazy słowników SQLite, D27) – źródłem prawdy jest baza słowników |
| `hist` | snapshoty danych źródłowych (CES, P1S, CAM) |
| `ev` | wyniki łączenia, zaawansowanie z pochodzeniem, kalkulacje EV (rewizje) |

### 8.2 Główne encje (do szczegółowego projektu)

| Encja | Opis |
|---|---|
| `meta.Zakres` | kod, nazwa, typ (SAC/CAS/WEW), pula, data utworzenia, twórca |
| `meta.Przebieg` | zakres, rodzaj, okres, tydzień, status, rewizja EV, zamrożenie |
| `meta.EtapPrzebiegu` | przebieg, etap, status, kto/kiedy, wersje wejść |
| `meta.Zdarzenie` | dziennik: przebieg, kto (AD), kiedy, opis |
| `meta.Plik` | ścieżka względna, hash, rozmiar, liczba wierszy, przebieg, źródło |
| `dict.StanSlownikow` | przypięty stan bazy słowników: wersja / znacznik czasu, kto, kiedy, przebieg |
| `dict.*` (tabele snapshotu) | kopie słowników i przypisań dla danego stanu (mapowanie, mapa przypisań, stawki, kalendarz, kursy) |
| `ev.Zaawansowanie` | WP, okres, wartość, pochodzenie (CAM/PRODUKCJA/ANALITYK), kto/kiedy |
| `ev.Wynik` | przebieg, rewizja, WP/projekt, BAC, BCWS, BCWP, ACWP, wskaźniki |

### 8.3 Bezpieczeństwo i audyt

- Grupa AD finansów → rola `pzl_ev_user`; osobno `pzl_ev_admin` (wdrożenia).
- Użytkownicy nie mają praw do tabel – wyłącznie EXECUTE na procedurach i SELECT na widokach.
- Brak uprawnień per zakres (D9). Baza wymusza reguły procesu (bramki etapów, jeden przebieg na
  zakres i tydzień, zamrożenie okresu), bo aplikacja działa lokalnie i nie może być jedyną kontrolą.
- Każda procedura zapisuje użytkownika AD (`ORIGINAL_LOGIN()`) w dzienniku.

---

## 9. Środowiska i wdrożenia

- **TEST** – developer; osobna baza i osobny korzeń folderów.
- **PROD** – wdraża administrator.
- Zmiany bazy jako numerowane, idempotentne skrypty migracyjne w repozytorium (lub projekt SSDT/DACPAC);
  baza przechowuje numer wersji schematu.
- Paczka wdrożeniowa: skrypty + instrukcja dla admina + wymagana wersja aplikacji.
- Aplikacja: dystrybucja z repozytorium / udziału sieciowego, instalacja Pythona i pakietów na
  stanowiskach (forma do ustalenia, O6).

---

## 10. Integracja z istniejącymi obiektami (`splmcd03`)

Na podstawie eksportu metadanych (`dependencies.csv`, `resolved_objects.csv`):

- `uspUpdateAHDD` orkiestruje `uspUpdateAHDD_ORDER`, `_PSPNR`, `_VORNR` (ta ostatnia wywołuje
  `uspUpdateZMTO`). Istnieje mechanizm logowania błędów i statusu odświeżenia
  (`ReportErrorInfo`, `StatusAktualizacjiRaportow`, `TableList`) – warto go wykorzystać do kontroli
  świeżości danych produkcyjnych.
- `vAHDD` jest aktualny dopiero po przebiegu `uspUpdateAHDD` – przebieg tygodniowy sprawdza
  datę odświeżenia przed pobraniem zaawansowania.
- Dane SAP P1S są replikowane do `PZL_SAP` (`Z_R3_PRPS_TBL`, `AUFK`, `AFKO`, `AFPO`, `AFVC/AFVV`, `JEST`…).
  Tabel kosztowych CES (podstawa CJI3) w eksporcie nie ma – koszty pochodzą z plików.
- `LOG.WBS` – tylko P1S. `Stanowiska`, `LearningCurve`, `PeriodDates`, `EmployeesHist` to istniejące
  tabele słownikowe – ich los do ustalenia (O9).
- Eksport zawiera tylko metadane; do szczegółowego projektu potrzebne są definicje
  (`vAHDD`, `WBS`, `uspUpdateAHDD_VORNR`) i kolumny tabel.

---

## 11. Wymagania niefunkcjonalne

| Obszar | Wymaganie |
|---|---|
| Wydajność | import 700 000+ wierszy na projekt w czasie akceptowalnym dla przebiegu tygodniowego (ładowanie wsadowe) |
| Odtwarzalność | każdy wynik EV odtwarzalny z przypiętego stanu słowników i zarejestrowanych plików |
| Audyt | każda akcja z użytkownikiem AD i czasem; historia wersji słowników |
| Spójność | ta sama wersja logiki dla wszystkich użytkowników (logika w bazie, kontrola wersji aplikacji) |
| Odporność | przerwana operacja nie zostawia częściowych danych (transakcje, idempotentny import) |
| Utrzymanie | reguły walidacji i szablony etapów konfigurowalne bez zmiany kodu |

---

## 12. Ryzyka i punkty newralgiczne

| # | Ryzyko | Rozwiązanie |
|---|---|---|
| 1 | Różne wersje aplikacji u użytkowników | logika w bazie, kontrola minimalnej wersji |
| 2 | Przebiegi trwające dni | trwały stan w bazie, kontynuacja przez inną osobę, unieważnianie etapów |
| 3 | Zmiana słownika w trakcie przebiegu | przypinanie stanu, decyzja o przeliczeniu w dzienniku |
| 4 | Różne litery dysków | UNC + ścieżki względne |
| 5 | Pliki otwarte w Excelu na dysku sieciowym | kopia do Landing Zone przed przetwarzaniem |
| 6 | Pliki CAM zmienione poza polami / z innego przebiegu | identyfikator i blokady w szablonie, kontrola przy imporcie |
| 7 | Excel zmienia typy (WBS jako liczba, daty, zera wiodące) | walidacja typów, CSV dla SAP |
| 8 | Etykiety poufności / szyfrowanie plików | do weryfikacji z IT |
| 9 | Reguły procesu omijane przez bezpośrednie połączenie z bazą | kontrola w procedurach |
| 15 | SQLite na dysku sieciowym (blokady SMB, brak WAL, uszkodzenie przy zerwaniu połączenia) | krótkie transakcje, jeden zapis naraz, kopie pliku (D26, `docs/mapowanie-ces-p1s.md` 4.1) |
| 10 | Istniejące słowniki w `PZLPROD.LOG` | `WBS`, `WBS_DIC` – źródło struktury P1S (odczyt, M6); pozostałe – O9 |
| 11 | Nieaktualne `vAHDD` | kontrola świeżości przed etapem 6 |
| 12 | Mieszanie źródeł zaawansowania | zapis pochodzenia, bramka „tylko CAM” na zamknięciu |
| 13 | Nowe elementy SAP bez przypisania | wykrywanie w etapie 4, eksport propozycji, bramka na zamknięciu |
| 14 | Różne zapisy tego samego CAM | ostrzeżenie walidacji (inaczej powstają dwa pliki CAM) |

---

## 13. Otwarte decyzje

| # | Pytanie | Rekomendacja |
|---|---|---|
| O3 | Plik CAM: jeden na CAM czy jeden na program? | jeden na CAM |
| O5 | Serwer / baza dla PZL-EV | osobna baza `PZL_EV`; część danych z `splmcd03` |
| O6 | Forma dystrybucji aplikacji | do ustalenia |
| O7 | Czy RABIT może eksportować CSV/TXT? | CSV preferowany |
| O9 | Los słowników w `PZLPROD.LOG` (`Stanowiska`, `LearningCurve`, `PeriodDates`) | do ustalenia z właścicielami |
| O10 | Źródło zaawansowania z produkcji (np. `vAHDD`) | do ustalenia |
| O11 | Zasady uzupełniania braków (ostatnia znana wartość / plan / ręcznie) | do ustalenia |
| O14 | Logika łączenia źródeł (etap 4) | do przedstawienia przez zespół |
| O15 | Źródło ETC | otwarte pytanie z readme |
