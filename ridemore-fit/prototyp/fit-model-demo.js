/* ridemore.bike Fit – MODEL DOPASOWANIA (wersja demonstracyjna)
   Parametryzowana konfiguracja zaleceń: dla każdej regulacji – pomiar, zakresy, źródło, sposób
   przeliczenia na milimetry i opis prostym językiem. Docelowo ten plik odpowiada modułowi E
   („modele fittingu”) i będzie wczytywany jako wersjonowana konfiguracja, a nie kod interfejsu.
   ZAKRESY SĄ ROBOCZE – do weryfikacji ze źródłami i z bike fitterem w Etapie 4. */
(function () {
  'use strict';
  var SRC = {
    ferrer: 'Ferrer-Roca V. i in., „A simple method to determine optimal saddle height…”, J Strength Cond Res 2012; 26(11): 3025–3029 – zgięcie kolana 30–40° przy maksymalnym wyproście, pomiar dynamiczny 2D.',
    practice: 'Zakres orientacyjny z praktyki bike fittingu (stosowany m.in. w aplikacjach do analizy wideo). Brak jednego źródła naukowego – do weryfikacji z bike fitterem.',
    kops: 'KOPS (kolano nad osią pedału) – tradycyjny punkt wyjścia ustawienia siodła przód/tył; badania nie potwierdzają go jako normy. Traktuj jako punkt startowy.'
  };

  // status: 'ok' | 'near' (strefa przejściowa) | 'out' | 'unknown'
  var ADJUSTMENTS = [
    {
      id: 'saddleH', order: 1, name: 'Wysokość siodła', part: 'siodło',
      metric: 'knee', rep: 'max', value: function (v) { return 180 - v; },
      measure: 'Zgięcie kolana na dole obrotu', unit: '°', scale: [20, 50],
      ranges: { all: { green: [30, 40], yellow: [25, 45] } }, target: 35, perMm: 0.35, step: 10, source: SRC.ferrer,
      low: { title: 'Siodło za wysoko', plain: 'Na dole obrotu noga prostuje się prawie całkowicie. Może to powodować bujanie biodrami i ból z tyłu kolana.', action: 'Obniż siodło' },
      high: { title: 'Siodło za nisko', plain: 'Na dole obrotu noga jest za mocno zgięta. Zwykle przeciąża to przód kolana i zmniejsza siłę nacisku.', action: 'Podnieś siodło' },
      ok: 'Noga na dole obrotu zgina się w zalecanym zakresie.',
      body: ['kolano'], how: 'Poluzuj obejmę sztycy, przesuń sztycę i zaznacz nową wysokość taśmą. Zmieniaj najwyżej o 5–10 mm naraz.'
    },
    {
      id: 'saddleFA', order: 2, name: 'Siodło przód / tył', part: 'siodło',
      metric: 'kneePedal', rep: 'at25', value: function (v) { return v; },
      measure: 'Kolano względem osi pedału (korba w poziomie)', unit: 'mm', scale: [-75, 15],
      ranges: { all: { green: [-45, -15], yellow: [-55, -5] } }, target: -30, perMm: 1, step: 10, source: SRC.kops,
      low: { title: 'Kolano daleko za pedałem', plain: 'Gdy korba jest z przodu, kolano jest wyraźnie za osią pedału. Siedzisz daleko z tyłu.', action: 'Przesuń siodło do przodu' },
      high: { title: 'Kolano przed pedałem', plain: 'Gdy korba jest z przodu, kolano wychodzi przed oś pedału. Siedzisz daleko z przodu, co może przeciążać przód kolana.', action: 'Przesuń siodło do tyłu' },
      ok: 'Kolano jest w typowym położeniu względem pedału.',
      body: ['kolano'], how: 'Poluzuj śruby jarzma siodła i przesuń siodło na szynach. Po zmianie sprawdź wysokość siodła – przesunięcie ją zmienia.'
    },
    {
      id: 'barReach', order: 3, name: 'Odległość do kierownicy', part: 'mostek',
      metric: 'shoulder', rep: 'at25', value: function (v) { return v; },
      measure: 'Kąt między tułowiem a ramieniem', unit: '°', scale: [65, 105],
      ranges: { all: { green: [80, 90], yellow: [75, 95] } }, target: 85, perMm: 0.3, step: 10, source: SRC.practice,
      low: { title: 'Kierownica za blisko', plain: 'Ramiona są mocno podciągnięte, łokcie bardzo zgięte. Pozycja jest ściśnięta, kolana mogą zbliżać się do łokci.', action: 'Wydłuż mostek' },
      high: { title: 'Kierownica za daleko', plain: 'Ramiona są wyciągnięte, łokcie prawie wyprostowane. Ciężar przechodzi na dłonie i kark.', action: 'Skróć mostek' },
      ok: 'Sięgasz do kierownicy bez nadmiernego wyciągania ramion.',
      body: ['kark', 'dłonie', 'barki'], how: 'Wymień mostek na krótszy/dłuższy. Alternatywa przy małej zmianie: przesuń klamkomanetki na kierownicy.'
    },
    {
      id: 'barHeight', order: 4, name: 'Wysokość kierownicy', part: 'podkładki pod mostkiem',
      metric: 'torso', rep: 'med', value: function (v) { return v; },
      measure: 'Pochylenie pleców', unit: '°', scale: [25, 70],
      ranges: { 'Szosowy': { green: [40, 50], yellow: [35, 55] }, 'Gravelowy': { green: [45, 55], yellow: [40, 60] }, 'MTB': { green: [50, 65], yellow: [45, 70] } },
      target: null, perMm: 0.1, step: 10, source: SRC.practice,
      low: { title: 'Kierownica za nisko', plain: 'Plecy są pochylone nisko jak na ten typ roweru i jazdę rekreacyjną. Kark i dolny odcinek pleców pracują ciężej.', action: 'Podnieś kierownicę' },
      high: { title: 'Kierownica wysoko', plain: 'Siedzisz bardziej wyprostowany niż typowo dla tego roweru. Jeśli jest wygodnie, nie musisz nic zmieniać.', action: 'Możesz obniżyć kierownicę' },
      ok: 'Pochylenie pleców pasuje do typu roweru.',
      body: ['kark', 'plecy'], how: 'Przełóż podkładkę spod mostka nad mostek (lub odwrotnie) albo odwróć mostek. Jedna podkładka to zwykle 5 mm.'
    }
  ];

  function rangeFor(adj, bikeType) { return adj.ranges[bikeType] || adj.ranges.all || adj.ranges['Szosowy']; }

  // Ocena jednej regulacji dla sesji. Zwraca status, wartość, zalecenie i liczbę mm.
  function evaluate(adj, session, bikeType) {
    var q = session.quality[adj.metric];
    var raw = session.agg[adj.metric].stat[adj.rep].value, v = adj.value(raw), r = rangeFor(adj, bikeType);
    var res = { adj: adj, value: v, range: r, quality: q };
    if (q !== 'good') { res.status = 'unknown'; return res; }
    var inG = v >= r.green[0] && v <= r.green[1], inY = v >= r.yellow[0] && v <= r.yellow[1];
    res.status = inG ? 'ok' : inY ? 'near' : 'out';
    if (inG) return res;
    var side = v < r.green[0] ? 'below' : 'above';
    // co oznacza „poniżej zakresu” dla danej regulacji
    var dir = { saddleH: { below: 'low', above: 'high' }, saddleFA: { below: 'low', above: 'high' }, barReach: { below: 'low', above: 'high' }, barHeight: { below: 'low', above: 'high' } }[adj.id][side];
    var target = adj.target != null ? adj.target : (r.green[0] + r.green[1]) / 2;
    var mm = Math.abs(v - target) / adj.perMm;
    var mmStep = Math.min(adj.step, Math.max(5, Math.round(mm / 5) * 5));
    res.dir = dir; res.text = adj[dir]; res.mm = mmStep; res.mmTotal = Math.round(mm / 5) * 5;
    return res;
  }
  function evaluateAll(session, bikeType) {
    var list = ADJUSTMENTS.map(function (a) { return evaluate(a, session, bikeType); });
    var todo = list.filter(function (r) { return r.status === 'out' || r.status === 'near'; })
      .sort(function (a, b) { return (a.status === 'out' ? 0 : 1) - (b.status === 'out' ? 0 : 1) || a.adj.order - b.adj.order; });
    return { list: list, todo: todo, next: todo[0] || null, okCount: list.filter(function (r) { return r.status === 'ok'; }).length };
  }

  window.RM_FIT = { VERSION: 'demo-0.1', ADJUSTMENTS: ADJUSTMENTS, evaluate: evaluate, evaluateAll: evaluateAll, rangeFor: rangeFor, SRC: SRC };
})();
