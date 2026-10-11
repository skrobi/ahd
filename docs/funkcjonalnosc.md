# PZL-EV – Specyfikacja funkcjonalna

Zakres: użytkownicy, ekrany, funkcje aplikacji (projekt i kreator, gotowość, słowniki, import,
przebiegi, pulpit, administracja), pliki generowane, scenariusze i zakres wersji.

Powiązane: przepływ i etapy – `docs/pipeline-fazy.md`; reguły słowników – `docs/slowniki.md`; nakładka i strona
P1S – `docs/performance-objectives.md`; role – `docs/uprawnienia.md`.

---

## 1. Użytkownicy

| Użytkownik | Jak korzysta |
|---|---|
| **Analityk** (finanse) | aplikacja `PZL-EV.exe` – wszystkie funkcje we wszystkich projektach |
| **CAM** | v1: pliki Excel na dysku sieciowym (`docs/zrodla-danych.md`, rozdz. 7); v2: aplikacja |
| **PM** | v2: aplikacja – przegląd projektów |

Role, zakres danych i egzekwowanie – `docs/uprawnienia.md`.

---

## 2. Ekrany

Menu modułów po lewej (Pulpit, Import RABIT, Słowniki, Projekty, Przebiegi, Administracja, Diagnostyka) zwija się do ikon („☰” u góry menu; nazwa modułu w podpowiedzi) – więcej miejsca na ekran modułu.

| Ekran | Zawartość | Główne akcje |
|---|---|---|
| **Pulpit** | karty faz globalnych (Import RABIT, Źródła danych, Słowniki globalne), „Wymaga uwagi”, ostatnie zdarzenia (F07) | klik w kartę – przejście do sekcji (Import / Administracja / Słowniki), „Odśwież” |
| **Import RABIT** | import źródeł, historia importów, decyzje dla plików (F05) | importuj |
| **Projekty** | lista projektów: kod, nazwa, typ, aktywny przebieg, folder – sam odczyt projektów (nakładka i gotowość – na ekranie projektu) | nowy projekt |
| **Projekt** | zakładki: Wskaźniki (wskaźniki projektu w grupach Operational EV, Materials, Financial EV; gotowość F02 zwinięta; znacznik gotowości i data danych RABIT w nagłówku), Struktura (nakładka z rozwinięciem P1S, WP, CAM, Cost Category, budżet i daty – `docs/performance-objectives.md`, rozdz. 4.2), Raporty, Słowniki projektu, Przebiegi, Foldery | edycja w komórkach struktury (zapis od razu); dołożenie elementów z kolejnego eksportu SAP; Raport kosztów (Excel) na zakładce Raporty (`docs/model-danych.md`, migracja 013); struktura do Excela; import źródeł projektu (F05); nowy przebieg; edycja słowników projektu w tabeli (Cost Category – pozycje globalne i zmiany projektu, `docs/slowniki.md`, rozdz. 6); pobierz / wczytaj słowniki z Excela |
| **Kreator projektu** | 5 kroków (F01) | utwórz projekt |
| **Przebiegi** | wszystkie przebiegi wszystkich projektów: tydzień, czy zamykający, stan, osoba | przejście do przebiegu |
| **Przebieg** | kroki i etapy (F06), panel wybranego etapu, znacznik stanu, problemy, rewizje, dziennik | akcje etapów |
| **Słowniki** | słowniki globalne i projektu: tabela z filtrowaniem, historia zmian (F03); struktura P1S z `PZLPROD` (tylko odczyt) | dodaj / edytuj / zamknij ważność wiersza; pobierz / wczytaj Excel; „Dodaj z HR” (Osoby); „Struktura P1S (PZLPROD)” |
| **Administracja** | definicje źródeł, parsery, lokalizacje RABIT, role i grupy AD (F08) | dodaj / zmień |
| **Diagnostyka** | test stosu (runtime, pakiety, ścieżka, konto), połączenie z `PZLPROD`, wersja serwera i schematu (`docs/architektura.md`, rozdz. 8) | Migracja |

Nagłówek aplikacji – `docs/architektura.md`, rozdz. 3.

---

## 3. Funkcje

### F01. Projekt i kreator projektu

**Projekt** to projekt PZL-EV, dla którego liczone są wskaźniki EV. Nie jest tym samym co projekt CES ani
`PROJORG` (projekt P1S).

| Atrybut | Znaczenie |
|---|---|
| Kod | unikalny; używany w nazwach folderów, plików i przebiegów |
| Nazwa | opis |
| Typ | SAC, CAS albo wewnętrzny – wyznacza wymagane słowniki (`docs/slowniki.md`, rozdz. 4) |
| Zakres | nakładka **Performance Objectives** – struktura kontraktu CES (`docs/performance-objectives.md`) |

Kroki kreatora:

1. **Podstawowe** – kod, nazwa, typ; aplikacja pokazuje słowniki wymagane dla typu.
2. **Performance Objectives** – budowa nakładki kontraktu CES (`docs/performance-objectives.md`): wczytanie
   z Excela albo ręcznie, z możliwością grupowania elementów we własnych węzłach. Nakładka wyznacza zakres
   projektu; stronę P1S tworzą `Legacy WBS` elementów nakładki i poddrzewa `LOG.WBS` pod nimi (`PZLPROD`).
3. **Słowniki projektu** – wczytanie słowników projektu z Excela (szablon do pobrania z elementami zakresu);
   zawartość i walidacja – `docs/slowniki.md`, rozdz. 3 i 5; słownik z błędem ERROR nie zostaje zapisany.
   Lista słowników globalnych z bieżącym stanem.
4. **Foldery** – podgląd struktury `Projekty\<Projekt>\` (`docs/architektura.md`, rozdz. 7); kontrole:
   dostępność korzenia, prawo zapisu, brak folderu o tym kodzie; informacja o zgłoszeniu do IT dostępu CAM
   do folderu `Zwrocone`.
5. **Podsumowanie (baza analityczna)**:
   - drzewo nakładki Performance Objectives połączone z WP, CAM, BAC i datami,
   - braki: element nakładki bez WP, WP bez budżetu,
   - sumy kontrolne: budżet, godziny, koszty materiałów; zestawienie według CAM; eksport do xlsx.

   **Utwórz projekt** – rejestracja w bazie, utworzenie folderów, kontrola struktury.

Na stronie projektu bazę analityczną zastępuje zakładka Struktura (te same sumy i braki, aktualne po każdej zmianie).

### F02. Gotowość projektu

Lista kontrolna na ekranie projektu:

| Kontrola | Poziom |
|---|---|
| nakładka Performance Objectives zawiera co najmniej jeden element | ERROR |
| Strona P1S: elementy nakładki mają `Legacy WBS`; `PZLPROD` dostępny (poddrzewa `LOG.WBS`) | WARNING |
| wymagane słowniki projektu dla typu są wypełnione | ERROR – blokuje uruchomienie przebiegu |
| WP mają przypisanego CAM | ERROR – blokuje uruchomienie przebiegu |
| CAM jest w słowniku Osoby | WARNING – bez tego nie powstaną pliki CAM |
| struktura folderów zgodna z konfiguracją | WARNING |
| dostęp CAM do folderu `Zwrocone` | WARNING |

### F03. Słowniki

- Ekran Słowniki: słowniki globalne i słowniki projektu, edycja w tabeli / formularzu, historia zmian każdego
  wiersza.
- Zapis, historia, walidacja, wymiana przez Excel i równoczesna edycja – `docs/slowniki.md`, rozdz. 1 i 5.
- Słownik Osoby: „Dodaj z HR” – okno szukania pracowników `PZLHRPROD` (USRID, PERNR, imię i nazwisko, e-mail, dział);
  wybrane osoby trafiają do podglądu, który pokazuje tylko osoby nowe.
- „Struktura P1S (PZLPROD)” – drzewo P1S z `LOG.WBS` tylko do odczytu: `Z_KAT_ZBIORCZA` → `Z_KATEGORIA` → `Z_OPIS`
  → `PROJORG` → elementy (`docs/zrodla-danych.md`, rozdz. 5.3).
- Przebiegi z wcześniejszym znacznikiem stanu pokazują zmianę (`docs/pipeline-fazy.md`, rozdz. 5).

### F05. Import

- **Importuj** – import ze wszystkich aktywnych lokalizacji RABIT (WebDAV) i z folderu `Do_importu`
  (`docs/pipeline-fazy.md`, G1): logowanie do SharePoint (brama F5, jak Office), lista plików pasujących do
  definicji z datami („zostanie zaimportowany”), import ze zmianą statusu każdego pliku. Pełny import – jedna osoba
  naraz, druga widzi, kto importuje i od kiedy; importy źródeł projektów działają równolegle z nim
  (`docs/pipeline-fazy.md`, rozdz. 1.4).
- **Importuj źródła projektu** (ekran projektu, Struktura) – import plików definicji ze słownika projektu „Źródła
  danych” (np. `ACTUALS_PROGRAM1` – pliki `ACTUALS_PROGRAM1_*`, parser `ACTUALS`); pozostałe pliki są pomijane. Plik
  zapisuje się raz, wspólnie dla wszystkich projektów; po imporcie – ponowny odczyt kosztów i produkcji.
- **Sprawdź źródła** – to samo bez importu.
- Import zapisuje dane w bazie; plików z SharePoint nie kopiuje do `Do_importu` (folder na pliki pobrane ręcznie).
- Wynik importu: decyzja dla każdego pliku, liczba wierszy, problemy.
- Historia importów: kto, kiedy, co – widoczna dla wszystkich.

### F06. Przebieg

- **Nowy przebieg** na stronie projektu (`docs/pipeline-fazy.md`, P0).
- Ekran przebiegu: kroki (Przygotowanie przebiegu, Walidacja, Uzgodnienie, Przegląd finansów, Zaawansowanie,
  Obliczenie i przegląd, Publikacja, Zamrożenie) z etapami P0–P9 i Z (`docs/pipeline-fazy.md`, rozdz. 4);
  panel wybranego etapu z jego akcją; znacznik stanu i zmiany po przypięciu; problemy; rewizje EV; dziennik.
- Przebieg zamykający okres jest wyraźnie oznaczony.

### F07. Pulpit i dziennik

- **Karty faz globalnych** – Import RABIT, Źródła danych, Słowniki globalne; klik w kartę przechodzi do sekcji
  (Import RABIT / Administracja / Słowniki). Jedyna akcja Pulpitu to „Odśwież”; lista projektów jest w module Projekty.
- **„Wymaga uwagi”** – otwarte problemy (`docs/pipeline-fazy.md`, rozdz. 1.3) i etapy wymagające akcji, m.in.:
  brak przebiegu w bieżącym tygodniu, etapy z błędem, pliki czekające na potwierdzenie, zmiany po przypięciu,
  projekty niegotowe, pliki nierozpoznane. Przy problemie – „Rozwiązane”
  (ręczne zamknięcie z wpisem w dzienniku); problem znika też sam, gdy przyczyna zniknie.
- **„Ostatnie zdarzenia”** – przekrój dzienników wszystkich przebiegów: do 60 najnowszych wpisów, po 6 na stronę (‹ ›), treść skrócona do jednej linii (pełna w podpowiedzi).
- Dziennik przebiegu: czas, użytkownik, opis – od najnowszego.

### F08. Administracja

- Definicje źródeł (`docs/zrodla-danych.md`, rozdz. 2) – z historią: dodanie, zmiana, dezaktywacja, usunięcie.
- Parsery (`docs/zrodla-danych.md`, rozdz. 2) – układ kolumn pliku, typy i pola wymagane; definicja źródła wskazuje
  parser. Z historią: dodanie, zmiana, dezaktywacja.
- Lokalizacje RABIT (`docs/zrodla-danych.md`, rozdz. 3): dodanie, zmiana, dezaktywacja.
- Role i przypisane grupy AD (`docs/uprawnienia.md`, rozdz. 3).

---

## 4. Pliki generowane

| Plik | Folder | Etap |
|---|---|---|
| `<Projekt>_AC_przeglad_T<NN>.xlsx`, `<Projekt>_ETC_wstepne_T<NN>.xlsx`, `<Projekt>_WBS_bez_mapowania_T<NN>.xlsx` | `Finanse\<RRRR-MM>\` | P4 |
| `<Projekt>_<RRRR-MM>_CAM_<CAM>.xlsx` | `CAM\<RRRR-MM>\Wyslane\` | P6 (przebieg zamykający) |
| `<Projekt>_EV_T<NN>_R<n>.xlsx` | `EV\<RRRR-MM>\` | P9 |
| `<Projekt>_<RRRR-MM>_Cobra_import.csv` | `EV\<RRRR-MM>\` | P9 (SAC) |

`T<NN>` – numer tygodnia z kalendarza okresów; `R<n>` – numer rewizji. Nazwy plików są propozycją do
potwierdzenia.

---

## 5. Scenariusze

1. **Nowy projekt M28** – kreator → projekt niegotowy (brak WP, CAM, budżetu) → uzupełnienie słowników →
   projekt gotowy.
2. **Przebieg tygodniowy F16** – zapis nakładających się okresów w słowniku odrzucony → poprawa; w łączeniu
   wykryte elementy P1S bez WP → przypisanie → ponowne przypięcie; potwierdzenie plików
   dla finansów; zaawansowanie z produkcji, uzupełnienia, EV wstępne, publikacja.
3. **Odrzucenie plików dla finansów** – komentarz → powrót do łączenia źródeł.
4. **Zmiana stawek** – edycja słownika → baner w przebiegach → ponowne przypięcie.
5. **Kontynuacja przebiegu S70i** rozpoczętego przez inną osobę.
6. **Przebieg zamykający S70i (SAC)** – tydzień oznaczony w kalendarzu jako zamknięcie: zaawansowanie
   z produkcji, pliki CAM z podpowiedzią, odrzucenie pliku (zmiana poza polami), ponowny import, 100% wartości
   od CAM, EV, plik dla Cobra, zamrożenie okresu.

---

## 6. Zakres wersji

| Wersja | Zakres |
|---|---|
| **v1** | rola Analityk; import źródeł i definicje źródeł; słowniki w aplikacji (Excel jako format wymiany); kreator projektu; przebieg tygodniowy i zamykający dla projektów SAC, CAS i wewnętrznych; zaawansowanie z produkcji; CAM przez pliki Excel; silnik EVM; pliki wynikowe i plik dla Cobra; widoki dla BI |
| **v2** | CAM w aplikacji (zaawansowanie, ETC, komentarz, przekazanie; zakres – cały projekt); rola PM |
| **później** | zawężenie zakresu CAM do jego węzłów; raporty Power BI (budowane osobno na widokach bazy) |

---

## 7. Otwarte kwestie

| # | Kwestia |
|---|---|
| O3 | Plik CAM: jeden na CAM (rekomendacja) czy jeden na projekt? |
| O16 | Zawartość plików dla finansów (P4) – lista i układ |
| O17 | Układ pliku CAM – które pola uzupełnia CAM (zaawansowanie %, ETC, komentarz) |
| O26 | Panel „Koszty wg kategorii P1S” na stronie projektu – w v1 czy później? |
