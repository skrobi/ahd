# CLAUDE.md — pzl-ev

## Rola tego pliku

Ten plik zawiera zasady pracy nad projektem.
Nie jest dokumentacją aplikacji. Dokumentacja techniczna znajduje się w `README.md` oraz w katalogu `md/`.

## 1. Zanim zmienisz kod

Najpierw zrozum istniejącą implementację.

Kolejność:

1. Przeczytaj `README.md`.
2. Ustal, której części aplikacji dotyczy zadanie.
3. Otwórz odpowiedni dokument z `docs/`.
4. Zlokalizuj konkretne pliki i metody.
5. Dopiero wtedy analizuj i modyfikuj kod.

Nie czytaj całego repozytorium, jeśli nie jest to potrzebne.

`README.md` jest mapą kodu, a dokumenty `docs/*.md` zawierają szczegółową dokumentację poszczególnych obszarów. Dokumentacja jest pomocą nawigacyjną, a nie bezwzględnym źródłem prawdy. Przed zmianą zawsze zweryfikuj aktualny kod.

## 2. Zakres zadania

Wykonuj tylko zmiany potrzebne do realizacji aktualnego zadania.

Nie wykonuj przy okazji:

- niepotrzebnych refaktoryzacji,
- zmian architektury,
- zmian API,
- zmian schematu bazy,
- aktualizacji bibliotek,
- zmian innych funkcjonalności,
- „poprawiania” kodu niezwiązanego z zadaniem.

Jeżeli podczas pracy znajdziesz problem niezwiązany z zadaniem, nie naprawiaj go automatycznie. Zgłoś go na końcu jako uwagę.

## 3. Wykorzystuj istniejące rozwiązania

Przed napisaniem nowego mechanizmu sprawdź, czy podobne rozwiązanie już istnieje.

Preferowana kolejność:

1. wykorzystaj istniejący kod,
2. rozszerz istniejący mechanizm,
3. dopiero jeśli jest to konieczne, utwórz nowy.

Nie twórz równoległych implementacji tej samej funkcjonalności. Przestrzegaj istniejących wzorców architektury projektu.

## 4. Dokumentacja

`README.md` wskazuje, gdzie znajduje się odpowiedni kontekst.

W szczególności:

- `architecture.md` — architektura i przepływ ,
- `funkcjonalnosc.md` — funkcjonalności założeniowe,
- `mapowanie-ces-p1s.md` — sposób mapowania,
- `pipeline-fazy.md` - przepływ etapów w ramach uruchomionego przepływu 


Jeżeli zmiana powoduje, że dokumentacja przestaje być aktualna, zaktualizuj odpowiedni dokument `docs/`.

Nie twórz dodatkowej dokumentacji, jeżeli informacja może zostać dopisana do istniejącego dokumentu.

## 5. Bezpieczeństwo

Nie osłabiaj istniejących mechanizmów:

- autoryzacji,
- autentykacji,
- CSRF,
- kontroli uprawnień,
- ochrony przed IDOR,
- prywatności danych.

Jeżeli istnieje już helper lub guard zapewniający daną regułę, wykorzystaj go zamiast tworzyć własną implementację.

## 8. Minimalna zmiana

Preferuj najmniejszą zmianę, która poprawnie rozwiązuje problem. Nie przepisuj istniejącego kodu tylko dlatego, że można go napisać inaczej. Nie zmieniaj istniejącego zachowania, jeżeli nie wynika to z wymagań zadania. Jeżeli zadanie wymaga istotnej decyzji architektonicznej, zatrzymaj się przed implementacją i przedstaw proponowane rozwiązanie.

## 8. Weryfikacja

Po implementacji:

1. sprawdź wszystkie zmienione pliki,
2. sprawdź, czy rozwiązanie spełnia wymagania zadania,
3. uruchom dostępne testy jeśli ich brakuje to dopisz i przeprowadź testy z wymaganymi przypadkami użycia
4. sprawdź potencjalne skutki uboczne,
5. upewnij się, że nie zmodyfikowano elementów poza zakresem.

Nie deklaruj, że funkcjonalność działa, jeżeli nie została zweryfikowana.

## 9. Praca z taskami

Jeżeli zadanie znajduje się w katalogu `tasks/`, traktuj jego:

- cel,
- wymagania,
- zakres,
- kryteria akceptacji

jako kontrakt implementacyjny. Jeżeli wykonanie zadania wymaga zmiany zakresu, zatrzymaj się i zapytaj przed implementacją dodatkowej funkcjonalności. Przed rozpoczęciem implementacji znajdź minimalny zestaw dokumentacji i plików potrzebnych do wykonania zadania. Po zakończeniu nie podsumuj. Zasygnalizuj jedynie czy skończone powodzeniem, jeśli nie to co powinno zostać zrobione oraz problemy, ograniczenia lub kwestie znalezione poza zakresem zadania.

---

## 10. Oszczędzanie kontekstu

Celem jest rozwiązanie problemu przy użyciu minimalnego niezbędnego kontekstu, bez utraty poprawności. Nie analizuj całej aplikacji przed wykonaniem małego zadania.

Preferowany przepływ:

README.md
→ właściwy dokument `docs/*.md`
→ właściwe pliki
→ implementacja
→ weryfikacja.

Jeżeli dokumentacja projektu zawiera potrzebną informację, nie szukaj jej ponownie w całym repozytorium. Najpierw wykorzystaj dokumentację, a następnie zweryfikuj tylko wskazane w niej pliki. Nie powtarzaj informacji, które już znajdują się w dokumentacji projektu.

## 11. Zasada nadrzędna

Najpierw zrozum istniejący system. Potem zmień tylko to, co jest potrzebne. Na końcu zweryfikuj zmianę.