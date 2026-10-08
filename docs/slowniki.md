# PZL-EV – Słowniki

Zakres: słowniki globalne i słowniki projektu – zawartość, zależność od typu projektu, edycja, wymiana przez
Excel i reguły walidacji. Słowniki zawierają wiedzę biznesową potrzebną do przekształcenia danych źródłowych
w dane raportowe.

Powiązane: `docs/model-danych.md` (historia i przypięcie przez przebieg), `docs/mapowanie-ces-p1s.md`
(raport mapowań CES ↔ P1S – rozstrzyganie; korekty mapowania – osobny mechanizm), `docs/funkcjonalnosc.md` (ekran
Słowniki, kreator projektu).

---

## 1. Zasady

- **Źródłem prawdy jest baza PZL-EV** (MS SQL, schemat `dict`). Edycja w aplikacji: tabela / formularz
  z polami typowanymi (data, liczba, wybór z listy); klucze WBS i numery zawsze jako tekst.
- **Historia:** każda zmiana zapisuje kto, kiedy, poprzednią i nową wartość oraz okres obowiązywania
  (`docs/model-danych.md`, rozdz. 3).
- **Walidacja przy zapisie** (rozdz. 5): ERROR nie pozwala zapisać (komunikat przy polu), WARNING wymaga
  potwierdzenia. Błędna wersja słownika nie powstaje.
- **Excel jako format wymiany:** słowniki globalne i słowniki projektu można pobrać do Excela i wczytać ponownie
  (także CSV).
  Wczytanie zastępuje całą zawartość słownika zawartością pliku – wiersze spoza pliku są usuwane (zamknięcie okresu,
  historia zostaje). Kolumny rozpoznawane po nagłówkach; podgląd różnic (+nowe / ~zmienione / −usunięte; przy
  dużych zmianach pierwsze 200 pozycji każdego rodzaju i liczba pozostałych); ta sama walidacja co przy zapisie
  w aplikacji; słownik z błędem ERROR nie zostaje zapisany. Numer zapisany w Excelu jako liczba
  jest uzupełniany zerami do długości klucza (np. `51105550` → `0051105550`).
- **Równoczesna edycja:** zapis jest krótką transakcją; jeśli wiersz zmienił ktoś inny od chwili otwarcia,
  zapis jest odrzucany z komunikatem i trzeba go ponowić na aktualnych danych.
- Przebieg czyta słowniki w stanie na swój znacznik stanu (`docs/model-danych.md`, rozdz. 4.2); zmiana
  słownika po przypięciu jest sygnalizowana w przebiegu (`docs/pipeline-fazy.md`, rozdz. 5).

---

## 2. Słowniki globalne

| Słownik | Zawartość | Klucz | Użycie |
|---|---|---|---|
| Kalendarz okresów | okres (`RRRR-MM`); tygodnie okresu (numer, od – do); oznaczenie tygodnia zamykającego okres | tydzień | przebieg: okres i czy zamyka okres (P0); nazwy plików |
| Stawki wydziałów | `MPK` (miejsce powstawania kosztów), `Department` (opis MPK, opcjonalny), `Year`, `Labor Rate`, `Overhead` (opcjonalny) | `MPK` + `Year` | łączenie źródeł – godziny na koszt (P3) |
| Kursy walut | waluta, okres, kurs (USD / PLN) | waluta + okres | przeliczenia walut (P3, P8) |
| Cost Category | numer elementu kosztowego → Opis, Obszar, Cost Category (rozdz. 6) | numer elementu kosztowego | walidacja (P2), łączenie źródeł (P3) |
| Osoby | pracownicy z HR – „Wczytaj z HR” (PZLHRPROD `HR.ORG`, osoby z niepustym `USRID`; zawartość zastępowana po podglądzie różnic): USRID (numer znaczka, login), imię i nazwisko, imię, nazwisko, e-mail, MPK (`KOSTL`), dział (`SHORT`, `LONG`), stanowisko (`STEXT`), pion, manager, PERNR | USRID | wybór CAM w strukturze projektu i słowniku „WP i CAM” (zapisywany USRID) |
| Raport mapowań CES ↔ P1S | raport mapowań SAP↔CES z `PZLPROD` – wszystkie kolumny pliku (`docs/mapowanie-ces-p1s.md`, rozdz. 2); wczytywany w całości nowym raportem | `src` + `pspnr` | mapowanie CES ↔ P1S (ekran Mapowanie, nakładka projektu, G2, P1, P3) |

Definicje źródeł i ich prefiksy są konfiguracją importu – `docs/zrodla-danych.md`, rozdz. 2.

---

## 3. Słowniki projektu

| Słownik | Zawartość | Klucz |
|---|---|---|
| WP i CAM | element P1S projektu → WP, CAM, Cost Category (Labor / Material / Subcontract – O24); powiązanie z nakładką przez `Legacy WBS` (`docs/performance-objectives.md`, rozdz. 4.1) | element P1S |
| Harmonogram i budżet | WP → `BAC HOURS` (godziny), `BAC MATERIAL` (koszt materiałów), Baseline Start, Baseline Koniec (RRRR-MM-DD; w plikach Excel rozpoznawane też dawne nagłówki „Planowany Start/Koniec”) – baseline projektu | WP |
| Stawki CAS | stawki CAS projektu (zawartość – O37); do ustalenia słownik nie jest wczytywany, a projekt CAS jest niegotowy (ERROR) | do ustalenia |
| Cost Category – zmiany w projekcie | zmiany i uzupełnienia słownika globalnego Cost Category dla projektu (rozdz. 6) | numer elementu kosztowego |
| Wykluczenia | elementy pomijane na późniejszym etapie analizy: kombinacja `Cost Element`, `WBS Element`, `Partner object` (co najmniej jedno z trzech) + wymagany opis (rozdz. 7) – **opcjonalny** | kombinacja trzech pól |

- Słowniki projektu powstają w kreatorze projektu z plików Excel (szablon z elementami projektu – jeden
  plik z arkuszami albo osobne pliki / CSV) i są dalej utrzymywane w aplikacji (`docs/funkcjonalnosc.md`, F01, F03):
  na ekranie Projekt w zakładce Słowniki projektu – lista słowników i tabela wybranego słownika z edycją
  w komórkach jak na ekranie Słowniki (dodaj / zmień / usuń wiersz, „Zapisz” z walidacją, „Odrzuć zmiany”, filtr)
  oraz pobranie i wczytanie z Excela z podglądem różnic. „WP i CAM” oraz „Harmonogram i budżet” zmienia się też
  w komórkach tabeli w zakładce Struktura – to te same słowniki (zapis od razu po zatwierdzeniu wiersza, z tą samą
  walidacją i historią; `docs/performance-objectives.md`, rozdz. 4.2).
- Harmonogram i budżet mogą się zmieniać w trakcie projektu – każda zmiana jest w historii, a przebieg liczy
  na stanie z chwili przypięcia.

---

## 4. Typ projektu a słowniki

Wszystkie typy projektów przechodzą ten sam przebieg (`docs/pipeline-fazy.md`). Typ wyznacza zestaw słowników
pobieranych do przebiegu oraz wymaganych do jego uruchomienia:

| Typ | Słowniki projektu wymagane | Słowniki szczególne | Waluta wyniku | Wynik dodatkowy |
|---|---|---|---|---|
| SAC (Sikorsky) | WP i CAM, Harmonogram i budżet | Stawki wydziałów (bieżące), Kursy walut (USD) | USD | plik dla Cobra |
| CAS Compliance | WP i CAM, Harmonogram i budżet, Stawki CAS | Stawki CAS – przeliczenie kosztów według reguł CAS | PLN | – |
| Wewnętrzny | WP i CAM, Harmonogram i budżet | – | PLN | – |

Słowniki globalne (rozdz. 2) i „Cost Category – zmiany w projekcie” są pobierane dla każdego typu.
Kontekst biznesowy typów – `readme.md`.

---

## 5. Reguły walidacji

### 5.1 Wszystkie słowniki

| Obszar | Reguła | Poziom |
|---|---|---|
| Typy | daty, liczby, wartości z listy; klucze WBS jako tekst | ERROR (pole nie przyjmie wartości) |
| Czystość | spacje na początku / końcu i niełamliwe usuwane automatycznie | automatycznie |
| Czystość | zapis podobny do istniejącej wartości | WARNING |
| Klucz | brak duplikatów klucza; brak nakładających się okresów `ValidFrom`–`ValidTo`; `ValidFrom` ≤ `ValidTo` | ERROR |
| Odwołania | wskazany element (projekt, WP, CAM, MPK) istnieje | ERROR |
| Historia | kto i kiedy – uzupełnia aplikacja; zmiana i usunięcie tylko przez zamknięcie okresu | automatycznie |

### 5.2 WP i CAM

| Reguła | Poziom |
|---|---|
| element P1S należy do projektu (kod P1S elementu nakładki – `Legacy WBS` albo cel mapowania – albo pod nim, także w `LOG.WBS`) i nie należy do innego projektu | ERROR |
| jeden WP na element P1S | ERROR |
| WP wymaga CAM | ERROR |
| CAM wybierany z listy osób; przy wczytaniu z Excela CAM spoza listy osób | – / WARNING |

### 5.3 Harmonogram i budżet

| Reguła | Poziom |
|---|---|
| WP z harmonogramu istnieje w słowniku „WP i CAM” projektu | ERROR |
| liczby i daty poprawne | ERROR |
| data rozpoczęcia ≤ data zakończenia (bazowe, planowane, rzeczywiste) | ERROR |
| budżet ≥ 0 | ERROR |
| budżet na elemencie bez WP | WARNING |
| WP bez budżetu | WARNING |

### 5.4 Stawki wydziałów i stawki CAS

| Reguła | Poziom |
|---|---|
| para `MPK` + `Year` unikalna (`Department` to tylko opis MPK) | ERROR |
| `MPK` i `Labor Rate` wymagane; `Labor Rate` > 0 | ERROR |
| `Overhead` opcjonalny; wypełniony – ≥ 0 | ERROR |

### 5.5 Cost Category

| Reguła | Poziom |
|---|---|
| numer elementu kosztowego unikalny w słowniku (globalnym / zmianach projektu); zapisywany jako tekst – numer liczbowy uzupełniany zerami do 10 znaków | ERROR |
| numer w słowniku bez Cost Category | WARNING |

### 5.6 Wykluczenia

| Reguła | Poziom |
|---|---|
| co najmniej jedno z pól `Cost Element`, `WBS Element`, `Partner object` wypełnione | ERROR |
| opis wypełniony | ERROR |
| powtórzona kombinacja `Cost Element` + `WBS Element` + `Partner object` | ERROR |

### 5.7 Raport mapowań CES ↔ P1S

| Reguła | Poziom |
|---|---|
| `src` = `SAP` albo `CES`; `pspnr` wypełniony | ERROR |
| para `src` + `pspnr` unikalna | ERROR |
| wiersz `src` = CES bez przypisania: brak elementu CES (`wbs_ces`, `wbs`) albo celu P1S (`pspnr_sap`, `wbs_sap`) i brak pary `project_ces` → `project_sap` | WARNING |

Reguły rozstrzygania na zawartości raportu (kilka celów jednego elementu CES, cel spoza `LOG.WBS`) sprawdza ekran
Mapowanie – `docs/mapowanie-ces-p1s.md`, rozdz. 10.

Kontrole słowników względem danych przebiegu (np. MPK z kosztów bez stawki na dany rok, numer elementu kosztowego bez
wpisu w Cost Category) – `docs/pipeline-fazy.md`, P2.

---

## 6. Cost Category

- **Globalny** słownik: numer elementu kosztowego z kosztów rzeczywistych (`Cost Element` w raporcie ACTUALS –
  `docs/zrodla-danych.md`, rozdz. 4) → Opis, Obszar, Cost Category. Początkowa zawartość – załącznik A.
- **„Cost Category – zmiany w projekcie”** – opcjonalny słownik projektu; zmienia i dodaje pozycje, ma
  pierwszeństwo przed globalnym. **Słownik efektywny projektu = globalny + zmiany projektu.**
- W zakładce Słowniki projektu tabela pokazuje słownik efektywny: pozycje globalne bez zmiany w projekcie mają stan
  „globalny”. Zmiana takiej linii zapisuje się jako zmiana projektu (słownik globalny bez zmian); usunięcie zmiany
  projektu przywraca pozycję globalną. Pozycji globalnej nie usuwa się w projekcie – tylko na ekranie Słowniki.
- Numer elementu kosztowego z kosztów projektu bez wpisu w słowniku efektywnym naprawia się dodaniem pozycji
  w słowniku projektu.

---

## 7. Wykluczenia

Opcjonalny słownik projektu wskazujący elementy danych źródłowych, które **nie są brane pod uwagę** na
późniejszym etapie analizy. Definiowany jest na etapie projektu; stosowany później (poza bieżącym zakresem – O48).
Wyklucza się po kombinacji trzech pól z raportu kosztów CES (`docs/zrodla-danych.md`, rozdz. 4):

| Pole | Znaczenie |
|---|---|
| `Cost Element` | numer elementu kosztowego |
| `WBS Element` | element WBS CES |
| `Partner object` | obiekt partnera (np. przeksięgowania międzyfirmowe) |
| Opis | powód wykluczenia – **wymagany** |

- Nie trzeba wypełniać wszystkich trzech pól kluczowych: wystarczy jedno, dwa albo komplet. Im mniej pól, tym
  szersze dopasowanie (np. sam `Cost Element` wyklucza wszystkie wiersze z tym elementem kosztowym).
- Wiersz danych jest wykluczony, gdy pasuje do wszystkich **wypełnionych** pól reguły.
- Moment zastosowania (który etap analizy, jak wykazywać wykluczony koszt) – O48.

---

## 8. Otwarte kwestie

| # | Kwestia |
|---|---|
| O9 | Los istniejących tabel słownikowych w `PZLPROD.LOG` (`Stanowiska`, `LearningCurve`, `PeriodDates`, `EmployeesHist`) – do ustalenia z właścicielami |
| O18 | Numeracja tygodni w kalendarzu okresów (ISO czy wewnętrzna) |
| O24 | Kolumna „Cost Category” w słowniku „WP i CAM” ma stałe wartości Labor / Material / Subcontract – czy ma przyjmować kategorie ze słownika Cost Category? |
| O25 | Numer elementu kosztowego bez wpisu w Cost Category: ERROR (dziś) czy WARNING? |
| O37 | Jakie stawki CAS są wykorzystywane i w jakim układzie |
| O48 | Wykluczenia: na którym etapie analizy są stosowane i jak wykazywany jest wykluczony koszt |

## Załącznik A. Początkowa zawartość słownika Cost Category

Pusta kolumna „Obszar” – brak wartości w ustaleniu źródłowym; „(brak)” – pozycja bez Cost Category (WARNING –
rozdz. 5.5). Numery krótsze niż 10 znaków (np. `9221X550`) zapisane jak w źródle.

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
