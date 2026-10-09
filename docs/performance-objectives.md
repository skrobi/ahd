# PZL-EV – Performance Objectives

Zakres: nakładka **Performance Objectives** – nadrzędna struktura kontraktu po stronie CES, która wyznacza
zakres projektu PZL-EV i oś raportowania wykonania (nie tylko po elemencie kosztowym). Tworzona przy zakładaniu
projektu, edytowalna.

Powiązane: `docs/funkcjonalnosc.md` (F01 – kreator projektu), `docs/mapowanie-ces-p1s.md` (globalne mapowanie,
czytane przy budowie nakładki), `docs/zrodla-danych.md` (struktura CES, drzewo P1S), `docs/slowniki.md`
(słownik „WP i CAM” – osobny), `docs/model-danych.md` (encja, przypięcie przez przebieg).

---

## 1. Czym jest Performance Objectives

- **Struktura kontraktu** – hierarchiczne drzewo elementów WBS CES (poziomy 1..n), po którym raportuje się
  wykonanie kontraktu. Zastępuje wcześniejszy wybór zakresu z drzewa P1S.
- **Wyznacza zakres projektu PZL-EV** – elementy objęte nakładką to elementy projektu. Koszty i zaawansowanie
  są do nich przypisywane na późniejszym etapie (rozdz. 6).
- **Edytowalna** – strukturę SAP można zmieniać i **grupować elementy PSP we własnych, wirtualnych węzłach**
  (węzły raportowe utworzone w aplikacji, bez odpowiednika w SAP).

---

## 2. Kotwica CES i powiązanie z P1S

- Kotwicą jest **CES**. Kluczem elementu jest kolumna **`WBS element`** (element PSP CES, np. `4D06WP`,
  `4D06WP.RA`, `4D06WP000001`); `Project definition` to projekt CES (np. `4D06WP`).
- Wskazanie elementu CES **pociąga całe jego poddrzewo z globalnego mapowania** (`docs/mapowanie-ces-p1s.md`).
  Strona **P1S wchodzi do drzewa jako dodatkowe zadania / informacja**, nie jako osobny zakres:
  - cel P1S z mapowania – element CES nakładki rozstrzygany tymi samymi regułami co ekran Mapowanie (korekta
    elementu → raport → dziedziczenie z projektu CES; projekt CES elementu z kolumny `Project definition`, a dla
    elementu dodanego ręcznie – najbliższego elementu nad nim) – status (`REPORT`, `OVERRIDE`, `INHERITED`, `UNMAPPED`) i cel przy elemencie,
  - kolumna `Legacy WBS` z Excela (np. `AC-CAB.6.38`),
  - poddrzewa celu i `Legacy WBS` z `LOG.WBS` (PZLPROD, po `PARENT`) – P1S bywa rozbudowane głębiej niż `Legacy WBS`;
    element należy do najbliższego kodu węzła nad nim (decyzja 2026-10-08: mapowanie zostaje obok `Legacy WBS`,
    dopóki nie okaże się, że sam `Legacy WBS` wystarcza).
- Mapowanie CES ↔ P1S jest **globalne i administracyjne** – budowa nakładki tylko je **czyta**; nakładka nie
  zmienia mapowania, a mapowanie nie zależy od projektu.

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
    przenoszone). Zapis „Zapisz Performance Objectives” tworzy nowe wersje zmienionych węzłów, „Anuluj” odrzuca zmiany;
  - w tabeli struktury zmienia się nazwę węzła i `Legacy WBS` (zapis od razu, z historią).
- **Element CES należy do co najwyżej jednej nakładki** (jednego projektu) – element z nakładki innego projektu
  jest odrzucany przy wczytaniu i zapisie (ERROR z kodem projektu, który go ma).
- **Elementy w strukturze:** przenoszenie z poddrzewem (przeciągnięcie na inny węzeł albo na korzeń, zmiana kolejności,
  poziom wyżej); usunięcie węzła przenosi jego elementy poziom wyżej. Level jest przeliczany z głębokości drzewa.
- Nakładka należy do projektu i ma historię zmian (`docs/model-danych.md`, rozdz. 3). Przebieg czyta ją
  w stanie na swój znacznik stanu (`docs/model-danych.md`, rozdz. 4.2).
- Nie jest to automatyczny import RABIT – to ręczny Excel eksportowany z SAP.

---

## 4. Relacja do innych pojęć

| Pojęcie | Relacja |
|---|---|
| Mapowanie CES ↔ P1S | globalne; czytane przy budowie nakładki, bez wpływu na projekt (rozdz. 2) |
| Słownik „WP i CAM” | **osobny** (`docs/slowniki.md`, rozdz. 3); powiązanie z węzłem nakładki przez `Legacy WBS` (rozdz. 4.1) |
| Zakres projektu | wyznaczany przez nakładkę (zastępuje drzewo P1S jako mechanizm zakresu) |
| Koszty i zaawansowanie | przypisywane do węzłów nakładki na późniejszym etapie, z plików (rozdz. 6) |

### 4.1 Powiązanie nakładki z WP

- Kody P1S węzła nakładki: `Legacy WBS` i cel z mapowania (rozdz. 2). Zakres projektu po stronie P1S to kody
  P1S elementów nakładki i elementy `LOG.WBS` pod nimi (pod celami mapowania i pod `Legacy WBS`). Element P1S należy
  do projektu, gdy jest jednym z tych kodów, leży pod nim w `LOG.WBS` (należy do najbliższego kodu nad nim) albo
  jego kod zaczyna się od kodu z kropką (np. `AC-CAB.6.38.01` pod `AC-CAB.6.38`).
- WP ze słownika „WP i CAM” podpina się pod węzeł nakładki, którego kod P1S obejmuje element (najdłuższy pasujący
  kod; przy kilku węzłach z tym kodem – pod najgłębszy). Węzeł wirtualny ma WP swoich elementów; sumy węzła obejmują jego poddrzewo, każdy WP liczony raz.
- Element nakładki, pod którym nie ma żadnego WP, jest brakiem w bazie analitycznej (kreator, krok 5).
- Element CES bez celu mapowania (`UNMAPPED`) i bez `Legacy WBS` nie ma kodu P1S – WARNING w gotowości projektu
  (`docs/funkcjonalnosc.md`, F02); brak raportu mapowań albo PZLPROD – WARNING z powodem.

### 4.2 Struktura projektu (ekran Projekt)

Zakładka **Struktura** ekranu Projekt to tabela-drzewo na wzór Deltek Cobra: nakładka połączona z harmonogramem
i budżetem (`StructureBuilder`).

- **Wiersze:** węzły nakładki, a pod każdym – elementy P1S rozwinięte z `LOG.WBS` spod jego kodu (rozdz. 4.1).
  Wiersz węzła pokazuje pierwszy kod P1S (`Legacy WBS`, a bez niego cel mapowania); cel mapowania inny niż
  `Legacy WBS` to osobny wiersz „cel mapowania CES ↔ P1S” pod węzłem. Rozwinięcie zatrzymuje się na kodzie innego
  węzła (ten element jest pod swoim węzłem), więc każdy element P1S występuje raz. Element ze słownika „WP i CAM”,
  którego nie ma w `LOG.WBS` (np. bez PZLPROD), jest pod wierszem o najdłuższym pasującym kodzie, a bez niego –
  w grupie „Elementy P1S spoza struktury”.
- **Kolumny w grupach** (`StructureColumns`) – jedna struktura WBS dla perspektywy operacyjnej (godziny) i finansowej
  (koszty); grupy zwijane jak grupowanie kolumn w Excelu („−” nad pierwszą kolumną grupy, zwinięta grupa – kolumna
  „+ Grupa”); Schedule, Operational EV i Financial EV domyślnie zwinięte. Opis każdej kolumny – podpowiedź nagłówka
  i „Opis kolumn” pod tabelą. Kolumny z danych (PZLPROD, ACTUALS, wyliczone) – tylko do odczytu (nagłówek kursywą).

  | Grupa | Kolumny |
  |---|---|
  | WBS Attributes | WBS Name (drzewo, zamrożona; poziom – wcięcie), CES Element i Legacy Element (P1S) (domyślnie schowane – „+” w nagłówku WBS Name), WP (checkbox), CAM (lista osób), Cost Category (lista kategorii projektu) |
  | Schedule | Baseline Start, Baseline Finish (RRRR-MM-DD, „Harmonogram i budżet”); Actual Start, Actual Finish – z godzin AHD (pierwsza data CATS, data EV = BAC); kolumny dat `vAHDD` do ustalenia (O10) – na razie puste |
  | Operational EV | BAC Hours (AHD: TECH ÷ produktywność IPT z 12 mies. + DJK), PV Hours (BAC Hours AHD WP rozłożone liniowo na dni robocze pn–pt baseline, stan na dziś), EV Hours (TECH_PON ÷ produktywność + DJK), AC Hours (CATS) |
  | Materials | BAC Material („Harmonogram i budżet”), Actual Material (vAPD: dostarczone / wydane, USD bez narzutu Z_CLO) |
  | Financial EV | BAC Hours baseline i BAC Cost („Harmonogram i budżet”), PV Cost (BAC Cost × udział dni roboczych baseline), EV Cost (BAC Cost × EV Hours ÷ BAC Hours AHD, najwyżej BAC Cost), ACWP |

  Wartości z PZLPROD to suma elementów P1S poddrzewa wiersza (każdy element raz); PV i EV kosztowe – suma WP poddrzewa.
  Dane produkcyjne są czytane na żywo (`IPzlProdSource.Production`, zakres P1S projektu zamiast filtra programu) przy
  otwarciu projektu, „Odśwież mapowanie i koszty” i po zmianie Legacy WBS; chwila odczytu – w nagłówku ekranu
  („Produkcja (PZLPROD) …”). Kolumny mają stałą szerokość – przy kolejnych kolumnach tabela przewija się w poziomie.
- **WP** to znacznik elementu P1S (wiersz z kodem P1S), który wskażą finansiści: zaznaczony element jest pakietem pracy
  i ma mieć koszty i budżet. Kodem nowego WP jest kod elementu P1S (WP z wcześniej wczytanego słownika zachowuje swój
  kod). WP nie jest osobnym poziomem drzewa.
- **CAM** wybiera się z listy osób z wyszukiwaniem po fragmencie USRID albo imienia i nazwiska (słownik Osoby wczytywany
  z HR – `docs/slowniki.md`, rozdz. 2; ta sama lista co w tabeli słownika): zapisywany jest USRID, wyświetlane imię
  i nazwisko. CAM wpisany wcześniej spoza słownika jest na liście pod swoją wartością.
- **Cost Category** – kategoria WP jako rodzaj kosztu (np. Production, Programs), żeby raportować nie tylko po elementach
  WBS, ale i po kategoriach (wiele WP w jednej kategorii). Wybiera się ją z listy słownika projektu „Kategorie WBS”
  (`docs/slowniki.md`, rozdz. 3 – edycja w zakładce Słowniki projektu); zapisywana w „WP i CAM” przy WP elementu (wybór
  kategorii w wierszu bez WP zaznacza WP, odznaczenie WP ją usuwa). To nie jest globalny słownik Cost Category (numer
  elementu kosztowego). Kategoria wpisana wcześniej spoza słownika (np. Labor / Material / Subcontract) jest na liście
  pod swoją wartością; zapis takiej kategorii – WARNING.
- **Sumy:** budżet i daty wiersza obejmują poddrzewo, każdy WP liczony raz; braki – element nakładki bez WP,
  WP bez budżetu.
- **ACWP** (tylko do odczytu): koszt rzeczywisty narastająco z ostatniego importu ACTUALS (PLN, bez wykluczeń
  projektu i bez rozliczenia wychodzącego – cost elementy oznaczone „Rozliczeniowy” w słowniku Cost Category,
  klasa obiektu partnera inna niż „Profit analysis”; procedura `REP_ProjectCostsByElement`, `docs/model-danych.md`,
  migracje 016–017). Bez oznaczenia cost elementów rozliczeniowych element WBS rozliczany co miesiąc ma ACWP 0. Koszt elementu CES trafia
  do WP na kodzie P1S jego węzła, a bez niego – do jedynego WP pod węzłem. Brak WP („koszt bez WP”) albo kilka WP pod
  węzłem („koszt niejednoznaczny” – reguła rozdziału to O45) – koszt bez przypisania, w kolumnie Braki. Wiersz węzła
  pokazuje koszt elementów CES swojego poddrzewa, wiersz elementu P1S – koszt przypisany do WP poddrzewa. Koszt
  elementu CES projektu, którego nie ma w nakładce – „spoza nakładki”. Kafelki ACWP i „Koszt bez WP” w zakładce
  Wskaźniki. Koszty są czytane przy otwarciu projektu i „Odśwież mapowanie i koszty” (nie po każdym zapisie wiersza).
  **Z kiedy są dane** – w nagłówku ekranu projektu: „Dane RABIT z … · import … · sprawdzone …” – data raportu w RABIT
  (modyfikacja pliku ACTUALS), data importu i ostatniego pobrania z RABIT; podpowiedź – każdy plik osobno. Ponowne
  pobranie raportu z tą samą treścią (import „pominięty” / „duplikat”) nie zmienia danych, ale przesuwa datę „dane z” na
  nowszy raport – wiadomo, że dane są aktualne na tę datę (`CAN_LatestFiles` + `META_SourceFileSeen`).
  To podgląd – formalny koszt WP liczy przebieg (P3, `ev.KosztWP`).
- **Pasek nad tabelą** – ikony z opisem w podpowiedzi: edycja Performance Objectives, dołożenie z Excela (eksport SAP),
  pobranie struktury do Excela, rozwiń / zwiń wszystko, odświeżenie mapowania i kosztów, cofnięcie wklejenia, odrzucenie
  niezapisanych zmian.
- **Struktura do Excela** – arkusz „Struktura”: całe drzewo (także zwinięte wiersze) w kolejności tabeli, nazwa wcięta
  według poziomu, kolumny Poziom, Rodzaj, Element CES, P1S, WP, CAM (USRID oraz imię i nazwisko), Cost Category, WP
  w poddrzewie, BAC HOURS, BAC MATERIAL, BAC, Baseline Start, Baseline Koniec, ACWP, Braki, Uwagi (wartości jak w tabeli –
  sumy poddrzewa). Eksportowany jest stan zapisany; przy niezapisanych zmianach – najpierw zapis albo „Odrzuć niezapisane”.
- **Edycja w komórkach** (zapis od razu po zatwierdzeniu wiersza – Enter albo przejście do innego wiersza, bez
  osobnego „Zapisz”; Esc cofa): nazwa i `Legacy WBS` węzła → nakładka; WP, CAM i Cost Category wiersza z kodem P1S → „WP i CAM”
  (klucz – kod P1S; zaznaczenie WP albo wybór CAM tworzy przypisanie, odznaczenie WP je usuwa razem z budżetem WP, jeśli
  WP nie jest przypisany do innego elementu; WP można zapisać bez CAM – brak „WP bez CAM”, CAM uzupełnia się później,
  przebieg blokuje gotowość projektu);
  BAC HOURS, BAC MATERIAL, BAC, Baseline Start, Baseline Koniec wiersza z jednym WP w poddrzewie – własnym albo jedynym
  pod nim (np. węzeł nakładki nad elementem P1S z WP) → „Harmonogram i budżet” tego WP (klucz – WP); wiersz z kilkoma WP
  w poddrzewie pokazuje sumę i nie jest edytowalny. Walidacja jak przy zapisie słownika: ERROR blokuje zapis (zmiany zostają w wierszu, komunikat
  nad tabelą), WARNING nie wstrzymuje. Błąd, który słownik miał już wcześniej w innym wierszu (np. element poza zakresem
  po odświeżeniu mapowania), nie blokuje zmiany – jest ostrzeżeniem. Zmiana `Legacy WBS` razem z WP / CAM w jednym
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
  ekranu, ponownie przy „Odśwież mapowanie i koszty”). Pomiar (SQL Server lokalnie, 4 wiersze): zapis 48 → 11 zapytań,
  odświeżenie 23 → 6 zapytań; liczba zapytań zapisu nie rośnie z liczbą wierszy. Edycja w toku jest zatwierdzana przy
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
| ~~O47~~ | Odświeżenie z SAP – **rozstrzygnięte (2026-10-08):** „Dołóż z Excela” dokłada nowe elementy i zachowuje węzły wirtualne i zmiany; podmiana z Excela w edytorze – świadomie bez nich (rozdz. 3) |
