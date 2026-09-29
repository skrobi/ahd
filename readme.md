# AHD - Earned Value Reporting Platform Knowledge Base

## Status dokumentu

Version: 0.1

Dokument roboczy służący do budowy docelowego rozwiązania wspierającego
Program Reporting oraz Earned Value Management (EVM) w PZL Mielec.

Dokument jest rozwijany iteracyjnie na podstawie:
- warsztatów z użytkownikami
- istniejących plików Excel
- szablonów raportowych
- logiki biznesowej
- procedur finansowych
- raportów SAP

---

# Cel projektu

Stworzenie wspólnej platformy raportowej umożliwiającej:

- automatyczne pobieranie danych źródłowych
- historyzację danych
- standaryzację raportowania
- ograniczenie pracy manualnej
- automatyczne przygotowanie danych EV
- automatyczne generowanie pakietów raportowych

Docelowo rozwiązanie ma zmniejszyć zależność od ręcznej obróbki danych
w Excelu.

---

# Aktualny stan

Proces jest wykonywany głównie manualnie.

Każdy analityk:

- pobiera dane samodzielnie
- wykonuje własne transformacje
- utrzymuje własne pliki Excel
- utrzymuje własne słowniki
- wykonuje korekty ręczne
- utrzymuje własne formuły

W efekcie:

- występuje duże ryzyko błędów
- trudno odtworzyć proces
- trudno wdrożyć nową osobę
- bardzo dużo czasu poświęcane jest na przygotowanie danych

---

# Typy projektów EV

## 1. Projekty Sikorsky (SAC)

Charakterystyka:

- oficjalny proces EV prowadzony jest przez Sikorsky
- wykorzystywana jest Cobra jako narzędzie do kalkulowania wskaźników i nadzorowania kosztów 
- wykorzystywany jest PM Compass do wprowadzania zaawanowania kosztów materiałów oraz nakładu godzin
- PZL dostarcza dane wejściowe (zaawansowanie wprowadzane przez CAM - Cost Account Manager)
- Actual Cost pochodzi z SAP (koszty i godziny)
- ETC przygotowywane jest lokalnie
- dane przekazywane są do Sikorsky

Specyfika:

- roboczogodziny PZL traktowane są jako koszt dostawcy, dlatego podczas wprowadzanie zaawnsowania godziny przeliczane są po obecnych stawkach na koszt $
- koszt pracy konwertowany jest do USD
- raportowanie odbywa się zgodnie z wymaganiami SAC

Model:

SAC Plik Cobra (zawiera budżet i harmonogram)
↓
PZL Finansce
↓
SAP
↓
Przetworzenie danych PZL (kolecja danyh)
↓
Plik Cobra
↓
Sikorsky
↓
Oficjalne EV

---

## 2. Projekty CAS Compliance

Charakterystyka:

- stosowane są stawki CAS
- koszty przeliczane są według reguł CAS
- nie opierają się wyłącznie na stawkach SAP
- duża część logiki znajduje się obecnie w Excelach

Model:

SAP
↓
Przetworzenie danych PZL (kolecja danyh)
↓
Przeliczenie CAS
↓
Kalkulacja EV

---

## 3. Projekty Wewnętrzne

Charakterystyka:

- pełna odpowiedzialność po stronie PZL
- własne szablony
- własne raporty
- własne kalkulacje EV

Model:

SAP
↓
Budżet
↓
Harmonogram
↓
ETC
↓
EV

---

# Główne problemy

## Problem 1 - Dane

Duże wolumeny danych.

Przykłady:

- setki tysięcy rekordów
- nawet 700 000+ linii dla pojedynczego projektu
- wiele plików dla różnych projektów

Skutki:
- czas pobierania danych (częściowo rozwiązany przez RABIT - narzędzie w SAP do pobieraina danych)
- problemy z Excelem
- problemy z wydajnością
- długi czas przygotowania danych

---

## Problem 2 - Wiedza procesowa

Wiedza jest rozproszona.

Znajduje się w:

- plikach Excel
- formułach
- słownikach
- tabelach pomocniczych
- wiedzy konkretnych osób

Brak centralnego repozytorium wiedzy.

---

## Problem 3 - Transformacje

Występuje duża liczba:

- mapowań
- słowników
- tabel translacyjnych
- wyszukań pionowych
- ręcznych korekt

Te transformacje nie są obecnie udokumentowane.

---

## Problem 4 - Standaryzacja

Każdy analityk często realizuje proces własnym sposobem.

Brakuje:

- wspólnych definicji
- wspólnych słowników
- wspólnej warstwy danych

---

# Wizja architektury

## Warstwa 1 - Data Collection

Źródła:

- SAP - PZL CES: CJI3; ZRD_KKAJ; Net Inv
- SharePoint
- Cobra exports
- ręczne pliki Excel
- dane CAM
- źródło stawek dla wydziałów
- mapowanie WBS na programy -> projekty -> Work Package
- słowniki CAM dla WP i WBS
- źródło zaawnaowania godzin i materiałów dla produkcji (tabela PZLPROD.LOG.vAHDD)
- źródło zaawansowania wprowadzania manualnego (przypisane osoby jako CAM wprowadzja w WP - work package zaawansowanie w postaci godzin lub ksztów materiałowych)

---

## Warstwa 2 - Landing Zone

Pierwsze miejsce przechowywania danych.

Postać:

- Excel
- CSV
- TXT
- MsSQL DB

Archiwizowane bez zmian.

---

## Warstwa 3 - Historical Repository

Baza danych przechowująca:

- wszystkie importy
- historię zmian
- kolejne snapshoty

Podstawowe tabele:

- ImportBatch
- SourceFile
- SourceSystem

---

## Warstwa 4 - Business Layer

Widoki SQL.

Przykłady:

- vw_actual_cost
- vw_budget
- vw_etc
- vw_ev
- vw_cpi
- vw_spi
- vw_eac
- vw_tcpi

---

## Warstwa 5 - Report Layer

Generowanie:

- raportów EV
- plików dla Cobra
- pakietów CAM
- danych dla PowerBI
- szablonów Excel

---

# Założenia technologiczne

Python:

Odpowiada za:

- pobieranie plików
- walidację plików
- ładowanie do bazy

SQL:

Odpowiada za:

- logikę biznesową
- agregacje
- kalkulacje EV
- historię zmian

Excel:

Odpowiada za:

- prezentację wyników
- raport końcowy
- interakcję użytkownika
- przechowywanie słowników 


# Słowniki biznesowe

## Cel

Słowniki biznesowe stanowią kluczowy element procesu EV i zawierają wiedzę biznesową niezbędną do przekształcania danych źródłowych w dane raportowe. W pierwszej fazie projektu słowniki będą utrzymywane w plikach Excel i okresowo importowane do bazy danych.

Model:

Excel (Master)
↓
Export (Pipline podczas uruchomienia)
↓
SQL Dictionary Tables
↓
Business Views
↓
EV Reports

---

## Zasada przechowywania

Excel jest źródłem prawdy (Source of Truth).

SQL przechowuje:

- aktualną wersję słownika
- historię zmian
- datę importu
- wersję słownika
- numer uruchominia pipline

Aktualizacja słownika odbywa się wyłącznie poprzez modyfikację pliku Excel.

---

## Przykładowe słowniki

### Struktura projektowa

P1S WBS | CAS WBS | Project Definition | Business Area | Program | Project | Customer | Cost Category | CAM | WP (yes/no) 


### Finansowe

Department | Year Labor Rate | Labor Rate | Ovehead

### Harmonogramy i budżet

P1S WBS | CAS WBS | Project Definition | Budżet godzinowy | Budżet materiałowy | Bazowa Planowana data rozpoczęcia | Bazowa Planowana data zakończenia | Planowana data rozpoczęcia  | Planowana data zakończenia | Rzeczywista data rozpoczęcia | Rzeczywista data zakończenia



---

## Minimalna struktura słownika

Każdy słownik powinien zawierać:

- Key
- Value
- ValidFrom
- ValidTo
- Owner
- Version
- LastUpdate
- Comments

---

## Właściciel słownika

Dla każdego słownika należy określić:

- właściciela biznesowego
- lokalizację pliku
- częstotliwość aktualizacji
- osobę odpowiedzialną za zatwierdzanie zmian

---

## Historia zmian

Każdy import słownika powinien umożliwiać:

- odtworzenie wcześniejszej wersji
- porównanie zmian między wersjami
- identyfikację osoby wprowadzającej zmianę

---

## Założenie projektowe

Nie planuje się budowy dedykowanego formularza do utrzymania słowników. Pliki Excel pozostają podstawowym narzędziem zarządzania słownikami, natomiast baza danych pełni rolę centralnego repozytorium przetwarzania oraz historii zmian.



---

# Lista otwartych pytań

1. Jakie raporty SAP są wykorzystywane?

2. Które dane pochodzą z Cobra?

3. Skąd pochodzi ETC?

4. Jak liczony jest progress w poszczególnych programach?

5. Jakie słowniki istnieją obecnie?

6. Jakie stawki CAS są wykorzystywane?

7. Które pliki są krytyczne dla procesu?

8. Jakie raporty są generowane dla kierownictwa?

9. Jakie raporty są przekazywane do klienta?

10. Które elementy procesu są dziś najbardziej czasochłonne?

---

# Długoterminowy cel

Stworzenie centralnego Program Reporting Engine umożliwiającego:

- automatyczne pobieranie danych
- pełną historię danych
- standaryzację EV
- eliminację ręcznego przetwarzania
- automatyczne generowanie raportów
- pojedyncze źródło prawdy dla danych programowych
