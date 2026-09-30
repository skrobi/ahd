# PZL-EV – Specyfikacja funkcjonalna

Wersja: 1.1 (wstępny projekt; 1.1: słowniki w bazie z interfejsem – M13, `NO_P1S` odłożony – M14)
Status: **do akceptacji** – opis zachowania aplikacji, bez implementacji.

Powiązane dokumenty: `readme.md`, `docs/architektura.md`, `prototyp/pzl-ev-prototyp.html`
(klikalny prototyp pokazujący opisane funkcje na danych przykładowych; ekran słowników w prototypie
pokazuje jeszcze pliki Excel – do aktualizacji po M13), `docs/mapowanie-ces-p1s.md`.

---

## 1. Użytkownicy

| Użytkownik | Jak korzysta | Uprawnienia |
|---|---|---|
| **Osoba z finansów** | aplikacja w przeglądarce (lokalnie) | wszystkie funkcje we wszystkich zakresach – bez podziału ról; każda akcja zapisana z kontem AD |
| **CAM** (Cost Account Manager) | wyłącznie pliki Excel na dysku sieciowym | odczyt `CAM\…\Wyslane`, zapis `CAM\…\Zwrocone` swojego zakresu |
| **Administrator** | wdrożenia na PROD | skrypty bazy, konfiguracja środowiska |
| **Developer** | środowisko TEST | rozwój aplikacji i bazy |

---

## 2. Pojęcia

| Pojęcie | Znaczenie |
|---|---|
| Zakres | program lub pula małych projektów raportowanych razem; typ SAC, CAS lub Wewnętrzny |
| Przebieg | jedno przetworzenie zakresu za okres: tygodniowy albo zamknięcie miesiąca |
| Etap | krok przebiegu z własnym statusem i bramką wyjścia |
| Słownik globalny | wspólny dla wszystkich zakresów (stawki wydziałów, kalendarz okresów, kursy USD/PLN) |
| Słownik zakresowy | należy do jednego zakresu (projekty i WP, CAM, harmonogram i budżet, stawki CAS) |
| Baza słowników | SQLite na dysku sieciowym (`00_Global\Baza\`): wszystkie słowniki, przypisania i konfiguracja, edytowane w aplikacji (M13); **Excel nie jest źródłem słowników** |
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
| **Pulpit** | karty zakresów ze stanem bieżącego przebiegu, lista „Wymaga uwagi”, ostatnie zdarzenia | przejście do zakresu / przebiegu, nowy zakres |
| **Zakresy** | lista zakresów: typ, liczba projektów i CAM, aktywny przebieg, folder | nowy zakres |
| **Zakres** | gotowość zakresu, konfiguracja, historia przebiegów, struktura folderów | nowy przebieg |
| **Kreator zakresu** | 6 kroków: podstawowe, projekty i WP, słowniki, CAM, foldery, podsumowanie | utwórz zakres |
| **Przebiegi** | wszystkie przebiegi wszystkich zakresów ze stanem i osobą | przejście do przebiegu |
| **Przebieg** | oś etapów, panel wybranego etapu, przypięte słowniki, dziennik przebiegu | akcje etapów |
| **Słowniki** | słowniki globalne i zakresowe w bazie: tabela z filtrowaniem, stan walidacji, historia zmian | dodaj / edytuj / zamknij ważność wiersza |
| **Przypisania** | dwa drzewa CES \| P1S, reguły projektu, wyjątki, WP i CAM zakresu, lista elementów `UNMAPPED` (F21–F24) | reguła projektu, wyjątek, przypisanie WP/CAM |

Nagłówek aplikacji zawsze pokazuje środowisko (TEST / PROD), zalogowanego użytkownika AD
oraz wersję aplikacji i schematu.

---

## 4. Funkcje

### F01. Utworzenie zakresu (kreator)

1. **Podstawowe** – kod (unikalny, używany w nazwach folderów, plików i przebiegów), nazwa, typ
   SAC / CAS / Wewnętrzny. Aplikacja pokazuje liczbę etapów wynikającą z typu.
2. **Projekty i WP** – wybór projektów P1S (`PROJORG`) z drzewa `LOG.WBS`; opcjonalnie projekty CES,
   które pojawiły się w importach i nie mają reguły projektu (od razu z formularzem reguły CES → P1S).
   Opcja „pula małych projektów”.
3. **Słowniki** – słowniki zakresu wymagane dla typu (WP, CAM, harmonogram i budżet; w CAS także
   stawki CAS) – uzupełniane w aplikacji; lista słowników globalnych z bieżącym stanem.
4. **CAM** – przypisanie CAM do WP w narzędziu przypisań (M8); CAM wybierany z listy.
5. **Foldery** – podgląd struktury; kontrole: dostępność korzenia (UNC), prawo zapisu, brak folderu
   o tym kodzie; informacja o zgłoszeniu do IT (dostęp CAM do `Zwrocone`).
6. **Podsumowanie** → **Utwórz zakres**: rejestracja w bazie słowników i w MS SQL, utworzenie
   folderów, treść zgłoszenia do IT, kontrola struktury. Nie powstają żadne pliki słowników.

Reguły: kod musi być unikalny; zakres z niekompletnymi słownikami (brak WP, CAM lub budżetu) nie może
uruchomić przebiegu.

### F02. Gotowość zakresu

Lista kontrolna na ekranie zakresu:
- zakres zarejestrowany w bazie,
- struktura folderów zgodna z konfiguracją,
- zakres obejmuje co najmniej jeden projekt P1S,
- słowniki zakresu wypełnione (WP, harmonogram i budżet; w CAS stawki CAS) (**blokuje** przebieg),
- WP mają przypisanego CAM (ostrzeżenie – bez tego nie powstaną pliki CAM),
- dostęp CAM do folderu `Zwrocone` (ostrzeżenie).

### F03. Edycja słowników i przypisań

- Ekran Słowniki (i Przypisania) w dowolnym momencie; dotyczy słowników globalnych i zakresowych.
- Edycja w tabeli / formularzu; pola typowane (data, liczba, wybór z listy), klucze WBS zawsze jako tekst.
- **Walidacja przy zapisie** wg rozdz. 6: błąd blokujący nie pozwala zapisać (komunikat przy polu),
  ostrzeżenie wymaga potwierdzenia.
- Zmiana wiersza = zamknięcie `ValidTo` starego i nowy wiersz (historia, kto, kiedy); „usunięcie” =
  zamknięcie ważności. Brak zmian → nic się nie zapisuje.
- Zapis to krótka transakcja; jednocześnie zapisuje jedna osoba (SQLite na dysku sieciowym) – druga
  widzi komunikat i ponawia.
- Aktywne przebiegi z przypiętym wcześniejszym stanem dostają informację (F16).

### F04. Uruchomienie przebiegu

- Wybór rodzaju: **tygodniowy** (bieżący tydzień, poniedziałek) albo **zamknięcie miesiąca**.
- Kontrole przed startem:
  - brak innego przebiegu tego zakresu dla tego tygodnia / zamknięcia (**blokuje**),
  - zgodna wersja aplikacji i schematu bazy (**blokuje**),
  - słowniki zakresu kompletne (**blokuje**),
  - tydzień: świeżość danych produkcyjnych (data odświeżenia `vAHDD`),
  - zamknięcie: informacja, jeśli okres jeszcze trwa.
- Podgląd stanu słowników, który zostanie przypięty (ostatnie zmiany: kto, kiedy).
- Start zapisuje przebieg, przypięcia i pierwsze zdarzenie w dzienniku.

### F05a. Import plików SAP (globalny, poza przebiegiem)

- Przy pobieraniu z RABIT **nie wiadomo, do którego zakresu należy plik** – import obejmuje
  wszystkie pliki RABIT. **Pobierz** kopiuje przez WebDAV nowe i zmienione pliki z folderu RABIT na
  SharePoint do `00_Global\RABIT\Do_importu`; **Importuj** ładuje je do bazy. Plik, którego WebDAV nie
  pobierze (> 50 MB), zapisuje się tam ręcznie z przeglądarki.
- Źródło pliku rozpoznawane po **prefiksie nazwy** (konfiguracja w bazie słowników – M15;
  w MVP przejściowo `konfiguracja/zrodla_rabit.csv`), np.
  `ACTUALS_PAF_01.xlsx` → `ACTUALS_PAF`. Importowany jest każdy rozpoznany plik samodzielnie.
- Dla każdego pliku decyzja: **zaimportowany** (nowy hash), **duplikat** (ten fizyczny plik był już
  zaimportowany), **pominięty** (te same metadane co przy poprzednim imporcie – bez kopiowania),
  **nierozpoznany** (brak prefiksu – nie importowany), **błąd**.
- Nowe pliki: kopia do Landing Zone, rejestracja (kto, kiedy, kod źródła, kolumny, liczba wierszy),
  wiersze w postaci surowej w bazie.

### F05b. Kompletność źródeł projektu

- Każdy projekt ma listę wymaganych źródeł RABIT (konfiguracja w bazie słowników – M10;
  w MVP przejściowo `konfiguracja/projekty_zrodla.csv`).
- PZL-EV pokazuje dla każdego projektu: ✓ źródło zaimportowane (ostatni import, data raportu, plik),
  ✗ brak importu, ⚠ ostatni import starszy niż próg; projekt „komplet” / „niekompletny”.
- Później: sprawdzenie, czy import obejmuje bieżący okres; blokada przebiegu dla niekompletnego projektu.
- MVP: `python -m pzl_ev.etap1 kompletnosc`.
- Import może uruchomić każda osoba z finansów w dowolnym momencie; historia importów jest widoczna
  dla wszystkich.
- MVP: `python -m pzl_ev.etap1 pobierz` + `import` (instrukcja `docs/mvp-etap1.md`).

### F05. Etap 1 przebiegu – Dane SAP zakresu

- Warunek: import plików SAP (F05a) wykonany; aplikacja pokazuje datę ostatniego importu i pliki,
  które pojawiły się od poprzedniego przebiegu.
- Wybór wierszy zakresu na podstawie projektów P1S zakresu, mapowania CES↔P1S (F21–F24) oraz okresu.
- Kontrole: daty w okresie przebiegu, plik nie zmienił się od importu (hash).
- Wiersze bez przypisania do żadnego zakresu są widoczne w etapie 4 (F08) jako nowe elementy.

### F06. Etap 2 – Przypięcie słowników

- Tabela słowników przebiegu: nazwa, zasięg (globalny / zakres), liczba wierszy, ostatnia zmiana
  (kto, kiedy).
- **Przypnij** zapisuje w przebiegu znacznik stanu słowników i kopiuje ten stan (migawkę) do MS SQL,
  schemat `dict` (D27) – procedury EV czytają wyłącznie migawkę.
- Słowniki nie są importowane z plików.

### F07. Etap 3 – Walidacja

- Walidacja plików SAP (rozdz. 6.5) i **spójności przypiętych słowników z danymi przebiegu**
  (np. wydział z kosztów CES bez stawki, element z kosztem `UNMAPPED`, WP bez budżetu). Poprawność
  samych słowników zapewnia walidacja przy zapisie (F03).
- Wynik: tabela problemów – waga (blokujący / ostrzeżenie), źródło, element, opis.
- **Błąd blokujący**: akcja **Popraw w aplikacji** (przejście do ekranu Słowniki / Przypisania z
  filtrem na problem) → **Przypnij ponownie i waliduj**.
- Brak błędów blokujących: ostrzeżenia trafiają do raportu przebiegu.

### F08. Etap 4 – Łączenie źródeł

- Wywołanie logiki łączenia w bazie (logika do przedstawienia przez zespół, O14).
- Podsumowanie: koszt rzeczywisty okresu, liczba zmapowanych WBS, liczba WP, suma kontrolna
  (koszt po połączeniu = koszt z plików).
- **Wykrywanie elementów bez przypisania**:
  - element CES ze statusem `UNMAPPED` (brak reguły projektu i wyjątku, w tym nowa definicja
    projektu) – z kosztem okresu,
  - element P1S z zaawansowaniem bez WP w zakresie,
  - kontrola, że projekt P1S nie należy do innego zakresu.
- Akcje:
  - **Przypisz w aplikacji** – ekran Przypisania z listą elementów (klucze wypełnione),
  - po zapisaniu przypisań: ponowne przypięcie stanu, **przeliczenie od etapu 3**,
  - **Kontynuuj bez tych elementów** – tylko w przebiegu tygodniowym; decyzja w dzienniku,
    wartość poza EV pokazana w raporcie.
- Zamknięcie miesiąca: elementy z kosztem muszą być przypisane (D20; szczegóły – pytanie P10 w
  `docs/mapowanie-ces-p1s.md`).

### F09. Etap 5 – Pliki dla finansów

- Generowanie plików do `Finanse\<RRRR-MM>\`, np. przegląd kosztu rzeczywistego, ETC wstępne,
  lista WBS bez mapowania.
- Etap czeka na **potwierdzenie w aplikacji** (formalność; może potwierdzić osoba prowadząca).
- **Potwierdź** – odblokowuje kolejne etapy; zapis kto i kiedy.
- **Odrzuć** – wymagany komentarz; powrót do etapu 4 (etapy późniejsze nieaktualne).

### F10. Etap 6 (tydzień) – Zaawansowanie z produkcji

- Pobranie zaawansowania godzin i materiałów z danych produkcyjnych P1S (źródło O10).
- Wynik: liczba WP z wartością i lista WP bez wartości.
- Pochodzenie wartości: `PRODUKCJA`.

### F11. Etap 7 (tydzień) – Uzupełnienie braków

- Tabela WP bez wartości: WP, CAM, ostatnia znana wartość, metoda, wartość.
- Metody: ostatnia znana wartość, wartość ręczna, plik CAM (opcjonalnie). Zasady – O11.
- Zapis: pochodzenie `ANALITYK`, kto, kiedy, metoda.

### F12. Etapy 6–7 (zamknięcie) – Pliki dla CAM i ich import

- **Generuj pliki dla CAM** – jeden plik na CAM (O3) do `CAM\<RRRR-MM>\Wyslane\`, z WP danego CAM;
  ukryty arkusz z identyfikatorem przebiegu; zablokowane komórki poza polami do uzupełnienia.
- **Skanuj folder Zwrócone** – status per CAM: wysłany / zwrócony / zaimportowany / odrzucony (+ powód).
- Powody odrzucenia: brak identyfikatora, plik z innego przebiegu lub okresu, zmiana poza polami,
  wartości poza zakresem.
- Etap czeka (dni), aż pliki wszystkich CAM zostaną zaimportowane; stan jest w bazie.
- Pochodzenie wartości: `CAM`.

### F13. Etap 8 – Walidacja zaawansowania

- Udział pochodzenia wartości (CAM / PRODUKCJA / ANALITYK) na wykresie paskowym.
- Kontrole: zaawansowanie 0–100%, spadek względem poprzedniego okresu (ostrzeżenie),
  EV nie większe niż BAC.
- Zamknięcie: **100% wartości od CAM** (blokuje).

### F14. Etap 9 – Generowanie EV

- Kalkulacja w bazie; wynik jako **rewizja** (R1, R2…) z zapisem przypiętego stanu słowników i plików.
- Tabela: projekt, BAC, BCWS, BCWP, ACWP, CPI, SPI (w tys. PLN, w SAC w tys. USD).
- Tydzień: oznaczenie „EV wstępne (nieformalne)”.
- Plik wynikowy `EV\<RRRR-MM>\<Zakres>_EV_<T<NN>|RRRR-MM>_R<n>.xlsx`.
- **Przelicz ponownie** – nowa rewizja, poprzednia zostaje.

### F15. Etap 10 (SAC) – Plik dla Cobra

- Koszt pracy przeliczony po bieżących stawkach na USD, zaawansowanie wg WP.
- Plik `EV\<RRRR-MM>\<Zakres>_<RRRR-MM>_Cobra_import.csv`.

### F16. Zmiana słowników w trakcie przebiegu

- Baner w przebiegu: „Słownik <nazwa> zmieniony po przypięciu (kto, kiedy, liczba zmian)”.
- **Kontynuuj na przypiętym stanie** – decyzja w dzienniku, baner znika dla tych zmian.
- **Przelicz od etapu 3** – przypięcie nowego stanu i nowa migawka; etapy od walidacji wykonywane ponownie.

### F17. Kontynuacja przebiegu przez inną osobę

- Każda osoba z finansów może wykonać dowolną akcję w dowolnym przebiegu (D9).
- Ekran przebiegu pokazuje, kto ostatnio pracował; każda akcja zapisana w dzienniku z kontem AD.
- Operacja w toku blokuje przebieg na czas jej trwania (druga osoba widzi komunikat).

### F18. Zamknięcie okresu

- Dostępne w przebiegu „zamknięcie miesiąca” po zakończeniu wszystkich etapów.
- **Zatwierdź zamknięcie okresu** – przebieg zamrożony (akcje niedostępne); ponowne przeliczenie
  wymaga nowej rewizji w historii.

### F19. Pulpit i dziennik

- „Wymaga uwagi”: brak przebiegu w bieżącym tygodniu, etapy wymagające akcji lub z błędem,
  pliki czekające na potwierdzenie, zmiany słowników po przypięciu, zakresy z niekompletnymi słownikami, elementy `UNMAPPED` z kosztem.
- „Ostatnie zdarzenia”: przekrój dzienników wszystkich przebiegów.
- Dziennik przebiegu: czas, użytkownik, opis – od najnowszego.

### F20. Kontrola wersji i środowiska

- Przy starcie: zgodność wersji aplikacji i schematu bazy; niezgodność blokuje pracę z komunikatem,
  co zaktualizować.
- Profil TEST / PROD widoczny w nagłówku.

---

### F21–F24. Mapowanie CES ↔ P1S *(założenia – `docs/mapowanie-ces-p1s.md`)*

- **F21 Drzewa CES i P1S** – dwa drzewa obok siebie, rozwijanie, wyszukiwanie, statusy mapowania.
- **F22 Reguła projektu** – CES Project → P1S Project; obejmuje obecne i przyszłe WBS (dziedziczenie).
- **F23 Wyjątek / gałąź** – WBS CES → WBS P1S (pierwszeństwo), `include_children`; dezaktywacja z
  historią. (`NO_P1S` – potwierdzony brak odpowiednika – odłożony, M14.)
- **F24 Po imporcie** – lista nowych elementów z wynikiem rozstrzygania (`odziedziczono`, `UNMAPPED`)
  i kontrola kompletności projektu CES.

## 5. Statusy etapu

| Status | Znaczenie | Przejścia |
|---|---|---|
| Oczekuje | poprzednie etapy niezakończone | → Do wykonania |
| Do wykonania | można uruchomić akcję etapu | → W toku |
| W toku | operacja trwa (blokada przebiegu) | → Zakończony / Wymaga akcji / Błąd |
| Wymaga akcji | potrzebna decyzja lub dane (potwierdzenie, pliki CAM, uzupełnienia, nowe elementy) | → Zakończony / Do wykonania |
| Błąd | błąd blokujący (np. walidacja) | → Do wykonania (po poprawie) / Zakończony (decyzja) |
| Zakończony | bramka spełniona | → Nieaktualny (gdy zmienią się wejścia) |
| Nieaktualny | wynik oparty na nieaktualnych danych | → Do wykonania |

Stan przebiegu wynika ze stanów etapów: pierwszy niezakończony etap wyznacza „co dalej”.

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

### 6.2 Przypisania zakresu (projekty, WP, CAM, mapowanie CES↔P1S)

| Reguła | Waga |
|---|---|
| projekt P1S należy do co najwyżej jednego zakresu w nakładającym się okresie | blokujący |
| element CES ma co najwyżej jeden aktywny cel P1S (M3 – bez 1:wiele) | blokujący |
| reguła projektu CES wskazuje `PROJORG` (M7) | blokujący |
| WP wymaga CAM | blokujący |
| CAM wybierany z listy osób (brak wariantów zapisu) | – |

### 6.3 Harmonogram i budżet

| Reguła | Waga |
|---|---|
| element (WP) należy do zakresu | blokujący |
| daty rozpoczęcia ≤ daty zakończenia (bazowe, planowane, rzeczywiste) | blokujący |
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

---

## 7. Pliki generowane

| Plik | Folder | Etap |
|---|---|---|
| `<Zakres>_AC_przeglad_<T<NN>>.xlsx`, `<Zakres>_ETC_wstepne_<T<NN>>.xlsx`, `<Zakres>_WBS_bez_mapowania.xlsx` | `Finanse\<RRRR-MM>\` | 5 |
| `<Zakres>_<RRRR-MM>_CAM_<CAM>.xlsx` | `CAM\<RRRR-MM>\Wyslane\` | 6 (zamknięcie) |
| `<Zakres>_EV_<T<NN>|RRRR-MM>_R<n>.xlsx` | `EV\<RRRR-MM>\` | 9 |
| `<Zakres>_<RRRR-MM>_Cobra_import.csv` | `EV\<RRRR-MM>\` | 10 (SAC) |

Nazwy plików są propozycją do potwierdzenia. Słowniki nie są plikami – nie powstają pliki walidacji
ani propozycji słowników.

---

## 8. Scenariusze (przejście przez prototyp)

1. **Nowy zakres M28** – kreator → zakres zablokowany (brak WP, CAM, budżetu) → uzupełnienie w
   aplikacji → zakres gotowy.
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

- Import / eksport słowników z i do Excela (ewentualnie później jako wygoda – nie źródło prawdy, M13).
- Status `NO_P1S` (odłożony do pierwszego rzeczywistego przypadku, M14).
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
