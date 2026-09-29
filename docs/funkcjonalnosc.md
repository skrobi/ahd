# AHD – Specyfikacja funkcjonalna

Wersja: 1.0 (wstępny projekt)
Status: **do akceptacji** – opis zachowania aplikacji, bez implementacji.

Powiązane dokumenty: `readme.md`, `docs/architektura.md`, `prototyp/ahd-prototyp.html`
(klikalny prototyp pokazujący opisane funkcje na danych przykładowych).

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
| Słownik zakresowy | należy do jednego zakresu (struktura projektowa, harmonogram i budżet, stawki CAS) |
| Wersja słownika | zatwierdzony stan słownika; powstaje tylko przy zmianie treści i poprawnej walidacji |
| Przypięcie | zapamiętanie w przebiegu, na których wersjach słowników liczył |
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
| **Kreator zakresu** | 6 kroków: podstawowe, struktura projektowa, słowniki, CAM, foldery, podsumowanie | utwórz zakres |
| **Przebiegi** | wszystkie przebiegi wszystkich zakresów ze stanem i osobą | przejście do przebiegu |
| **Przebieg** | oś etapów, panel wybranego etapu, przypięte słowniki, dziennik przebiegu | akcje etapów |
| **Słowniki** | słowniki globalne i zakresowe: plik, wersja, ostatni import, historia wersji | publikacja słownika globalnego |

Nagłówek aplikacji zawsze pokazuje środowisko (TEST / PROD), zalogowanego użytkownika AD
oraz wersję aplikacji i schematu.

---

## 4. Funkcje

### F01. Utworzenie zakresu (kreator)

1. **Podstawowe** – kod (unikalny, używany w nazwach folderów, plików i przebiegów), nazwa, typ
   SAC / CAS / Wewnętrzny. Aplikacja pokazuje liczbę etapów wynikającą z typu.
2. **Struktura projektowa** – informacja, że zakres wyznacza słownik struktury; podgląd kolumn
   wzorca; opcjonalnie wybór elementów, które pojawiły się w dotychczasowych pobraniach i nie należą
   do żadnego zakresu (trafią do wzorca z wypełnionymi kluczami). Opcja „pula małych projektów”.
3. **Słowniki** – słowniki zakresu wymagane dla typu (struktura, harmonogram i budżet; w CAS także
   stawki CAS); lista słowników globalnych z bieżącymi wersjami.
4. **CAM** – informacja, że lista CAM pochodzi z kolumny CAM słownika struktury.
5. **Foldery** – podgląd struktury; kontrole: dostępność korzenia (UNC), prawo zapisu, brak folderu
   o tym kodzie; informacja o zgłoszeniu do IT (dostęp CAM do `Zwrocone`).
6. **Podsumowanie** → **Utwórz zakres**: rejestracja w bazie, utworzenie folderów, wzorce słowników
   (nagłówki, typy kolumn, arkusz instrukcji), treść zgłoszenia do IT, kontrola struktury.

Reguły: kod musi być unikalny; zakres bez zatwierdzonej wersji słowników nie może uruchomić przebiegu.

### F02. Gotowość zakresu

Lista kontrolna na ekranie zakresu:
- zakres zarejestrowany w bazie,
- struktura folderów zgodna z konfiguracją,
- słownik struktury obejmuje co najmniej jeden projekt,
- słowniki zakresu mają zatwierdzone wersje (**blokuje** przebieg),
- kolumna CAM wypełniona (ostrzeżenie – bez niej nie powstaną pliki CAM),
- dostęp CAM do folderu `Zwrocone` (ostrzeżenie).

### F03. Publikacja słownika globalnego

- Dostępna z ekranu Słowniki w dowolnym momencie.
- Kopia do Landing Zone → walidacja → nowa wersja tylko przy zmianie treści.
- Ten sam hash: komunikat „plik bez zmian – nowa wersja nie powstała”.
- Aktywne przebiegi z przypiętą starszą wersją dostają informację (F16).

### F04. Uruchomienie przebiegu

- Wybór rodzaju: **tygodniowy** (bieżący tydzień, poniedziałek) albo **zamknięcie miesiąca**.
- Kontrole przed startem:
  - brak innego przebiegu tego zakresu dla tego tygodnia / zamknięcia (**blokuje**),
  - zgodna wersja aplikacji i schematu bazy (**blokuje**),
  - słowniki zakresu mają zatwierdzone wersje (**blokuje**),
  - tydzień: świeżość danych produkcyjnych (data odświeżenia `vAHDD`),
  - zamknięcie: informacja, jeśli okres jeszcze trwa.
- Podgląd wersji słowników, które zostaną przypięte.
- Start zapisuje przebieg, przypięcia i pierwsze zdarzenie w dzienniku.

### F05a. Import plików SAP (globalny, poza przebiegiem)

- Przy pobieraniu z RABIT **nie wiadomo, do którego zakresu należy plik** – import obejmuje
  wszystkie pliki z folderu RABIT (SharePoint, logowanie SSO) lub z folderu lokalnego / sieciowego.
- Dla każdego pliku decyzja: **zaimportowany** (nowy hash), **duplikat** (treść już w bazie),
  **pominięty** (te same metadane co przy poprzednim imporcie – bez pobierania), **błąd**.
- Nowe pliki: kopia do Landing Zone, rejestracja (kto, kiedy, kolumny, sygnatura kolumn, typ raportu,
  liczba wierszy), wiersze w postaci surowej w bazie.
- Pliki wieloczęściowe sprawdzane jako zestaw (te same kolumny, łączna liczba wierszy).
- Import może uruchomić każda osoba z finansów w dowolnym momencie; historia importów jest widoczna
  dla wszystkich.
- MVP: `python -m ahd.etap1 import` (instrukcja `docs/mvp-etap1.md`).

### F05. Etap 1 przebiegu – Dane SAP zakresu

- Warunek: import plików SAP (F05a) wykonany; aplikacja pokazuje datę ostatniego importu i pliki,
  które pojawiły się od poprzedniego przebiegu.
- Wybór wierszy zakresu na podstawie elementów WBS ze słownika „Struktura projektowa” oraz okresu.
- Kontrole: daty w okresie przebiegu, plik nie zmienił się od importu (hash).
- Wiersze bez przypisania do żadnego zakresu są widoczne w etapie 4 (F08) jako nowe elementy.

### F06. Etap 2 – Słowniki

- Tabela słowników przebiegu: nazwa, zasięg (globalny / zakres), plik, przypięta wersja, wynik.
- Globalne są tylko przypinane. Zakresowe są importowane: ten sam hash = „bez zmian”,
  zmiana = „kandydat nowej wersji” (wersja powstanie po walidacji w etapie 3).

### F07. Etap 3 – Walidacja

- Walidacja plików SAP i kandydatów nowych wersji słowników wg reguł z rozdz. 6.
- Wynik: tabela problemów – waga (blokujący / ostrzeżenie), źródło, wiersz, kolumna, opis.
- **Błąd blokujący**: kandydat odrzucony, obowiązuje poprzednia wersja; raport błędów zapisany
  obok pliku (`<plik>.walidacja.xlsx`). Akcje: **Plik poprawiony – waliduj ponownie** albo
  **Kontynuuj na obowiązującej wersji**.
- Brak błędów blokujących: nowa wersja słownika utworzona i przypięta; ostrzeżenia trafiają do raportu.

### F08. Etap 4 – Łączenie źródeł

- Wywołanie logiki łączenia w bazie (logika do przedstawienia przez zespół, O14).
- Podsumowanie: koszt rzeczywisty okresu, liczba zmapowanych WBS, liczba WP, suma kontrolna
  (koszt po połączeniu = koszt z plików).
- **Wykrywanie elementów bez przypisania** w słowniku struktury:
  - element CES bez wiersza (w tym nowa definicja projektu) – z kosztem okresu,
  - element P1S bez wiersza lub bez pary w CES – z zaawansowaniem,
  - kontrola, że żaden element nie należy do słownika innego zakresu.
- Akcje:
  - **Eksportuj elementy do uzupełnienia** – plik `Slowniki\Propozycje\<Zakres>_nowe_elementy_<RunId>.xlsx`
    w układzie kolumn słownika, z wypełnionymi kluczami,
  - po uzupełnieniu słownika: nowa wersja, przypięcie, **przeliczenie od etapu 3**,
  - **Kontynuuj bez tych elementów** – tylko w przebiegu tygodniowym; decyzja w dzienniku,
    wartość poza EV pokazana w raporcie.
- Zamknięcie miesiąca: elementy z kosztem muszą być przypisane (D20).

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

- Kalkulacja w bazie; wynik jako **rewizja** (R1, R2…) z zapisem przypiętych wersji i plików.
- Tabela: projekt, BAC, BCWS, BCWP, ACWP, CPI, SPI (w tys. PLN, w SAC w tys. USD).
- Tydzień: oznaczenie „EV wstępne (nieformalne)”.
- Plik wynikowy `EV\<RRRR-MM>\<Zakres>_EV_<T<NN>|RRRR-MM>_R<n>.xlsx`.
- **Przelicz ponownie** – nowa rewizja, poprzednia zostaje.

### F15. Etap 10 (SAC) – Plik dla Cobra

- Koszt pracy przeliczony po bieżących stawkach na USD, zaawansowanie wg WP.
- Plik `EV\<RRRR-MM>\<Zakres>_<RRRR-MM>_Cobra_import.csv`.

### F16. Nowa wersja słownika w trakcie przebiegu

- Baner w przebiegu: „Dostępna nowsza wersja <słownik> vN (przebieg używa vM)”.
- **Kontynuuj na obecnej** – decyzja w dzienniku, baner znika dla tej wersji.
- **Przelicz od etapu 3** – przypięcie nowej wersji; etapy od walidacji do wykonania ponownie.

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
  pliki czekające na potwierdzenie, dostępne nowsze wersje słowników, zakresy z niekompletnymi słownikami.
- „Ostatnie zdarzenia”: przekrój dzienników wszystkich przebiegów.
- Dziennik przebiegu: czas, użytkownik, opis – od najnowszego.

### F20. Kontrola wersji i środowiska

- Przy starcie: zgodność wersji aplikacji i schematu bazy; niezgodność blokuje pracę z komunikatem,
  co zaktualizować.
- Profil TEST / PROD widoczny w nagłówku.

---

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

### 6.1 Wszystkie słowniki

| Poziom | Reguła | Waga |
|---|---|---|
| Plik | plik i arkusz istnieją, plik da się odczytać (nie zaszyfrowany) | blokujący |
| Struktura | wymagane kolumny obecne; brak zduplikowanych nagłówków; nieznane kolumny | blokujący / ostrzeżenie |
| Typy | daty, liczby, wartości yes/no; klucze WBS jako tekst (np. „1.10” nie może stać się 1.1); brak `#N/A` i błędów formuł | blokujący |
| Czystość | spacje na początku/końcu, spacje niełamliwe, różna wielkość liter | ostrzeżenie |
| Klucz | brak duplikatów klucza; brak nakładających się okresów ValidFrom–ValidTo; ValidFrom ≤ ValidTo | blokujący |
| Metadane | Owner, Version, LastUpdate wypełnione | blokujący |
| Wersjonowanie | zmieniony wiersz ma podbitą Version; usunięty wiersz zamiast zamknięcia ValidTo | blokujący / ostrzeżenie |

### 6.2 Struktura projektowa

| Reguła | Waga |
|---|---|
| wiersz ma co najmniej jeden z kluczy: P1S WBS lub CAS WBS | blokujący |
| para P1S WBS + CAS WBS nie powtarza się w nakładających się okresach | blokujący |
| element nie występuje w słowniku innego zakresu w nakładającym się okresie | blokujący |
| WP = yes wymaga CAM | blokujący |
| podobne zapisy tego samego CAM (np. „J. Kowalski” / „J.Kowalski”) | ostrzeżenie |
| Program / Project / Project Definition wypełnione | blokujący |

### 6.3 Harmonogram i budżet

| Reguła | Waga |
|---|---|
| element istnieje w słowniku struktury zakresu | blokujący |
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
| `<plik>.walidacja.xlsx` | obok pliku słownika | 3 |
| `<Zakres>_nowe_elementy_<RunId>.xlsx` | `Slowniki\Propozycje\` | 4 |
| `<Zakres>_AC_przeglad_<T<NN>>.xlsx`, `<Zakres>_ETC_wstepne_<T<NN>>.xlsx`, `<Zakres>_WBS_bez_mapowania.xlsx` | `Finanse\<RRRR-MM>\` | 5 |
| `<Zakres>_<RRRR-MM>_CAM_<CAM>.xlsx` | `CAM\<RRRR-MM>\Wyslane\` | 6 (zamknięcie) |
| `<Zakres>_EV_<T<NN>|RRRR-MM>_R<n>.xlsx` | `EV\<RRRR-MM>\` | 9 |
| `<Zakres>_<RRRR-MM>_Cobra_import.csv` | `EV\<RRRR-MM>\` | 10 (SAC) |

Nazwy plików są propozycją do potwierdzenia.

---

## 8. Scenariusze (przejście przez prototyp)

1. **Nowy zakres M28** – kreator → zakres zablokowany (puste słowniki) → uzupełnienie słowników →
   zakres gotowy.
2. **Tydzień F16** – błąd blokujący w słowniku struktury (nakładające się okresy) → poprawa →
   nowa wersja; w łączeniu wykryte nowe elementy z CES i P1S → eksport → uzupełnienie → przeliczenie;
   potwierdzenie plików; produkcja, uzupełnienia, EV wstępne.
3. **Odrzucenie plików dla finansów** – komentarz → powrót do łączenia.
4. **Nowa wersja stawek** – publikacja → baner w przebiegach → przeliczenie od etapu 3.
5. **Kontynuacja przebiegu S70i** rozpoczętego przez inną osobę.
6. **Zamknięcie miesiąca S70i (SAC)** – pliki CAM, odrzucenie pliku (zmiana poza polami), ponowny
   import, 100% CAM, EV, plik Cobra, zamrożenie okresu.

---

## 9. Poza zakresem wersji 1

- Formularz edycji słowników w aplikacji (słowniki pozostają w Excelu).
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
