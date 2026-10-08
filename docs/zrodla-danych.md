# PZL-EV – Źródła danych

Zakres: źródła danych PZL-EV i ich znaczenie (kontrakt danych) – definicja źródła, raporty RABIT, struktura P1S,
zaawansowanie z produkcji, pliki CAM, reguły walidacji źródeł.

Powiązane: `docs/pipeline-fazy.md` (G1 – przebieg importu, P5–P6 – pobranie zaawansowania),
`docs/model-danych.md` (warstwy i wersjonowanie), `docs/mapowanie-ces-p1s.md` (raport mapowań SAP↔CES).

---

## 1. Lista źródeł

| Źródło | System | Dane | Pozyskanie |
|---|---|---|---|
| Raporty CES (CJI3, ZRD_KKAJ, Net Inv) | SAP CES przez RABIT | koszty rzeczywiste, zobowiązania | import plików z lokalizacji RABIT na SharePoint przez WebDAV (rozdz. 3) |
| Struktura P1S i kategoryzacja | `PZLPROD.LOG.WBS`, `LOG.WBS_DIC` (`splmcd03`) | drzewo elementów P1S, kategorie | odczyt bezpośredni kontem AD (`pzl-ev.json`, sekcja `PzlProd`), źródło przyrostowe (rozdz. 5) |
| Raport mapowań SAP↔CES | eksport z `PZLPROD` do Excela | przypisania elementów CES do P1S | wczytanie pliku do słownika globalnego „Raport mapowań CES ↔ P1S” na ekranie Słowniki (`docs/mapowanie-ces-p1s.md`, rozdz. 2) |
| Pracownicy (słownik Osoby) | `PZLHRPROD.HR.ORG` | USRID, imię, nazwisko, e-mail, MPK, dział, stanowisko, pion | odczyt bezpośredni kontem AD (`pzl-ev.json`, sekcja `PzlHrProd`) na żądanie – „Wczytaj z HR” na ekranie Słowniki (`docs/slowniki.md`, rozdz. 2) |
| Zaawansowanie z produkcji | `PZLPROD.LOG.vAHDD` (O10) | zaawansowanie godzin i materiałów | odczyt w etapie P5 (rozdz. 6) |
| Pliki CAM | Excel na dysku sieciowym | zaawansowanie od CAM w przebiegu zamykającym | import w etapie P6 (rozdz. 7) |
| Cobra | Sikorsky (projekty SAC) | budżet i harmonogram SAC | do ustalenia (O28) |

Danych w systemach źródłowych PZL-EV nie modyfikuje.

---

## 2. Definicja źródła

Każdy raport importowany do PZL-EV ma formalną definicję zapisaną w bazie (`meta.SourceDefinition`, z historią)
i utrzymywaną na ekranie Administracja (`docs/funkcjonalnosc.md`, F08).

| Element definicji | Znaczenie |
|---|---|
| Kod źródła | identyfikator, np. `ACTUALS_CES` |
| Rozpoznanie pliku | prefiks nazwy pliku (początek nazwy, np. `ACTUALS_` obejmuje wszystkie pliki `ACTUALS_…`; zapis `ACTUALS_*` znaczy to samo); wygrywa najdłuższy pasujący prefiks, bez rozróżniania wielkości liter |
| Typ raportu | np. koszty rzeczywiste, zobowiązania, prognoza |
| Układ kolumn | kolumny pliku, ich typy i pola wymagane – w parserze wskazanym w definicji |
| Ziarno | co oznacza jeden wiersz (np. pozycja kosztowa elementu WBS i elementu kosztowego w okresie) |
| Klucz | kolumny jednoznacznie identyfikujące wiersz |
| Znaczenie kolumn | która kolumna to element WBS, element kosztowy, kwota, ilość, okres |
| Znaczenie okresu | koszt okresu albo narastająco; kolumna wyznaczająca okres |
| Waluta i jednostki | waluta każdej kwoty, jednostki ilości |
| Interpretacja wartości | format liczb (np. polski: spacja tysięcy, przecinek dziesiętny), znak, puste wartości |
| Reguły walidacji | kontrole wiersza i pliku z poziomem ERROR / WARNING (rozdz. 8) |
| Parser | przekształcenie wierszy pliku do postaci kanonicznej (z wersją) – pilnuje układu kolumn |

- Prefiks służy wyłącznie do rozpoznania pliku. Znaczenie danych wynika z definicji, nie z nazwy pliku.
- Każdy prefiks to **osobne źródło** z własnym kodem i definicją, nawet gdy kilka źródeł ma identyczny układ
  kolumn (np. wszystkie `ACTUALS_*` – rozdz. 4). Układy nie są łączone w jedno źródło.
- Plik bez pasującego prefiksu nie jest importowany; po dodaniu definicji zostanie zaimportowany przy kolejnym
  imporcie.
- **Etap 1 (aplikacja):** definicja na ekranie Administracja to kod, prefiks, typ raportu, parser i aktywność.
  Układ kolumn, typy i pola wymagane trzyma **parser** – definicja tylko go wskazuje, więc kilka prefiksów o tym samym
  układzie korzysta z jednego parsera (np. `ACTUALS_*` – rozdz. 4); ziarno, klucz i znaczenie okresu nie są polami
  definicji. Zapis tworzy nową wersję definicji. Definicję można usunąć: bieżąca wersja zostaje zamknięta (historia
  i zaimportowane dane zostają), pliki o tym prefiksie są odtąd nierozpoznane, a kod i prefiks można użyć ponownie.
- **Parser** (`meta.Parser`, Administracja → Parsery, z historią) to pola danych kanonicznych: kolumna w pliku (pusta – pole nie jest czytane z pliku), nazwa kolumny w bazie, typ (tekst z długością,
  kwota / liczba, liczba całkowita, data), opcjonalnie dopełnianie zerami tekstu z cyfr i znacznik **wymagane**
  (wartość w każdym wierszu). „Kolumny z pliku…” wczytuje wiersz nagłówków z pliku źródła tak samo jak import:
  dokłada pola dla nowych kolumn (tekst, nazwa w bazie z nazwy kolumny), oznacza kolumny, których w pliku brak,
  i pokazuje przykładowe wartości. Dane wszystkich parserów są w jednej stałej tabeli `CAN_Row` (migracja 007,
  `docs/model-danych.md`, rozdz. 5.1): zapis parsera przydziela nowemu polu wolny **slot** jego rodzaju (tekst do
  400 znaków, dłuższy tekst, kwota / liczba, liczba całkowita, data) i nie zmienia tabel. Pole zachowuje slot we
  wszystkich wersjach parsera; zmiana rodzaju pola (np. kwota → tekst, tekst ponad 400 znaków) jest odrzucana –
  dodaje się nowe pole; slot pola usuniętego z parsera zostaje przy jego danych i nie jest przydzielany innemu polu
  (pole dodane ponownie pod tą samą nazwą wraca do swojego slotu). Ekran pokazuje zajęte sloty (np. „T 19/40 · N 4/20”).
- Źródło z parserem: plik jest zapisywany w bazie (wersja pliku i dane kanoniczne) tylko wtedy, gdy przejdzie
  walidację – parser aktywny, w pliku są wszystkie kolumny parsera, pola wymagane wypełnione i wartości zgodne
  z typami. Kolumny pliku spoza parsera nie są zapisywane (opis decyzji podaje ich nazwy – żeby je zachować, dodaje
  się pola parsera). Plik, który walidacji nie przejdzie, ma decyzję „błąd” i nie zostawia danych w bazie; przy
  kolejnym imporcie jest pobierany ponownie (pomijane są tylko pliki, których wersja jest w bazie). Źródło bez
  parsera – zapisywana jest tylko wersja pliku (SHA-256, kolumny, liczba wierszy), bez danych.
- **Sam plik nie jest przechowywany** (decyzja 2026-10-03, migracja 008): każdy raport RABIT to pełne dane
  (ten sam układ, dane narastająco), a po udanym imporcie są one w `CAN_Row` – kopia pliku w bazie nic nie wnosi,
  a wydłużała zapis.
- **Import dużych plików** – cztery etapy, widoczne w statusie pliku na ekranie Import (z postępem i czasem etapu):
  1. *pobieranie na dysk* – plik trafia do folderu tymczasowego na dysku lokalnym użytkownika (`%TEMP%\PZL-EV\import`,
     usuwany po pliku), SHA-256 liczony w trakcie; dalsze odczyty idą z dysku, nie z sieci. SharePoint: najpierw
     bezpośrednio przez HTTPS (konto Windows i ciasteczka bramy F5 z logowania w aplikacji – bez limitu rozmiaru usługi
     WebClient), a gdy brama tego nie przepuści – przez WebDAV (ścieżka UNC; kolejne pliki tej witryny w tym imporcie od
     razu przez WebDAV). Folder `Do_importu` – kopia pliku. Pliki nie są kopiowane do `Do_importu` (to folder wejściowy –
     import widziałby je ponownie jako nowe pliki);
  2. *sprawdzanie pliku* – duplikat (ten sam SHA-256), nagłówek, kolumny parsera;
  3. *odczyt i zapis wierszy* – jeden przebieg pliku (CSV albo Excel – odczyt strumieniowy, wiersz po wierszu):
     wiersze są parsowane w osobnym wątku i jednocześnie zapisywane wsadowo do `CAN_Row` w jednej transakcji. Błąd
     wartości, puste pole wymagane albo niezgodne sumy kwot (z kolumn pliku i z wartości pól) wycofują zapis;
  4. *kontrola w bazie* – liczba wierszy i sumy kwot po zapisie = odczytane z pliku (niezgodność wycofuje cały zapis),
     zatwierdzenie; plik lokalny jest potem usuwany.

  Liczby są zaokrąglane do 8 miejsc po przecinku. „Przerwij” działa także w trakcie pliku – jego zapis jest wycofany.
  Gdy SQL Server wycofa zapis pliku jako ofiarę zakleszczenia z inną sesją (błąd 1205), import sam go powtarza (do
  3 prób, plik czytany ponownie z dysku lokalnego; status: „ponowienie 2/3 po zakleszczeniu w bazie”, wpis w logu).
  Drugą sesję zakleszczenia pokazuje graf z sesji `system_health` (uprawnienie VIEW SERVER STATE):

  ```sql
  SELECT CAST(event_data AS XML).value('(event/@timestamp)[1]', 'varchar(30)') AS Kiedy,
         CAST(event_data AS XML).query('//deadlock') AS Graf
  FROM sys.fn_xe_file_target_read_file('system_health*.xel', NULL, NULL, NULL)
  WHERE object_name = 'xml_deadlock_report' ORDER BY Kiedy DESC;
  ```
  Czasy etapów każdego pliku i sposób pobrania (HTTPS / WebDAV, MB/s) są w logu. Dane każdej wersji pliku zostają w bazie –
  najnowsza jest danymi bieżącymi, starsze służą do porównań. Arkusz Excela ma najwyżej 1 048 576 wierszy – większe
  raporty tylko jako CSV.
- Definicje powstają dla źródeł w miarę ustalania ich zawartości (O27).

---

## 3. Pliki RABIT

- RABIT automatycznie (także pod nieobecność pracownika) zrzuca wyniki raportów SAP na SharePoint i przy
  każdym uruchomieniu **nadpisuje ten sam plik**. Historia wersji jest w bazie (wersje plików wg hash SHA-256).
- Nazwy plików są proste, bez projektu, dat i wersji (np. `ACTUALS_PAF_01.xlsx`).
- Raporty jednego zrzutu dotyczą jednego okresu.
- Jeden plik może obejmować wiele projektów (np. całe PWC). Import nie zna projektów – dane projektu wybiera
  przebieg według zakresu projektu (`docs/pipeline-fazy.md`, P1).
- Duże raporty jednego źródła są dzielone na części (`_01`, `_02`, `_03`) – każda część to osobny plik tego
  samego źródła, o układzie zgodnym z definicją. Pliki o innym prefiksie to osobne źródła (rozdz. 2).
- **Lokalizacje RABIT:** raporty mogą trafiać do wielu folderów na SharePoint. Lista lokalizacji jest
  konfiguracją importu w bazie (`meta.SourceLocation`, ekran Administracja – `docs/funkcjonalnosc.md`, F08):
  nazwa, ścieżka, aktywna. Link do folderu SharePoint skopiowany z przeglądarki jest zamieniany na ścieżkę WebDAV.
  Przykład ścieżki:
  `\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659`.
- Plik jest identyfikowany przez **lokalizację i nazwę** – pliki o tej samej nazwie w różnych lokalizacjach to
  różne pliki. Rozpoznanie źródła po prefiksie (rozdz. 2) nie zależy od lokalizacji.
- **Dostęp:** lokalizacje są czytane przez WebDAV (usługa WebClient Windows, konto użytkownika). Konto
  uruchamiające import musi mieć dostęp do wszystkich aktywnych lokalizacji. Usługa WebClient ma limit
  rozmiaru pliku (domyślnie ok. 50 MB, `FileSizeLimitInBytes` zmienia administrator);
  większy plik pobiera się ręcznie w przeglądarce do folderu `00_Global\RABIT\Do_importu`
  (`docs/architektura.md`, rozdz. 7) i importuje tak samo.
- Niedostępne dla użytkownika: API REST SharePoint, synchronizacja OneDrive, eksport listy do Excela,
  pobieranie ZIP.
- Najczęstsza przyczyna braku dostępu przez WebDAV przy działającej przeglądarce: adres z kropkami to dla Windows
  strefa Internet – usługa WebClient nie wysyła logowania Windows, dopóki adres nie jest w strefie Intranet lokalny
  albo w `AuthForwardServerList` (`HKLM\SYSTEM\CurrentControlSet\Services\WebClient\Parameters`); za proxy – adres
  musi być na liście wyjątków.
- **Ustalenia testu dostępu (stanowisko analityka, 2026-10-02):** SharePoint RABIT jest za bramą logowania F5 (BIG-IP).
  Działa tylko WebDAV przez usługę WebClient i dopiero po zalogowaniu do bramy (wcześniej: przez Office – „Eksport do
  Excela” albo otwarcie pliku w Excelu); po wygaśnięciu sesji logowanie trzeba powtórzyć. Drogi HTTP z aplikacji
  (`owssvr.dll`, pobranie pliku, PROPFIND, REST, `Lists.asmx`) kończą się na stronie logowania bramy albo HTTP 403;
  provider OLEDB listy niedostępny dla procesu 64-bit. Test dostępu (drogi A–I) służył tylko diagnozie i został
  usunięty z aplikacji.
- **Logowanie do bramy w aplikacji** – część **Importuj** i **Sprawdź źródła** (ekran Import, zawsze przed odczytem
  lokalizacji SharePoint): jak Office – protokół MS-OFBA (zapytanie z `X-FORMS_BASED_AUTH_ACCEPTED: t`, odpowiedź 403
  z adresem strony logowania i adresem powrotu); okno logowania na silniku przeglądarki Windows, który dzieli trwałe
  ciasteczka z usługą WebClient – przy ważnej sesji zamyka się samo. Bez MS-OFBA okno otwiera stronę folderu.
  Czy sesja z okna aplikacji wystarcza usłudze WebClient – do potwierdzenia na stanowisku.
- .NET zwraca na ścieżkach WebDAV nazwy plików z końcowym znakiem `\0` oraz wpisy „.” i „..”
  (dotnet/runtime#62429) – import czyści nazwy (`FolderEntries`).
- Import czyta tylko główny folder lokalizacji (bez podfolderów). Gdy folder
  nie ma plików, a ma podfoldery – WARNING z ich nazwami. **Sprawdź źródła** (ekran Import) pokazuje bez importu
  dostęp, czytaną ścieżkę, pliki i ich rozpoznanie; szczegóły – log aplikacji (`app/README.md`).
- Format: jeśli RABIT pozwala – CSV/TXT (brak limitu wierszy i konwersji typów przez Excel; O7).

---

## 4. Raporty kosztów rzeczywistych CES (`ACTUALS_*`)

Pliki z prefiksem `ACTUALS_` to zrzuty kosztów rzeczywistych pobierane z SAP CES przez RABIT, w formacie Excel
(`.xlsx`). Każdy taki prefiks (np. `ACTUALS_PAF`, `ACTUALS_CES`) to **osobne źródło** (rozdz. 2); wszystkie mają
wspólny układ kolumn (od 2026-10):

`Project Definition | WBS Element | Cost Element | Cost element name | CO object name | Transaction Currency |
Value TranCurr | Object Currency | Value in Obj. Crcy | Report currency | Val.in rep.cur. | Total Quantity |
Partner Object Class | Partner object | Original material | Original material description | Original Order Number |
Item | Purchase order number | Fiscal Year | Created on | Period | Invoice Number`

Przykład: `2DI473 | 2DI473001001 | 51105550 | PZL Mat Consump | PAF2 - materiały pod RTS batch 6 | USD | 261,54 |
PLN | 1 254,51 | USD | 261,54 | 0,000 | … | 8000123401 | 10 | 4500012301 | 2026 | 2026-03-29 | 3 | FV/2026/03/011`.

| Kolumna | Znaczenie |
|---|---|
| `Project Definition` | projekt CES (np. `4D03GZ`) |
| `WBS Element` | element WBS CES (np. `4D03GZ000001`) – klucz mapowania CES ↔ P1S |
| `Cost Element` | numer elementu kosztowego – klucz słownika Cost Category (`docs/slowniki.md`, rozdz. 6) |
| `CO object name` | opis obiektu (wyszukiwanie) |
| `Value TranCurr`, `Value in Obj. Crcy`, `Val.in rep.cur.` | kwota w walucie transakcji, obiektu (PLN) i raportowej (USD) |
| `Total Quantity` | ilość |
| `Fiscal Year`, `Period` | rok i okres księgowy |

- Liczby w formacie polskim (spacja tysięcy, przecinek dziesiętny).
- Struktura CES jest **płaska**: projekt → lista elementów WBS (numeracja ciągła z lukami).
- Dane kanoniczne – parser `ACTUALS` (migracja 004; dane w `CAN_Row` – migracja 007): pola wszystkich kolumn powyżej; numer
  elementu kosztowego złożony z cyfr uzupełniany zerami do 10 znaków; minus na końcu liczby (zapis SAP, np. `48,00-`) oznacza wartość ujemną.
  Pola wymagane wskazuje parser (`WBS Element`, `Fiscal Year`, `Period`). Pola `Cost element descr.`,
  `Partner-CCtr`, `Source object name` (wcześniejszy układ) zostają w parserze ze swoimi slotami, ale nie są czytane z pliku. Układ
  raportu może się zmieniać – zmienia się wtedy pola parsera ACTUALS (Administracja → Parsery), nie kod ani
  definicje. Klucz wiersza nie jest ustalony (O27) – bez kontroli duplikatów. Kontrola przepływu przy imporcie:
  liczba wierszy i sumy wszystkich pól liczbowych (kwot) parsera w danych kanonicznych zgodne z kolumnami pliku –
  przed zapisem i ponownie w bazie po zapisie (rozdz. 2).

---

## 5. Struktura P1S i kategoryzacja

### 5.1 `PZLPROD.LOG.WBS`

Wszystkie elementy WBS P1S (nadrzędne i szczegółowe), czytane bez zmian.

| Kolumna | Użycie |
|---|---|
| `PSPNR` | identyfikator elementu; łączenie z raportem mapowań (`PSPNR` = `pspnr_sap`) |
| `PARENT` | `PSPNR` elementu nadrzędnego – hierarchia |
| `STUFE` | poziom w hierarchii (1 = korzeń `PROJORG`) |
| `WBS_ELEMENT` | kod WBS P1S (np. `MC-00.001.0001.001`) |
| `PROJORG` | projekt SAP P1S – korzeń drzewa elementów |
| `PROJECT` | grupa raportowa wg `WBS_DIC`; często równa `PROJORG` (np. `AC-I39`), nie zawsze (np. `MC-00.001` pod `MC-00`) |
| `PROJNAME`, `LTXA1`, `Z_OPIS` | opisy (wyszukiwanie) |
| `PRCTR` | profit center |
| `Z_KAT_ZBIORCZA`, `Z_KATEGORIA` | kategorie – poziomy drzewa (rozdz. 5.3) |
| `Z_MODEL`, `MATNR_LO`, `SERNR_LO`, `KDAUF`/`KDPOS`, `KUNNR`, `BSTNK`, `MATNR`, `MAKTX`, `AUFNR`, `TECHS` | atrybuty opisowe: model, materiał i numer seryjny, zlecenie sprzedaży, klient, zamówienie klienta, materiał, zlecenie |
| `Z_ACTIVE`, `LOEKZ` | aktywność i znacznik usunięcia: `LOEKZ` niepuste = usunięty; `Z_ACTIVE` puste, `0` albo `N` = nieaktywny (do potwierdzenia – Diagnostyka → Sprawdź PZLPROD pokazuje faktyczne wartości) |
| `ERDAT`, `AEDAT` | daty utworzenia i zmiany – wykrywanie nowych elementów |

### 5.2 `PZLPROD.LOG.WBS_DIC`

Słownik grupujący projekty P1S.

| Kolumna | Użycie |
|---|---|
| `Z_PROJECT` + `Z_GRP` | klucz grupy: `PROJECT` (grupa po projekcie / WBS) albo `PRCTR` (grupa po profit center) |
| `Z_OPIS`, `Z_KATEGORIA`, `Z_KAT_ZBIORCZA`, `Z_INFO` | opis i kategorie grupy (np. „Internal Work”, „Spares & Services”) |
| `ERDAT`, `Z_USER` | kto i kiedy dopisał grupę |

### 5.3 Drzewo P1S

Jedno drzewo P1S jest używane przy budowie nakładki Performance Objectives (`docs/performance-objectives.md`) i w widoku mapowania
(`docs/mapowanie-ces-p1s.md`, rozdz. 11):

```text
Z_KAT_ZBIORCZA
└─ Z_KATEGORIA
   └─ Z_OPIS
      └─ PROJORG
         └─ elementy WBS/PSP (hierarchia wg PARENT)
```

- `PROJORG` jest umieszczany według kategorii swojego wiersza (`STUFE` = 1); `PROJORG` bez kategorii trafia
  do grupy **„Bez kategorii w WBS”**.
- Elementy nieaktywne i usunięte (`Z_ACTIVE`, `LOEKZ`) **zostają** w drzewie (wyszarzone) – mogą mieć koszty.
- Przykład gałęzi: `AC-I39` (`PROJORG` = `PROJECT`, `PRCTR` PIDS70OM) → `AC-I39.1` → `AC-I39.1.01` →
  `AC-I39.1.01.01`, `AC-I39.1.01.02`, …

---

## 6. Zaawansowanie z produkcji

- Źródło: `PZLPROD.LOG.vAHDD` (do potwierdzenia – O10) – zaawansowanie godzin i materiałów według elementów
  P1S. Pobierane w etapie P5 dla elementów WBS projektu.
- `vAHDD` jest aktualny dopiero po przebiegu procedury `uspUpdateAHDD`, która uruchamia `uspUpdateAHDD_ORDER`,
  `_PSPNR` i `_VORNR` (ta ostatnia wywołuje `uspUpdateZMTO`). Status odświeżenia i błędy zapisują
  `StatusAktualizacjiRaportow`, `ReportErrorInfo` i `TableList` – na tej podstawie etap P0 sprawdza świeżość
  danych produkcyjnych.

### 6.1 Istniejące obiekty na `splmcd03`

Według eksportu metadanych (`dependencies.csv`, `resolved_objects.csv`, `export_log.txt`):

- Dane SAP P1S są replikowane do `PZL_SAP` (`Z_R3_PRPS_TBL`, `AUFK`, `AFKO`, `AFPO`, `AFVC/AFVV`, `JEST`…).
  Struktura P1S jest czytana z przetworzonej tabeli `LOG.WBS`, bez sięgania do `Z_R3_PRPS_TBL`.
- Tabel kosztowych CES (podstawa CJI3) w replikacji nie ma – koszty pochodzą z plików RABIT.
- `Stanowiska`, `LearningCurve`, `PeriodDates`, `EmployeesHist` to istniejące tabele słownikowe
  (`docs/slowniki.md`, O9).
- Eksport zawiera tylko metadane; do szczegółowego projektu potrzebne są definicje `vAHDD`, `WBS`,
  `uspUpdateAHDD_VORNR` i kolumny tabel.

---

## 7. Pliki CAM

Plik CAM generuje PZL-EV w przebiegu zamykającym okres (`docs/pipeline-fazy.md`, P6); CAM uzupełnia go
i odkłada do folderu `Zwrocone`.

- Jeden plik na CAM w ramach projektu (O3), z WP danego CAM ze słownika „WP i CAM”.
- Ukryty arkusz: identyfikator przebiegu, projekt, okres, CAM, wersja szablonu.
- Wartość zaawansowania z produkcji wpisana jako **podpowiedź**; CAM potwierdza ją albo zmienia.
- Komórki poza polami do uzupełnienia są zablokowane. Pola uzupełniane przez CAM – O17.
- Plik jest odrzucany przy imporcie, gdy: brak identyfikatora, plik z innego przebiegu lub okresu, zmiana
  poza polami do uzupełnienia, wartości poza zakresem.

---

## 8. Reguły walidacji źródeł

Poziomy ERROR / WARNING – `docs/pipeline-fazy.md`, rozdz. 1.3.

| Reguła | Poziom | Kiedy |
|---|---|---|
| lokalizacja RABIT niedostępna (brak dostępu, błąd WebDAV) | ERROR dla lokalizacji – pozostałe lokalizacje importują się dalej | import |
| plik bez pasującego prefiksu | WARNING (plik nierozpoznany, nieimportowany) | import |
| brak aktywnej lokalizacji RABIT | WARNING – import czyta tylko folder `Do_importu` | import |
| folder lokalizacji bez plików, z podfolderami | WARNING z nazwami podfolderów (import ich nie czyta) | import |
| brak w pliku kolumny parsera (zmiana układu raportu) | ERROR dla pliku – plik nie jest zapisywany w bazie; po poprawie pól parsera kolejny import pobiera go ponownie | import |
| kolumna pliku spoza parsera | bez problemu – kolumna tylko w wierszach surowych (opis decyzji importu) | import |
| wartość niezgodna z typem pola, tekst dłuższy niż pole, puste pole wymagane | ERROR dla pliku (wiersz i kolumna) – plik nie jest zapisywany w bazie | import |
| parser definicji nieaktywny albo usunięty | ERROR dla pliku – plik nie jest zapisywany w bazie | import |
| duplikat klucza w pliku albo między częściami jednego źródła | ERROR | import / P2 |
| reguły szczegółowe źródła | wg definicji | import |

Kontrole danych względem okresu przebiegu i słowników – `docs/pipeline-fazy.md`, P2.

---

## 9. Otwarte kwestie

| # | Kwestia |
|---|---|
| O7 | Czy RABIT może eksportować CSV/TXT? (CSV preferowany) |
| O10 | Źródło zaawansowania z produkcji (`vAHDD`) – potwierdzenie i definicja widoku |
| O27 | Zawartość pozostałych raportów RABIT (zobowiązania, „PZL roll”, „hedge”, „workaround”), ich prefiksy i definicje źródeł; układ `ACTUALS_*` – rozdz. 4 |
| O28 | Które dane pochodzą z Cobra i w jakiej formie |
| O38 | Wycofanie pliku, którego RABIT już nie generuje (dziś jego ostatnia wersja pozostaje najnowsza) |
