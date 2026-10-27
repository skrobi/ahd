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

## 2. Dwie warstwy: użytkownik i bike fitter

**Decyzja (27.10):** użytkownik końcowy nie musi wiedzieć, jaki powinien być kąt kolana. Dostaje **zalecenie, co zmienić
w rowerze**, opisane prostym językiem. Kąty, definicje, jakość śledzenia i cykle są elementem pośrednim. Trafiają do zakładki
**„Szczegóły (dla bike fittera)”**.

```
Moje analizy (pulpit)  → tylko analizy i ich efekty
├─ Analiza pozycji
│   ├─ Podsumowanie      → następny krok, 4 regulacje, „co dała zmiana”, symulacja „sprawdź, zanim zmienisz”
│   └─ Szczegóły         → kąty, definicje, wykresy cyklu, jakość, cykle, reguły i źródła zaleceń, porównanie pomiarów
Nowa analiza · Komfort jazdy · Historia zmian · Moje rowery · Profil
```

## 3. Główny przepływ

Nagranie → analiza → **„Twój następny krok”** (np. „Podnieś siodło o ok. 10 mm”) → użytkownik zmienia i zapisuje
zmianę → ocena komfortu → kolejne nagranie → **„Co dała zmiana”** (przed/po na paskach zakresów + odczucia).

Zasady (wzorem MyVeloFit, Bike Fast Fit i BikeViewer):

1. **4 regulacje** zamiast kątów: wysokość siodła, siodło przód/tył, odległość do kierownicy, wysokość kierownicy.
2. Każda regulacja ma **pasek zakresu** (czerwony – żółty – zielony – żółty – czerwony) ze znacznikiem, status słowny
   („W porządku”, „Blisko granicy”, „Do zmiany”, „Nie oceniono”) i opis prostym językiem.
3. **Jedna zmiana naraz**, maksymalnie 10 mm na krok, w kolejności: siodło wysokość → siodło przód/tył → odległość → wysokość kierownicy.
4. Przy zaleceniu: „jak to zrobić” i „na co wpłynie” (kolano, kark, dłonie, plecy).
5. **„Sprawdź, zanim zmienisz”**: suwaki (siodło, siodło przód/tył, mostek, kierownica) pokazują szacowane przesunięcie
   w zakresach. Szacunek pochodzi z modelu geometrycznego i jest tak opisany.
6. Gdy jakość nagrania jest za słaba dla danej regulacji – status „Nie oceniono” i brak zalecenia.
7. Pulpit: karty analiz ze statusem 4 regulacji, następnym krokiem i **efektem poprzedniej zmiany**.

## 4. Model dopasowania (`prototyp/fit-model-demo.js`)

Zalecenia liczy osobny, parametryzowany plik konfiguracji. Docelowo jest to moduł E („modele fittingu”) – wersjonowany i
niezależny od interfejsu. Dla każdej regulacji: pomiar, zakresy (zależne od typu roweru), źródło, przelicznik na mm, teksty.

| Regulacja | Pomiar | Zakres roboczy | Źródło |
|---|---|---|---|
| Wysokość siodła | zgięcie kolana przy maks. wyproście (180° − kąt kolana) | 30–40° | Ferrer-Roca i in., J Strength Cond Res 2012; 26(11): 3025–3029 |
| Siodło przód/tył | kolano (nadkłykieć) względem osi pedału, korba w poziomie | od −45 do −15 mm | KOPS – tradycyjny punkt wyjścia, nie norma |
| Odległość do kierownicy | kąt tułów–ramię | 80–90° | praktyka bike fittingu – do weryfikacji |
| Wysokość kierownicy | pochylenie pleców | szosa 40–50°, gravel 45–55°, MTB 50–65° | praktyka bike fittingu – do weryfikacji |

**Zakresy są robocze.** Przed wdrożeniem muszą zostać zweryfikowane ze źródłami i z doświadczonym bike fitterem (Etap 4).
Liczba mm to szacunek z modelu geometrycznego, zaokrąglony do 5 mm.

### Definicje pomiarów (zakładka „Szczegóły”)

| Pomiar | Punkty | Definicja |
|---|---|---|
| Kąt kolana | krętarz większy – **nadkłykieć boczny k. udowej** – kostka boczna | kąt wewnętrzny w kolanie; 180° = wyprost |
| Kąt biodra | akromion – **krętarz większy** – nadkłykieć boczny | kąt wewnętrzny w biodrze |
| Kąt stawu skokowego | nadkłykieć boczny – **kostka boczna** – głowa V k. śródstopia | kąt wewnętrzny w kostce |
| Kąt łokcia | akromion – **nadkłykieć boczny k. ramiennej** – wyrostek rylcowaty | kąt wewnętrzny w łokciu |
| Kąt barku | krętarz – **akromion** – nadkłykieć k. ramiennej | kąt tułów–ramię |
| Pochylenie tułowia | krętarz → akromion vs pozioma | 0° poziomo, 90° pionowo |
| Kolano względem pedału | nadkłykieć boczny vs oś pedału przy 25% cyklu | mm, ujemne = za osią |
| Ruch miednicy | trajektoria krętarza, skala ze znacznika | amplituda pionowa w mm |
| Pozycja stopy | kostka → głowa V k. śródstopia vs pozioma | dodatnia = palce w dół |
| Tor kolana | rzepka, staw skokowy, kolec biodrowy | wymaga kamery z przodu – „niedostępny” |

### Przegląd podobnych aplikacji (27.10.2026)

| Aplikacja | Co wzięliśmy | Uwagi |
|---|---|---|
| [MyVeloFit](https://www.myvelofit.com/fit-academy/myvelofit-s-bike-fit-report-explained/) | 4 regulacje, każda: zakres + zalecenie; zaczynać od wysokości siodła, małe kroki | komercyjna, AI z wideo |
| [Bike Fast Fit](https://dcrainmaker.com/2013/10/automated-measurement-review.html) (EZ / Elite / [Pro](https://apps.apple.com/app/id6502648219)) | kadr z opisanymi pomiarami, paski zakresów, raport przed/po | komercyjna, iOS |
| [BikeViewer](https://bikeviewer.com/) | proste podpowiedzi („łokcie prawie wyprostowane: kierownica za daleko”), symulacja „co jeśli”, opis wpływu na kark/dłonie/plecy | komercyjna, zamknięty kod; sama zaznacza, że to model geometrii, a nie symulacja biomechaniczna, a mapa mięśni jest poglądowa |
| [AndreaBe99/computer-vision-project](https://github.com/AndreaBe99/computer-vision-project) | kandydat do Etapu 3 (MediaPipe, moduł bike fitting) | projekt studencki, **brak określonej licencji** – nie wolno kopiować kodu bez zgody autora |

Symulacji pracy mięśni **nie dodajemy** do prototypu: wymaga modelu mięśniowo-szkieletowego (np. OpenSim) i walidacji.
Bez tego byłaby atrapą. Do rozważenia jako osobny etap.

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

Warto przejść po kolei: **Moje analizy** → karta z 08.10 → **Podsumowanie** („Podnieś siodło o ok. 10 mm”) → karta z 21.10
(„Co dała zmiana”, następny krok „Skróć mostek”, suwaki w „Sprawdź, zanim zmienisz”) → zakładka **Szczegóły** →
**Nowa analiza** (kreator, dowolny plik `.mp4`, symulacja stanów) → **Komfort jazdy** → **Moje rowery** → **Historia zmian**.

### Test automatyczny (opcjonalnie, dla programisty)

Wymaga Node.js i pakietu `playwright` z przeglądarką Chromium:

```
node ridemore-fit/tests/smoke.js [folder_na_zrzuty_ekranu]
```

Test otwiera 11 widoków (podsumowania i szczegóły) w szerokości 1440 px i 390 px. Sprawdza błędy JavaScript, poziome przewijanie strony
i obecność paska „dane demonstracyjne”. Dodatkowo przechodzi przez scenariusze:

- kreator blokuje przejście bez listy kontrolnej, odrzuca plik `.avi` i przyjmuje `.mp4`;
- symulacja analizy kończy się stanem „zakończone z ostrzeżeniami”;
- nieudane zadanie można ponowić, a licznik prób rośnie;
- wybór pomiaru zmienia wykres i otwiera definicję;
- tor kolana ma stan „niedostępne”, a kąt stawu skokowego jest oznaczony jako ograniczony;
- wzory kątów dają poprawne wyniki dla 90°, 180° i 45°;
- podsumowanie pokazuje „Podnieś siodło o ok. 10 mm” (08.10) i „Skróć mostek o ok. 10 mm” (21.10), bez tabeli kątów;
- suwak krótszego mostka przenosi „odległość do kierownicy” z „Blisko granicy” do „W porządku”;
- pulpit pokazuje efekt zmiany siodła;
- porównanie i historia pokazują zapisane zmiany ustawień.

## 7. Co działa, a co nie

| Działa w prototypie | Nie działa / tylko projekt |
|---|---|
| Nawigacja i wszystkie ekrany, układ desktop i wąski | Logowanie – zakłada istniejące konto ridemore.bike |
| Zalecenia „co zmienić” z roboczego modelu dopasowania, paski zakresów, przed/po, symulacja „co jeśli” | Zweryfikowane zakresy i przeliczniki mm (Etap 4); symulacja mięśni |
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
2. **Kierunek UX/UI**: akceptacja podziału „Podsumowanie / Szczegóły” i 4 regulacji.
3. **Zakresy zaleceń**: kto zweryfikuje zakresy (bike fitter) i czy zakresy mają zależeć od typu roweru i stylu jazdy (rekreacja / wyczyn).
4. **Polityka nagrań**: domyślne usuwanie i 7 dni dla nieudanych analiz.
5. **Strona nagrania**: czy pierwsza wersja ma wspierać tylko jedną stronę ciała na sesję (tak jak w prototypie)?
6. **Hosting**: zgodność z Pythonem 3.6/3.9 i bibliotekami (MediaPipe, OpenCV, modele pozy) – sprawdzenie w Etapie 2,
   bez założeń z góry.
