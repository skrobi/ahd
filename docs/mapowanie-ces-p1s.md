# PZL-EV – Mapowanie CES ↔ P1S (założenia)

Wersja: 0.2 (założenia + decyzje z 30.09.2026 – bez implementacji)
Powiązane: `docs/architektura.md` (D25), `docs/pipeline-fazy.md` (G3), `docs/funkcjonalnosc.md` (F21–F24).

> W specyfikacji źródłowej występuje nazwa „AHD” – w projekcie oznacza ona **PZL-EV**.

---

## 1. Cel

PZL-EV umożliwia mapowanie struktury WBS z **SAP CES** (koszty) na strukturę WBS z **SAP P1S**
(produkcja). Struktury są niezależne – nie zakładamy zgodności numeracji, nazw ani poziomów hierarchii.
PZL-EV jest **warstwą mapowania** między systemami; mapowanie jest przechowywane w bazie PZL-EV i używane
automatycznie przy kolejnych importach.

```text
SAP CES → CES Project / CES WBS → mapowanie PZL-EV → P1S Project / P1S WBS → SAP P1S
```

Danych źródłowych SAP nie modyfikujemy. Źródłem prawdy o powiązaniu jest **zatwierdzona reguła
mapowania**.

---

## 2. Reguły mapowania

### 2.1 Reguła nadrzędna – projekt CES → projekt P1S

```text
4D03GZ → XYZ      (MAPPING_TYPE = PROJECT, STATUS = CONFIRMED)
```

Wszystkie WBS projektu CES `4D03GZ` – obecne i **przyszłe** – są przypisane do projektu P1S `XYZ`
bez ponownego zatwierdzania. Dziedziczenie jest **logiczne**: nie tworzymy rekordów
`4D03GZ000001 → XYZ`, `4D03GZ000002 → XYZ` … Ewentualny wynik materializowany (wydajność) jest
wyłącznie cache/widokiem, nie źródłem prawdy.

### 2.2 Wyjątek – WBS CES → WBS P1S

```text
4D03GZ000038 → XYZ.007
```

Wyjątek ma pierwszeństwo przed regułą projektu.

### 2.3 Gałąź P1S (`include_children`)

Reguła może wskazywać węzeł P1S wraz z potomkami (`include_children = true`) – obejmuje też potomków
dodanych później, dopóki reguła jest aktywna.

### 2.4 Krotność (decyzja M3)

- **wiele CES → jeden P1S** – obsługiwane,
- **jeden CES → wiele P1S – niedozwolone.** Koszt CES występujący raz jest **raz pokazywany**
  (bez powielania i bez dzielenia). Element CES ma w danym momencie **co najwyżej jeden** cel P1S.
- Reguła projektu i wyjątek nie tworzą 1:wiele – wyjątek **zastępuje** regułę projektu dla danego WBS.
- `include_children` nie powiela kosztu na potomków: określa zasięg węzła P1S (np. przy prezentacji
  i agregacji), a koszt CES jest przypisany raz – do wskazanego węzła.

### 2.5 Brak odpowiednika – `NO_P1S`

Świadomie potwierdzony brak elementu P1S. Różni się od `UNMAPPED` (decyzja jeszcze nie podjęta).
Element nie jest przypisywany do „przypadkowego” WBS.

---

## 3. Kolejność rozstrzygania

```text
CES WBS → ustal Project Definition
        → 1. dokładne mapowanie WBS CES → WBS P1S (wyjątek)
        → 2. reguła projektu CES → projekt P1S (dziedziczenie)
        → 3. automatyczna propozycja (NIE jest zatwierdzeniem)
        → 4. UNMAPPED
```

### Statusy

| Status | Znaczenie |
|---|---|
| `UNMAPPED` | brak zatwierdzonego mapowania (może mieć propozycję z poziomu 3) |
| `MAPPED` | bezpośrednie, zatwierdzone mapowanie WBS (bez reguły projektu) |
| `INHERITED` | mapowanie wynika z reguły projektu |
| `OVERRIDE` | własne mapowanie WBS nadpisujące regułę projektu |
| `NO_P1S` | potwierdzony brak odpowiednika P1S |

### Automatyczne propozycje (poziom 3)

Kryteria: identyfikator projektu, materiał, numer seryjny, zlecenie, model, klient, dane zamówienia,
istniejące relacje P1S, struktura hierarchiczna; nazwy i opisy – tylko pomocniczo. Propozycja wymaga
zatwierdzenia przez użytkownika, chyba że później zostanie zdefiniowana jednoznaczna reguła
automatycznego zatwierdzania.

---

## 4. Model danych (logiczny)

`CES_P1S_MAPPING`

| Pole | Opis |
|---|---|
| `id` | identyfikator reguły |
| `mapping_type` | `PROJECT` / `WBS` |
| `ces_project`, `ces_wbs` | strona CES (`ces_wbs` puste dla reguły projektu) |
| `p1s_project`, `p1s_wbs` | strona P1S (puste dla `NO_P1S`; `p1s_wbs` puste = cały projekt); **jedna aktywna reguła WBS na element CES** w danym okresie |
| `include_children` | czy obejmuje potomków węzła P1S |
| `mapping_status` | `MAPPED` / `OVERRIDE` / `NO_P1S` (statusy `INHERITED` i `UNMAPPED` są wyliczane, nie zapisywane) |
| `mapping_method` | np. ręczne / z zatwierdzonej propozycji |
| `active`, `valid_from`, `valid_to` | aktywność i okres obowiązywania (raportowanie historyczne) |
| `created_at/by`, `updated_at/by` | audyt |

**Historia zmian:** kto i kiedy utworzył, kto i kiedy zmienił, poprzednia i nowa wartość.

Dodatkowo potrzebny **rejestr elementów CES i P1S** (kod, projekt, rodzic, opis, pierwsze/ostatnie
wystąpienie) – na nim działa drzewo, wykrywanie nowych elementów i `include_children`.
- elementy **CES** – z importów RABIT (G2),
- elementy **P1S** – z przetworzonej tabeli **`PZLPROD.LOG.WBS`** (decyzja M2; bez sięgania do
  `PZL_SAP.dbo.Z_R3_PRPS_TBL`).

### 4.1 Przechowywanie (decyzja M1)

Reguły mapowania, rejestry elementów i historia zmian są w **lekkiej bazie plikowej (SQLite)**
w repozytorium PZL-EV na dysku sieciowym, np. `\\serwer\udzial\PZL-EV\00_Global\Baza\pzl_ev.sqlite`.
Zarządzanie przez **narzędzie CRUD z drzewem** (dwa drzewa CES | P1S) – bez Excela jako źródła prawdy
dla przypisań.

Ryzyka SQLite na udziale sieciowym i sposób ich ograniczenia:

| Ryzyko | Ograniczenie |
|---|---|
| blokady plików przez SMB bywają zawodne; tryb WAL nie działa na udziale sieciowym | tryb dziennika `DELETE`, krótkie transakcje, jeden zapis naraz (blokada aplikacyjna + `busy_timeout`) |
| jednoczesny zapis kilku osób | zapis tylko w krótkich operacjach CRUD/importu; odczyt równoległy bez ograniczeń |
| uszkodzenie pliku przy zerwaniu połączenia | automatyczna kopia pliku przed każdym importem / sesją edycji; kopie dzienne |
| rozmiar (surowe wiersze importów to setki tysięcy rekordów tygodniowo) | do decyzji: mapowanie i rejestry w jednym małym pliku, dane importów w osobnym pliku/plikach (pytanie P1) |

---

## 5. Interfejs wizualny

Dwa drzewa obok siebie (CES | P1S). Użytkownik może: rozwijać/zwijać, wyszukiwać, wybrać węzeł CES
i P1S, utworzyć mapowanie, regułę projektu, wyjątek, oznaczyć `NO_P1S`, usunąć/dezaktywować,
zobaczyć istniejące mapowania. Drag & drop – tylko jako dodatek.

Formularze: **reguła projektu** (cały projekt / pojedynczy WBS) i **wyjątek** (z podglądem reguły
nadrzędnej, którą nadpisuje).

**Kolejność implementacji:** model danych → logika priorytetów → dziedziczenie → dopiero UI.

---

## 6. Miejsce w pipeline

```text
RABIT / SAP CES → import (G2) → CES staging → Project Definition
   → mapowanie (wyjątek WBS / reguła projektu / propozycja / UNMAPPED)
   → struktura P1S → wspólny poziom raportowania → godziny / koszty / wartości → EV / CAM / raporty
```

Po każdym imporcie CES: lista nowych elementów wymagających uwagi, np.

```text
4D03GZ000041   → odziedziczono: XYZ
4D03H1000038   → UNMAPPED
4D03H1000039   → NO_P1S
```

**Kontrola kompletności projektu CES:** lista WBS ze statusami (`INHERITED`, `OVERRIDE → XYZ.002`,
`NO_P1S`, `UNMAPPED`).

**Wymaganie kluczowe:** jedno zatwierdzenie `4D03GZ → XYZ` obsługuje wszystkie obecne i przyszłe WBS
projektu. Ręczna interwencja tylko przy wyjątku, `NO_P1S`, braku reguły projektu lub braku
jednoznaczności.

---

## 7. Kryteria ukończenia (skrót)

Drzewa CES i P1S z wyszukiwaniem; reguła projektu obejmuje obecne i nowe WBS; wyjątek z pierwszeństwem;
`NO_P1S`; wiele:1; **blokada 1:wiele** (M3); `include_children`; statusy `INHERITED/OVERRIDE/NO_P1S/UNMAPPED`;
mapowania w bazie i używane automatycznie przy importach; historia zmian; brak mapowania nie gubi
danych CES; elementy bez odpowiednika P1S wykazywalne; źródłem prawdy jest reguła, nie rekordy pochodne.

---

## 8. Spójność z dotychczasową dokumentacją

### 8.1 Zgodne

| Założenie mapowania | Dotychczasowa dokumentacja |
|---|---|
| Mapowanie w bazie, historia zmian, `valid_from/valid_to` | wersjonowanie i dwie osie czasu (arch. 6.3), audyt AD (arch. 8.3) |
| Propozycja ≠ zatwierdzenie | wcześniejsza rekomendacja: prefiks/klucz tylko jako podpowiedź (K6) |
| Wiele CES → jeden P1S; bez 1:wiele (M3) | D19 dopuszczała dowolną krotność – M3 ją zawęża (koszt pokazywany raz) |
| Brak mapowania nie gubi danych CES | G2: surowe dane zawsze w bazie; D20: zamknięcie wymaga przypisania kosztów |
| Każdy z finansów może zmieniać mapowania, audyt w historii | D9 (bez podziału uprawnień) |
| UI w przeglądarce | D2 |
| Nowe elementy wykrywane po imporcie, bez wyprzedzania | D17, odpowiedź „dopiero po ściągnięciu” |

### 8.2 Rozbieżności do rozstrzygnięcia

| # | Rozbieżność | Dotychczas | Nowe założenie | Propozycja | Stan |
|---|---|---|---|---|---|
| S1 | **Gdzie żyje powiązanie CES↔P1S** | D19, arch. 6.4, spec. 6.2, readme: para `P1S WBS | CAS WBS` w wierszu słownika Excel „Struktura projektowa” | reguły w bazie PZL-EV, zarządzane w UI | powiązanie CES↔P1S **wyłącznie** w warstwie mapowania (D25); słownik „Struktura projektowa” opisuje już tylko **P1S WBS** (Program, Project, CAM, WP, Cost Category…) | ✅ **M1:** przypisania w lekkiej bazie (SQLite) na dysku sieciowym, zarządzane narzędziem CRUD + drzewo |
| S2 | **Zasada „Excel jest źródłem prawdy, bez formularza”** (readme – słowniki) | wszystkie słowniki w Excelu | mapowanie edytowane w aplikacji | mapowanie CES↔P1S to **wyjątek od zasady** – nie jest słownikiem Excel; ewentualnie eksport/import reguł do Excela jako wygoda | ✅ **M1:** przypisania nie są słownikiem Excel – wyjątek od zasady „Excel jest źródłem prawdy” |
| S3 | **Definicja zakresu** (D17) | zakres = wiersze słownika struktury (pary P1S/CES) | wspólny poziom raportowania = struktura P1S | zakres = zbiór **projektów P1S** (ze słownika struktury); dane CES trafiają do zakresu przez mapowanie | otwarte |
| S4 | **Wykrywanie nowych elementów** (D17, arch. 6.4, spec. F08) | eksport brakujących elementów do `Slowniki\Propozycje` i dopisanie wiersza w Excelu | nowe WBS dziedziczą regułę projektu; uwagi wymagają tylko `UNMAPPED` | G3 = mapowanie; eksport „Propozycje” dotyczy już tylko nowych elementów **P1S** do słownika struktury | otwarte |
| S5 | **Źródło struktury P1S** | „dopiero po ściągnięciu – nie wybiegamy przed szereg” (P1S tylko z danych z zaawansowaniem) | pełne drzewo P1S w UI, `include_children` obejmuje nowych potomków | potrzebne źródło hierarchii P1S (raport RABIT z P1S albo `PZL_SAP.Z_R3_PRPS_TBL`) – **do decyzji** | ✅ **M2:** hierarchia P1S z przetworzonej tabeli `PZLPROD.LOG.WBS` (bez `Z_R3_PRPS_TBL`) |
| S6 | **Nazwa kolumny CES** | readme/słownik: „CAS WBS” | „CES WBS” | ujednolicić na **CES WBS** (CAS = typ projektu Compliance, CES = system) | otwarte |
| S7 | **Wartości przy 1:wiele** | brak | nie dzielimy bez reguły podziału | do ustalenia, gdzie w EV trafia koszt CES z relacją 1:wiele bez reguły (blokada zamknięcia? poziom projektu?) | ✅ **M3:** 1:wiele niedozwolone – koszt CES pokazywany dokładnie raz |
| S8 | **Koszty `NO_P1S` w EV** | D20: koszty muszą być przypisane na zamknięciu | `NO_P1S` = świadomy brak odpowiednika | do ustalenia: czy `NO_P1S` przechodzi bramkę zamknięcia i gdzie raportujemy ten koszt | otwarte |
| S9 | **Odtwarzalność przebiegu** | przebieg przypina wersje słowników i dane (P1) | mapowanie ma `valid_from/valid_to` | przebieg przypina również **stan mapowania** (znacznik czasu); zmiana mapowania w trakcie przebiegu → decyzja „kontynuuj / przelicz” jak przy słownikach | otwarte |
| S10 | **„Projekt” w `projekty_zrodla.csv`** (D24) | nieokreślone | rozróżnienie projekt CES / projekt P1S | doprecyzować, czy chodzi o projekt P1S (raportowy), czy CES | otwarte |
| S11 | **Ustalenie Project Definition dla WBS CES** | brak | krok obowiązkowy rozstrzygania | z kolumny raportu CES (która?) czy z prefiksu kodu WBS (np. 6 znaków) | otwarte |

---

## 9. Decyzje (30.09.2026)

| # | Decyzja | Skutek |
|---|---|---|
| M1 | Przypisania (reguły mapowania, rejestry elementów, historia) w **lekkiej bazie SQLite na dysku sieciowym**, zarządzane **narzędziem CRUD z drzewem** | rozwiązuje S1, S2; mapowanie nie jest w Excelu |
| M2 | Struktura P1S z **`PZLPROD.LOG.WBS`** (dane już przetworzone), bez `PZL_SAP.dbo.Z_R3_PRPS_TBL` | rozwiązuje S5; potrzebny opis kolumn `LOG.WBS` (pytanie P2) |
| M3 | **Brak relacji 1:wiele** – koszt CES występujący raz jest raz pokazywany | rozwiązuje S7; walidacja blokuje drugi aktywny cel dla tego samego elementu CES |

## 10. Pytania otwarte

| # | Pytanie |
|---|---|
| P1 | Czy „wszystko” w SQLite na dysku sieciowym obejmuje też dane importów (surowe wiersze), czy tylko przypisania / słowniki / konfigurację? Rekomendacja: przypisania w małym pliku, dane importów w osobnym pliku (lub w MS SQL, jeśli będzie dostępny). Dotyczy D3 w architekturze. |
| P2 | Kolumny `PZLPROD.LOG.WBS`: kod WBS P1S, projekt, rodzic (lub poziom), opis – jak się nazywają? |
| P3 | Czy dalej obowiązuje: zakres = zbiór projektów P1S, a słownik struktury opisuje tylko P1S (S3, S4)? W praktyce atrybuty P1S (CAM, WP, Cost Category) też mogą trafić do narzędzia CRUD zamiast Excela. |
| P4 | Czy koszt `NO_P1S` przechodzi bramkę zamknięcia miesiąca i gdzie jest raportowany (S8)? |
| P5 | Skąd Project Definition dla WBS CES – kolumna raportu czy prefiks kodu (S11)? |
| P6 | „Projekt” w `projekty_zrodla.csv` – projekt P1S czy CES (S10)? Ujednolicenie „CAS WBS” → „CES WBS” (S6)? |
