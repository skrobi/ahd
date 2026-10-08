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
- **Kolumny:** Nazwa (drzewo, zamrożona), Element CES i P1S (domyślnie schowane – „+” / „−” w nagłówku Nazwa, jak
  grupowanie kolumn w Excelu), WP (checkbox), CAM (lista osób), BAC HOURS, BAC MATERIAL, Baseline Start, Baseline Koniec
  (RRRR-MM-DD), Braki. Kolumny mają stałą szerokość – przy kolejnych kolumnach tabela przewija się w poziomie. Za BAC
  MATERIAL nie ma na razie kolumn (dawna Cost Category zniknęła z tabeli – zostaje w słowniku „WP i CAM”; co ma tu być –
  do ustalenia).
- **WP** to znacznik elementu P1S (wiersz z kodem P1S), który wskażą finansiści: zaznaczony element jest pakietem pracy
  i ma mieć koszty i budżet. Kodem nowego WP jest kod elementu P1S (WP z wcześniej wczytanego słownika zachowuje swój
  kod). WP nie jest osobnym poziomem drzewa.
- **CAM** wybiera się z listy osób z wyszukiwaniem po fragmencie USRID albo imienia i nazwiska (słownik Osoby wczytywany
  z HR – `docs/slowniki.md`, rozdz. 2; ta sama lista co w tabeli słownika): zapisywany jest USRID, wyświetlane imię
  i nazwisko. CAM wpisany wcześniej spoza słownika jest na liście pod swoją wartością.
- **Sumy:** budżet i daty wiersza obejmują poddrzewo, każdy WP liczony raz; braki – element nakładki bez WP,
  WP bez budżetu.
- **Edycja w komórkach** (zapis od razu po zatwierdzeniu wiersza – Enter albo przejście do innego wiersza, bez
  osobnego „Zapisz”; Esc cofa): nazwa i `Legacy WBS` węzła → nakładka; WP i CAM wiersza z kodem P1S → „WP i CAM”
  (klucz – kod P1S; zaznaczenie WP albo wybór CAM tworzy przypisanie, odznaczenie WP je usuwa razem z budżetem WP, jeśli
  WP nie jest przypisany do innego elementu; WP wymaga CAM – zaznacz WP i wybierz CAM przed opuszczeniem wiersza);
  BAC HOURS, BAC MATERIAL, Baseline Start, Baseline Koniec wiersza, którego sumy to jego własny WP → „Harmonogram
  i budżet” (klucz – WP). Walidacja jak przy zapisie słownika: ERROR blokuje zapis (zmiany zostają w wierszu, komunikat
  nad tabelą), WARNING nie wstrzymuje. Błąd, który słownik miał już wcześniej w innym wierszu (np. element poza zakresem
  po odświeżeniu mapowania), nie blokuje zmiany – jest ostrzeżeniem. Zmiana `Legacy WBS` razem z WP / CAM w jednym
  wierszu przypisuje WP do nowego kodu P1S. Kolumny zablokowane do edycji – lista `StructureEdits.Locked` (do ustalenia).
- **Jak w Excelu:** zaznaczanie komórek, pisanie zastępuje zawartość komórki, Ctrl+C / Ctrl+V – kopiowanie i wklejanie
  bloku od bieżącej komórki (jedna komórka wypełnia zaznaczenie, wiersz nagłówków pomijany, komórki, których w danym
  wierszu nie można zmienić, pomijane – liczba pod tabelą), Delete – wyczyszczenie (bez WP), Ctrl+D – wypełnienie w dół,
  Alt+→ / Alt+← – rozwinięcie / zwinięcie wiersza. Komórka liczby / daty z błędem jest podświetlona od razu po wpisaniu.
  Zmienione wiersze są zapisywane po kolei (kolejka), po serii – jedno odświeżenie; edycja w toku jest zatwierdzana przy
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
