# PZL-EV – Specyfikacja funkcjonalna

Wersja: 1.2 (wstępny projekt; 1.1: słowniki w bazie z interfejsem – M13, `NO_P1S` odłożony – M14;
1.2: ustalenia z prototypu v3 – „projekt” zamiast „zakresu”, mapowanie z raportu mapowań, kreator projektu,
import RABIT bez wymaganych źródeł, słownik Cost Category – M16–M26)
Status: **do akceptacji** – opis zachowania aplikacji, bez implementacji.

Powiązane dokumenty: `readme.md`, `docs/architektura.md`, `prototyp/pzl-ev-prototyp.html`
(klikalny prototyp pokazujący opisane funkcje na danych przykładowych: import RABIT, przypisania
CES↔P1S, słowniki w bazie, przebiegi), `docs/mapowanie-ces-p1s.md`.
Ustalenia M16–M26 ilustruje `prototyp/PZL-EV Pipeline v3.html` (dane przykładowe; źródłem prawdy jest
dokumentacja).

---

## 1. Użytkownicy

| Użytkownik | Jak korzysta | Uprawnienia |
|---|---|---|
| **Osoba z finansów** | aplikacja w przeglądarce (lokalnie) | wszystkie funkcje we wszystkich projektach – bez podziału ról; każda akcja zapisana z kontem AD |
| **CAM** (Cost Account Manager) | wyłącznie pliki Excel na dysku sieciowym | odczyt `CAM\…\Wyslane`, zapis `CAM\…\Zwrocone` swojego projektu |
| **Administrator** | wdrożenia na PROD | skrypty bazy, konfiguracja środowiska |
| **Developer** | środowisko TEST | rozwój aplikacji i bazy |

---

## 2. Pojęcia

| Pojęcie | Znaczenie |
|---|---|
| Projekt | projekt PZL-EV budowany do przeliczania wskaźników EV (M22 – dawniej „zakres”); obejmuje węzły drzewa P1S zaznaczone w kreatorze (M23); typ SAC, CAS lub Wewnętrzny. *(Wcześniej: program lub pula małych projektów – M8, zastąpione)* |
| Zakres projektu (P1S) | grupy drzewa P1S (`sel`) i pojedyncze `PROJORG` (`p1s`) należące do projektu, z ich elementami WBS/PSP (M23) |
| Projekt CES / `PROJORG` | projekt w SAP CES / projekt SAP P1S (korzeń drzewa P1S); 1 `PROJORG` = wiele projektów CES (M17) – nie mylić z projektem PZL-EV |
| Raport mapowań | raport mapowań SAP↔CES z `PZLPROD` (tylko odczyt) – źródło prawdy przypisań CES↔P1S (M16) |
| Korekta | globalna zmiana przypisania elementu CES albo projektu CES względem raportu, z uzasadnieniem i historią (M18) |
| Przebieg | jedno przetworzenie projektu za okres: tygodniowy albo zamknięcie miesiąca |
| Etap | krok przebiegu z własnym statusem i bramką wyjścia |
| Słownik globalny | wspólny dla wszystkich projektów (stawki wydziałów, kalendarz okresów, kursy USD/PLN) |
| Słownik projektu | należy do jednego projektu: WP i CAM, Harmonogram i budżet, w CAS Stawki CAS, opcjonalnie Cost Category – zmiany w projekcie (M24, M26) |
| Cost Category | globalny słownik numerów elementów kosztowych (`ACTUALS_CES`) → Opis, Obszar, Cost Category + zmiany w projekcie (M26) |
| Baza słowników | SQLite na dysku sieciowym (`00_Global\Baza\`): wszystkie słowniki, przypisania i konfiguracja, edytowane w aplikacji (M13); **Excel nie jest źródłem słowników** – służy do wczytania / pobrania z tą samą walidacją (M24, M26) |
| Stan słownika | zawartość słownika na dany moment; każda zmiana zapisana w aplikacji przechodzi walidację przy zapisie i zostaje w historii (`ValidFrom`/`ValidTo`, kto, kiedy) |
| Przypięcie | zapamiętanie w przebiegu stanu słowników, na którym liczył, i kopia tego stanu (migawka) do MS SQL (D27) |
| Landing Zone | archiwum kopii oryginalnych plików (z hashem w bazie) |
| Rewizja EV | kolejny wynik EV tego samego przebiegu; poprzednie zostają w historii |
| Pochodzenie wartości | źródło zaawansowania: CAM, PRODUKCJA lub ANALITYK (wpis osoby z finansów) |
| P1S / CES | SAP produkcyjny (zaawansowanie) / SAP finansowy (koszty) |

---

## 3. Mapa ekranów

| Ekran | Zawartość | Główne akcje |
|---|---|---|
| **Pulpit** | karty projektów ze stanem bieżącego przebiegu, lista „Wymaga uwagi”, ostatnie zdarzenia | przejście do projektu / przebiegu, nowy projekt |
| **Import** | pobranie i import plików RABIT, historia importów; konfiguracja = tylko „Prefiksy plików RABIT” (M21) | pobierz, importuj |
| **Projekty** | lista projektów: typ, zakres P1S (liczba `PROJORG`) i CAM, aktywny przebieg, folder | nowy projekt |
| **Projekt** | gotowość projektu, zakres P1S, słowniki projektu (pobierz / wczytaj Excel), historia przebiegów, struktura folderów | nowy przebieg |
| **Kreator projektu** | 5 kroków (M23): Podstawowe, Projekty P1S, Słowniki projektu, Foldery, Podsumowanie (baza analityczna – M25) | utwórz projekt |
| **Przebiegi** | wszystkie przebiegi wszystkich projektów ze stanem i osobą | przejście do przebiegu |
| **Przebieg** | oś etapów, panel wybranego etapu, przypięte słowniki, dziennik przebiegu | akcje etapów |
| **Słowniki** | słowniki globalne (w tym Cost Category – M26) i słowniki projektu w bazie: tabela z filtrowaniem, stan walidacji, historia zmian | dodaj / edytuj / zamknij ważność wiersza; pobierz / wczytaj Excel |
| **Mapowanie CES ↔ P1S** (ADMIN) | dwa drzewa CES \| P1S w strukturze kategoryzacji, statusy `OVERRIDE/REPORT/INHERITED/UNMAPPED`, węzeł „Nieprzypisane”; bez kosztów (F21–F24, M16–M19) | korekta elementu CES, korekta projektu CES, usunięcie korekty, historia |

Nagłówek aplikacji zawsze pokazuje środowisko (TEST / PROD), zalogowanego użytkownika AD
oraz wersję aplikacji i schematu.

---

## 4. Funkcje

### F01. Utworzenie projektu (kreator) *(M23–M25)*

1. **Podstawowe** – kod (unikalny, używany w nazwach folderów, plików i przebiegów), nazwa, typ
   SAC / CAS / Wewnętrzny. Aplikacja pokazuje liczbę etapów wynikającą z typu.
2. **Projekty P1S** – zakres projektu z **drzewa P1S** w strukturze kategoryzacji
   (`Z_KAT_ZBIORCZA → Z_KATEGORIA → Z_OPIS → PROJORG → elementy WBS/PSP`, M20). Drzewo rozwija się w dół
   (bez „wchodzenia” w grupy), z checkboxem na każdym poziomie. Można zaznaczyć kilka grup z różnych poziomów
   oraz pojedyncze `PROJORG`; grupa obejmuje wszystkie swoje `PROJORG` i ich elementy WBS/PSP. Zapis:
   `sel` (ścieżki grup) + `p1s` (pojedyncze `PROJORG`). Rozstrzyganie konfliktów:
   - `PROJORG` należący do innego projektu jest pomijany,
   - pojedynczo wskazany `PROJORG` ma pierwszeństwo przed grupą,
   - wśród grup wygrywa projekt utworzony wcześniej,
   - `PROJORG`, które pojawią się później w zaznaczonej grupie – O22 (prototyp: wchodzą automatycznie).
3. **Słowniki projektu** – z Excela (M24): **WP i CAM**, **Harmonogram i budżet**, w CAS także
   **Stawki CAS**, **Cost Category** (M26).
   - Format: jeden plik z arkuszami (szablon do pobrania, z elementami P1S z zakresu) albo osobne pliki / CSV;
     kolumny rozpoznawane po nagłówkach (w prototypie: WP i CAM – WP, Element P1S, CAM, Cost Category;
     Harmonogram i budżet – WP, BAC HOURS,BAC MATERIAL , Planowany Start, Planowany Koniec).
   - Walidacja jak przy zapisie w aplikacji (rozdz. 6.3); **słownik z błędami nie zostaje zapisany**.
   - Lista słowników globalnych z bieżącym stanem.
4. **Foldery** – podgląd struktury `Projekty\<Projekt>\`; kontrole: dostępność korzenia (UNC), prawo zapisu,
   brak folderu o tym kodzie; informacja o zgłoszeniu do IT (dostęp CAM do `Zwrocone`).
5. **Podsumowanie = baza analityczna** (M25) → **Utwórz projekt**:
   - drzewo (Z_KATEGORIA → `Z_OPIS` → WBS_ELEMENT (CES pspnr_ces - z mapowania) połączone z WP, CAM, BAC_*, datami i liczbą elementów CES
     z mapowania (F21–F24),
   - braki: element z kosztami CES albo zaawansowaniem bez WP, WP bez budżetu,
   - sumaryczny budżet na końcu, ilość godzin sumaryczna, sumaryczna ilość kosztów materiałów jako końcowa weryfikacja projektu
   - utworzenie: rejestracja w bazie słowników i w MS SQL, utworzenie folderów,
     kontrola struktury.

Krok „CAM” usunięty – CAM jest kolumną słownika „WP i CAM” (M23). Po utworzeniu projektu każdy słownik
projektu można **pobrać do Excela i wczytać ponownie**: podgląd różnic (+nowe / ~zmienione / −usunięte),
zapis z historią (M24). Baza analityczna także na stronie projektu – O23.

Reguły: kod musi być unikalny; projekt z niekompletnymi słownikami (brak WP, CAM lub budżetu) nie może
uruchomić przebiegu.

### F02. Gotowość projektu

Lista kontrolna na ekranie projektu:
- projekt zarejestrowany w bazie,
- struktura folderów zgodna z konfiguracją,
- zakres P1S projektu obejmuje co najmniej jeden `PROJORG` (M23),
- słowniki projektu wypełnione (WP, harmonogram i budżet; w CAS stawki CAS) (**blokuje** przebieg),
- WP mają przypisanego CAM (ostrzeżenie – bez tego nie powstaną pliki CAM),
- dostęp CAM do folderu `Zwrocone` (ostrzeżenie).

### F03. Edycja słowników i przypisań

- Ekran Słowniki (i Mapowanie CES ↔ P1S) w dowolnym momencie; dotyczy słowników globalnych i słowników projektu.
- Edycja w tabeli / formularzu; pola typowane (data, liczba, wybór z listy), klucze WBS zawsze jako tekst.
- **Walidacja przy zapisie** wg rozdz. 6: błąd blokujący nie pozwala zapisać (komunikat przy polu),
  ostrzeżenie wymaga potwierdzenia.
- Zmiana wiersza = zamknięcie `ValidTo` starego i nowy wiersz (historia, kto, kiedy); „usunięcie” =
  zamknięcie ważności. Brak zmian → nic się nie zapisuje.
- Zapis to krótka transakcja; jednocześnie zapisuje jedna osoba (SQLite na dysku sieciowym) – druga
  widzi komunikat i ponawia.
- Aktywne przebiegi z przypiętym wcześniejszym stanem dostają informację (F16 – `docs/pipeline-fazy.md`, rozdz. 4a).
- **Excel jako format wymiany** (M24, M26): słowniki projektu i Cost Category można pobrać do Excela i wczytać
  ponownie – podgląd różnic (+nowe / ~zmienione / −usunięte), ta sama walidacja co przy zapisie w aplikacji,
  zapis z historią. Źródłem prawdy pozostaje baza słowników.

### F04–F18. Import i przebieg – opis w `docs/pipeline-fazy.md`

Wszystkie etapy (fazy globalne i etapy przebiegu) są opisane **w jednym miejscu** – `docs/pipeline-fazy.md`.
Numery funkcji pozostają jako identyfikatory:

| Funkcja | Faza / etap w `docs/pipeline-fazy.md` |
|---|---|
| F04 Uruchomienie przebiegu | P0 |
| F05a Import plików SAP (globalny, poza przebiegiem) | G1, G2 |
| F05 Dane SAP projektu | P1 (etap 1) |
| F06 Przypięcie słowników | P2 (etap 2) |
| F07 Walidacja | P3 (etap 3) |
| F08 Łączenie źródeł | P4 (etap 4) |
| F09 Pliki dla finansów | P5 (etap 5) |
| F10 Zaawansowanie z produkcji (tydzień) | P6 (etap 6) |
| F11 Uzupełnienie braków (tydzień) | P7 (etap 7) |
| F12 Pliki dla CAM i ich import (zamknięcie) | P6–P7 (etapy 6–7) |
| F13 Walidacja zaawansowania | P8 (etap 8) |
| F14 Generowanie EV | P9 (etap 9) |
| F15 Plik dla Cobra (SAC) | P10 (etap 10) |
| F16 Zmiana słowników w trakcie przebiegu | rozdz. 4a |
| F17 Kontynuacja przebiegu przez inną osobę | rozdz. 4a |
| F18 Zamknięcie okresu | Zamknięcie okresu |

### F19. Pulpit i dziennik

- „Wymaga uwagi”: brak przebiegu w bieżącym tygodniu, etapy wymagające akcji lub z błędem,
  pliki czekające na potwierdzenie, zmiany słowników po przypięciu, projekty z niekompletnymi słownikami, elementy `UNMAPPED` z kosztem.
- „Ostatnie zdarzenia”: przekrój dzienników wszystkich przebiegów.
- Dziennik przebiegu: czas, użytkownik, opis – od najnowszego.

### F20. Kontrola wersji i środowiska

- Przy starcie: zgodność wersji aplikacji i schematu bazy; niezgodność blokuje pracę z komunikatem,
  co zaktualizować.
- Profil TEST / PROD widoczny w nagłówku.

---

### F21–F24. Mapowanie CES ↔ P1S *(założenia – `docs/mapowanie-ces-p1s.md`)*

Część administracyjna – tylko przypisanie elementów CES do elementów P1S, **bez kosztów** (M16).

- **F21 Drzewa CES i P1S** – dwa drzewa w tej samej strukturze kategoryzacji (M20); element CES umieszczony
  wg przypisanego elementu P1S; węzeł „Nieprzypisane” z propozycją celu dla projektów CES spoza raportu;
  wyszukiwanie; statusy `OVERRIDE` / `REPORT` / `INHERITED` / `UNMAPPED` (M18, M19).
- **F22 Raport mapowań** – przypisania z raportu mapowań SAP↔CES (`PZLPROD`, tylko odczyt), dopasowanie
  po `pspnr` (M16); WBS CES spoza raportu dziedziczy odpowiednik swojego projektu CES (M17). Przypisania
  z raportu nie da się usunąć.
- **F23 Korekty** – korekta elementu CES (dowolny element P1S) albo korekta projektu CES (cel WBS spoza
  raportu); pierwszeństwo przed raportem, obowiązkowe uzasadnienie zmiany względem raportu, historia;
  usunięcie korekty przywraca raport albo dziedziczenie (M18). (`NO_P1S` – odłożony, M14.)
- **F24 Po imporcie** – nowe elementy CES z wynikiem rozstrzygania (`INHERITED` – cel projektu CES,
  `UNMAPPED`).

> *Wcześniej (zastąpione: M16–M19):* F21 drzewa ze statusami; F22 reguła projektu CES → P1S (obecne i
> przyszłe WBS); F23 wyjątek / gałąź WBS CES → WBS P1S, `include_children`, dezaktywacja z historią;
> F24 lista nowych elementów (`odziedziczono`, `UNMAPPED`) i kontrola kompletności projektu CES.

### F25. Słownik Cost Category *(M26)*

- **Globalny** słownik: numer elementu kosztowego z kosztów rzeczywistych (`ACTUALS_CES`) → Opis, Obszar,
  Cost Category (kategoria kosztu). Początkowa zawartość – załącznik A.
- **„Cost Category – zmiany w projekcie”** – opcjonalny słownik każdego projektu; zmienia i dodaje pozycje,
  ma pierwszeństwo przed globalnym. **Słownik efektywny projektu = globalny + zmiany projektu.**
- Oba słowniki: edycja w aplikacji z historią oraz Excel (pobierz / wczytaj z podglądem różnic). Numer
  zapisany w Excelu jako liczba jest uzupełniany zerami do 10 znaków (np. `51105550` → `0051105550`).
- Przebieg przypina oba słowniki (P2); walidacja w przebiegu (P3, rozdz. 6.6) – `docs/pipeline-fazy.md`.
- Powiązanie z kolumną „Cost Category” słownika „WP i CAM” – O24; panel „Koszty wg kategorii P1S” na stronie
  projektu – O26.

## 5. Statusy etapu

Opis statusów i przejść etapu przebiegu: `docs/pipeline-fazy.md`, rozdz. 1.1.

---

## 6. Reguły walidacji

### 6.1 Wszystkie słowniki (walidacja przy zapisie w aplikacji)

Słowniki są edytowane w aplikacji, więc problemy typowe dla Excela (nieczytelny plik, brak arkusza,
`#N/A`, „1.10” zamienione na 1.1) nie występują – pola są typowane w formularzu.

| Poziom | Reguła | Waga |
|---|---|---|
| Typy | daty, liczby, wartości z listy; klucze WBS jako tekst | blokujący (pole nie przyjmie wartości) |
| Czystość | spacje na początku/końcu i niełamliwe usuwane automatycznie; podobny zapis istniejącej wartości | ostrzeżenie |
| Klucz | brak duplikatów klucza; brak nakładających się okresów `ValidFrom`–`ValidTo`; `ValidFrom` ≤ `ValidTo` | blokujący |
| Referencje | wskazany element (projekt, WP, CAM, wydział) istnieje | blokujący |
| Historia | kto / kiedy uzupełnia aplikacja; zmiana i usunięcie tylko przez zamknięcie `ValidTo` | automatycznie |

### 6.2 Przypisania projektu (projekty, WP, CAM, mapowanie CES↔P1S)

| Reguła | Waga |
|---|---|
| `PROJORG` należy do co najwyżej jednego projektu; przy konflikcie w kreatorze `PROJORG` innego projektu jest pomijany (M23) | blokujący |
| element CES ma co najwyżej jeden aktywny cel P1S (M3 – bez 1:wiele) | blokujący |
| ~~reguła projektu CES wskazuje `PROJORG` (M7)~~ *(zastąpione: M16, M18 – korekta może wskazać dowolny element P1S)* | ~~blokujący~~ |
| korekta zmieniająca przypisanie z raportu mapowań ma uzasadnienie (M18) | blokujący |
| przypisania z raportu mapowań nie można usunąć – tylko skorygować (M18) | blokujący |
| WP wymaga CAM | blokujący |
| element P1S słownika „WP i CAM” należy do zakresu projektu i nie do innego projektu (M24) | blokujący |
| jeden WP na element P1S (M24) | blokujący |
| CAM wybierany z listy osób (brak wariantów zapisu); przy wczytaniu z Excela CAM spoza listy osób (M24) | – / ostrzeżenie |

### 6.3 Harmonogram i budżet

| Reguła | Waga |
|---|---|
| element (WP) należy do projektu; WP z harmonogramu musi być w „WP i CAM” (M24) | blokujący |
| liczby i daty poprawne (także przy wczytaniu z Excela – M24) | blokujący |
| daty rozpoczęcia ≤ daty zakończenia (bazowe, planowane, rzeczywiste; Start ≤ Koniec) | blokujący |
| budżet ≥ 0 | blokujący |
| budżet na elemencie z WP = no | ostrzeżenie |
| WP = yes bez budżetu | ostrzeżenie |

### 6.4 Stawki wydziałów / stawki CAS

| Reguła | Waga |
|---|---|
| para Department + Year unikalna | blokujący |
| Labor Rate > 0, Overhead ≥ 0 | blokujący |
| wydział z kosztów CES bez stawki na dany rok | blokujący w przebiegu (etap 3) |

### 6.5 Pliki SAP

| Reguła | Waga |
|---|---|
| części jednego źródła mają identyczny układ kolumn | blokujący |
| brak duplikatów wierszy między częściami | blokujący |
| daty księgowania w okresie przebiegu | blokujący |
| plik zmieniony po rejestracji (inny hash) | blokujący |


Wczytanie słownika z Excela (M24, M26) stosuje reguły 6.1–6.4 i 6.6 jak przy zapisie w aplikacji; słownik z
błędem blokującym **nie zostaje zapisany**.

### 6.6 Cost Category (M26)

| Reguła | Waga |
|---|---|
| numer elementu kosztowego unikalny w słowniku (globalnym / zmianach projektu); tekst 10 znaków – liczba z Excela uzupełniana zerami | blokujący / automatycznie |
| numer z kosztów projektu (`ACTUALS_CES`) bez wpisu w słowniku globalnym ani w zmianach projektu | blokujący w przebiegu (etap 3); naprawa: dodanie w słowniku projektu; waga do potwierdzenia – O25 |
| numer w słowniku bez Cost Category | ostrzeżenie |
---

## 7. Pliki generowane

| Plik | Folder | Etap |
|---|---|---|
| `<Projekt>_AC_przeglad_<T<NN>>.xlsx`, `<Projekt>_ETC_wstepne_<T<NN>>.xlsx`, `<Projekt>_WBS_bez_mapowania.xlsx` | `Finanse\<RRRR-MM>\` | 5 |
| `<Projekt>_<RRRR-MM>_CAM_<CAM>.xlsx` | `CAM\<RRRR-MM>\Wyslane\` | 6 (zamknięcie) |
| `<Projekt>_EV_<T<NN>|RRRR-MM>_R<n>.xlsx` | `EV\<RRRR-MM>\` | 9 |
| `<Projekt>_<RRRR-MM>_Cobra_import.csv` | `EV\<RRRR-MM>\` | 10 (SAC) |

Nazwy plików są propozycją do potwierdzenia. Słowniki nie są plikami – nie powstają pliki walidacji
ani propozycji słowników.

---

## 8. Scenariusze (przejście przez prototyp)

1. **Nowy projekt M28** – kreator → projekt zablokowany (brak WP, CAM, budżetu) → uzupełnienie w
   aplikacji → projekt gotowy.
2. **Tydzień F16** – zapis nakładających się okresów odrzucony przy zapisie → poprawa; w łączeniu
   wykryte elementy CES `UNMAPPED` i P1S bez WP → przypisanie w aplikacji → przeliczenie;
   potwierdzenie plików; produkcja, uzupełnienia, EV wstępne.
3. **Odrzucenie plików dla finansów** – komentarz → powrót do łączenia.
4. **Zmiana stawek** – edycja w aplikacji → baner w przebiegach → przeliczenie od etapu 3.
5. **Kontynuacja przebiegu S70i** rozpoczętego przez inną osobę.
6. **Zamknięcie miesiąca S70i (SAC)** – pliki CAM, odrzucenie pliku (zmiana poza polami), ponowny
   import, 100% CAM, EV, plik Cobra, zamrożenie okresu.

---

## 9. Poza zakresem wersji 1

- ~~Import / eksport słowników z i do Excela (ewentualnie później jako wygoda – nie źródło prawdy, M13).~~
  *(zmienione: M24, M26 – słowniki projektu i Cost Category mają pobranie / wczytanie Excela w wersji 1;
  źródłem prawdy pozostaje baza)*
- Status `NO_P1S` (odłożony do pierwszego rzeczywistego przypadku, M14; potwierdzone 30.09.2026).
- Dostęp CAM do aplikacji.
- Podział uprawnień w finansach.
- Automatyczne pobieranie z RABIT (jeśli nie będzie gotowe – pliki umieszczane ręcznie).
- Raporty Power BI (baza udostępni widoki, raporty powstaną osobno).

---

## 10. Otwarte pytania funkcjonalne

| # | Pytanie |
|---|---|
| O3 | Plik CAM: jeden na CAM czy jeden na program? |
| O10 | Źródło zaawansowania z produkcji dla przebiegów tygodniowych |
| O11 | Zasady uzupełniania braków (metody, domyślna metoda) |
| O14 | Logika łączenia źródeł (etap 4) |
| O15 | Źródło ETC i jego miejsce w przebiegu |
| O16 | Zawartość plików dla finansów (etap 5) – lista i układ |
| O17 | Układ pliku CAM – które pola uzupełnia CAM (zaawansowanie %, ETC, komentarz?) |
| O18 | Numeracja tygodni (ISO czy wewnętrzna) i folder dla zamknięcia miesiąca |
| O19 | Przebieg przypina stan słowników (D27) – czy ma też przypinać stan raportu mapowań SAP↔CES (raport zmienia się w bazie `PZLPROD`)? (zob. S9 w `docs/mapowanie-ces-p1s.md`) |
| O20 | Czy korekta projektu CES ma przenosić także jego WBS z raportu mapowań? Dziś (M18) przenosi tylko WBS spoza raportu |
| O21 | Kategoria jest w każdym wierszu `LOG.WBS`, a drzewo grupuje `PROJORG` według jego wiersza (`STUFE` 1). Czy elementy jednego `PROJORG` mogą mieć różne kategorie? |
| O22 | Zakres z grupy: czy `PROJORG`, które później pojawią się w zaznaczonej grupie, mają wchodzić do projektu automatycznie? Prototyp: tak |
| O23 | Baza analityczna (M25) także na stronie projektu (po zmianie słowników), nie tylko w kreatorze? |
| O24 | Kolumna „Cost Category” w słowniku „WP i CAM” ma dziś stałe wartości Labor / Material / Subcontract – czy ma przyjmować kategorie ze słownika Cost Category (M26)? |
| O25 | Numer elementu kosztowego bez wpisu w Cost Category: błąd blokujący (dziś, M26) czy ostrzeżenie? |
| O26 | Panel „Koszty wg kategorii P1S” na stronie projektu – zostaje czy to inny etap? |

`NO_P1S` (M14) – nadal odłożone.

---

## Załącznik A. Początkowa zawartość słownika Cost Category (M26)

Pusta kolumna „Obszar” – brak wartości w ustaleniu źródłowym; „(brak)” – pozycja bez Cost Category
(ostrzeżenie – rozdz. 6.6). Numery krótsze niż 10 znaków (np. `9221X550`) zapisane jak w źródle.

| Nr elementu kosztowego | Opis | Obszar | Cost Category |
|---|---|---|---|
| 0051105550 | PZL Mat Consump | | Direct Materials |
| 0057100000 | Proj Sttlmnt Bill | | (brak) |
| 0051110550 | PZL Oth Dir Serv ODS | | Direct Services |
| 0057511550 | PZL ODC NVA | | Other Direct Cost |
| 0057712550 | PZL Direct MTS BFM | | Chemicals |
| 0057714550 | PZL Dir Packaging NV | | Packaging Materials |
| 0057120550 | PZL Travel | | Travel costs |
| 0092210550 | PZL Quality Control | Quality Control | Manufacturing and QA labor |
| 0092211550 | PZL Pain Spec Proc | Manufacturing | Manufacturing and QA labor |
| 0092212550 | PZL Machining (W30) | Manufacturing | Manufacturing and QA labor |
| 0092213550 | PZL Sheet metl W40 | Manufacturing | Manufacturing and QA labor |
| 0092214550 | PZL Sub assem W51 | Manufacturing | Manufacturing and QA labor |
| 0092216550 | PZL Fnl assem W53 | Manufacturing | Manufacturing and QA labor |
| 0092215550 | PZL Sub assem W52 | Manufacturing | Manufacturing and QA labor |
| 0092217550 | PZL Hangar Ops W60 | Manufacturing | Manufacturing and QA labor |
| 0092218550 | PZL Svc ctr W70 DUS | Manufacturing | Manufacturing and QA labor |
| 0092219550 | Tooling | Manufacturing | Manufacturing and QA labor |
| 0092223550 | PZL LM Aero Coop W54 | Manufacturing | Manufacturing and QA labor |
| 0094410550 | PZL Des Eng/Proc Eng | Engineering | Engineering labor |
| 0094414550 | PZL Des Industrial E | Engineering | Engineering labor |
| 0094412550 | PZL Programs | Programs | Programs labor |
| 0094490550 | PZL LL Des Eng/Proc | Engineering | Engineering labor |
| 0096606550 | PZL Gen Svcs labor | General Services | General Services |
| 9221X550 | PZL MFG Indirect | Manufacturing | Manufacturing and QA labor |
| 9222X550 | PZL Quality Indirect | Quality Control | Manufacturing and QA labor |
| 9229X550 | LL Mfg Indirect | Manufacturing | Manufacturing and QA labor |
| 9229D550 | PZL LL LM Aero C W54 | LL Manufacturing | Manufacturing and QA LL labor |
| 9441X550 | PZL ENG Indirect | Engineering | Engineering labor |
| 9660R550 | PZL Mfg Svcs labor | Manufacturing Services | Manufacturing Services |
| 0096610550 | Procurement | Procurement | Procurement Labor |
| 9662R550 | PZL Customs and Tran | Manufacturing Services | Manufacturing Services |
| 0096626550 | PZL Shipping | General Services | General Services |
| 0096616550 | PZL Finance | Finance | Finance |
