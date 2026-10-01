# PZL-EV – Obliczenia EV

Zakres: silnik EVM (kontrakt EVM) – dane wejściowe per WP, wskaźniki wynikowe, poziomy agregacji, kontrole
oraz kwestie metodyczne do ustalenia z zespołem Finansów.

Powiązane: `docs/pipeline-fazy.md` (P8 – kiedy silnik jest uruchamiany), `docs/model-danych.md` (rewizja),
`docs/slowniki.md` (harmonogram i budżet, kursy walut).

---

## 1. Silnik EVM

- Osobny moduł aplikacji (C#): otrzymuje dane wejściowe i zwraca wskaźniki. Nie zależy od Excela, RABIT,
  WebDAV, bazy ani interfejsu – dane przygotowują procedury i widoki bazy (`docs/architektura.md`, rozdz. 6).
- Deterministyczny: te same dane wejściowe i ta sama wersja silnika dają ten sam wynik. Wersja silnika jest
  zapisywana w rewizji (`docs/model-danych.md`, rozdz. 4.3).
- Przypadki testowe silnika pochodzą z obecnych obliczeń analityków w Excelu: dla tych samych danych wynik
  silnika jest zgodny z wynikiem w Excelu.

---

## 2. Dane wejściowe (per WP)

| Wejście | Źródło w PZL-EV |
|---|---|
| BAC i baseline rozłożona w czasie | słownik „Harmonogram i budżet” (`docs/slowniki.md`, rozdz. 3) |
| Data stanu (Status Date) | przebieg – ustalana przy uruchomieniu (`docs/pipeline-fazy.md`, P0) |
| Metoda EV | ustawienie WP – słownik projektu |
| Zaawansowanie z pochodzeniem | `ev.Zaawansowanie` (P5–P7) |
| ACWP | `ev.KosztWP` – wynik łączenia źródeł (P3) |
| ETC | źródło do ustalenia (O15) |
| Metoda EAC | ustawienie WP – słownik projektu |
| Waluta wyniku i kursy | typ projektu (`docs/slowniki.md`, rozdz. 4), słownik kursów walut |

- Metoda EV jest zawsze **jawnym ustawieniem WP**. Silnik nie przyjmuje dla całego projektu założenia
  BCWP = BAC × % zaawansowania. Zestaw metod i słownik ustawień WP – O35.
- WP bez kompletu danych wejściowych nie jest liczony; problem trafia do raportu przebiegu
  (`docs/pipeline-fazy.md`, rozdz. 1.3).

---

## 3. Wskaźniki wynikowe

| Wskaźnik | Znaczenie | Wyznaczenie |
|---|---|---|
| BAC | budżet całkowity | z baseline |
| BCWS | wartość planowana na datę stanu | z baseline rozłożonej w czasie |
| BCWP | wartość wypracowana | według metody EV WP |
| ACWP | koszt rzeczywisty | z łączenia źródeł |
| CV | odchylenie kosztowe | BCWP − ACWP |
| SV | odchylenie harmonogramowe | BCWP − BCWS |
| CPI | wskaźnik wykonania kosztów | BCWP / ACWP |
| SPI | wskaźnik wykonania harmonogramu | BCWP / BCWS |
| ETC | szacunek kosztu do zakończenia | ze źródła ETC (O15) |
| EAC | szacunek kosztu na zakończenie | według metody EAC WP (np. ACWP + ETC) |
| VAC | odchylenie na zakończenie | BAC − EAC |
| TCPI | wymagany wskaźnik wykonania kosztów | (BAC − BCWP) / (BAC − ACWP); wariant względem EAC – O35 |

Wskaźnik z mianownikiem równym zero nie ma wartości (pusty), a nie zero.

---

## 4. Poziomy agregacji i prezentacja

- Poziomy: **WP → CAM → `PROJORG` → projekt**.
- Wartości (BAC, BCWS, BCWP, ACWP, CV, SV, ETC, EAC, VAC) są sumowane; wskaźniki (CPI, SPI, TCPI) są liczone
  z sum na danym poziomie, nie uśredniane.
- Prezentacja w tysiącach waluty wyniku: PLN, w projektach SAC – USD.
- Wynik przebiegu w środku okresu jest oznaczony jako **EV wstępne**; zatwierdzona rewizja przebiegu
  zamykającego okres – jako **EV formalne**.

---

## 5. Kontrole wyniku

| Kontrola | Poziom |
|---|---|
| spójność sum między poziomami | ERROR |
| BCWP ≤ BAC | ERROR |
| WP bez kompletu danych wejściowych | WARNING (WP poza wynikiem, pokazany w raporcie przebiegu) |

---

## 6. Do ustalenia z Finansami

| # | Kwestia |
|---|---|
| O15 | Źródło ETC i moment jego wprowadzenia w przebiegu |
| O30 | ACWP – koszt okresu czy narastająco; obsługa korekt wstecznych w SAP |
| O35 | Metody EV per WP (zestaw metod, słownik ustawień WP), granulacja rozkładu baseline w czasie, metody EAC, wariant TCPI |
| O36 | Konwencja daty stanu (dzień uruchomienia przebiegu czy koniec tygodnia z kalendarza okresów) |
