# ridemore.bike Fit – moduł analizy pozycji na rowerze

Etap 1: audyt i projekt UX/UI. Ten folder zawiera **klikalny prototyp interfejsu na danych demonstracyjnych**.
Nie ma w nim analizy wideo, kolejki zadań, backendu PHP ani bazy danych.

> Folder jest niezależny od reszty repozytorium `ahd` (aplikacja PZL-EV, C#/WPF) i nie korzysta z żadnego jej pliku.
> Docelowo powinien trafić do repozytorium platformy ridemore.bike (patrz „Decyzje do podjęcia”).

---

## 1. Stan na start (audyt)

| Obszar | Ustalenie |
|---|---|
| Repozytorium `ahd` | Aplikacja PZL-EV (C#/.NET + WPF, MS SQL) – niezwiązana z ridemore.bike. Brak PHP, Pythona, MariaDB i elementów marki. |
| Kod ridemore.bike | Według Ciebie nie ma jeszcze kodu w żadnym repozytorium. Prototyp jest więc samodzielny. |
| Identyfikacja wizualna | Przejęta z publicznego arkusza stylów `ridemore.bike/assets/css/style.css`: tło `#F4F5F1`, tekst `#15201A`, akcent `#2C6B4F` (zieleń), żółty `#FFC93F`, fonty Archivo / Instrument Sans / IBM Plex Mono / Big Shoulders Display, promień 10 px. |
| Konta użytkowników | Istnieją w ridemore.bike. Prototyp zakłada, że moduł Fit korzysta z tego samego konta; dane Fit będą w osobnych tabelach. |

## 2. Struktura nawigacji

```
ANALIZA                         DOPASOWANIE
├─ Pulpit                        ├─ Komfort jazdy
├─ Nowa sesja (kreator 4 kroki)  ├─ Historia zmian
├─ Analiza w toku                ├─ Moje rowery
├─ Wyniki analizy                └─ Profil
└─ Porównanie sesji
```

Na komputerze nawigacja jest w kolumnie po lewej. W wąskim oknie (telefon) zmienia się w przewijany pasek u góry.

## 3. Główny przepływ

Wybór roweru → instrukcja nagrania → przesłanie wideo → analiza → kontrola jakości → wyniki → ocena komfortu →
zapis zmiany ustawień → porównanie z kolejną sesją.

| Krok | Ekran | Co robi użytkownik |
|---|---|---|
| 1 | Nowa sesja · krok 1 | Wybiera rower i chwyt; widzi aktualne ustawienia i link „zapisz zmianę”, jeśli coś zmienił. |
| 2 | Nowa sesja · krok 2 | Schemat ustawienia kamery i lista kontrolna (8 punktów). Bez zaznaczenia wszystkich nie przejdzie dalej. |
| 3 | Nowa sesja · krok 3 | Plik MP4/MOV (sprawdzenie formatu i rozmiaru), strona do kamery, długość znacznika skali, polityka nagrania, zgoda. |
| 4 | Analiza w toku | Stany: oczekujące → w trakcie (9 etapów) → zakończone / zakończone z ostrzeżeniami / nieudane; ponowienie (próba n z 3). |
| 5 | Wyniki | Kolejno: **jakość** → **pomiar** → **interpretacja** → **zalecenia (planowane)**. |
| 6 | Komfort jazdy | Skala 0–10 dla 11 obszarów, strona ciała, lokalizacja, moment pojawienia się. |
| 7 | Moje rowery | Zapis zmiany: parametr, przed, po, jednostka, data, powód. |
| 8 | Porównanie / Historia | Zmiany ustawień między sesjami, różnice pomiarów, nałożone przebiegi, różnica komfortu. |

## 4. Zasady prezentacji wyników

1. **Cztery warstwy, wizualnie rozdzielone** (kolorowy pasek z lewej i etykieta): 1 · jakość i ograniczenia,
   2 · pomiar z nagrania, 3 · interpretacja (opis, nie ocena), 4 · zalecenia (funkcja planowana, dziś pusta).
2. **Jakość każdego pomiaru osobno**: `OK` / `Ograniczona` (wartość z „≈”) / `Niewiarygodny` (wartość ukryta) /
   `Niedostępny` (np. tor kolana z kamery bocznej). Etykieta zawsze ma tekst, nie tylko kolor.
3. **Każdy kąt ma definicję** (przycisk ⓘ): punkty anatomiczne, wierzchołek, wzór, raportowane wartości.
4. Dla każdego pomiaru: strona ciała, **wartość reprezentatywna** (mediana z cykli), **zakres w cyklu** (min–max, ROM),
   **zmienność** (SD między cyklami), przebieg 0–100% cyklu (średnia ± SD, opcjonalnie pojedyncze cykle).
5. **Brak norm i „idealnej pozycji”**. W porównaniu jedyna automatyczna uwaga to: różnica mniejsza niż zmienność
   między cyklami albo ograniczona jakość w jednej z sesji. To uwagi o pomiarze, nie normy biomechaniczne.
6. Stała ramka „Ograniczenia analizy 2D” na ekranie wyników.
7. Faza cyklu: 0% = korba w górnym martwym punkcie (GMP), 50% = dolny martwy punkt (DMP).

### Robocze definicje pomiarów (do zatwierdzenia w Etapie 4)

| Pomiar | Punkty | Definicja |
|---|---|---|
| Kąt kolana | krętarz większy – **nadkłykieć boczny k. udowej** – kostka boczna | kąt wewnętrzny w kolanie; 180° = wyprost |
| Kąt biodra | akromion – **krętarz większy** – nadkłykieć boczny | kąt wewnętrzny w biodrze |
| Kąt stawu skokowego | nadkłykieć boczny – **kostka boczna** – głowa V k. śródstopia | kąt wewnętrzny w kostce |
| Kąt łokcia | akromion – **nadkłykieć boczny k. ramiennej** – wyrostek rylcowaty | kąt wewnętrzny w łokciu |
| Pochylenie tułowia | krętarz → akromion vs pozioma | 0° poziomo, 90° pionowo |
| Ruch miednicy | trajektoria krętarza, skala ze znacznika | amplituda pionowa w mm; przechyłu i rotacji z jednego punktu 2D nie da się wyznaczyć |
| Pozycja stopy | kostka → głowa V k. śródstopia vs pozioma | dodatnia = palce w dół |
| Tor kolana | rzepka, staw skokowy, kolec biodrowy | wymaga kamery z przodu – w tej wersji „niedostępny” |

## 5. Nagrania i prywatność (zapisane w interfejsie)

- Domyślnie: **nagranie usuwane po analizie**, wyniki pomiarów zostają.
- Zachowanie nagrania tylko po świadomym wyborze (do ręcznej korekty punktów i ponownej analizy nowszą wersją algorytmu).
  Można je usunąć w każdej chwili.
- Nagranie nieudanej analizy: przechowywane do 7 dni, żeby można było ponowić zadanie, potem usuwane (**propozycja do akceptacji**).
- Pliki poza publicznym katalogiem WWW, dostęp tylko dla właściciela. Dane profilu (dolegliwości, zakresy ruchu) traktowane jako dane o zdrowiu.

## 6. Uruchomienie prototypu (Windows 10/11)

1. Pobierz repozytorium (na GitHubie: **Code → Download ZIP**) i rozpakuj.
2. Otwórz folder `ridemore-fit\prototyp`.
3. Kliknij dwukrotnie plik **`index.html`**. Otworzy się w przeglądarce (Chrome, Edge lub Firefox).
4. Nie trzeba niczego instalować. Przy połączeniu z internetem ładują się fonty marki; bez internetu strona działa z fontami systemowymi.
5. Żeby zobaczyć widok telefonu: w przeglądarce naciśnij **F12**, a potem **Ctrl+Shift+M**.

Warto przejść po kolei: **Pulpit → Nowa analiza** (przejdź kreator, wybierz dowolny plik `.mp4`) → **Analiza w toku**
(przyciski „Symulacja stanów”) → **Wyniki analizy** (kliknij wiersze tabeli, ⓘ, „Odtwórz”, „Korekta punktów”) →
**Komfort jazdy** → **Moje rowery** (zapisz zmianę) → **Historia zmian** → **Porównanie sesji**.

### Test automatyczny (opcjonalnie, dla programisty)

Wymaga Node.js i pakietu `playwright` z przeglądarką Chromium:

```
node ridemore-fit/tests/smoke.js [folder_na_zrzuty_ekranu]
```

Test otwiera 11 widoków w szerokości 1440 px i 390 px. Sprawdza błędy JavaScript, poziome przewijanie strony
i obecność paska „dane demonstracyjne”. Dodatkowo przechodzi przez scenariusze:

- kreator blokuje przejście bez listy kontrolnej, odrzuca plik `.avi` i przyjmuje `.mp4`;
- symulacja analizy kończy się stanem „zakończone z ostrzeżeniami”;
- nieudane zadanie można ponowić, a licznik prób rośnie;
- wybór pomiaru zmienia wykres i otwiera definicję;
- tor kolana ma stan „niedostępne”, a kąt stawu skokowego jest oznaczony jako ograniczony;
- wzory kątów dają poprawne wyniki dla 90°, 180° i 45°;
- porównanie i historia pokazują zapisane zmiany ustawień.

## 7. Co działa, a co nie

| Działa w prototypie | Nie działa / tylko projekt |
|---|---|
| Nawigacja i wszystkie 9 ekranów, układ desktop i wąski | Logowanie – zakłada istniejące konto ridemore.bike |
| Kreator nowej sesji z walidacją formatu (MP4/MOV) i rozmiaru pliku w przeglądarce | Wysyłanie pliku na serwer (plik nie opuszcza przeglądarki) |
| Symulacja stanów zadania (w tym błąd i ponowienie) | Kolejka zadań, proces Python, limity hostingu |
| Wyniki: schemat sylwetki z animacją cyklu, wybór pomiaru, wykres z dymkiem, cykle, jakość w czasie | Prawdziwe nagranie z naniesionymi punktami; estymacja pozy |
| Ręczna korekta punktu na schemacie (przeciąganie) | Przeliczenie wyników po korekcie |
| Porównanie dwóch sesji, historia zmian, zapis zmiany ustawienia i oceny komfortu – **do odświeżenia strony** | Trwały zapis (baza MariaDB), eksport CSV/JSON (przycisk wyłączony – Etap 6) |
| Definicje geometryczne pomiarów | Zatwierdzone definicje i testy matematyczne (Etap 4) |

Dane demonstracyjne (`prototyp/demo-data.js`) są liczone z prostego modelu geometrycznego sylwetki 2D z dodanym szumem.
**Nie są wynikiem analizy wideo ani modelu AI.** Każdy ekran oznacza to paskiem u góry i ramką „Dane demonstracyjne”.

## 8. Decyzje do podjęcia przed Etapem 2

1. **Repozytorium docelowe**: ten folder leży tymczasowo w `ahd` (PZL-EV). Zalecam założyć osobne repozytorium
   modułu (np. `ridemore-fit`) albo przenieść go do repozytorium platformy, gdy kod ridemore.bike będzie dostępny.
2. **Kierunek UX/UI**: akceptacja układu ekranów, czterech warstw wyników i sposobu oznaczania jakości.
3. **Polityka nagrań**: domyślne usuwanie i 7 dni dla nieudanych analiz.
4. **Strona nagrania**: czy pierwsza wersja ma wspierać tylko jedną stronę ciała na sesję (tak jak w prototypie)?
5. **Hosting**: zgodność z Pythonem 3.6/3.9 i bibliotekami (MediaPipe, OpenCV, modele pozy) – sprawdzenie w Etapie 2,
   bez założeń z góry.
