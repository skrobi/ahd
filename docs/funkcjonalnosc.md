# PZL-EV – Specyfikacja funkcjonalna

Zakres: użytkownicy, ekrany, funkcje aplikacji (projekt i kreator, gotowość, słowniki, mapowanie, import,
przebiegi, pulpit, administracja), pliki generowane, scenariusze i zakres wersji.

Powiązane: przepływ i etapy – `docs/pipeline-fazy.md`; reguły słowników – `docs/slowniki.md`; mapowanie –
`docs/mapowanie-ces-p1s.md`; role – `docs/uprawnienia.md`.

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

| Ekran | Zawartość | Główne akcje |
|---|---|---|
| **Pulpit** | karty projektów ze stanem bieżącego przebiegu, „Wymaga uwagi”, ostatnie zdarzenia (F07) | przejście do projektu / przebiegu, nowy projekt |
| **Import** | import źródeł, historia importów, decyzje dla plików (F05) | importuj |
| **Projekty** | lista projektów: typ, zakres P1S (liczba `PROJORG`), aktywny przebieg, folder | nowy projekt |
| **Projekt** | gotowość (F02), zakres P1S, słowniki projektu, historia przebiegów, struktura folderów | nowy przebieg; pobierz / wczytaj słowniki z Excela |
| **Kreator projektu** | 5 kroków (F01) | utwórz projekt |
| **Przebiegi** | wszystkie przebiegi wszystkich projektów: tydzień, czy zamykający, stan, osoba | przejście do przebiegu |
| **Przebieg** | kroki i etapy (F06), panel wybranego etapu, znacznik stanu, problemy, rewizje, dziennik | akcje etapów |
| **Słowniki** | słowniki globalne i projektu: tabela z filtrowaniem, historia zmian (F03) | dodaj / edytuj / zamknij ważność wiersza; pobierz / wczytaj Excel |
| **Mapowanie CES ↔ P1S** | dwa drzewa, statusy, węzeł „Nieprzypisane” (F04) | korekta elementu / projektu CES, usunięcie korekty, historia |
| **Administracja** | definicje źródeł, lokalizacje RABIT, role i grupy AD (F08) | dodaj / zmień |

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
| Zakres P1S | węzły drzewa P1S (`docs/zrodla-danych.md`, rozdz. 5.3): grupy z dowolnych poziomów kategoryzacji (`sel` – ścieżki grup) i pojedyncze `PROJORG` (`p1s`), z ich elementami WBS/PSP |

Reguły zakresu:

- Grupa obejmuje wszystkie swoje `PROJORG` i ich elementy WBS/PSP.
- `PROJORG` należy do co najwyżej jednego projektu. `PROJORG` należący do innego projektu jest pomijany.
- Pojedynczo wskazany `PROJORG` ma pierwszeństwo przed grupą; wśród grup wygrywa projekt utworzony wcześniej.
- `PROJORG` pojawiający się później w zaznaczonej grupie – O22.

Kroki kreatora:

1. **Podstawowe** – kod, nazwa, typ; aplikacja pokazuje słowniki wymagane dla typu.
2. **Projekty P1S** – drzewo P1S rozwijane w dół, z polem wyboru na każdym poziomie; można zaznaczyć kilka grup
   z różnych poziomów oraz pojedyncze `PROJORG`.
3. **Słowniki projektu** – wczytanie słowników projektu z Excela (szablon do pobrania z elementami P1S zakresu);
   zawartość i walidacja – `docs/slowniki.md`, rozdz. 3 i 5; słownik z błędem ERROR nie zostaje zapisany.
   Lista słowników globalnych z bieżącym stanem.
4. **Foldery** – podgląd struktury `Projekty\<Projekt>\` (`docs/architektura.md`, rozdz. 7); kontrole:
   dostępność korzenia, prawo zapisu, brak folderu o tym kodzie; informacja o zgłoszeniu do IT dostępu CAM
   do folderu `Zwrocone`.
5. **Podsumowanie (baza analityczna)**:
   - drzewo (`Z_KATEGORIA` → `Z_OPIS` → element P1S) połączone z WP, CAM, BAC, datami i liczbą elementów CES
     przypisanych w mapowaniu,
   - braki: element z kosztami CES albo zaawansowaniem bez WP, WP bez budżetu,
   - sumy kontrolne: budżet, godziny, koszty materiałów; zestawienie według CAM; eksport do xlsx.

   **Utwórz projekt** – rejestracja w bazie, utworzenie folderów, kontrola struktury.

Baza analityczna na stronie projektu (po zmianie słowników) – O23.

### F02. Gotowość projektu

Lista kontrolna na ekranie projektu:

| Kontrola | Poziom |
|---|---|
| zakres P1S obejmuje co najmniej jeden `PROJORG` | ERROR |
| wymagane słowniki projektu dla typu są wypełnione | ERROR – blokuje uruchomienie przebiegu |
| WP mają przypisanego CAM | WARNING – bez tego nie powstaną pliki CAM |
| struktura folderów zgodna z konfiguracją | WARNING |
| dostęp CAM do folderu `Zwrocone` | WARNING |

### F03. Słowniki

- Ekran Słowniki: słowniki globalne i słowniki projektu, edycja w tabeli / formularzu, historia zmian każdego
  wiersza.
- Zapis, historia, walidacja, wymiana przez Excel i równoczesna edycja – `docs/slowniki.md`, rozdz. 1 i 5.
- Przebiegi z wcześniejszym znacznikiem stanu pokazują zmianę (`docs/pipeline-fazy.md`, rozdz. 5).

### F04. Mapowanie CES ↔ P1S

Ekran opisany w `docs/mapowanie-ces-p1s.md`, rozdz. 11; reguły – tamże.

### F05. Import

- **Importuj** – import ze wszystkich aktywnych lokalizacji RABIT (WebDAV) i z folderu `Do_importu`
  (`docs/pipeline-fazy.md`, G1).
- Wynik importu: decyzja dla każdego pliku, liczba wierszy, problemy.
- Historia importów: kto, kiedy, co – widoczna dla wszystkich.

### F06. Przebieg

- **Nowy przebieg** na stronie projektu (`docs/pipeline-fazy.md`, P0).
- Ekran przebiegu: kroki (Przygotowanie przebiegu, Walidacja, Uzgodnienie, Przegląd finansów, Zaawansowanie,
  Obliczenie i przegląd, Publikacja, Zamrożenie) z etapami P0–P9 i Z (`docs/pipeline-fazy.md`, rozdz. 4);
  panel wybranego etapu z jego akcją; znacznik stanu i zmiany po przypięciu; problemy; rewizje EV; dziennik.
- Przebieg zamykający okres jest wyraźnie oznaczony.

### F07. Pulpit i dziennik

- **„Wymaga uwagi”** – otwarte problemy (`docs/pipeline-fazy.md`, rozdz. 1.3) i etapy wymagające akcji, m.in.:
  brak przebiegu w bieżącym tygodniu, etapy z błędem, pliki czekające na potwierdzenie, zmiany po przypięciu,
  projekty niegotowe, elementy `UNMAPPED` z kosztem, pliki nierozpoznane.
- **„Ostatnie zdarzenia”** – przekrój dzienników wszystkich przebiegów.
- Dziennik przebiegu: czas, użytkownik, opis – od najnowszego.

### F08. Administracja

- Definicje źródeł (`docs/zrodla-danych.md`, rozdz. 2) – z historią.
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
   wykryte elementy CES `UNMAPPED` i P1S bez WP → przypisanie → ponowne przypięcie; potwierdzenie plików
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
| **v1** | rola Analityk; import źródeł i definicje źródeł; słowniki i mapowanie w aplikacji (Excel jako format wymiany); kreator projektu; przebieg tygodniowy i zamykający dla projektów SAC, CAS i wewnętrznych; zaawansowanie z produkcji; CAM przez pliki Excel; silnik EVM; pliki wynikowe i plik dla Cobra; widoki dla BI |
| **v2** | CAM w aplikacji (zaawansowanie, ETC, komentarz, przekazanie; zakres – cały projekt); rola PM |
| **później** | zawężenie zakresu CAM do jego węzłów; status `NO_P1S` (`docs/mapowanie-ces-p1s.md`, rozdz. 7); raporty Power BI (budowane osobno na widokach bazy) |

---

## 7. Otwarte kwestie

| # | Kwestia |
|---|---|
| O3 | Plik CAM: jeden na CAM (rekomendacja) czy jeden na projekt? |
| O16 | Zawartość plików dla finansów (P4) – lista i układ |
| O17 | Układ pliku CAM – które pola uzupełnia CAM (zaawansowanie %, ETC, komentarz) |
| O22 | Czy `PROJORG`, które później pojawią się w zaznaczonej grupie, mają wchodzić do projektu automatycznie? |
| O23 | Baza analityczna także na stronie projektu (po zmianie słowników), nie tylko w kreatorze? |
| O26 | Panel „Koszty wg kategorii P1S” na stronie projektu – w v1 czy później? |
