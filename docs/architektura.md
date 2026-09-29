# AHD – Architektura rozwiązania (propozycja do akceptacji)

Wersja: 0.2 (draft)
Status: **do akceptacji** – dokument nie zawiera implementacji; kod powstaje dopiero po zatwierdzeniu.

Dokument uzupełnia `readme.md` o decyzje architektoniczne podjęte w trakcie dyskusji
oraz wskazuje punkty newralgiczne i otwarte decyzje.

---

## 1. Podjęte decyzje

| # | Decyzja | Uzasadnienie |
|---|---|---|
| D1 | Aplikacja w **Pythonie**, uruchamiana **lokalnie** przez użytkownika | brak konieczności uruchamiania na serwerze, każdy może mieć własną instancję |
| D2 | Interfejs **przeglądarkowy** (lokalny serwer na `localhost`) | wygoda użytkownika, ta sama aplikacja może później trafić na serwer |
| D3 | **Centralna, zdalna baza MS SQL** jako jedyne źródło stanu | każdy użytkownik widzi, co już przetworzono, kto i kiedy |
| D4 | **Logika biznesowa w bazie** (procedury, widoki), Python = orkiestracja, pliki, walidacja struktury, generowanie Excel | wynik nie zależy od wersji aplikacji na danym komputerze |
| D5 | Logowanie i uprawnienia **po AD** (Windows Authentication) | brak haseł na stanowiskach, audyt „kto co zrobił” z automatu |
| D6 | Developer pracuje na **TEST**, wdrożenia na **PROD** wykonuje admin | wymóg organizacyjny PZL |
| D7 | Pipeline uruchamiany **co tydzień (poniedziałek)**, okres rozliczeniowy **miesięczny** | rytm pracy zespołu |
| D8 | Wolumen danych przez sieć nie stanowi problemu | ładowanie z aplikacji lokalnej do bazy zdalnej |
| D9 | Przypisanie użytkowników do zakresów utrzymywane w **słowniku**, zarządzanym przez finanse | jedno miejsce konfiguracji |
| D10 | Pliki pośrednie dla finansów **wymagają potwierdzenia** przed generowaniem plików CAM | kontrola jakości przed wysłaniem do CAM |
| D11 | Dopuszczalne ponowne przeliczenie EV | z zachowaniem poprzednich rewizji |
| D12 | Pliki oryginalne przechowywane w centralnym folderze (Landing Zone), w bazie ścieżka + hash | odtwarzalność bez przyrostu bazy |

---

## 2. Model pipeline – rekomendacja: wszystko per program, słowniki globalne jako osobny proces

### 2.1 Problem

Pipeline globalny rozgałęziający się od etapu 4 wymaga przekazywania odpowiedzialności
między osobami i synchronizacji programów (np. F-16 nie powinien czekać na pliki z Cobra
dla programów SAC). Komplikuje to współbieżność i odpowiedzialność.

### 2.2 Propozycja

1. **Każdy przebieg pipeline dotyczy jednego zakresu** (program lub pula małych projektów
   – „Zakres raportowy”). Brak pipeline globalnego.
2. **Słowniki globalne nie są etapem przebiegu**, tylko osobnym procesem „publikacji słownika”
   (import + walidacja + nowa wersja). Może go wykonać uprawniona osoba w dowolnym momencie.
3. Przebieg w momencie startu **przypina (pin)** konkretne wersje słowników globalnych
   i słowników swojego zakresu. Do końca przebiegu pracuje na tych wersjach.
4. Jeśli w trakcie przebiegu pojawi się nowsza wersja słownika, aplikacja pokazuje
   informację „dostępna nowsza wersja słownika X” – użytkownik decyduje:
   kontynuuje na starej wersji albo **przelicza od etapu 3** (etapy późniejsze zostają unieważnione).

Efekty:
- współbieżność ograniczona do: jeden aktywny przebieg na zakres i tydzień (blokada w bazie),
  publikacja słownika – blokada na słownik,
- brak przekazywania pipeline między osobami – każdy zakres jest niezależny,
- różne typy projektów (SAC / CAS / wewnętrzne) mają **własne szablony etapów**
  (np. etap „pliki Cobra” występuje tylko w SAC).

### 2.3 Etapy przebiegu

| # | Etap | Bramka wyjścia |
|---|---|---|
| 1 | Pobranie plików SAP (ręcznie z SharePoint lub automatycznie – RABIT) | komplet plików, hash, zgodny okres, zgodny układ kolumn w plikach wieloczęściowych |
| 2 | Import słowników zakresu (+ przypięcie wersji słowników globalnych) | brak zmian → brak nowej wersji; zmiana → nowa wersja |
| 3 | Walidacja wszystkich importów | brak błędów blokujących |
| 4 | Łączenie źródeł (logika do przedstawienia) | kontrole pokrycia (np. WBS bez mapowania) |
| 5 | Pliki pośrednie dla finansów | **potwierdzenie przez finanse** (kto, kiedy, komentarz); odrzucenie → powrót do 4 |
| 6 | Pliki do uzupełnienia przez CAM | szablony z identyfikatorem przebiegu |
| 7 | Import plików CAM (po kilku dniach) | plik pasuje do wygenerowanego szablonu i przebiegu |
| 8 | Walidacja danych CAM | kompletność, zakresy, spójność z budżetem |
| 9 | Generowanie EV | zapis użytych wersji słowników i danych (rewizja) |

### 2.4 Przebieg jako trwały obiekt (maszyna stanów)

- Przebieg (`Run`) żyje w bazie tygodniami: ma status każdego etapu, historię zdarzeń,
  wersje danych wejściowych każdego etapu.
- Aplikację można zamknąć między etapami – po otwarciu stan jest odczytywany z bazy.
- Przebieg może kontynuować **inna uprawniona osoba** (np. zastępstwo), każda akcja jest
  zapisana z użytkownikiem AD.
- Ponowne wykonanie etapu unieważnia etapy późniejsze (status „nieaktualny”).
- Tygodniowe przebiegi w miesiącu są kolejnymi przebiegami tego samego okresu;
  przebieg zamykający miesiąc oznaczany jest jako **zamknięcie okresu** i po zatwierdzeniu
  jest zamrażany. Ponowne przeliczenie tworzy nową rewizję.

---

## 3. Struktura folderów i konfiguracja zakresu

Proponowana hierarchia (korzeń na SharePoint / dysku sieciowym):

```
AHD/
├── 00_Global/
│   └── Slowniki/                  słowniki globalne (np. stawki, CAM, kalendarz okresów)
├── 01_LandingZone/                archiwum oryginałów (tylko zapis przez aplikację)
│   └── <Zakres>/<RRRR-MM>/<RunId>/
└── Zakresy/
    └── <Zakres>/                  np. F16, S70i, PULA_MALE_PROJEKTY
        ├── Slowniki/              słowniki dedykowane zakresowi
        ├── SAP/<RRRR-MM>/Tydz<NN>/
        ├── Finanse/<RRRR-MM>/
        ├── CAM/<RRRR-MM>/Wyslane/
        ├── CAM/<RRRR-MM>/Zwrocone/
        └── EV/<RRRR-MM>/
```

- **Uruchomienie nowego zakresu** odbywa się z aplikacji (kreator): tworzy strukturę folderów,
  wzorcowe pliki słowników, rejestruje zakres w bazie (typ SAC/CAS/wewnętrzny, szablon etapów,
  lista słowników, powiązane projekty/WBS).
- **Konfiguracja zakresu jest w bazie**, folder jest tylko magazynem plików.
- Aplikacja przy każdym uruchomieniu sprawdza zgodność folderów i słowników z konfiguracją
  (brakujące pliki, zmienione nazwy, niedozwolone formaty).
- Ścieżki w bazie zapisywane są **względem korzenia** – lokalny korzeń (np. ścieżka OneDrive,
  która jest różna u każdego użytkownika) ustawia się w lokalnej konfiguracji aplikacji.

---

## 4. Słowniki i wersjonowanie

### 4.1 Przepływ

```
Excel (właściciel) → kopia do Landing Zone (+SHA-256)
  → STG (surowe wiersze, tekst + numer wiersza Excel)
  → walidacja → [błędy: raport dla właściciela, wersja odrzucona]
  → treść bez zmian: brak nowej wersji
  → treść zmieniona: nowa DictionaryVersion + historia wierszy (SCD2)
  → widoki: "aktualne" oraz "na wersję / na dzień"
```

### 4.2 Dwie osie czasu

- **biznesowa** – ValidFrom / ValidTo z Excela (od kiedy obowiązuje stawka),
- **techniczna** – wersja słownika / przebieg, w którym wartość była znana.

Dzięki przypinaniu wersji do przebiegu każdy raport EV można odtworzyć 1:1.

### 4.3 Zakres słowników

- **globalne** – wspólne dla wszystkich zakresów,
- **zakresowe** – dedykowane zakresowi lub puli projektów,
- ten sam słownik (np. mapowanie WBS) nie może mieć dwóch źródeł prawdy – aplikacja
  pilnuje, że dany klucz należy tylko do jednego zakresu.

### 4.4 Walidacja (poziomy)

| Poziom | Przykłady | Waga |
|---|---|---|
| Plik | brak pliku/arkusza, plik zaszyfrowany lub zablokowany | blokujący |
| Struktura | brak lub zmiana nagłówka, duplikat nagłówka | blokujący |
| Typy | data jako tekst, „1.10” → 1.1, zera wiodące, `#N/A` | blokujący |
| Czystość | spacje końcowe/niełamliwe, wielkość liter | ostrzeżenie |
| Klucz | duplikaty, nakładające się okresy ValidFrom/ValidTo | blokujący |
| Reguły biznesowe | start > koniec, WP=yes bez CAM, stawka ≤ 0 | blokujący / ostrzeżenie |
| Między słownikami | CAM spoza słownika CAM, wydział bez stawki na rok | blokujący |
| Wersjonowanie | zmiana bez podbicia Version, usunięty wiersz zamiast zamknięcia ValidTo | blokujący / ostrzeżenie |
| Pokrycie danych (po etapie 4) | WBS z SAP bez mapowania | ostrzeżenie + raport |

Zasada: błąd blokujący odrzuca **całą** nową wersję słownika; obowiązuje ostatnia poprawna.
Reguły walidacji są opisane deklaratywnie (konfiguracja per słownik), nie „zaszyte” w kodzie.

---

## 5. Pliki SAP

- Jeden, dwa lub więcej plików na projekt (limit wierszy Excela) – aplikacja traktuje je
  jako **zestaw** jednego źródła: identyczny układ kolumn, brak duplikatów między częściami,
  zgodny okres, sumy kontrolne.
- Rekomendacja: jeśli RABIT pozwala, eksport do **CSV/TXT** zamiast XLSX (brak limitu wierszy,
  szybszy odczyt, brak konwersji typów przez Excel).

---

## 6. Pliki CAM

- Szablon zawiera ukryty arkusz z: ID przebiegu, zakres, okres, CAM, wersja szablonu.
- Komórki poza polami do uzupełnienia są zablokowane.
- Import odrzuca plik: bez identyfikatora, z innego przebiegu/okresu, zmodyfikowany poza polami.
- Rekomendacja: **jeden plik na CAM** (w ramach zakresu) – jednoznaczna odpowiedzialność,
  równoległa praca bez konfliktów współedycji na SharePoint, możliwość częściowego importu;
  dodatkowo zbiorczy plik programu tylko do odczytu. *(decyzja otwarta)*

---

## 7. Aplikacja

- Lokalny serwer WWW w Pythonie + przeglądarka. Cały stan w bazie, aplikacja jest bezstanowa.
- Długie operacje (import kilkuset tysięcy wierszy) wykonywane w tle z paskiem postępu.
- Baza przechowuje **minimalną wymaganą wersję aplikacji** oraz **wersję schematu** –
  niezgodna aplikacja odmawia pracy.
- Profile konfiguracji: TEST / PROD; brak haseł w konfiguracji (Windows Authentication).
- Dystrybucja: repozytorium / paczka na udziale sieciowym; instalacja Pythona i pakietów
  możliwa na stanowiskach. *(forma paczki do ustalenia)*

---

## 8. Baza danych

Schematy (propozycja):

| Schemat | Zawartość |
|---|---|
| `meta` | zakresy, przebiegi, etapy, zdarzenia, blokady, pliki (ścieżka, hash), wersje aplikacji/schematu |
| `stg` | surowe dane z plików (per przebieg) |
| `dict` | definicje słowników, wersje, dane słownikowe z historią |
| `hist` | snapshoty danych źródłowych (SAP, CAM) |
| `ev` | wyniki łączenia i kalkulacje EV (rewizje) |
| `sec` | role, przypisania użytkowników do zakresów |

- Część danych źródłowych pochodzi z `splmcd03` (`PZLPROD.LOG`, `PZL_SAP`) – sposób dostępu
  (ta sama instancja / linked server / odczyt przez aplikację) zależy od wyboru serwera AHD.
- Zmiany schematu jako numerowane skrypty migracyjne w repozytorium; admin wdraża na PROD.

### 8.1 Bezpieczeństwo

- Grupy AD → role bazodanowe: `ahd_analyst`, `ahd_finance_approver`, `ahd_dict_owner`, `ahd_admin`.
- Użytkownicy **nie mają praw do tabel** – wyłącznie EXECUTE na procedurach i SELECT na widokach.
- **Uprawnienia do zakresu sprawdzane w bazie** (procedury), nie tylko w aplikacji – aplikacja
  działa lokalnie, więc użytkownik mógłby połączyć się z bazą z pominięciem aplikacji.
- Słownik przypisań użytkowników jest krytyczny dla bezpieczeństwa – importować go może
  tylko rola finansów.

---

## 9. Punkty newralgiczne

1. Rozbieżne wersje aplikacji u użytkowników → logika w bazie + kontrola wersji.
2. Długo trwające przebiegi (dni) → trwały stan w bazie, przejmowanie przebiegu, unieważnianie etapów.
3. Zmiana słownika w trakcie przebiegu → przypinanie wersji, świadoma decyzja o przeliczeniu.
4. Ścieżki różne u różnych użytkowników (OneDrive) → ścieżki względne + lokalny korzeń.
5. Pliki otwarte / w trakcie edycji / współedycja SharePoint → kopia do Landing Zone, hash, publikacja.
6. Pliki CAM zmienione poza polami lub z innego przebiegu → identyfikator i blokady w szablonie.
7. Excel zmieniający typy danych (WBS, daty, zera wiodące) → walidacja typów, preferencja CSV dla SAP.
8. Etykiety poufności / szyfrowanie plików (Purview, IRM) → do weryfikacji z IT.
9. Uprawnienia omijane przez bezpośrednie połączenie z bazą → kontrola w procedurach.
10. Istniejące słowniki w `PZLPROD.LOG` (`WBS`, `Stanowiska`, `LearningCurve`, `PeriodDates`)
    → ryzyko dwóch źródeł prawdy; wymaga decyzji.
11. Świeżość `vAHDD` zależy od przebiegu `uspUpdateAHDD` → kontrola aktualności przed etapem 1/4.

---

## 10. Otwarte decyzje

| # | Pytanie | Rekomendacja |
|---|---|---|
| O1 | Model: wszystko per zakres + słowniki globalne jako osobny proces z przypinaniem wersji? | tak |
| O2 | Korzeń folderów: SharePoint (sync OneDrive) czy dysk sieciowy? | jeden korzeń, ścieżki względne |
| O3 | Plik CAM: na CAM czy na program? | na CAM |
| O4 | Czy tygodniowe przebiegi obejmują etapy CAM, czy CAM tylko raz w miesiącu? | do ustalenia |
| O5 | Serwer / baza dla AHD | osobna baza `AHD` |
| O6 | Forma dystrybucji aplikacji (repozytorium + skrypt instalacyjny / paczka) | do ustalenia |
| O7 | Czy RABIT może eksportować CSV/TXT? | CSV preferowany |
| O8 | Czy CAM korzystają z aplikacji, czy tylko z plików? | tylko pliki (etap 1 wersji) |
| O9 | Los słowników w `PZLPROD.LOG` | do ustalenia z właścicielami |
