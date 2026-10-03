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
  Strona **P1S wchodzi do drzewa jako dodatkowe zadania / informacja** (kolumna `Legacy WBS`, np. `AC-CAB.6.38`),
  nie jako osobny zakres.
- Mapowanie CES ↔ P1S jest **globalne i administracyjne** – budowa nakładki tylko je **czyta**; nakładka nie
  zmienia mapowania, a mapowanie nie zależy od projektu.

---

## 3. Źródło i edycja

- **Utworzenie przy zakładaniu projektu** (krok „Performance Objectives” – `docs/funkcjonalnosc.md`, F01):
  wczytanie z **Excela** (układ w rozdz. 5) albo budowa ręczna.
- **Edycja w aplikacji:** dodawanie i przenoszenie elementów, tworzenie wirtualnych węzłów grupujących,
  zmiana nazw.
- **Odświeżenie na żądanie:** ponowny import Excela na ekranie projektu zastępuje strukturę strukturą z pliku;
  elementy o tym samym `WBS element` zachowują identyfikator i historię. Węzły wirtualne i ręczne zmiany nie są
  przenoszone – trzeba je odtworzyć przed zapisem (rozwiązanie tymczasowe; docelowo – O47).
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

- Zakres projektu po stronie P1S to `Legacy WBS` elementów nakładki. Element P1S należy do projektu, gdy jest
  równy `Legacy WBS` elementu nakładki albo leży pod nim (np. `AC-CAB.6.38.01` pod `AC-CAB.6.38`).
- WP ze słownika „WP i CAM” podpina się pod węzeł nakładki z najdłuższym pasującym `Legacy WBS` (przy równych –
  pod najgłębszy). Węzeł wirtualny ma WP swoich elementów; sumy węzła obejmują jego poddrzewo, każdy WP liczony raz.
- Element nakładki, pod którym nie ma żadnego WP, jest brakiem w bazie analitycznej (kreator, krok 5).

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
| O47 | Odświeżenie z SAP: zachowanie wirtualnych węzłów i ręcznych zmian (dziś – nie są przenoszone, rozdz. 3) |
