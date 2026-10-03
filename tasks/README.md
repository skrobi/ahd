# PZL-EV – zadania (backlog)

Plan budowy aplikacji podzielony na fazy i zadania. Każde zadanie to osobny plik – **kontrakt implementacyjny**
(`CLAUDE.md`, rozdz. 9): cel, wymagania, zakres, kryteria akceptacji. Zadania można zmieniać, dodawać i usuwać;
zmiana zakresu zadania w trakcie realizacji wymaga uzgodnienia.

## Zasady

- **Dane:** aplikacja pracuje na bazie MS SQL (`PZLTEST.FINOP.PZLEV_*`); warstwa w pamięci była przejściowa (F0.4) – usunięta 2026-10-02 z aplikacji i z testów; Pulpit czyta z bazy.
  Każdy moduł rozmawia z danymi przez `Data/I<Moduł>Store` – kontrakt odpowiadający przyszłym procedurom i widokom.
  Przejście na MS SQL (F10) zmienia tylko implementacje magazynów i skrypty `sql/mssql` (`docs/architektura.md`, rozdz. 5.3).
- **Testy kontraktu:** testy magazynów i serwisów na bazie SQL (`PZLEV_TEST_SQL`); bez bazy – pomijane.
- **Weryfikacja przepływu:** każdy etap zapisuje liczbę wierszy i sumy kontrolne; dane wzorcowe (F0.9) mają oczekiwane wyniki.
- **Kolejność typów projektów:** wewnętrzny → SAC → CAS.
- **Status zadania** – w pliku zadania i w tabeli poniżej: do zrobienia / w toku / zrobione / zablokowane (O…).

## Kamienie milowe

| Kamień | Fazy | Wynik |
|---|---|---|
| M1 | F0, F1 | szkielet aplikacji, słowniki globalne na danych w pamięci |
| M2 | F2, F3 | import i mapowanie; suma z pliku = suma danych kanonicznych |
| M3 | F4 | projekt z nakładką i słownikami (scenariusz 1) |
| M4 | F5–F7 | przebieg P0–P7 (scenariusze 2–4) |
| M5 | F8, F9 | EV, publikacja, przepływ end-to-end (scenariusze 5–6) |
| M6 | F10 | MS SQL – te same testy kontraktu na bazie |

## F0. Fundament (framework)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F0.1 Przeniesienie PoC do docelowej aplikacji](F0.1-przeniesienie-poc-do-docelowej-aplikacji.md) | Shell / całość | – | – | zrobione |
| [F0.2 Projekt testów i uruchamianie testów w budowie](F0.2-projekt-testow-i-uruchamianie-testow-w-budowie.md) | całość | F0.1 | – | zrobione |
| [F0.3 Konfiguracja aplikacji](F0.3-konfiguracja-aplikacji.md) | Shell | F0.1 | – | zrobione |
| [F0.4 Warstwa danych przejściowa (w pamięci)](F0.4-warstwa-danych-przejsciowa-w-pamieci.md) | Shared | F0.2 | – | zastąpione – baza SQL |
| [F0.5 Usługi wspólne: użytkownik, dziennik, problemy](F0.5-uslugi-wspolne-uzytkownik-dziennik-problemy.md) | Shared | F0.4 | – | zrobione |
| [F0.6 Silnik etapów](F0.6-silnik-etapow.md) | Shared / Runs | F0.5 | – | do zrobienia |
| [F0.7 Pliki: Excel, CSV, dysk sieciowy](F0.7-pliki-excel-csv-dysk-sieciowy.md) | Shared | F0.2 | – | w toku |
| [F0.8 Wspólne elementy interfejsu](F0.8-wspolne-elementy-interfejsu.md) | Shared/Views | F0.1 | – | w toku |
| [F0.9 Dane wzorcowe](F0.9-dane-wzorcowe.md) | testdata | F0.4 | decyzja: próbki od użytkownika | w toku |

## F1. Słowniki globalne (G3)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F1.1 Mechanizm słownika: historia, walidacja, równoczesna edycja](F1.1-mechanizm-slownika-historia-walidacja-rownoczesn.md) | MasterData | F0.4, F0.5 | – | zrobione |
| [F1.2 Słowniki globalne: kalendarz, stawki, kursy, Cost Category, osoby](F1.2-slowniki-globalne-kalendarz-stawki-kursy-cost-ca.md) | MasterData | F1.1 | O18 | zrobione |
| [F1.3 Wymiana słowników przez Excel](F1.3-wymiana-slownikow-przez-excel.md) | MasterData | F1.1, F0.7 | – | zrobione |
| [F1.4 Ekran Słowniki](F1.4-ekran-slowniki.md) | MasterData | F1.1–F1.3, F0.8 | – | zrobione |

## F2. Import (G1) i definicje źródeł

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F2.1 Definicje źródeł i lokalizacje RABIT](F2.1-definicje-zrodel-i-lokalizacje-rabit.md) | Administration / Import | F0.4, F0.5 | O27 | zrobione |
| [F2.2 Import plików (G1)](F2.2-import-plikow-g1.md) | Import | F2.1, F0.7 | O29, O31 | zrobione |
| [F2.3 Parser ACTUALS_* → dane kanoniczne](F2.3-parser-actuals-dane-kanoniczne.md) | Import | F2.2 | O27, O7 | zrobione |
| [F2.4 Ekran Import](F2.4-ekran-import.md) | Import | F2.2, F2.3, F0.8 | – | zrobione |
| [F2.5 Narzędzie testowe w Pythonie](F2.5-narzedzie-testowe-w-pythonie.md) | Import | F2.4 | – | zrobione – usunięte |

## F3. Mapowanie CES ↔ P1S (G2)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F3.1 Źródło PZLPROD (LOG.WBS, WBS_DIC, raport mapowań)](F3.1-zrodlo-pzlprod-log-wbs-wbs-dic-raport-mapowan.md) | Mapping | F0.4 | – | zrobione |
| [F3.2 Rozstrzyganie mapowania i korekty (G2)](F3.2-rozstrzyganie-mapowania-i-korekty-g2.md) | Mapping | F3.1, F2.3 | O20 | zrobione |
| [F3.3 Drzewo P1S](F3.3-drzewo-p1s.md) | Mapping | F3.1 | O21 | zrobione |
| [F3.4 Ekran Mapowanie CES ↔ P1S](F3.4-ekran-mapowanie-ces-p1s.md) | Mapping | F3.2, F3.3 | – | zrobione – ekran do sprawdzenia na stanowisku |

## F4. Projekty (F01, F02)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F4.1 Projekt i foldery](F4.1-projekt-i-foldery.md) | Projects | F0.7 | – | do zrobienia |
| [F4.2 Nakładka Performance Objectives](F4.2-nakladka-performance-objectives.md) | Projects | F4.1, F3.2 | O46, O47 | do zrobienia |
| [F4.3 Słowniki projektu](F4.3-slowniki-projektu.md) | Projects / MasterData | F1.1, F4.2 | O24, O37, O44 | do zrobienia |
| [F4.4 Kreator projektu](F4.4-kreator-projektu.md) | Projects | F4.1–F4.3 | O23 | do zrobienia |
| [F4.5 Gotowość projektu i ekrany Projekty / Projekt](F4.5-gotowosc-projektu-i-ekrany-projekty-projekt.md) | Projects | F4.4 | – | do zrobienia |

## F5. Przebieg – rdzeń (P0, P1, P2, Z)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F5.1 P0 Uruchomienie przebiegu](F5.1-p0-uruchomienie-przebiegu.md) | Runs | F0.6, F4.5, F1.2 | O36 | do zrobienia |
| [F5.2 P1 Przypięcie stanu](F5.2-p1-przypiecie-stanu.md) | Runs | F5.1, F2.3, F3.2 | O30, O45 | do zrobienia |
| [F5.3 Zmiana po przypięciu](F5.3-zmiana-po-przypieciu.md) | Runs | F5.2 | – | do zrobienia |
| [F5.4 P2 Walidacja](F5.4-p2-walidacja.md) | Runs | F5.2 | O25 | do zrobienia |
| [F5.5 Z Zamknięcie okresu](F5.5-z-zamkniecie-okresu.md) | Runs | F8.2 | – | do zrobienia |
| [F5.6 Ekrany Przebiegi i Przebieg](F5.6-ekrany-przebiegi-i-przebieg.md) | Runs | F5.1–F5.4 | – | do zrobienia |

## F6. Uzgodnienie (P3, P4)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F6.1 P3 Łączenie źródeł](F6.1-p3-laczenie-zrodel.md) | Reconciliation | F5.4 | O14, O33 | do zrobienia |
| [F6.2 P4 Pliki dla finansów](F6.2-p4-pliki-dla-finansow.md) | Reconciliation | F6.1 | O16 | do zrobienia |

## F7. Zaawansowanie (P5–P7)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F7.1 P5 Zaawansowanie z produkcji](F7.1-p5-zaawansowanie-z-produkcji.md) | Progress | F5.2, F3.1 | O10 | do zrobienia |
| [F7.2 P6 Uzupełnienie w środku okresu](F7.2-p6-uzupelnienie-w-srodku-okresu.md) | Progress | F7.1 | O11 | do zrobienia |
| [F7.3 P6 Pliki CAM w przebiegu zamykającym](F7.3-p6-pliki-cam-w-przebiegu-zamykajacym.md) | Progress | F7.1, F0.7 | O3, O17, O34 | do zrobienia |
| [F7.4 P7 Walidacja zaawansowania](F7.4-p7-walidacja-zaawansowania.md) | Progress | F7.2, F7.3 | – | do zrobienia |

## F8. Silnik EVM (P8)

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F8.1 Silnik EVM](F8.1-silnik-evm.md) | Evm | F6.1, F7.4 | O35, O15, O30, O36 | do zrobienia |
| [F8.2 Rewizje i przegląd wyniku](F8.2-rewizje-i-przeglad-wyniku.md) | Evm | F8.1 | – | do zrobienia |

## F9. Publikacja, Pulpit, przepływ end-to-end

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F9.1 P9 Publikacja](F9.1-p9-publikacja.md) | Export | F8.2 | O28 | do zrobienia |
| [F9.2 Pulpit na danych](F9.2-pulpit-na-danych.md) | Dashboard | F5.6 | – | w toku – etap 1 z bazy, przebiegi po F5 |
| [F9.3 Test przepływu end-to-end](F9.3-test-przeplywu-end-to-end.md) | całość | F9.1 | – | do zrobienia |

## F10. Przejście na MS SQL

| Zadanie | Moduł | Zależy od | Otwarte kwestie | Status |
|---|---|---|---|---|
| [F10.1 Skrypty migracyjne schematu](F10.1-skrypty-migracyjne-schematu.md) | baza | F0.4 | O5 | zrobione – etap 1; do wykonania na PZLTEST |
| [F10.2 Procedury i widoki](F10.2-procedury-i-widoki.md) | baza | F10.1 | – | do zrobienia |
| [F10.3 Magazyny SQL i testy kontraktu na bazie](F10.3-magazyny-sql-i-testy-kontraktu-na-bazie.md) | całość | F10.2 | – | w toku – import, administracja, słowniki globalne |
| [F10.4 PZLPROD, WebDAV, wydajność](F10.4-pzlprod-webdav-wydajnosc.md) | całość | F10.3 | O5, O6 | do zrobienia |
| [F10.5 Paczka wdrożeniowa](F10.5-paczka-wdrozeniowa.md) | całość | F10.3 | – | do zrobienia |

## Otwarte kwestie blokujące

| Kwestia | Kto | Blokuje |
|---|---|---|
| O14 – logika łączenia źródeł | zespół | F6.1 |
| O35, O15, O30, O36 – metody EV, ETC, ACWP, data stanu | Finanse | F8.1 |
| O16, O17 – układ plików dla finansów i CAM | Finanse, CAM | F6.2, F7.3 |
| O10 – definicja vAHDD | właściciele PZLPROD | F7.1 |
| O44, O45 – powiązanie nakładki z WP | zespół | F4.3, F5.2 |
| O11, O33, O34 | zespół | F7.2, F6.1, F7.3 |

Poza planem (v2): rola CAM i PM w aplikacji, raporty Power BI.
