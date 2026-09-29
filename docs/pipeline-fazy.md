# AHD – Fazy pipeline (koncepcja)

Wersja: 0.2 (koncepcja do dyskusji)
Powiązane: `docs/architektura.md`, `docs/funkcjonalnosc.md`, `docs/mvp-etap1.md` (faza G1–G2 – działa).

---

## 1. Zasady wspólne dla wszystkich faz („kontrakt klocka”)

Każda faza jest osobnym klockiem o tym samym kształcie:

| Element | Znaczenie |
|---|---|
| **Wejście** | czyta **wyłącznie** dane utrwalone przez wcześniejsze fazy (tabele w bazie, zarejestrowane pliki, przypięte wersje słowników) – nigdy „z pamięci” poprzedniego kroku |
| **Bramka wejścia** | warunki, bez których faza nie ruszy (np. poprzednia faza zakończona, słownik ma zatwierdzoną wersję) |
| **Przetwarzanie** | logika biznesowa w bazie (procedury SQL); Python orkiestruje, czyta i zapisuje pliki |
| **Wyjście** | tabele w bazie + ewentualne pliki; każdy rekord ma identyfikator pochodzenia (import, przebieg, wersja) |
| **Bramka wyjścia** | kontrole, które muszą przejść, żeby następna faza mogła ruszyć |
| **Zapis stanu** | status fazy, kto, kiedy, **identyfikatory wejść** (hashe plików, wersje słowników, rewizje) – w `meta.EtapPrzebiegu` i dzienniku |
| **Idempotencja** | ponowne uruchomienie z tymi samymi wejściami daje ten sam wynik i nie dubluje danych |
| **Unieważnienie** | zmiana wejść fazy oznacza ją i wszystkie późniejsze jako **nieaktualne** |

Komunikacja między klockami odbywa się **przez bazę**: faza N zapisuje wynik i status, faza N+1 sprawdza
status i identyfikatory wejść. Dzięki temu fazy mogą być wykonywane przez różne osoby, w różne dni,
z różnych komputerów.

---

## 2. Mapa faz

```mermaid
flowchart TD
  subgraph G["Fazy globalne – bez zakresu, w dowolnym momencie"]
    G1["G1 Pobranie RABIT<br/>(WebDAV → Do_importu, każda wersja)"] --> G2["G2 Import i rozładowanie<br/>(definicje plików → tabele z markerem)"]
    G2 --> G3["G3 Klasyfikacja elementów WBS<br/>(nowe elementy, klucze → propozycje)"]
    G4["G4 Publikacja słowników globalnych"]
  end
  subgraph P["Przebieg zakresu – tygodniowy lub zamknięcie miesiąca"]
    P1["P1 Dane zakresu<br/>(wybór i przypięcie danych)"] --> P2["P2 Słowniki zakresu"]
    P2 --> P3["P3 Walidacja"]
    P3 --> P4["P4 Łączenie źródeł"]
    P4 --> P5["P5 Pliki dla finansów<br/>+ potwierdzenie"]
    P5 --> P6["P6 Zaawansowanie<br/>(produkcja / pliki CAM)"]
    P6 --> P7["P7 Uzupełnienia / import CAM"]
    P7 --> P8["P8 Walidacja zaawansowania"]
    P8 --> P9["P9 Generowanie EV"]
    P9 --> P10["P10 Plik Cobra (SAC)"]
    P9 --> Z["Zamknięcie okresu"]
    P10 --> Z
  end
  G3 --> P1
  G4 --> P1
```

| Faza | Zakres | Kto | Kiedy | Stan |
|---|---|---|---|---|
| G1 Pobranie RABIT | globalna | dowolna osoba z finansów / harmonogram | co najmniej tak często jak RABIT | **działa** |
| G2 Import i rozładowanie | globalna | j.w. | po G1 | rejestr plików działa; rozładowanie po zdefiniowaniu plików |
| G3 Klasyfikacja elementów WBS | globalna | automatycznie po G2, decyzje – finanse | po G2 | koncepcja |
| G4 Słowniki globalne | globalna | właściciel słownika | przy zmianie | koncepcja |
| P1–P10 | zakres | osoba prowadząca zakres | tydzień / zamknięcie | prototyp |

---

## 3. Fazy globalne

Kontekst: **RABIT** automatycznie (także pod nieobecność pracownika) zrzuca wyniki różnych raportów SAP
na SharePoint i przy każdym uruchomieniu **nadpisuje ten sam plik**. Na etapie pobierania nie wiadomo,
czego dotyczy plik – o tym decyduje dopiero G2 na podstawie definicji plików (do ustalenia później).

### G1. Pobranie plików RABIT *(działa – do uzupełnienia o wersjonowanie)*

| | |
|---|---|
| **Cel** | Nie zgubić żadnej wersji raportu zrzuconej przez RABIT i mieć ją na dysku firmy. |
| **Wejście** | Folder RABIT na SharePoint (WebDAV, konto Windows). |
| **Jak pracuje** | Porównuje pliki źródłowe z ostatnio pobranymi (rozmiar, data modyfikacji); kopiuje tylko nowe i zmienione. Nie interpretuje treści. |
| **Ryzyko** | RABIT nadpisuje plik – jeśli między dwoma uruchomieniami RABIT nie było pobrania, poprzednia wersja przepada. Dlatego G1 powinien **zachowywać każdą pobraną wersję** (np. `Do_importu\<nazwa>__<data modyfikacji>.xlsx` albo archiwum dzienne) i być uruchamiany co najmniej tak często jak RABIT (np. harmonogram zadań Windows). |
| **Kontrole** | Dostępność WebDAV; limit 50 MB (większy plik – ręcznie). |
| **Efekt** | Każda wersja każdego pliku RABIT w `00_Global\RABIT\Do_importu`. |
| **Przekazanie** | Folder `Do_importu` jest wejściem G2. |

### G2. Import i rozładowanie do tabel *(działa częściowo: rejestr plików; rozładowanie – po zdefiniowaniu plików)*

| | |
|---|---|
| **Cel** | Każdą nową wersję pliku **rozpoznać** i załadować jej dane do **właściwej tabeli** z markerem pochodzenia – tak, żeby dało się odtworzyć dowolny wcześniejszy stan i porównać dwa stany. |
| **Wejście** | Pliki w `Do_importu`; słownik **Definicje plików RABIT** (do zdefiniowania później). |
| **Słownik definicji plików** | wzorzec nazwy pliku (RABIT nadpisuje ten sam plik, więc nazwa jest stała) → typ raportu → tabela docelowa → mapowanie kolumn na pola tabeli → **oczekiwana sygnatura kolumn** (kontrola, czy układ raportu się nie zmienił) → klucz wiersza. |
| **Jak pracuje** | 1) Rejestr pliku: hash SHA-256, duplikat = pomijany (działa). 2) Dopasowanie do definicji po nazwie + sprawdzenie sygnatury kolumn. 3) Załadowanie wierszy do tabeli typu raportu (konwersja typów, daty, liczby, WBS jako tekst). |
| **Marker pochodzenia** | Każdy wiersz ma: `IdImportu` (kto, kiedy), `Sha256` pliku, **datę raportu** (data modyfikacji pliku w RABIT = kiedy SAP wygenerował dane), typ raportu. Jedna wersja pliku = jeden **snapshot** raportu. |
| **Archiwalność** | Tabele tylko dopisywane (bez nadpisywania). Widok „najnowszy stan” wybiera ostatni snapshot każdego raportu; porównanie dwóch snapshotów pokazuje różnice (nowe / zmienione / usunięte wiersze). Oryginał pliku zostaje w Landing Zone. |
| **Kontrole** | Plik bez definicji → status „niezdefiniowany” (zarejestrowany i zarchiwizowany, **nie ładowany**; alert). Zmieniona sygnatura kolumn → „układ zmieniony” (nie ładowany, alert). Suma kontrolna: liczba wierszy i sumy kwot przed/po. |
| **Efekt** | Tabele raportów z markerami + rejestr plików ze statusem: załadowany / duplikat / niezdefiniowany / układ zmieniony / błąd. |
| **Przekazanie** | Nowe snapshoty są wejściem G3 (elementy WBS) i P1 (dane zakresu). Po dopisaniu definicji plik „niezdefiniowany” można załadować ponownie z Landing Zone – bez ponownego pobierania. |

### G3. Klasyfikacja elementów WBS *(koncepcja)*

| | |
|---|---|
| **Cel** | Wiedzieć o **każdym elemencie WBS** występującym w danych, do którego projektu / zakresu należy – i szybko wychwycić nowe. |
| **Wejście** | Elementy WBS (CES i P1S) z nowych snapshotów G2; słowniki „Struktura projektowa” wszystkich zakresów; **reguły kluczy** (np. segment WBS / Project Definition / Business Area → projekt → zakres). |
| **Jak pracuje** | 1) Zbiera unikalne elementy z nowych snapshotów i porównuje z rejestrem znanych elementów. 2) Dla nowych stosuje reguły kluczy i wyznacza **propozycję** projektu i zakresu. 3) Tworzy listę do decyzji i eksportuje propozycje wierszy do słowników struktury właściwych zakresów. |
| **Kontrole** | Element pasujący do kilku zakresów → konflikt (do decyzji). Element bez dopasowania → „nieprzypisany” (alert na pulpicie, z kosztem). |
| **Efekt** | Rejestr elementów WBS: przypisany / zaproponowany / nieprzypisany / konflikt, z datą pierwszego wystąpienia i snapshotem źródłowym. |
| **Przekazanie** | P1 bierze do zakresu elementy przypisane w słowniku struktury; propozycje trafiają do właściciela zakresu (akceptacja = wpis w słowniku). P4 nie wykrywa już nowych elementów – korzysta z rejestru G3 i blokuje zamknięcie, jeśli element z kosztem jest nieprzypisany. |

### G4. Publikacja słowników globalnych *(koncepcja)*

| | |
|---|---|
| **Cel** | Udostępnić zatwierdzoną wersję słownika wspólnego (stawki wydziałów, kalendarz okresów, kursy USD/PLN, **definicje plików RABIT**, **reguły kluczy WBS**). |
| **Wejście** | Plik Excel słownika w `00_Global\Slowniki`. |
| **Jak pracuje** | Kopia do Landing Zone + hash → walidacja (rozdz. 6 specyfikacji) → nowa wersja tylko przy zmianie treści. |
| **Kontrole** | Błąd blokujący odrzuca wersję; obowiązuje poprzednia. |
| **Efekt** | `dict.WersjaSlownika` + dane słownika z historią (SCD2). |
| **Przekazanie** | Przebieg przypina wersję w P1/P2; aktywne przebiegi ze starszą wersją dostają decyzję „kontynuuj / przelicz od P3”. |

---

## 4. Fazy przebiegu zakresu

Przebieg = jeden zakres × okres (tydzień albo zamknięcie miesiąca). Stan przebiegu jest w bazie,
fazy mogą być wykonywane w różne dni i przez różne osoby z finansów.

### P1. Dane zakresu

| | |
|---|---|
| **Cel** | Wybrać z danych globalnych **tylko to, co należy do zakresu i okresu**, i zamrozić ten wybór na czas przebiegu. |
| **Wejście** | Tabele raportów z G2 (najnowsze snapshoty); rejestr elementów z G3; słownik „Struktura projektowa” zakresu (CAS WBS / P1S WBS); kalendarz okresów. |
| **Jak pracuje** | Filtruje wiersze po elementach WBS zakresu i datach okresu (koszt okresu i narastająco). **Przypina zestaw plików** (lista hashy) – analogicznie do przypinania wersji słowników. |
| **Kontrole** | Ostatni import G2 nie starszy niż X dni; brak plików „niezdefiniowanych” / „układ zmieniony” (ostrzeżenie); elementy zakresu nieprzypisane w G3 (ostrzeżenie; zamknięcie – blokada); daty w okresie. |
| **Efekt** | Snapshot danych zakresu `hist.DaneZakresu` (RunId, Sha256) + lista przypiętych plików. |
| **Przekazanie** | P2–P4 pracują wyłącznie na tym snapshocie. Nowy import w trakcie przebiegu → informacja „dostępne nowsze dane” i decyzja: kontynuuj / przelicz od P1. |

### P2. Słowniki zakresu

| | |
|---|---|
| **Cel** | Mieć aktualne, sprawdzone słowniki zakresu (struktura P1S↔CES, harmonogram i budżet, stawki CAS). |
| **Wejście** | Pliki słowników w `Zakresy\<Zakres>\Slowniki`; wersje słowników globalnych z G4. |
| **Jak pracuje** | Import słowników zakresu (hash bez zmian = brak nowej wersji; zmiana = kandydat); przypięcie wersji globalnych. |
| **Efekt** | Kandydaci nowych wersji + lista przypiętych wersji w przebiegu. |
| **Przekazanie** | Kandydaci trafiają do walidacji P3; nowa wersja powstaje dopiero po jej przejściu. |

### P3. Walidacja

| | |
|---|---|
| **Cel** | Nie dopuścić błędnych słowników i danych do obliczeń. |
| **Wejście** | Kandydaci słowników z P2; snapshot danych z P1. |
| **Jak pracuje** | Reguły deklaratywne per słownik (struktura, typy, klucze, okresy ważności, reguły biznesowe, odwołania między słownikami, wersjonowanie) + kontrole danych (np. wydział bez stawki). |
| **Kontrole** | Błąd blokujący → kandydat odrzucony, raport błędów obok pliku, obowiązuje poprzednia wersja. |
| **Efekt** | Zatwierdzone wersje słowników (przypięte) + lista ostrzeżeń. |
| **Przekazanie** | P4 startuje tylko bez błędów blokujących (albo po świadomej decyzji „kontynuuj na poprzedniej wersji”). |

### P4. Łączenie źródeł

| | |
|---|---|
| **Cel** | Zbudować jeden spójny obraz zakresu: koszt rzeczywisty (ACWP) przypisany do WP, CAM, kategorii kosztów, w walucie raportowej. |
| **Wejście** | Snapshot danych (P1), przypięte słowniki (P3), stawki i kursy. |
| **Jak pracuje** | Logika łączenia w bazie (**do przedstawienia przez zespół – O14**): mapowanie WBS CES → WP, przeliczenia stawek (CAS / SAP), przeliczenie godzin na koszt, waluta. |
| **Kontrole** | Elementy bez przypisania (z rejestru G3) – tydzień: można pominąć z decyzją, zamknięcie: nie; suma kontrolna (koszt po połączeniu = koszt ze snapshotu). |
| **Efekt** | `ev.KosztWP` (RunId, WP, okres, kwoty, pochodzenie) + raport pokrycia. |
| **Przekazanie** | P5 generuje z tego pliki dla finansów; P9 używa jako ACWP. |

### P5. Pliki dla finansów + potwierdzenie

| | |
|---|---|
| **Cel** | Dać finansom do sprawdzenia wynik łączenia, zanim zostanie użyty dalej (i wysłany do CAM). |
| **Wejście** | `ev.KosztWP`, raport pokrycia z P4. |
| **Jak pracuje** | Generuje pliki do `Zakresy\<Zakres>\Finanse\<RRRR-MM>\` (zawartość – O16); czeka na potwierdzenie w aplikacji. |
| **Kontrole** | Potwierdzenie (może je wykonać osoba prowadząca) albo odrzucenie z komentarzem. |
| **Efekt** | Pliki + zapis potwierdzenia (kto, kiedy). |
| **Przekazanie** | Potwierdzenie odblokowuje P6; odrzucenie cofa do P4 (P5+ nieaktualne). |

### P6–P8. Zaawansowanie

Dwa warianty, ten sam cel: **dla każdego WP wartość zaawansowania z zapisanym pochodzeniem**.

| | Tydzień | Zamknięcie miesiąca |
|---|---|---|
| **P6** | Pobranie zaawansowania z danych produkcyjnych P1S (np. `vAHDD`, źródło – O10) po P1S WBS ze słownika | Generowanie **pliku na CAM** (WP danego CAM z kolumny CAM słownika; ukryty identyfikator przebiegu; zablokowane komórki) do `CAM\…\Wyslane` |
| **P7** | Uzupełnienie braków przez analityka (metody – O11) | Import plików z `CAM\…\Zwrocone` (kontrola identyfikatora, okresu, zmian poza polami); status per CAM; może trwać dni |
| **P8** | Walidacja: 0–100%, spadki vs poprzedni okres, EV ≤ BAC; wartości spoza CAM dozwolone | Te same kontrole + **100% wartości od CAM** |
| **Efekt** | `ev.Zaawansowanie` (WP, okres, wartość, pochodzenie: PRODUKCJA / ANALITYK / CAM, kto, kiedy) | j.w., pochodzenie wyłącznie CAM |
| **Przekazanie** | P9 czyta `ev.Zaawansowanie` i status P8 | j.w. |

### P9. Generowanie EV

| | |
|---|---|
| **Cel** | Policzyć wskaźniki EV zakresu w sposób odtwarzalny. |
| **Wejście** | `ev.KosztWP` (ACWP), `ev.Zaawansowanie`, budżet i harmonogram (BAC, BCWS) z przypiętych słowników, ETC (źródło – O15). |
| **Jak pracuje** | Kalkulacja w bazie: BCWS, BCWP, ACWP, CPI, SPI, EAC, TCPI – na WP, CAM, projekt, zakres. |
| **Kontrole** | Spójność sum na poziomach; EV ≤ BAC. |
| **Efekt** | **Rewizja** wyników `ev.Wynik` (R1, R2…) z listą przypiętych wejść (hashe plików, wersje słowników, rewizja zaawansowania) + plik `EV\<RRRR-MM>\…`. |
| **Przekazanie** | Tydzień: EV wstępne (koniec przebiegu). Zamknięcie: P10 (SAC) i zatwierdzenie okresu. |

### P10. Plik dla Cobra (tylko SAC)

| | |
|---|---|
| **Cel** | Przekazać Sikorsky dane w formacie Cobra. |
| **Wejście** | Rewizja EV z P9, stawki bieżące, kurs USD. |
| **Efekt** | `EV\<RRRR-MM>\<Zakres>_<RRRR-MM>_Cobra_import.csv`. |

### Zamknięcie okresu

| | |
|---|---|
| **Cel** | Zamrozić formalny wynik miesiąca. |
| **Jak pracuje** | Zatwierdzenie w aplikacji; przebieg staje się tylko do odczytu. Ponowne przeliczenie = nowa rewizja w historii. |
| **Efekt** | Zamrożona rewizja EV – podstawa raportów i porównań kolejnych okresów. |

---

## 5. Co przepływa między fazami (podsumowanie)

| Z → Do | Nośnik | Identyfikator łączący |
|---|---|---|
| G1 → G2 | pliki w `Do_importu` | nazwa, rozmiar, data |
| G2 → G3, P1 | tabele raportów (snapshoty) | IdImportu + Sha256 + data raportu |
| G3 → P1, P4 | rejestr elementów WBS, propozycje do słowników | element WBS + snapshot pierwszego wystąpienia |
| G4 → P1/P2 | `dict.WersjaSlownika` | numer wersji |
| P1 → P2–P4 | `hist.DaneZakresu`, lista przypiętych plików | RunId + Sha256 |
| P2 → P3 | kandydaci wersji | RunId + hash pliku słownika |
| P3 → P4 | przypięte wersje słowników | RunId + wersje |
| P4 → P5, P9 | `ev.KosztWP` | RunId |
| P5 → P6 | potwierdzenie | RunId + status |
| P6/P7 → P8 → P9 | `ev.Zaawansowanie` | RunId + pochodzenie |
| P9 → P10, zamknięcie | `ev.Wynik` | RunId + rewizja |

---

## 6. Otwarte kwestie wynikające z koncepcji

| # | Kwestia |
|---|---|
| K1 | Słownik **Definicje plików RABIT** (nazwa → typ → tabela → mapowanie kolumn → sygnatura) – gdy będzie wiadomo, które raporty są potrzebne |
| K5 | Wersjonowanie w G1 (RABIT nadpisuje plik) i harmonogram pobierania |
| K6 | Reguły kluczy w G3: po czym rozpoznać projekt (segment WBS, Project Definition, Business Area…) i czy reguła tylko proponuje, czy przypisuje |
| K7 | Retencja snapshotów (wolumen: setki tysięcy wierszy × raporty × tygodnie) |
| K2 | Co zawierają poszczególne pliki RABIT (koszty, zobowiązania, „PZL roll”, „hedge”, „workaround”) i które fazy ich używają |
| K3 | Próg „świeżości” danych przed P1 (ile dni od ostatniego importu) |
| K4 | Koszt okresu vs narastająco – jak liczyć ACWP przy korektach wstecznych w SAP |
| O10, O11, O14–O17 | jak w `docs/architektura.md` / `docs/funkcjonalnosc.md` |
