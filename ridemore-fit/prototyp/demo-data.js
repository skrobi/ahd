/* ridemore.bike Fit – DANE DEMONSTRACYJNE
   Wszystko w tym pliku jest WYMYŚLONE na potrzeby prototypu interfejsu.
   Przebiegi kątów NIE pochodzą z analizy wideo ani z modelu AI – są liczone
   z prostego modelu geometrycznego (sylwetka 2D na rowerze) z dodanym szumem.
   W docelowej aplikacji dane przyjdą z modułu Python przez wersjonowany kontrakt JSON. */
(function () {
  'use strict';
  var D2R = Math.PI / 180;

  // --- geometria pomocnicza (te same wzory posłużą później do testów matematycznych) ---
  function angleAt(c, a, b) {
    var v1x = a.x - c.x, v1y = a.y - c.y, v2x = b.x - c.x, v2y = b.y - c.y;
    var cos = (v1x * v2x + v1y * v2y) / (Math.hypot(v1x, v1y) * Math.hypot(v2x, v2y));
    return Math.acos(Math.max(-1, Math.min(1, cos))) / D2R;
  }
  // kąt odcinka a→b względem poziomu (oś y obrazu skierowana w dół), w stopniach
  function angleToHorizontal(a, b) { return Math.atan2(a.y - b.y, Math.abs(b.x - a.x)) / D2R; }
  function circleIntersect(a, ra, b, rb, pick) {
    var dx = b.x - a.x, dy = b.y - a.y, d = Math.hypot(dx, dy);
    var l = (ra * ra - rb * rb + d * d) / (2 * d), h = Math.sqrt(Math.max(0, ra * ra - l * l));
    var mx = a.x + l * dx / d, my = a.y + l * dy / d;
    return pick({ x: mx + h * dy / d, y: my - h * dx / d }, { x: mx - h * dy / d, y: my + h * dx / d });
  }

  // Sylwetka 2D: początek układu = oś suportu, mm, oś y w dół. t = faza korby 0..1 (0 = GMP).
  var BASE = { hx: -200, hy: -765, r: 172.5, th: 420, sh: 480, lf: 150, torso: 580, tilt: 42, ua: 305, fa: 275, wx: 555, wy: -720, phase: 0 };
  function pose(t, geo) {
    var o = Object.assign({}, BASE, geo || {});
    var th = t * 2 * Math.PI;
    var P = { x: o.r * Math.sin(th), y: -o.r * Math.cos(th) };            // oś pedału
    var alpha = (20 + 14 * Math.sin(th - 2.6 + o.phase)) * D2R;            // nachylenie stopy
    var MT = { x: P.x, y: P.y - 30 };                                      // głowa V k. śródstopia ≈ nad osią pedału
    var A = { x: MT.x - o.lf * Math.cos(alpha), y: MT.y - o.lf * Math.sin(alpha) }; // kostka boczna
    var H = { x: o.hx, y: o.hy };                                          // krętarz większy
    var K = circleIntersect(H, o.th, A, o.sh, function (p, q) { return p.x > q.x ? p : q; });
    var S = { x: H.x + o.torso * Math.cos(o.tilt * D2R), y: H.y - o.torso * Math.sin(o.tilt * D2R) }; // akromion
    var W = { x: o.wx, y: o.wy };                                          // nadgarstek
    var E = circleIntersect(S, o.ua, W, o.fa, function (p, q) { return p.y > q.y ? p : q; });
    return {
      pts: { P: P, MT: MT, A: A, K: K, H: H, S: S, E: E, W: W, C: { x: 0, y: 0 } },
      knee: angleAt(K, H, A), hip: angleAt(H, S, K), ankle: angleAt(A, K, MT),
      elbow: angleAt(E, S, W), torso: angleToHorizontal(H, S), foot: angleToHorizontal(A, MT) * -1
    };
  }

  // deterministyczny generator liczb pseudolosowych
  function rng(seed) { return function () { seed |= 0; seed = seed + 0x6D2B79F5 | 0; var t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
  function gauss(r) { var u = r() || 1e-9, v = r(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); }
  function median(a) { var s = a.slice().sort(function (x, y) { return x - y; }), m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; }
  function mean(a) { return a.reduce(function (s, x) { return s + x; }, 0) / a.length; }
  function sd(a) { var m = mean(a); return Math.sqrt(a.reduce(function (s, x) { return s + (x - m) * (x - m); }, 0) / Math.max(1, a.length - 1)); }

  var N = 101; // próbki cyklu 0..100%
  var CURVE_METRICS = ['knee', 'hip', 'ankle', 'elbow', 'torso', 'foot', 'pelvisY'];
  var NOISE = { knee: 0.7, hip: 0.6, ankle: 2.2, elbow: 0.8, torso: 0.3, foot: 2.4, pelvisY: 1.2 };

  function genSession(seed, geo, nCycles, rejected) {
    var r = rng(seed), cycles = [];
    for (var c = 0; c < nCycles; c++) {
      var g = Object.assign({}, geo, {
        hy: geo.hy + gauss(r) * 2.2, hx: geo.hx + gauss(r) * 1.4, phase: gauss(r) * 0.08,
        tilt: (geo.tilt || BASE.tilt) + gauss(r) * 0.6, wx: (geo.wx || BASE.wx) + gauss(r) * 1.2, wy: (geo.wy || BASE.wy) + gauss(r) * 1
      });
      var curves = {}; CURVE_METRICS.forEach(function (m) { curves[m] = []; });
      var pAmp = (geo.pelvisAmp || 5) + gauss(r) * 0.8;
      for (var i = 0; i < N; i++) {
        var p = pose(i / (N - 1), g);
        ['knee', 'hip', 'ankle', 'elbow', 'torso', 'foot'].forEach(function (m) { curves[m].push(p[m] + gauss(r) * NOISE[m]); });
        curves.pelvisY.push(pAmp * Math.cos(4 * Math.PI * i / (N - 1)) + gauss(r) * NOISE.pelvisY);
      }
      var rej = rejected[c];
      cycles.push({ n: c + 1, valid: !rej, reason: rej || null, cadence: Math.round(88 + gauss(r) * 1.6), curves: curves });
    }
    return aggregate(cycles);
  }

  function aggregate(cycles) {
    var valid = cycles.filter(function (c) { return c.valid; }), agg = {};
    CURVE_METRICS.forEach(function (m) {
      var meanC = [], sdC = [];
      for (var i = 0; i < N; i++) {
        var col = valid.map(function (c) { return c.curves[m][i]; });
        meanC.push(mean(col)); sdC.push(sd(col));
      }
      var per = valid.map(function (c) {
        var a = c.curves[m]; var mx = Math.max.apply(null, a), mn = Math.min.apply(null, a);
        return { max: mx, min: mn, rom: mx - mn, med: median(a) };
      });
      var stat = {};
      ['max', 'min', 'rom', 'med'].forEach(function (k) {
        var col = per.map(function (p) { return p[k]; });
        stat[k] = { value: median(col), sd: sd(col) };
      });
      agg[m] = { mean: meanC, sd: sdC, stat: stat };
    });
    return { cycles: cycles, agg: agg, valid: valid.length };
  }

  // ---- definicje pomiarów (geometria + punkty anatomiczne) ----
  var METRICS = [
    { id: 'knee', name: 'Kąt kolana', unit: '°', vertex: 'K', pts: ['H', 'K', 'A'],
      points: ['Krętarz większy kości udowej (biodro)', 'Nadkłykieć boczny kości udowej (kolano) – wierzchołek', 'Kostka boczna'],
      def: 'Kąt wewnętrzny w punkcie kolana między odcinkami kolano→biodro i kolano→kostka. 180° oznacza pełny wyprost; im mniejsza wartość, tym większe zgięcie.',
      formula: 'θ = arccos( (H−K)·(A−K) / (|H−K|·|A−K|) )',
      reps: [['max', 'Maksimum w cyklu (największy wyprost)'], ['min', 'Minimum w cyklu (największe zgięcie)'], ['rom', 'Zakres ruchu (maks − min)']] },
    { id: 'hip', name: 'Kąt biodra', unit: '°', vertex: 'H', pts: ['S', 'H', 'K'],
      points: ['Wyrostek barkowy łopatki (akromion)', 'Krętarz większy kości udowej – wierzchołek', 'Nadkłykieć boczny kości udowej'],
      def: 'Kąt wewnętrzny w punkcie biodra między odcinkami biodro→bark i biodro→kolano. Mała wartość = biodro mocno zamknięte (zwykle przy górnym położeniu korby).',
      formula: 'θ = ∠(S−H, K−H)',
      reps: [['min', 'Minimum w cyklu (biodro najbardziej zamknięte)'], ['max', 'Maksimum w cyklu'], ['rom', 'Zakres ruchu']] },
    { id: 'ankle', name: 'Kąt stawu skokowego', unit: '°', vertex: 'A', pts: ['K', 'A', 'MT'],
      points: ['Nadkłykieć boczny kości udowej', 'Kostka boczna – wierzchołek', 'Głowa V kości śródstopia (szacowana na bucie)'],
      def: 'Kąt wewnętrzny w punkcie kostki między odcinkami kostka→kolano i kostka→głowa V kości śródstopia. Punkt na stopie jest szacowany na powierzchni buta, co zwiększa niepewność.',
      formula: 'θ = ∠(K−A, MT−A)',
      reps: [['min', 'Minimum w cyklu'], ['max', 'Maksimum w cyklu'], ['rom', 'Zakres ruchu']] },
    { id: 'elbow', name: 'Kąt łokcia', unit: '°', vertex: 'E', pts: ['S', 'E', 'W'],
      points: ['Akromion', 'Nadkłykieć boczny kości ramiennej – wierzchołek', 'Wyrostek rylcowaty (nadgarstek)'],
      def: 'Kąt wewnętrzny w łokciu między odcinkami łokieć→bark i łokieć→nadgarstek, przy chwycie zadeklarowanym dla sesji (np. klamkomanetki).',
      formula: 'θ = ∠(S−E, W−E)',
      reps: [['med', 'Mediana w cyklu'], ['rom', 'Zakres ruchu']] },
    { id: 'torso', name: 'Pochylenie tułowia', unit: '°', vertex: 'H', pts: ['H', 'S'],
      points: ['Krętarz większy', 'Akromion', 'Linia pozioma (z kalibracji / linii podłogi)'],
      def: 'Kąt między odcinkiem krętarz→akromion a linią poziomą. 0° = tułów poziomo, 90° = pionowo. Poziom wyznaczany z kalibracji kamery; przechylenie kamery wpływa bezpośrednio na wynik.',
      formula: 'θ = atan2( −(S.y−H.y), |S.x−H.x| )',
      reps: [['med', 'Mediana w cyklu'], ['rom', 'Zakres ruchu']] },
    { id: 'pelvisY', name: 'Ruch miednicy (pionowy)', unit: 'mm', vertex: 'H', pts: ['H'],
      points: ['Krętarz większy – trajektoria w kolejnych klatkach', 'Skala z kalibracji znacznikiem'],
      def: 'Przemieszczenie pionowe punktu krętarza względem jego średniego położenia, przeliczone na mm skalą ze znacznika. To przybliżenie ruchu miednicy – z jednego punktu w widoku z boku nie da się wyznaczyć przechyłu ani rotacji miednicy.',
      formula: 'Δy(t) = (y_H(t) − ȳ_H) / skala [px/mm]',
      reps: [['rom', 'Amplituda pionowa (maks − min)'], ['max', 'Maks. uniesienie'], ['min', 'Maks. opuszczenie']] },
    { id: 'foot', name: 'Pozycja stopy', unit: '°', vertex: 'A', pts: ['A', 'MT'],
      points: ['Kostka boczna', 'Głowa V kości śródstopia (szacowana na bucie)', 'Linia pozioma'],
      def: 'Kąt odcinka kostka→głowa V kości śródstopia względem poziomu. Wartość dodatnia: przód stopy niżej niż kostka (palce w dół).',
      formula: 'θ = atan2( MT.y−A.y, |MT.x−A.x| )',
      reps: [['max', 'Maks. (palce najbardziej w dół)'], ['min', 'Min.'], ['rom', 'Zakres ruchu']] },
    { id: 'kneeTrack', name: 'Tor ruchu kolana (przód)', unit: 'mm', vertex: 'K', pts: [], na: true,
      points: ['Środek rzepki', 'Środek stawu skokowego', 'Kolec biodrowy przedni górny'],
      def: 'Boczne wychylenie kolana w płaszczyźnie czołowej. Wymaga nagrania z przodu – z kamery bocznej nie da się go wiarygodnie wyznaczyć.',
      formula: '—', reps: [] }
  ];

  // ---- rowery ----
  var BIKES = [
    { id: 'b1', name: 'Szosa endurance', type: 'Szosowy', size: '56', year: 2023, active: true,
      geometry: { stack: [590, 'mm'], reach: [383, 'mm'], seatAngle: [73.5, '°'], headAngle: [72.5, '°'], chainstay: [415, 'mm'], bbDrop: [70, 'mm'], wheelbase: [1005, 'mm'] },
      components: { crank: [172.5, 'mm'], bar: [420, 'mm (c-c)'], stem: [100, 'mm'], stemAngle: [-6, '°'], spacers: [25, 'mm'], saddleW: [143, 'mm'], pedals: ['SPD-SL', ''] },
      contact: { saddleH: [750, 'mm'], setback: [62, 'mm'], saddleTilt: [-1, '°'], reach: [535, 'mm'], drop: [72, 'mm'], cleat: ['8 mm za osią, neutralnie', ''] } },
    { id: 'b2', name: 'Gravel all-road', type: 'Gravelowy', size: 'M', year: 2024, active: false,
      geometry: { stack: [602, 'mm'], reach: [378, 'mm'], seatAngle: [73.0, '°'], headAngle: [71.0, '°'], chainstay: [430, 'mm'], bbDrop: [72, 'mm'], wheelbase: [1032, 'mm'] },
      components: { crank: [172.5, 'mm'], bar: [440, 'mm (c-c)'], stem: [80, 'mm'], stemAngle: [-6, '°'], spacers: [30, 'mm'], saddleW: [143, 'mm'], pedals: ['SPD', ''] },
      contact: { saddleH: [742, 'mm'], setback: [58, 'mm'], saddleTilt: [0, '°'], reach: [520, 'mm'], drop: [48, 'mm'], cleat: ['neutralnie', ''] } },
    { id: 'b3', name: 'MTB trail', type: 'MTB', size: 'L', year: 2021, active: false, incomplete: true,
      geometry: { stack: [628, 'mm'], reach: [470, 'mm'], seatAngle: [76.5, '°'], headAngle: [65.0, '°'], chainstay: [435, 'mm'], bbDrop: [null, 'mm'], wheelbase: [null, 'mm'] },
      components: { crank: [170, 'mm'], bar: [780, 'mm'], stem: [45, 'mm'], stemAngle: [0, '°'], spacers: [null, 'mm'], saddleW: [null, 'mm'], pedals: ['platformy', ''] },
      contact: { saddleH: [null, 'mm'], setback: [null, 'mm'], saddleTilt: [null, '°'], reach: [null, 'mm'], drop: [null, 'mm'], cleat: ['—', ''] } }
  ];
  var FIELD_LABELS = {
    stack: 'Stack', reach: 'Reach', seatAngle: 'Kąt rury podsiodłowej', headAngle: 'Kąt główki ramy', chainstay: 'Długość tylnego trójkąta', bbDrop: 'Obniżenie suportu', wheelbase: 'Rozstaw osi',
    crank: 'Długość korby', bar: 'Szerokość kierownicy', stem: 'Długość mostka', stemAngle: 'Kąt mostka', spacers: 'Podkładki pod mostkiem', saddleW: 'Szerokość siodła', pedals: 'System pedałów',
    saddleH: 'Wysokość siodła (oś suportu → góra siodła)', setback: 'Cofnięcie siodła (za osią suportu)', saddleTilt: 'Kąt siodła', cleat: 'Bloki – ustawienie'
  };
  var CONTACT_LABELS = { reach: 'Odległość siodło → kierownica (reach)', drop: 'Różnica wysokości siodło → kierownica (drop)' };

  // ---- sesje ----
  var rejA = { 0: 'Niepełny cykl (początek fragmentu)', 17: 'Przesłonięcie stopy', 18: 'Przesłonięcie stopy', 43: 'Niepełny cykl (koniec fragmentu)' };
  var rejB = { 0: 'Niepełny cykl (początek fragmentu)', 29: 'Skok punktu kostki', 30: 'Niska pewność punktów (rozmycie)', 31: 'Niska pewność punktów (rozmycie)', 45: 'Niepełny cykl (koniec fragmentu)' };
  var GEO_A = { hy: -765, hx: -200 }, GEO_B = { hy: -770, hx: -201.5, pelvisAmp: 4.2 };

  function strip(n, warn, bad) { var s = []; for (var i = 0; i < n; i++) s.push(bad.indexOf(i) >= 0 ? 'b' : warn.indexOf(i) >= 0 ? 'w' : 'g'); return s; }

  var SESSIONS = [
    { id: 'S-0008', date: '2026-10-27', bike: 'b1', status: 'running', progress: 46, stage: 2, note: 'Po korekcie bloków' },
    Object.assign({ id: 'S-0007', date: '2026-10-21', bike: 'b1', status: 'warn', side: 'prawa', duration: '0:45', fragment: '0:08–0:40', fps: 60, res: '1920×1080',
      geo: GEO_B, settingsNote: 'Po zmianie z 14.10: siodło +5 mm, kąt siodła −1°', keepVideo: false,
      calibration: { marker: 'Znacznik 200 mm', scale: 1.94, tilt: 0.8, ok: true },
      quality: { overall: 'warn', framesOk: 91, knee: 'good', hip: 'good', ankle: 'warn', elbow: 'good', torso: 'good', pelvisY: 'good', foot: 'warn', kneeTrack: 'na',
        conf: { knee: 0.93, hip: 0.91, ankle: 0.68, elbow: 0.88, torso: 0.92, pelvisY: 0.86, foot: 0.64 } },
      warnings: ['Fragment 0:29–0:33: rozmycie ruchu stopy – 3 cykle odrzucone.', 'Punkt głowy V kości śródstopia szacowany na bucie – kąt stawu skokowego i pozycja stopy mają ograniczoną wiarygodność.', 'Tor kolana niedostępny: nagranie tylko z boku.'],
      qstrip: strip(32, [20, 21, 24], [22, 23]) }, genSession(707, GEO_B, 46, rejB)),
    Object.assign({ id: 'S-0006', date: '2026-10-08', bike: 'b1', status: 'done', side: 'prawa', duration: '0:50', fragment: '0:10–0:40', fps: 60, res: '1920×1080',
      geo: GEO_A, settingsNote: 'Ustawienia wyjściowe', keepVideo: true,
      calibration: { marker: 'Znacznik 200 mm', scale: 1.91, tilt: 1.2, ok: true },
      quality: { overall: 'good', framesOk: 96, knee: 'good', hip: 'good', ankle: 'good', elbow: 'good', torso: 'good', pelvisY: 'good', foot: 'good', kneeTrack: 'na',
        conf: { knee: 0.95, hip: 0.93, ankle: 0.81, elbow: 0.9, torso: 0.94, pelvisY: 0.88, foot: 0.78 } },
      warnings: ['Tor kolana niedostępny: nagranie tylko z boku.'],
      qstrip: strip(30, [12], []) }, genSession(606, GEO_A, 44, rejA)),
    { id: 'S-0005', date: '2026-09-15', bike: 'b2', status: 'failed', error: { code: 'E_POSE_COVERAGE', msg: 'Nie wykryto sylwetki w 62% klatek. Najczęstsza przyczyna: nagranie pod światło lub zbyt ciemne.', attempt: '1 z 3' } }
  ];

  // ---- komfort (0 = brak dyskomfortu, 10 = najsilniejszy, uniemożliwia jazdę) ----
  var REGIONS = ['Szyja', 'Barki i kark', 'Dłonie i nadgarstki', 'Dolny odcinek pleców', 'Krocze / kontakt z siodłem', 'Biodro', 'Kolano – przód', 'Kolano – tył', 'Kolano – bok', 'Łydka / ścięgno Achillesa', 'Stopy'];
  var ONSETS = ['od początku', 'po ~15 min', 'po ~30 min', 'po ~60 min', 'po 90+ min', 'dopiero po jeździe'];
  var COMFORT = {
    'S-0006': { ride: '90 min, trenażer, jazda w tlenie', items: { 'Barki i kark': [3, 'obie', 'góra barków', 'po ~60 min'], 'Dolny odcinek pleców': [2, 'obie', 'lędźwie', 'po 90+ min'], 'Krocze / kontakt z siodłem': [4, 'obie', 'ucisk z przodu', 'po ~30 min'], 'Kolano – przód': [5, 'prawa', 'pod rzepką', 'po ~60 min'] } },
    'S-0007': { ride: '90 min, trenażer, jazda w tlenie', items: { 'Barki i kark': [3, 'obie', 'góra barków', 'po ~60 min'], 'Dolny odcinek pleców': [2, 'obie', 'lędźwie', 'po 90+ min'], 'Krocze / kontakt z siodłem': [3, 'obie', 'ucisk z przodu', 'po ~60 min'], 'Kolano – przód': [3, 'prawa', 'pod rzepką', 'po 90+ min'] } }
  };

  // ---- historia zmian ustawień ----
  var CHANGES = [
    { date: '2026-10-25', bike: 'b1', param: 'Bloki – ustawienie', before: '8 mm za osią', after: '6 mm za osią', unit: '', reason: 'Drętwienie palców stopy prawej po ~60 min', before_s: 'S-0007', after_s: 'S-0008' },
    { date: '2026-10-14', bike: 'b1', param: 'Wysokość siodła', before: '745', after: '750', unit: 'mm', reason: 'Dyskomfort przodu kolana prawego po ~60 min; odczucie „siedzenia nisko”', before_s: 'S-0006', after_s: 'S-0007', metric: 'knee', rep: 'max', comfort: 'Kolano – przód' },
    { date: '2026-10-14', bike: 'b1', param: 'Kąt siodła', before: '0', after: '−1', unit: '°', reason: 'Ucisk w okolicy krocza po ~30 min', before_s: 'S-0006', after_s: 'S-0007', metric: 'hip', rep: 'min', comfort: 'Krocze / kontakt z siodłem' },
    { date: '2026-09-20', bike: 'b2', param: 'Długość mostka', before: '90', after: '80', unit: 'mm', reason: 'Napięcie barków na długich podjazdach', before_s: null, after_s: 'S-0005' }
  ];

  var PROFILE = {
    name: 'Rowerzysta Demo', account: 'konto ridemore.bike (istniejące)',
    basic: { height: 182, mass: 76, inseam: 86, shoe: 44, birth: 1986, sex: 'nie podano' },
    segments: { thigh: 46, shank: 45, foot: 27, torso: 58, upperArm: 34, forearm: 28, shoulders: 41, armSpan: 184 },
    rom: { slr: [78, 'prawa'], slrL: [75, 'lewa'], lunge: [11, 'prawa'], lungeL: [12, 'lewa'], hipFlex: [118, 'obie'] },
    complaints: [
      { region: 'Kolano – przód', side: 'prawa', since: '2026-08', when: 'po ok. 60 min jazdy', note: 'ustępuje po 1 dniu, bez urazu' },
      { region: 'Dolny odcinek pleców', side: 'obie', since: '2025', when: 'długie jazdy > 3 h', note: '' }
    ]
  };

  window.RM = {
    DEMO: true, pose: pose, angleAt: angleAt, angleToHorizontal: angleToHorizontal, N: N,
    METRICS: METRICS, BIKES: BIKES, FIELD_LABELS: FIELD_LABELS, CONTACT_LABELS: CONTACT_LABELS,
    SESSIONS: SESSIONS, REGIONS: REGIONS, ONSETS: ONSETS, COMFORT: COMFORT, CHANGES: CHANGES, PROFILE: PROFILE
  };
})();
