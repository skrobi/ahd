# PZL-EV – Słowniki

Zakres: słowniki globalne i słowniki projektu – zawartość, zależność od typu projektu, edycja, wymiana przez
Excel i reguły walidacji. Słowniki zawierają wiedzę biznesową potrzebną do przekształcenia danych źródłowych
w dane raportowe.

Powiązane: `docs/model-danych.md` (historia i przypięcie przez przebieg), `docs/funkcjonalnosc.md` (ekran
Słowniki, kreator projektu), `docs/zrodla-danych.md` (struktura P1S).

---

## 1. Zasady

- **Źródłem prawdy jest baza PZL-EV** (MS SQL, schemat `dict`). Edycja w aplikacji: tabela / formularz
  z polami typowanymi (data, liczba, wybór z listy); klucze WBS i numery zawsze jako tekst.
- **Tabela słownika jak arkusz Excela** (ekran Słowniki i zakładka Słowniki projektu – ta sama tabela):
  pisanie zastępuje zawartość komórki, Enter / Tab – następna komórka, Esc – cofnięcie; zaznaczanie komórek,
  Ctrl+C / Ctrl+V – kopiowanie i wklejanie bloku (od bieżącej komórki, brakujące wiersze dopisywane), Delete –
  wyczyszczenie zaznaczonych, Ctrl+D – wypełnienie w dół. Kolumna z listą wartości – lista z możliwością wpisania;
  kolumna powiązana z innym słownikiem (np. CAM – słownik Osoby z HR) – lista z wyszukiwaniem po fragmencie USRID
  albo imienia i nazwiska: zapisywany USRID, wyświetlane imię i nazwisko; wpisany lub wklejony USRID albo
  jednoznaczne imię i nazwisko są rozpoznawane. Komórka z problemem jest podświetlona od razu po wpisaniu (czerwona
  – ERROR: zły typ, pole wymagane, wartość spoza listy; żółta – WARNING: wartość spoza słownika powiązanego), opis
  w podpowiedzi; pełna walidacja (duplikaty, reguły słownika) – przy zapisie. Ctrl+Z / „Cofnij” cofa ostatnie zmiany
  tabeli (wpis, wklejenie, Delete, Ctrl+D, dodanie i usunięcie wierszy); „Usuń wiersze” / Ctrl+minus usuwa wiersze
  zaznaczonych komórek; kliknięcie nagłówka sortuje (liczby według wartości). Komórka w trakcie edycji jest zatwierdzana
  przed zapisem, zmianą słownika i wczytaniem z pliku.
- **Wczytanie z pliku bez cichej utraty danych:** kolumna opcjonalna, której nie ma w pliku, zachowuje wartości słownika
  (ostrzeżenie); kolumna spoza słownika – pominięta z ostrzeżeniem; plik, który usuwa ponad połowę wierszy –
  ostrzeżenie; komunikaty wskazują wiersz pliku jak w Excelu („wiersz N w pliku”); błędy formuł (`#N/A` itd.) – ERROR;
  liczby także w zapisie angielskim (`1,234.56`); kolumna powiązana (CAM) – imię i nazwisko zamieniane na USRID.
  Słowniki „WP i CAM” i „Harmonogram i budżet”: wiersz z samym kluczem (szablon uzupełniony częściowo) jest pomijany.
  Skoroszyt z kilkoma arkuszami bez arkusza słownika – błąd (nie pierwszy arkusz). Plik otwarty w Excelu można wczytać.
- **Pobrany plik / szablon:** format całej kolumny (kolumny tekstowe – tekst, więc Excel nie usuwa zer wiodących i nie
  zamienia kodów na daty także w nowych wierszach), autofiltr, zamrożony nagłówek, lista wyboru dla kolumn z listą
  wartości, lista osób dla CAM (arkusz „Listy” na końcu skoroszytu), opis kolumny w komentarzu nagłówka. Szablon
  słowników projektu dopisuje elementy P1S z zakresu bez WP i WP bez harmonogramu.
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
- **Klucz bez względu na wielkość liter:** „e123” i „E123” to ten sam wiersz – w jednym słowniku to duplikat,
  a wczytanie pliku z inną pisownią zmienia istniejący wiersz (historia zostaje), nie dodaje nowego.
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
| Kursy walut | waluta, `Od` – `Do` (`Do` puste – bezterminowo), `Kurs` (do PLN: ile PLN za jednostkę), `Kurs CAS` (projekty CAS – rozdz. 6.1); wiersz ma Kurs albo Kurs CAS; okresy jednej waluty się nie nakładają (migracja 045 – wcześniej okres RRRR-MM); dane startowe 046 – przybliżone kursy USD 2024-01 … 2026-10 do zastąpienia kursami finansów | waluta + `Od` | Kurs – przeliczenia walut w strukturze projektu (kurs obowiązujący w dniu stanu), P3, P8; Kurs CAS – koszty projektów CAS |
| Cost Category | numer elementu kosztowego → Opis, Obszar, Cost Category, Rozliczeniowy, Rodzaj kosztu (Hours / Materials / Other, puste – Other: podział ACWP na ACWP Hours / ACWP Materials / ACWP Other w strukturze projektu, suma = ACWP; migracja 038 – wstępnie wg Cost Category zawierającego „labor” / „material”; 040 – konta materiałowe 0051105550 PZL Mat Consump, 0051102550 PZL Mat Comsump-SAC, 0051103550 IC Profit Mat-SAC jako Materials), Grupa narzutowa CAS (wybór z „Grup narzutowych CAS” – zapisany identyfikator grupy) (rozdz. 6) | numer elementu kosztowego | walidacja (P2), łączenie źródeł (P3), raport kosztów CES |
| Kategorie narzutu CAS | pula → `Kolejność` kolumny w raporcie, `Rodzaj` (Koszt – TOTAL COST, FCCOM – TOTAL FCCOM), opis (rozdz. 6.1) | pula | raport kosztów CES, formuły, wskaźniki |
| Grupy narzutowe CAS | grupa narzutowa elementu kosztowego → opis (rozdz. 6.1) | grupa | raport kosztów CES, formuły, Cost Category |
| Formuły narzutów CAS | grupa + pula → formuła z klocków; wiersz = pula naliczana w grupie; bez pozycji na liście ekranu Słowniki – edycja tylko w oknie „Formuły narzutów” (rozdz. 6.1) | grupa + pula | raport kosztów CES |
| Wskaźniki narzutu CAS | kategoria (pula) → `Wskaźnik` obowiązujący `Od` – `Do` (zwykle rok; `Do` puste – bezterminowo) (rozdz. 6.1) | `Kategoria` + `Od` | raport kosztów CES |
| Osoby | pracownicy z HR – „Dodaj z HR” (PZLHRPROD `HR.ORG`, osoby z niepustym `USRID`): okno szukania po USRID, PERNR, imieniu i nazwisku, e-mailu, dziale; „Dodaj” przy wyniku odkłada osobę na listę, „Zatwierdź” – podgląd z samymi nowymi osobami (nic nie jest usuwane): USRID (numer znaczka, login), imię i nazwisko, imię, nazwisko, e-mail, MPK (`KOSTL`), dział (`SHORT`, `LONG`), stanowisko (`STEXT`), pion, manager, PERNR | USRID | wybór CAM w strukturze projektu i słowniku „WP i CAM” (zapisywany USRID) |
| Wskaźniki DJK | prefiks grupy stanowisk (IPT, np. `W2`, `W51`) → `Udział DJK` (0–1) – godziny jakości doliczane do godzin TECH / TECH_PON; liczy się najdłuższy pasujący prefiks; dane startowe (migracja 023): W2–W4 10%, W5 15%, W6 20% | grupa stanowisk | Operational EV struktury projektu (`docs/zrodla-danych.md`, rozdz. 6) |
| Parametry produkcji | `Okno produktywności IPT (mies.)` (12), `Statusy materiałów dostarczonych` (DOST, WYD), `Wartość materiałów ÷ (1 + Z_CLO)` (tak); puste słowniki – wartości raportu S70MR (informacja na ekranie projektu) | parametr | odczyt danych produkcyjnych (`sql/pzlprod/produkcja.sql`) |

Definicje źródeł i ich prefiksy są konfiguracją importu – `docs/zrodla-danych.md`, rozdz. 2.

Na ekranie Słowniki przycisk **„Struktura P1S (PZLPROD)”** otwiera drzewo `LOG.WBS` (Z_KAT_ZBIORCZA → Z_KATEGORIA →
Z_OPIS → PROJORG → elementy) z wyszukiwarką – tylko do odczytu, to nie jest słownik (`docs/zrodla-danych.md`, rozdz. 5.3).

---

## 3. Słowniki projektu

| Słownik | Zawartość | Klucz |
|---|---|---|
| WP i CAM | element P1S projektu → WP, CAM, Cost Category (kategoria ze słownika „Kategorie WBS” projektu), MPK (lista MPK ze słownika „Stawki wydziałów” – stawka ETC Cost); powiązanie z nakładką przez `Legacy WBS` (`docs/performance-objectives.md`, rozdz. 4.1) | element P1S |
| Harmonogram i budżet | WP → `BAC HOURS` (godziny), `BAC MATERIAL` (koszt materiałów), `BAC` (budżet kosztowy WP – kwota), Baseline Start, Baseline Koniec (RRRR-MM-DD; w plikach Excel rozpoznawane też nagłówki „Planowany Start/Koniec”) – baseline projektu; Actual Start, Actual Koniec – daty rzeczywiste WP nadpisujące daty z raportu produkcyjnego (puste – data z produkcji; `docs/performance-objectives.md`, rozdz. 4.2); Forecast Start, Forecast Koniec – prognoza; ETC HOURS, ETC MATERIAL – godziny i materiały do poniesienia nadpisujące wartości z produkcji (puste – z produkcji); kwoty (`BAC MATERIAL`, `BAC`, `ETC MATERIAL`) – w walucie projektu, `Waluta` – kod waluty projektu zapisywany automatycznie przy zmianie kwot (puste – waluta projektu; kwoty bez zmian – waluta wiersza zostaje; zmiana kwoty w wierszu w innej walucie – pozostałe kwoty wiersza przeliczone kursem bieżącym z „Kursy walut”, a bez kursu wiersz zostaje w swojej walucie); kod z trzech wielkich liter, inaczej ERROR | WP |
| Kategorie WBS | kategorie elementów WBS indywidualne dla projektu: `Cost Category` (np. Production, Programs), `Opis` – lista wyboru kolumny Cost Category w strukturze projektu i w „WP i CAM”; raport po kategoriach (wiele elementów WBS w jednej kategorii); inne niż globalny słownik Cost Category (numer elementu kosztowego) – **opcjonalny** | Cost Category |
| Cost Category – zmiany w projekcie | zmiany i uzupełnienia słownika globalnego Cost Category dla projektu (rozdz. 6) | numer elementu kosztowego |
| Wykluczenia | elementy pomijane na późniejszym etapie analizy: kombinacja `Cost Element`, `WBS Element`, `Partner object` (co najmniej jedno z trzech) + wymagany opis (rozdz. 7) – **opcjonalny** | kombinacja trzech pól |
| Źródła danych | definicje źródeł (Administracja) importowane dla projektu – „Importuj źródła projektu” importuje tylko ich pliki: `Definicja źródła` (lista aktywnych definicji), `Opis`; definicja nieistniejąca albo nieaktywna – WARNING – **opcjonalny** | Definicja źródła |
| Parametry projektu | `Parametr`, `Wartość`, `Opis`; parametr `Pole kwoty ACTUALS` – **zestaw walutowy ACTUALS** (kwota i jej kolumna waluty) dla ACWP struktury, raportu kosztów, ETC Cost i przebiegu: `ValueObjCrcy` (Object Currency / Value in Obj. Crcy), `ValueRepCur` (Report currency / Val.in rep.cur.) – waluta transakcji (Value TranCurr) nie jest zestawem (kwoty w różnych walutach się nie sumują – decyzja finansów); ustawiany w kreatorze projektu (krok 1), zmiana – zakładka Konfiguracja; waluta projektu – kod z kolumny waluty zestawu w danych ACTUALS projektu (kilka kodów – ostrzeżenie, kwoty w różnych walutach się nie sumują); inna wartość – ERROR; brak wiersza – `ValueObjCrcy` – **opcjonalny** | Parametr |

- Słowniki projektu powstają w kreatorze projektu z plików Excel (szablon z elementami projektu – jeden
  plik z arkuszami albo osobne pliki / CSV) i są dalej utrzymywane w aplikacji (`docs/funkcjonalnosc.md`, F01, F03):
  na ekranie Projekt w zakładce Słowniki projektu (Źródła danych i Parametry projektu – zakładka Konfiguracja, z listą
  definicji źródeł projektu z Administracji tylko do odczytu) – lista słowników i tabela wybranego słownika z edycją
  w komórkach jak na ekranie Słowniki (dodaj / zmień / usuń wiersz, „Zapisz” z walidacją, „Odrzuć zmiany”, filtr)
  oraz pobranie i wczytanie z Excela z podglądem różnic (Cost Category w arkuszu „WP i CAM” – lista kategorii projektu
  w arkuszu „Listy”). „WP i CAM” oraz „Harmonogram i budżet” zmienia się też
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
| CAS Compliance | WP i CAM, Harmonogram i budżet | narzuty CAS – słowniki globalne (rozdz. 6.1): Kategorie, Grupy, Formuły i Wskaźniki narzutu CAS, grupa narzutowa w Cost Category, Kurs CAS w „Kursy walut” – koszty po narzutach (TOTAL COST) | PLN | – |
| Wewnętrzny | WP i CAM, Harmonogram i budżet | – | PLN | – |

Słowniki globalne (rozdz. 2) i „Cost Category – zmiany w projekcie” są pobierane dla każdego typu.
Waluta wyniku według typu albo zestaw walutowy ACTUALS projektu („Parametry projektu”, w nim liczy struktura
projektu) – O51 (`docs/ev-obliczenia.md`, rozdz. 6).
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
| element P1S należy do projektu (kod P1S elementu nakładki – `Legacy WBS` – albo pod nim, także w `LOG.WBS`) i nie należy do innego projektu | ERROR |
| jeden WP na element P1S | ERROR |
| WP bez CAM – zapis dozwolony (najpierw wskazuje się WP, CAM uzupełnia się później); w strukturze brak „WP bez CAM”; gotowość projektu – ERROR (blokuje przebieg) | WARNING |
| CAM wybierany z listy osób; przy wczytaniu z Excela CAM spoza listy osób | – / WARNING |
| Cost Category spoza słownika „Kategorie WBS” projektu (sprawdzane, gdy słownik ma pozycje) | WARNING |

### 5.3 Harmonogram i budżet

| Reguła | Poziom |
|---|---|
| WP z harmonogramu istnieje w słowniku „WP i CAM” projektu | ERROR |
| liczby i daty poprawne | ERROR |
| data rozpoczęcia ≤ data zakończenia (bazowe, planowane, rzeczywiste) | ERROR |
| budżet ≥ 0 | ERROR |
| budżet na elemencie bez WP | WARNING |
| WP bez budżetu (`BAC HOURS`, `BAC MATERIAL` i `BAC` puste) | WARNING |

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
| „Grupa narzutowa CAS” spoza słownika „Grupy narzutowe CAS” albo pusta | uwaga w raporcie kosztów CES (wiersz bez narzutów) |
| „Wskaźniki narzutu CAS”: `Kategoria` (pula) z listy, `Od` wymagane, `Wskaźnik` ≥ 0 (15 cyfr znaczących, jak w Excelu); okresy `Od`–`Do` jednej puli bez nakładania | ERROR |
| „Kategorie narzutu CAS”: nazwa bez „+” i inna niż „Base cost elements”; `Kolejność` i `Rodzaj` wymagane / kolejność powtórzona | ERROR / WARNING |
| „Formuły narzutów CAS”: formuła poprawnie zbudowana (składnik – operator – składnik, nawiasy domknięte); narzut puli zależny od niego samego (wprost albo przez narzuty innych pul grupy) | ERROR |
| usunięcie puli albo grupy używanej w formułach, wskaźnikach albo Cost Category (globalnie lub w projekcie) | ERROR |
| pula albo składnik formuły spoza „Pul narzutu CAS”, brak wskaźnika naliczanej puli | uwaga w raporcie kosztów CES |

### 5.6 Wykluczenia

| Reguła | Poziom |
|---|---|
| co najmniej jedno z pól `Cost Element`, `WBS Element`, `Partner object` wypełnione | ERROR |
| opis wypełniony | ERROR |
| powtórzona kombinacja `Cost Element` + `WBS Element` + `Partner object` | ERROR |

### 5.7 Parametry produkcji

Wydzielenia godzin z elementu P1S (np. Paint) to węzły wirtualne nakładki z kodem `ELEMENT_P1S.NAZWA.CPLGR`, nie słownik –
`docs/performance-objectives.md`, rozdz. 4.2.

| Reguła | Poziom |
|---|---|
| „Wskaźniki DJK”: udział od 0 do 1; prefiks zawarty w innym (W5 i W51) | ERROR / WARNING |
| „Parametry produkcji”: okno 1–120 miesięcy; statusy niepuste; Z_CLO – tak / nie | ERROR |

### 5.8 Kategorie WBS

| Reguła | Poziom |
|---|---|
| `Cost Category` wymagana i unikalna w projekcie (bez względu na wielkość liter) | ERROR |
| zapis podobny do istniejącej kategorii | WARNING |

Kontrole słowników względem danych przebiegu (np. MPK z kosztów bez stawki na dany rok, numer elementu kosztowego bez
wpisu w Cost Category) – `docs/pipeline-fazy.md`, P2.

---

## 6. Cost Category

- **Globalny** słownik: numer elementu kosztowego z kosztów rzeczywistych (`Cost Element` w raporcie ACTUALS –
  `docs/zrodla-danych.md`, rozdz. 4) → Opis, Obszar, Cost Category. Początkowa zawartość – załącznik A.
- **„Rozliczeniowy”** (pole wyboru): cost element rozliczenia SAP (settlement, np. 0091902551 „PZL Invent Cost Set”).
  Wiersze z takim cost elementem (słownik efektywny projektu – zmiana projektu przed globalnym) są pomijane w ACWP
  struktury projektu i w raporcie kosztów CES, bez względu na klasę obiektu partnera (także „Profit analysis” –
  rozliczenie do segmentu rentowności); w przebiegu – oznaczone jako rozliczenie. Inaczej koszty elementu WBS
  rozliczanego co miesiąc sumują się do 0 (`docs/performance-objectives.md`, rozdz. 4.2; migracja 043).
- **„Uzupełnij z ACTUALS”** (ekran Słowniki, Cost Category): dopisuje cost elementy z całego ostatniego importu ACTUALS
  (wszystkie projekty), których w słowniku nie ma – numer i opis z danych; Cost Category i „Rozliczeniowy” uzupełnia
  finansista. Podgląd przed zapisem jak przy wczytaniu z Excela; istniejące pozycje bez zmian, nic nie jest usuwane
  (procedura `CAN_ActualsCostElements`, `docs/model-danych.md`, migracja 018).
- **„Cost Category – zmiany w projekcie”** – opcjonalny słownik projektu; zmienia i dodaje pozycje, ma
  pierwszeństwo przed globalnym. **Słownik efektywny projektu = globalny + zmiany projektu.**
- W zakładce Słowniki projektu tabela pokazuje słownik efektywny: pozycje globalne bez zmiany w projekcie mają stan
  „globalny”. Zmiana takiej linii zapisuje się jako zmiana projektu (słownik globalny bez zmian); usunięcie zmiany
  projektu przywraca pozycję globalną. Pozycji globalnej nie usuwa się w projekcie – tylko na ekranie Słowniki.
- Numer elementu kosztowego z kosztów projektu bez wpisu w słowniku efektywnym naprawia się dodaniem pozycji
  w słowniku projektu.

### 6.1 Narzuty CAS – raport kosztów CES

W projektach CAS na koszty Cost Elementów (także suche koszty – materiały, usługi, podróże) nakładane są narzuty CAS:
te same wskaźniki dla wszystkich projektów, zmienne w czasie. Reguła odwzorowuje arkusz finansów (formuły narzutów
po Cost Elementach dla lat 2025–2026).

- **Powiązania identyfikatorem:** pule i grupy narzutowe są wskazywane identyfikatorem wiersza słownika (RowId), nie
  nazwą – formuły („Pula”), wskaźniki („Kategoria”) i Cost Category („Grupa narzutowa CAS”) zapisują identyfikator, a tabele, historia,
  Excel i raport pokazują bieżącą nazwę. Zmiana nazwy puli albo grupy w jej słowniku jest od razu widoczna wszędzie;
  puli ani grupy używanej gdzie indziej nie można usunąć (ERROR z nazwą słownika, który jej używa). W Excelu kolumny
  powiązane mają nazwy (lista w arkuszu „Listy”) – wczytanie zamienia nazwę na identyfikator.
- **Mapowanie:** kolumna „Grupa narzutowa CAS” słownika Cost Category – globalnie, ze zmianą w projekcie jak pozostałe
  kolumny (słownik efektywny). Grupy nie da się wywnioskować z Cost Category: np. „Engineering labor” to ENG labor
  (ENG Fringe + ENG OH) albo ENG LL labor (praca leasingowana – bez ENG Fringe).
- **Grupy narzutowe CAS** (dane startowe – migracja 034): MFG labor, ENG labor, ENG LL labor, MSV labor, GS labor,
  MTL labor, Direct Materials, Direct Services, Travel, ODC (bez formuł – bez narzutów).
- **Kategorie narzutu CAS** (dane startowe – migracja 034; kolejność kolumn raportu): MFG Fringe, MFG OH, ENG Fringe,
  ENG OH, MTL Fringe, Material Handling (MTL), Miscellaneous Material Pool (MMP), General Services (GS), GS Fringe,
  MSV Fringe, General & Administrative (GA), Corporate Flowdowns (FD) – rodzaj Koszt, sumowane w `TOTAL COST`
  z kosztem bazowym; FCCOM MFG, FCCOM ENG, FCCOM MTL, FCCOM GS, FCCOM G&A – rodzaj FCCOM, w `TOTAL FCCOM`. Nowa pula
  w słowniku to nowa kolumna raportu, kolumna macierzy formuł i klocki edytora – bez zmiany programu.
- **Formuła narzutu** puli w grupie – z klocków: `Base cost elements` (koszt bazowy kolumny – okres albo rok), narzut
  innej puli (w tym samym wierszu raportu), wskaźnik puli (wiersz wskaźników raportu), operatory `+ − × ÷` i nawiasy.
  Program nie zawiera reguł podstaw – dane startowe (migracja 034) odwzorowują formuły arkusza finansów, np. MFG labor:
  MFG OH = (Base + MFG Fringe) × wskaźnik MFG OH; Direct Materials: GS = Material Handling × wskaźnik GS,
  G&A = (Material Handling + GS) × wskaźnik G&A (suche koszty – bez kosztu bazowego).
- **Okno „Formuły narzutów”** (ekran Słowniki, przycisk przy słownikach pul, grup i wskaźników): macierz – wiersze to
  grupy, kolumny to pule; zaznaczone pole = pula naliczana w grupie, podpowiedź pokazuje formułę. Kliknięcie pola
  otwiera **edytor formuły z klocków**: paleta (operatory, koszt, narzuty pul, wskaźniki pul), formuła z klocków
  z kursorem wstawiania – klocek wstawia się kliknięciem albo przeciągnięciem, klocki formuły można przestawiać
  przeciągnięciem i usuwać (×, Backspace, Delete); formuła do czytania i kontrola na bieżąco (operator między
  składnikami, nawiasy, klocek narzutu własnej puli niedozwolony). Pole puste (pula nienaliczana) otwiera
  edytor z pustą formułą – przycisk „Koszt × wskaźnik puli” wstawia formułę podstawową; „Pula nienaliczana
  w grupie” odznacza pole. Zmiany macierzy zapisuje „Zapisz” (historia
  wersji; zapętlenie formuł w grupie – pole czerwone, zapis zablokowany).
- **Wskaźnik kolumny:** kolumna okresu (`Period MM/RRRR`) – wskaźnik obowiązujący w tym miesiącu; kolumna roku –
  w ostatnim miesiącu roku z danymi (bieżący rok – do ostatniego okresu danych). Projekt z kosztami z kilku lat ma
  blok na każdy rok, każdy z wskaźnikami swojego roku. Wskaźnik zmieniony w trakcie roku albo brak wskaźnika
  naliczanej puli – uwaga w raporcie (arkusz „Uwagi”; brak wskaźnika = narzut 0).
- **Raport** (ekran Projekt, zakładka Raporty, „Raport kosztów CES (Excel)” – projekty CAS): wiersze i kolumny kosztu
  raportu kosztów (`docs/model-danych.md`, migracja 013) + kolumna „Grupa narzutowa CAS”, potem blok na każdą kolumnę
  kosztu: pule (kolejność ze słownika), `TOTAL COST …`, `TOTAL FCCOM …`. Narzuty to **formuły Excela** odwołujące się do kosztu wiersza
  i wiersza wskaźników nad nagłówkiem – zmiana wskaźnika albo kosztu w pliku przelicza raport. Arkusz „Wskaźniki” –
  użyte wskaźniki z okresem obowiązywania.
- **Waluta i kurs CAS (reguła tylko dla projektów CAS, decyzja 2026-10-15):** narzuty CAS liczy się samemu, więc koszt
  bazowy jest **zawsze** w PLN z ACTUALS (`Value in Obj. Crcy`), także gdy projekt raportuje w USD – kwot USD z SAP
  (`Val.in rep.cur.`) projekt CAS nie używa. Waluta projektu – zestaw walutowy w „Parametry projektu”: Object currency –
  PLN, bez przeliczenia; Report currency (np. USD) – koszt PLN każdego okresu przeliczony **kursem CAS** („Kursy walut”,
  kolumna Kurs CAS – kurs zakupu waluty) obowiązującym w okresie kosztu (okresy 13–16 – grudzień), potem narzuty. Brak
  kursu CAS dla któregokolwiek okresu z kosztem – błąd z listą okresów (raport się nie tworzy, ACWP pusty). Narzuty
  w raporcie i w koszcie projektu liczy ta sama usługa (`CasCosts`, `CasBurden`): **ACWP projektu CAS = TOTAL COST**
  (koszt + pule rodzaju Koszt, bez pul FCCOM), podział ACWP Hours / Materials / Other według rodzaju kosztu cost elementu
  (narzut idzie za kosztem, od którego jest liczony); przebieg tygodniowy projektu CAS – na kosztach po narzutach
  (`docs/pipeline-fazy.md`, rozdz. 4.1). Dane – procedura `REP_ProjectCasCosts` (migracja 045). ETC, EAC i budżet
  projektu CAS z narzutami i kursem CAS – osobne zadanie (`tasks/F4.7-cas-etc-eac-z-narzutami.md`).

---

## 7. Wykluczenia

Opcjonalny słownik projektu wskazujący elementy danych źródłowych, które **nie są brane pod uwagę** na
późniejszym etapie analizy. Definiowany jest na etapie projektu. Stosowany dziś w raporcie kosztów projektu
(`REP_ProjectCosts`, migracja 013) oraz w ACWP i Actual DLH struktury projektu (`REP_ProjectCostsByElement`, migracja
016); przypięcie i kontrole przebiegu (`RUN_PinBalance`) wykluczeń nie stosują – O48.
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
- Zastosowanie w przebiegu (przypięcie, łączenie źródeł – P3) i sposób wykazania wykluczonego kosztu – O48.

---

## 8. Otwarte kwestie

| # | Kwestia |
|---|---|
| O9 | Los istniejących tabel słownikowych w `PZLPROD.LOG` (`Stanowiska`, `LearningCurve`, `PeriodDates`, `EmployeesHist`) – do ustalenia z właścicielami |
| O18 | Numeracja tygodni w kalendarzu okresów (ISO czy wewnętrzna) |
| O25 | Numer elementu kosztowego bez wpisu w Cost Category: ERROR (dziś) czy WARNING? |
| O37 | Jakie stawki CAS są wykorzystywane i w jakim układzie – narzuty CAS na koszty Cost Elementów: słowniki globalne (rozdz. 6.1); przeliczenie w przebiegu – wariant procesu CAS (`docs/pipeline-fazy.md`, rozdz. 4.1); słownika projektu „Stawki CAS” nie ma – **rozstrzygnięte** |
| O48 | Wykluczenia: stosowanie w przebiegu (przypięcie, P3) i sposób wykazania wykluczonego kosztu (raport kosztów i ACWP struktury już je pomijają – rozdz. 7) |

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
