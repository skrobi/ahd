# PZL-EV – Performance Objectives

Zakres: nakładka **Performance Objectives** – nadrzędna struktura kontraktu po stronie CES, która wyznacza
zakres projektu PZL-EV i oś raportowania wykonania (nie tylko po elemencie kosztowym). Tworzona przy zakładaniu
projektu, edytowalna.

Powiązane: `docs/funkcjonalnosc.md` (F01 – kreator projektu), `docs/zrodla-danych.md` (struktura CES, drzewo P1S), `docs/slowniki.md`
(słownik „WP i CAM” – osobny), `docs/model-danych.md` (encja, przypięcie przez przebieg).

---

## 1. Czym jest Performance Objectives

- **Struktura kontraktu** – hierarchiczne drzewo elementów WBS CES (poziomy 1..n), po którym raportuje się
  wykonanie kontraktu.
- **Wyznacza zakres projektu PZL-EV** – elementy objęte nakładką to elementy projektu. Koszty i zaawansowanie
  są do nich przypisywane na późniejszym etapie (rozdz. 6).
- **Edytowalna** – strukturę SAP można zmieniać i **grupować elementy PSP we własnych, wirtualnych węzłach**
  (węzły raportowe utworzone w aplikacji, bez odpowiednika w SAP).

---

## 2. Kotwica CES i strona P1S

- Kotwicą jest **CES**. Kluczem elementu jest kolumna **`WBS element`** (element PSP CES, np. `4D06WP`,
  `4D06WP.RA`, `4D06WP000001`); `Project definition` to projekt CES (np. `4D06WP`).
- Strona **P1S wchodzi do drzewa jako dodatkowe zadania / informacja**, nie jako osobny zakres
  (`ObjectivesMapping`):
  - kolumna `Legacy WBS` z Excela (np. `AC-CAB.6.38`) – kod P1S węzła,
  - poddrzewa `Legacy WBS` z `LOG.WBS` (PZLPROD, po `PARENT`, odczyt na żywo) – P1S bywa rozbudowane głębiej niż
    `Legacy WBS`; element należy do najbliższego kodu węzła nad nim.

---

## 3. Źródło i edycja

- **Utworzenie przy zakładaniu projektu** (krok „Performance Objectives” – `docs/funkcjonalnosc.md`, F01):
  wczytanie z **Excela** (układ w rozdz. 5) albo budowa ręczna.
- **Edycja w kreatorze:** dodawanie i przenoszenie elementów, tworzenie wirtualnych węzłów grupujących,
  zmiana nazw (rozdz. niżej – elementy w strukturze).
- **Po utworzeniu projektu** (ekran Projekt, zakładka Struktura):
  - „Dołóż z Excela” – kolejny eksport SAP dokłada elementy, których nakładka jeszcze nie ma (np. kolejne
    `Project definition` – projekt PZL-EV „kabina” obejmuje kilkanaście projektów CES, uruchamianych z czasem), pod
    ich rodzica z pliku (istniejący element albo nowy); istniejące węzły, węzły wirtualne i zmiany w aplikacji
    zostają bez zmian;
  - „Edytuj Performance Objectives” – ten sam edytor co w kreatorze (przeciąganie, przesuwanie, poziom wyżej,
    usuwanie, węzły wirtualne, elementy CES); „Wczytaj Excel” w edytorze **podmienia** strukturę strukturą z pliku
    (elementy o tym samym `WBS element` zachowują identyfikator i historię; węzły wirtualne i ręczne zmiany nie są
    przenoszone). Zapis „Zapisz Performance Objectives” tworzy nowe wersje zmienionych węzłów, „Anuluj” odrzuca zmiany.
    Tabela edytora na ekranie projektu sięga do dołu okna (jak tabela struktury); w kreatorze – stała wysokość;
  - w tabeli struktury zmienia się nazwę węzła i `Legacy WBS` (zapis od razu, z historią).
- **Element CES należy do co najwyżej jednej nakładki** (jednego projektu) – element z nakładki innego projektu
  jest odrzucany przy wczytaniu i zapisie (ERROR z kodem projektu, który go ma).
- **Elementy w strukturze:** przenoszenie z poddrzewem (przeciągnięcie na inny węzeł albo na korzeń, zmiana kolejności:
  wyżej / niżej / na początek / na koniec rodzeństwa, poziom wyżej); usunięcie węzła przenosi jego elementy poziom
  wyżej. Level jest przeliczany z głębokości drzewa. W edytorze: wszystkie polecenia na jednym pasku nad tabelą
  (wczytaj Excel, wyczyść, + węzeł wirtualny, + element CES, grupuj, kolejność, usuń, zwiń / rozwiń); nazwa, Element CES
  (bez węzłów wirtualnych) i P1S zmienia się w komórkach tabeli (dwuklik, F2 albo pisanie; Enter zatwierdza, Esc
  anuluje; pusta nazwa – bez zmiany), nowy węzeł zaczyna od edycji nazwy; zwijanie węzłów (▸ / ▾, „Zwiń wszystko”, „Rozwiń
  wszystko”), zaznaczenie kilku wierszy (Ctrl / Shift) i menu prawego przycisku myszy – „Przenieś do” (najwyższy poziom
  albo węzeł wirtualny; zaznaczone trafiają na koniec jego elementów w kolejności drzewa), „Grupuj w nowy węzeł” (nowy
  węzeł wirtualny w miejscu pierwszego zaznaczonego z zaznaczonymi jako elementami), kolejność, zwijanie.
- Nakładka należy do projektu i ma historię zmian (`docs/model-danych.md`, rozdz. 3). Przebieg czyta ją
  w stanie na swój znacznik stanu (`docs/model-danych.md`, rozdz. 4.2).
- Nie jest to automatyczny import RABIT – to ręczny Excel eksportowany z SAP.

---

## 4. Relacja do innych pojęć

| Pojęcie | Relacja |
|---|---|
| Struktura P1S (`LOG.WBS`) | czytana na żywo z PZLPROD; poddrzewa pod `Legacy WBS` węzłów (rozdz. 2) |
| Słownik „WP i CAM” | **osobny** (`docs/slowniki.md`, rozdz. 3); powiązanie z węzłem nakładki przez `Legacy WBS` (rozdz. 4.1) |
| Zakres projektu | wyznaczany przez nakładkę |
| Koszty i zaawansowanie | przypisywane do węzłów nakładki na późniejszym etapie, z plików (rozdz. 6) |

### 4.1 Powiązanie nakładki z WP

- Kod P1S węzła nakładki: `Legacy WBS` (rozdz. 2). Zakres projektu po stronie P1S to kody
  P1S elementów nakładki i elementy `LOG.WBS` pod nimi. Element P1S należy
  do projektu, gdy jest jednym z tych kodów, leży pod nim w `LOG.WBS` (należy do najbliższego kodu nad nim) albo
  jego kod zaczyna się od kodu z kropką (np. `AC-CAB.6.38.01` pod `AC-CAB.6.38`).
- WP ze słownika „WP i CAM” podpina się pod węzeł nakładki, którego kod P1S obejmuje element (najdłuższy pasujący
  kod; przy kilku węzłach z tym kodem – pod najgłębszy). Węzeł wirtualny ma WP swoich elementów; sumy węzła obejmują jego poddrzewo, każdy WP liczony raz.
- Element nakładki, pod którym nie ma żadnego WP, jest brakiem w bazie analitycznej (kreator, krok 5).
- Element CES bez `Legacy WBS` nie ma kodu P1S – WARNING „Strona P1S” w gotowości projektu
  (`docs/funkcjonalnosc.md`, F02); brak PZLPROD – WARNING z powodem (zakres P1S bez poddrzew `LOG.WBS`).

### 4.2 Struktura projektu (ekran Projekt)

Zakładka **Struktura** ekranu Projekt to tabela-drzewo na wzór Deltek Cobra: nakładka połączona z harmonogramem
i budżetem (`StructureBuilder`).

- **Wiersze:** węzły nakładki, a pod każdym – elementy P1S rozwinięte z `LOG.WBS` spod jego kodu (rozdz. 4.1).
  Wiersz węzła pokazuje jego kod P1S (`Legacy WBS`). Rozwinięcie zatrzymuje się na kodzie innego
  węzła (ten element jest pod swoim węzłem), więc każdy element P1S występuje raz. Element ze słownika „WP i CAM”,
  którego nie ma w `LOG.WBS` (np. bez PZLPROD), jest pod wierszem o najdłuższym pasującym kodzie, a bez niego –
  w grupie „Elementy P1S spoza struktury”.
- **Kolumny w grupach** (`StructureColumns`) – jedna struktura WBS dla perspektywy operacyjnej (godziny) i finansowej
  (koszty); grupy zwijane jak grupowanie kolumn w Excelu („−” nad pierwszą kolumną grupy, zwinięta grupa – kolumna
  „+ Grupa”); Schedule, Operational EV i Financial EV domyślnie zwinięte. Opis każdej kolumny – podpowiedź nagłówka
  i okno „Skróty i opis kolumn” (przycisk „?” nad tabelą). Kolumny z danych (PZLPROD, ACTUALS, wyliczone) – tylko do odczytu (nagłówek kursywą).

  | Grupa | Kolumny |
  |---|---|
  | WBS Attributes | WBS Name (drzewo, zamrożona; poziom – wcięcie), CES Element i Legacy Element (P1S) (domyślnie schowane – „+” w nagłówku WBS Name), WP (checkbox), CAM (lista osób), Cost Category (lista kategorii projektu) |
  | Schedule | Baseline Start, Baseline Finish (RRRR-MM-DD, „Harmonogram i budżet”); Actual Start, Actual Finish – data wpisana dla WP w „Harmonogram i budżet” (Actual Start / Actual Koniec), a bez niej z produkcji: start – najwcześniejszy `GSTRI` zleceń, koniec – najpóźniejszy `LTRMI`, gdy wszystkie zlecenia zamknięte (inaczej puste); element wirtualny – `DATA_REAL` operacji |
  | Operational EV | TECH Hours (AHD: TECH ÷ produktywność IPT z 12 mies. + DJK), PV Hours (TECH Hours WP rozłożone liniowo na dni robocze pn–pt baseline, stan na dziś), EV Hours (TECH_PON ÷ produktywność + DJK), AC Hours (CATS) |
  | Materials | BAC Material („Harmonogram i budżet”), Actual Material (vAPD: dostarczone / wydane, USD bez narzutu Z_CLO) |
  | Financial EV | BAC Hours baseline i BAC Cost („Harmonogram i budżet”), PV Cost (BAC Cost × udział dni roboczych baseline), EV Cost (BAC Cost × EV Hours ÷ TECH Hours, najwyżej BAC Cost), ACWP, Actual DLH |

  **Wiersz z własnym WP** pokazuje ten WP: budżet i daty (edytowalne, także gdy pod nim są inne WP), godziny z PZLPROD
  przypisane do WP oraz PV / EV. Godziny elementu trafiają do najbliższego WP w górę drzewa (własnego albo przodka) – WP
  zagnieżdżony (np. wydzielenie z własnym CAM) ma swoje godziny, WP nadrzędny („spinający”) – resztę, bez podwójnego
  liczenia. **Wiersz bez własnego WP** – sumy WP i elementów poddrzewa (każdy element raz; kilka węzłów z tym samym kodem –
  w najgłębszym).
  **Wydzielenia godzin** (np. malowanie, kontrola, obróbka z jednego elementu P1S): węzeł wirtualny nakładki (edycja
  Performance Objectives – „+ węzeł wirtualny”, albo kolumna Legacy Element w wierszu węzła) z kodem P1S
  `ELEMENT_P1S.NAZWA.CPLGR`, np. `AC-R39.1.18.03.25.PAINT.W20`, `AC-R39.1.18.03.25.QUALITY.DJ`,
  `ELEMENT.MACHINING.W30` (nazwa węzła np. „Produkcja CNC”). Element – najdłuższy prefiks kodu, który jest w `LOG.WBS`;
  za nim nazwa i CPLGR (grupa stanowisk, prefiks – `W5` obejmuje W51, W52…; kilka wydzieleń jednego elementu – najdłuższy
  pasujący CPLGR). Operacje `vAHDD` elementu z tą grupą stanowisk (z DJK stanowiska) należą do węzła i są odejmowane od
  elementu – reszta zostaje na elemencie. Węzeł ma WP, CAM, Cost Category, budżet i daty jak element P1S; materiały i ACWP
  zostają na elemencie. SWBS nie jest brany pod uwagę. Kod niezgodny z konwencją – brak „kod wydzielenia” w kolumnie Braki,
  powód w podpowiedzi nazwy i w nagłówku „Produkcja (PZLPROD)”. Godziny wydzielane są tylko z operacji samego elementu
  (PSPNR) – operacje elementów pod nim w `LOG.WBS` wydziela się kodem z ich elementem.
  Dane produkcyjne są czytane na żywo (`IPzlProdSource.Production`, filtr – zakres P1S projektu) przy
  otwarciu projektu, „Odśwież P1S i koszty” i po zmianie Legacy WBS; chwila odczytu – w nagłówku ekranu
  („Produkcja (PZLPROD) …”). Kolumny mają stałą szerokość – przy kolejnych kolumnach tabela przewija się w poziomie. Tabela kończy się na dole okna (wysokość
  dopasowana do okna przy każdej zmianie jego rozmiaru, bez górnego limitu; strona bez przewijania – wiersze przewija
  tabela; przy zwiniętym menu – szersza); skróty klawiszowe i opis kolumn – okno „?”; nad tabelą daty danych RABIT
  i produkcji – osobna linia pod nagłówkiem projektu. Komunikaty (operacja w toku – „Trwa…”, wynik zapisu, wczytania,
  wklejenia) – jedna linia o stałej wysokości pod datami danych (pełna treść w podpowiedzi); pokazanie i ukrycie
  komunikatu nie przesuwa zakładek ani tabeli.
- **WP** to znacznik elementu P1S (wiersz z kodem P1S), który wskażą finansiści: zaznaczony element jest pakietem pracy
  i ma mieć koszty i budżet. Kodem nowego WP jest kod elementu P1S (WP zapisany już w słowniku zachowuje swój
  kod). WP nie jest osobnym poziomem drzewa.
- **CAM** wybiera się z listy osób z wyszukiwaniem po fragmencie USRID albo imienia i nazwiska (słownik Osoby wczytywany
  z HR – `docs/slowniki.md`, rozdz. 2; ta sama lista co w tabeli słownika): zapisywany jest USRID, wyświetlane imię
  i nazwisko. CAM zapisany spoza słownika jest na liście pod swoją wartością.
- **Cost Category** – kategoria WP jako rodzaj kosztu (np. Production, Programs), żeby raportować nie tylko po elementach
  WBS, ale i po kategoriach (wiele WP w jednej kategorii). Wybiera się ją z listy słownika projektu „Kategorie WBS”
  (`docs/slowniki.md`, rozdz. 3 – edycja w zakładce Słowniki projektu); zapisywana w „WP i CAM” przy WP elementu (wybór
  kategorii w wierszu bez WP zaznacza WP, odznaczenie WP ją usuwa). To nie jest globalny słownik Cost Category (numer
  elementu kosztowego). Kategoria zapisana spoza słownika (np. Labor / Material / Subcontract) jest na liście
  pod swoją wartością; zapis takiej kategorii – WARNING.
- **Sumy:** budżet i daty wiersza obejmują poddrzewo, każdy WP liczony raz; braki – element nakładki bez WP,
  WP bez budżetu.
- **ACWP** (tylko do odczytu): koszt rzeczywisty narastająco z ostatniego importu ACTUALS (pole kwoty ze słownika
  „Parametry projektu”, domyślnie waluta obiektu – PLN; bez wykluczeń
  projektu i bez rozliczenia wychodzącego – cost elementy oznaczone „Rozliczeniowy” w słowniku Cost Category,
  klasa obiektu partnera inna niż „Profit analysis”; procedura `REP_ProjectCostsByElement`, `docs/model-danych.md`,
  migracje 016–017). Bez oznaczenia cost elementów rozliczeniowych element WBS rozliczany co miesiąc ma ACWP 0. Koszt elementu CES trafia
  do WP na kodzie P1S jego węzła, a bez niego – do jedynego WP pod węzłem. Brak WP („koszt bez WP”) albo kilka WP pod
  węzłem („koszt niejednoznaczny” – reguła rozdziału to O45) – koszt bez przypisania, w kolumnie Braki. Wiersz węzła
  pokazuje koszt elementów CES swojego poddrzewa, wiersz elementu P1S – koszt przypisany do WP poddrzewa. Koszt
  elementu CES projektu, którego nie ma w nakładce – „spoza nakładki”. Kafelki ACWP i „Koszt bez WP” w zakładce
  Wskaźniki. Koszty są czytane przy otwarciu projektu i „Odśwież P1S i koszty” (nie po każdym zapisie wiersza).
  **Z kiedy są dane** – w nagłówku ekranu projektu: „Dane RABIT z … · import … · sprawdzone …” – data raportu w RABIT
  (modyfikacja pliku ACTUALS), data importu i ostatniego pobrania z RABIT; podpowiedź – każdy plik osobno. Ponowne
  pobranie raportu z tą samą treścią (import „pominięty” / „duplikat”) nie zmienia danych, ale przesuwa datę „dane z” na
  nowszy raport – wiadomo, że dane są aktualne na tę datę (`CAN_LatestFiles` + `META_SourceFileSeen`).
  To podgląd – formalny koszt WP liczy przebieg (P3, `ev.KosztWP`).
- **Daty rzeczywiste WP** (Actual Start, Actual Finish): data z raportu produkcyjnego (vAHDD) może zostać nadpisana –
  WP bez danych produkcyjnych (daty podaje CAM) albo prace zakończone, choć jedna operacja nie jest zamknięta (wyrobu nie
  ma już w zakładzie). Nadpisanie zapisuje się dla WP w „Harmonogram i budżet” (Actual Start / Actual Koniec); pusta
  komórka – data z produkcji (wyczyszczenie komórki przywraca datę z produkcji). Data z produkcji jest w tabeli wyszarzona,
  wpisana – zwykłym kolorem. Wiersz z WP w poddrzewie pokazuje daty WP: start najwcześniejszy, koniec najpóźniejszy, gdy
  wszystkie WP zakończone (wpisany start bez końca i bez daty końca z produkcji – WP trwa); wiersz bez WP – daty
  z produkcji poddrzewa.
- **Actual DLH** (tylko do odczytu): godziny rzeczywiste narastająco z ostatniego importu ACTUALS – kolumna „Total
  Quantity” (pole parsera `TotalQuantity`), te same wiersze (bez wykluczeń i rozliczenia wychodzącego) i to samo
  przypisanie do WP co ACWP (procedura `REP_ProjectCostsByElement` z polem godzin). Godziny elementu bez WP zostają
  na węźle nakładki (bez osobnego braku). Brak pola godzin w parserze – kolumna pusta, ACWP bez zmian.
- **Pasek nad tabelą** – ikony z opisem w podpowiedzi: edycja Performance Objectives, dołożenie z Excela (eksport SAP),
  pobranie struktury do Excela, wczytanie struktury z Excela, rozwiń / zwiń wszystko, odświeżenie P1S i kosztów, cofnięcie wklejenia, odrzucenie
  niezapisanych zmian.
- **Struktura do Excela** – arkusz „Struktura”: całe drzewo (także zwinięte wiersze) w kolejności tabeli, nazwa wcięta
  według poziomu, kolumny Poziom, Rodzaj, Element CES, P1S, WP, CAM (USRID oraz imię i nazwisko), Cost Category, WP
  w poddrzewie, BAC HOURS, BAC MATERIAL, BAC, Baseline Start, Baseline Koniec, ACWP, Actual DLH, Braki, Uwagi (wartości jak w tabeli –
  sumy poddrzewa). Eksportowany jest stan zapisany; przy niezapisanych zmianach – najpierw zapis albo „Odrzuć niezapisane”.
  Kolumny do uzupełnienia mają żółty nagłówek z opisem w komentarzu: WP, CAM (lista osób w arkuszu „Listy”), Cost
  Category, Baseline Start, Baseline Finish, Actual Start, Actual Finish, BAC Hours baseline, BAC Material, BAC Cost.
- **Struktura z Excela** – „Wczytaj strukturę z Excela”: arkusz „Struktura” pobrany z aplikacji i uzupełniony ręcznie.
  Wiersz pliku → wiersz struktury po `Legacy Element (P1S)`; czytane są tylko kolumny do uzupełnienia: WP (niepusta
  komórka – element jest WP, kod WP = kod elementu), CAM (USRID), Cost Category, a w wierszu z WP (własnym albo
  zaznaczonym w pliku) – budżet i daty WP (Actual Start / Finish równe dacie z produkcji – bez nadpisania). Pusta komórka – bez zmiany (czyszczenie – w tabeli); pozostałe kolumny (sumy
  poddrzewa, AHD, ACTUALS) są pomijane. Pomijane z ostrzeżeniem: element P1S spoza struktury, budżet w wierszu bez WP;
  z błędem: niepoprawna liczba lub data, ten sam WP z innym budżetem w dwóch wierszach. Przed zapisem – podsumowanie
  (nowe WP, CAM, Cost Category, budżet i daty WP, pominięte) do potwierdzenia; zapis jak edycja w komórkach (jeden zapis
  na słownik, wiersz z błędem nie blokuje pozostałych).
- **Edycja w komórkach** (zapis od razu po zatwierdzeniu wiersza – Enter albo przejście do innego wiersza, bez
  osobnego „Zapisz”; Esc cofa): nazwa i `Legacy WBS` węzła → nakładka; WP, CAM i Cost Category wiersza z kodem P1S → „WP i CAM”
  (klucz – kod P1S; zaznaczenie WP albo wybór CAM tworzy przypisanie, odznaczenie WP je usuwa razem z budżetem WP, jeśli
  WP nie jest przypisany do innego elementu; WP można zapisać bez CAM – brak „WP bez CAM”, CAM uzupełnia się później,
  przebieg blokuje gotowość projektu);
  BAC HOURS, BAC MATERIAL, BAC, Baseline Start, Baseline Koniec, Actual Start, Actual Koniec wiersza z jednym WP w poddrzewie – własnym albo jedynym
  pod nim (np. węzeł nakładki nad elementem P1S z WP) → „Harmonogram i budżet” tego WP (klucz – WP); wiersz z kilkoma WP
  w poddrzewie pokazuje sumę i nie jest edytowalny. Walidacja jak przy zapisie słownika: ERROR blokuje zapis (zmiany zostają w wierszu, komunikat
  w linii komunikatów), WARNING nie wstrzymuje. Błąd, który słownik ma już w innym wierszu (np. element poza zakresem
  po odświeżeniu P1S), nie blokuje zmiany – jest ostrzeżeniem. Zmiana `Legacy WBS` razem z WP / CAM w jednym
  wierszu przypisuje WP do nowego kodu P1S. Kolumny zablokowane do edycji – lista `StructureEdits.Locked` (do ustalenia).
- **Jak w Excelu:** zaznaczanie komórek, pisanie zastępuje zawartość komórki, Ctrl+C / Ctrl+V – kopiowanie i wklejanie
  bloku od bieżącej komórki (jedna komórka wypełnia zaznaczenie, wiersz nagłówków pomijany, komórki, których w danym
  wierszu nie można zmienić, pomijane – liczba pod tabelą), Delete – wyczyszczenie (bez WP), Ctrl+D – wypełnienie w dół,
  WP z CAM i budżetem można wkleić za jednym razem: w wierszu, w którym WP jest właśnie zaznaczany (bez innych WP
  w poddrzewie), budżet i daty są od razu edytowalne i zapisują się pod nowym WP. Ctrl+Z / „Cofnij wklejenie” –
  cofnięcie ostatniego wklejenia, wyczyszczenia albo wypełnienia (do 20 kroków; komórki
  dostają wartości sprzed operacji i wiersze są ponownie zapisywane), Alt+→ / Alt+← – rozwinięcie / zwinięcie wiersza. Komórka liczby / daty z błędem jest podświetlona od razu po wpisaniu.
  **Zapis wsadowy:** wiersze zatwierdzone w krótkim czasie (kilka kliknięć WP w ciągu 0,3 s, Enter w kolejnych wierszach,
  wklejenie bloku, Delete, Ctrl+D, Ctrl+Z) idą jednym zapisem na słownik – „WP i CAM” i „Harmonogram i budżet” wczytane
  raz, zmiany wszystkich wierszy nakładane po kolei, jedna transakcja i jeden wpis w dzienniku na słownik
  (`ProjectService.SaveStructureEdits`); nazwa i `Legacy WBS` – wiersz po wierszu. Paczka z błędem (ERROR) albo konfliktem
  jest zapisywana wiersz po wierszu – poprawne wiersze się zapisują, błędne zostają żółte z komunikatem. Po serii – jedno
  odświeżenie danych projektu (każdy słownik czytany raz; osoby, nakładki innych projektów, foldery i koszty – z pamięci
  ekranu, ponownie przy „Odśwież P1S i koszty”). Pomiar (SQL Server lokalnie, 4 wiersze): zapis – 11 zapytań,
  odświeżenie – 6 zapytań; liczba zapytań zapisu nie rośnie z liczbą wierszy. Edycja w toku jest zatwierdzana przy
  wyjściu z tabeli (przycisk, inna zakładka). Wiersz z niezapisanymi zmianami (zapis w toku albo nieudany) jest żółty
  i zachowuje zmiany po odświeżeniu; powrót do listy i edycja Performance Objectives czekają na zapis. Odznaczenie WP
  z budżetem lub datami wymaga potwierdzenia (usuwa harmonogram WP).

---

## 5. Układ Excela

Eksport struktury WBS z SAP (format `.xlsx`), kolumny:

`Project definition | Level | WBS element | Name | Person responsible | Applicant | Profit center |
Costing Sheet | Results Analysis Key | Statistical | Open Date | Legacy # | Cross reference | Status |
Acct asst elem.ind. | Created by | Company code | SAC Obj Number | Legacy WBS | Performance Obligation`

Kolumny istotne dla nakładki:

| Kolumna | Znaczenie |
|---|---|
| `Project definition` | projekt CES – kotwica (np. `4D06WP`) |
| `Level` | poziom w hierarchii (1 = korzeń); buduje drzewo |
| `WBS element` | element PSP CES – **klucz** (np. `4D06WP.RA`, `4D06WP000001`) |
| `Name` | nazwa elementu / zadania |
| `Person responsible` | osoba odpowiedzialna |
| `Profit center` | centrum zysku (np. `PIDCABIN`) |
| `Legacy WBS` | odpowiednik P1S – dodatkowe zadanie / informacja w drzewie (np. `AC-CAB.6.38`) |
| `Performance Obligation` | identyfikator zobowiązania wykonania (np. `SAC-PZLCABMY10-00001`) |
| `SAC Obj Number` | numer obiektu SAC (np. `6156`) |
| `Statistical`, `Acct asst elem.ind.` | znaczniki elementu: statystyczny / rozliczeniowy (`X`) |

Pozostałe kolumny (`Applicant`, `Costing Sheet`, `Results Analysis Key`, `Open Date`, `Legacy #`,
`Cross reference`, `Status`, `Created by`, `Company code`) są wczytywane jako atrybuty opisowe elementu.

---

## 6. Poza bieżącym zakresem

Przypisywanie kosztów rzeczywistych i zaawansowania do węzłów nakładki (budowa gotowego raportu wykonania)
to **późniejszy etap** – dane będą przypisywane z plików (O45). Ten dokument opisuje nakładkę na etapie
tworzenia i utrzymania projektu.

---

## 7. Otwarte kwestie

| # | Kwestia |
|---|---|
| O45 | Przypisywanie kosztów i zaawansowania do węzłów nakładki (z plików) – późniejszy etap |
