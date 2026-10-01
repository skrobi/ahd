# PZL-EV – Uprawnienia

Zakres: tożsamość użytkownika, role, zakres danych i sposób egzekwowania dostępu (kontrakt RBAC).

Powiązane: `docs/architektura.md` (rozdz. 6 – dostęp do bazy przez procedury i widoki, dziennik),
`docs/funkcjonalnosc.md` (ekrany, zakres wersji).

---

## 1. Tożsamość

- Użytkownik jest identyfikowany kontem Windows / AD. Aplikacja łączy się z bazą tym kontem (Windows
  Authentication); w konfiguracji nie ma haseł.
- Model: **konto AD → rola → funkcje → zakres danych**.

---

## 2. Role

| Rola | Wersja | Funkcje | Zakres danych |
|---|---|---|---|
| **Analityk** | v1 | wszystkie funkcje aplikacji: import, definicje źródeł, słowniki, mapowanie, projekty, przebiegi, obliczenia EV, administracja (role, kalendarz, konfiguracja) | wszystkie projekty |
| **CAM** | v2 | przegląd projektu; wprowadzanie zaawansowania, ETC i komentarza dla swoich WP; przekazanie (Submit) do dalszego przetwarzania | projekty, w których jest CAM w słowniku „WP i CAM” – widzi cały projekt |
| **PM** | v2 | przegląd projektów: stan przebiegów, wyniki EV | O43 |

- W v1 aplikacji używa wyłącznie rola Analityk; CAM pracuje na plikach Excel (`docs/zrodla-danych.md`,
  rozdz. 7).
- Zawężenie zakresu CAM do węzłów, w których jest przypisany, jest przewidziane po v2.

---

## 3. Przypisanie ról

- Rola jest przypisana do grupy AD (konfiguracja w bazie, ekran Administracja – `docs/funkcjonalnosc.md`, F08).
- Użytkownik należący do kilku grup ma sumę funkcji tych ról.
- Użytkownik bez roli nie może pracować w aplikacji.

---

## 4. Egzekwowanie

- Interfejs pokazuje tylko funkcje roli użytkownika; warstwa aplikacji sprawdza rolę przed każdą akcją.
- Baza: rola bazy `pzl_ev_user` (Analityk) ma wyłącznie prawo wykonywania procedur i odczytu widoków
  (`docs/architektura.md`, rozdz. 6). CAM w v2 otrzymuje osobną rolę bazy z procedurami ograniczonymi do jego
  projektów.
- Każda akcja jest zapisywana w dzienniku z kontem AD.

---

## 5. Dysk sieciowy

Uprawnienia do folderów (`docs/architektura.md`, rozdz. 7) nadaje IT:

| Kto | Dostęp |
|---|---|
| Analityk | zapis w całym korzeniu `PZL-EV` środowiska |
| CAM | odczyt `CAM\<RRRR-MM>\Wyslane`, zapis `CAM\<RRRR-MM>\Zwrocone` swojego projektu |

---

## 6. Role techniczne (poza aplikacją)

| Rola | Zakres |
|---|---|
| Administrator (IT) | wdrożenia bazy i aplikacji na PROD, uprawnienia do folderów i grup AD |
| Developer | rozwój aplikacji i bazy na TEST |

---

## 7. Otwarte kwestie

| # | Kwestia |
|---|---|
| O43 | Zakres danych roli PM (wszystkie projekty czy wskazane) |
