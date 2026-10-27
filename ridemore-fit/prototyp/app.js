/* ridemore.bike Fit – prototyp UX/UI (bez backendu).
   Ekrany renderowane po stronie przeglądarki z jawnie oznaczonych danych demonstracyjnych (demo-data.js).
   Nic nie jest wysyłane na serwer. */
(function () {
  'use strict';
  var RM = window.RM;
  var app = document.getElementById('app');
  var cleanup = [];           // timery/animacje bieżącego ekranu
  var state = { wizard: null, metric: 'knee', showCycles: false, cmpMetric: 'knee', bikeTab: 'contact', profileTab: 'basic', jobs: {} };

  // ---------- pomocnicze ----------
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function num(v, d) { if (v == null || isNaN(v)) return '—'; return (d == null ? 1 : d) === 0 ? String(Math.round(v)) : v.toFixed(d == null ? 1 : d).replace('.', ','); }
  function signed(v, d) { if (v == null || isNaN(v)) return '—'; var s = num(Math.abs(v), d); return (v > 0.04 ? '+' : v < -0.04 ? '−' : '±') + s; }
  function dateTxt(iso) { var p = iso.split('-'); return p[2] + '.' + p[1] + '.' + p[0]; }
  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }
  function session(id) { return RM.SESSIONS.filter(function (s) { return s.id === id; })[0]; }
  function bike(id) { return RM.BIKES.filter(function (b) { return b.id === id; })[0]; }
  function metric(id) { return RM.METRICS.filter(function (m) { return m.id === id; })[0]; }
  function analysed() { return RM.SESSIONS.filter(function (s) { return s.agg; }); }
  function toast(msg) { var t = $('#toast'); t.textContent = msg; t.style.display = 'block'; clearTimeout(t._h); t._h = setTimeout(function () { t.style.display = 'none'; }, 3200); }

  var Q_LABEL = { good: 'Wiarygodny', warn: 'Ograniczona wiarygodność', bad: 'Niewiarygodny', na: 'Niedostępny' };
  function qBadge(level, short) { return '<span class="q ' + level + '">' + (short ? { good: 'OK', warn: 'Ograniczona', bad: 'Niewiarygodny', na: 'Brak' }[level] : Q_LABEL[level]) + '</span>'; }
  var ST_LABEL = { pending: 'Oczekujące', running: 'W trakcie', done: 'Zakończone', warn: 'Zakończone z ostrzeżeniami', failed: 'Nieudane' };
  function stBadge(st) { return '<span class="st ' + st + '">' + ST_LABEL[st] + '</span>'; }
  function demoNote(txt) { return '<div class="callout demo"><div class="ic">D</div><div class="small"><b>Dane demonstracyjne.</b> ' + txt + '</div></div>'; }

  var ICONS = {
    home: 'M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z',
    plus: 'M12 5v14M5 12h14',
    chart: 'M4 20V10M10 20V4M16 20v-7M22 20H2',
    cmp: 'M8 3v18M16 3v18M3 8h5M16 16h5',
    heart: 'M12 20s-7-4.5-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.5-7 10-7 10z',
    hist: 'M12 7v5l3 2M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5',
    bike: 'M5.5 17.5a3.5 3.5 0 1 0 0-.01M18.5 17.5a3.5 3.5 0 1 0 0-.01M5.5 17.5l4-8h6l3 8M9.5 9.5L8 6H6M15.5 9.5l-3 8',
    user: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21a8 8 0 0 1 16 0',
    job: 'M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M5.6 18.4l2.8-2.8M15.6 8.4l2.8-2.8'
  };
  function icon(n) { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="' + ICONS[n] + '"/></svg>'; }

  // ---------- nawigacja ----------
  var NAV = [
    ['pulpit', 'Moje analizy', 'home'], ['nowa', 'Nowa analiza', 'plus'], ['komfort/S-0007', 'Komfort jazdy', 'heart'],
    ['historia', 'Historia zmian', 'hist'], ['rowery', 'Moje rowery', 'bike'], ['profil', 'Profil', 'user']
  ];
  var NAV_ALIAS = { wyniki: 'pulpit', analiza: 'pulpit', porownanie: 'pulpit' };
  function renderNav(route) {
    var head = route.split('/')[0];
    $('#nav').innerHTML = NAV.map(function (n) {
      if (n[0] === 'grp') return '<div class="grp">' + n[1] + '</div>';
      var on = n[0].split('/')[0] === (NAV_ALIAS[head] || head);
      return '<a href="#/' + n[0] + '" class="' + (on ? 'on' : '') + '">' + icon(n[2]) + '<span>' + n[1] + '</span></a>';
    }).join('');
  }

  // ---------- wykres liniowy cyklu 0–100% ----------
  // series: [{y:[], sd:[], color, dash, label, thin}]
  function lineChart(el, opt) {
    var W = 720, H = opt.height || 280, m = { l: 46, r: 14, t: 14, b: 40 };
    var all = [];
    opt.series.forEach(function (s) { s.y.forEach(function (v, i) { all.push(v + (s.sd ? s.sd[i] : 0), v - (s.sd ? s.sd[i] : 0)); }); });
    var lo = Math.min.apply(null, all), hi = Math.max.apply(null, all), pad = (hi - lo) * 0.08 || 1;
    lo -= pad; hi += pad;
    var step = niceStep((hi - lo) / 5); lo = Math.floor(lo / step) * step; hi = Math.ceil(hi / step) * step;
    var n = opt.series[0].y.length;
    function X(i) { return m.l + (W - m.l - m.r) * i / (n - 1); }
    function Y(v) { return m.t + (H - m.t - m.b) * (1 - (v - lo) / (hi - lo)); }
    var g = '';
    for (var v = lo; v <= hi + 1e-9; v += step) g += '<line x1="' + m.l + '" x2="' + (W - m.r) + '" y1="' + Y(v) + '" y2="' + Y(v) + '" stroke="#E2E5DD"/><text x="' + (m.l - 8) + '" y="' + (Y(v) + 4) + '" text-anchor="end" font-size="11" fill="#8A948C" font-family="IBM Plex Mono">' + num(v, step < 1 ? 1 : 0) + '</text>';
    [[0, 'GMP'], [25, '90°'], [50, 'DMP'], [75, '270°'], [100, 'GMP']].forEach(function (c) {
      var x = X(c[0] / 100 * (n - 1));
      g += '<line x1="' + x + '" x2="' + x + '" y1="' + m.t + '" y2="' + (H - m.b) + '" stroke="' + (c[0] % 50 === 0 ? '#D3D8CE' : '#EDF1EC') + '"/>';
      g += '<text x="' + x + '" y="' + (H - m.b + 16) + '" text-anchor="middle" font-size="11" fill="#5E6A62" font-family="IBM Plex Mono">' + c[0] + '%</text>';
      g += '<text x="' + x + '" y="' + (H - m.b + 30) + '" text-anchor="middle" font-size="10" fill="#8A948C" font-family="IBM Plex Mono">' + c[1] + '</text>';
    });
    g += '<text x="12" y="' + (m.t + 2) + '" font-size="11" fill="#8A948C" font-family="IBM Plex Mono" transform="rotate(-90 12 ' + (m.t + 2) + ')" text-anchor="end">' + esc(opt.unit) + '</text>';
    var marks = '';
    opt.series.forEach(function (s) {
      if (s.sd) {
        var up = s.y.map(function (v, i) { return X(i) + ',' + Y(v + s.sd[i]); });
        var dn = s.y.map(function (v, i) { return X(i) + ',' + Y(v - s.sd[i]); }).reverse();
        marks += '<polygon points="' + up.concat(dn).join(' ') + '" fill="' + s.color + '" opacity="0.14"/>';
      }
    });
    opt.series.forEach(function (s) {
      marks += '<polyline points="' + s.y.map(function (v, i) { return X(i) + ',' + Y(v); }).join(' ') + '" fill="none" stroke="' + s.color + '" stroke-width="' + (s.thin ? 1 : 2) + '"' + (s.thin ? ' opacity="0.35"' : '') + (s.dash ? ' stroke-dasharray="6 4"' : '') + ' stroke-linejoin="round" stroke-linecap="round"/>';
    });
    el.innerHTML = '<svg viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="' + esc(opt.title || 'wykres') + '">' + g + marks +
      '<line class="xh" y1="' + m.t + '" y2="' + (H - m.b) + '" stroke="#15201A" stroke-width="1" visibility="hidden"/>' +
      '<rect x="' + m.l + '" y="' + m.t + '" width="' + (W - m.l - m.r) + '" height="' + (H - m.t - m.b) + '" fill="transparent"/></svg><div class="tip"></div>';
    var svg = el.querySelector('svg'), tip = el.querySelector('.tip'), xh = el.querySelector('.xh');
    var main = opt.series.filter(function (s) { return !s.thin; });
    svg.addEventListener('mousemove', function (e) {
      var r = svg.getBoundingClientRect(), sx = (e.clientX - r.left) / r.width * W;
      var i = Math.round((sx - m.l) / (W - m.l - m.r) * (n - 1)); if (i < 0 || i >= n) { tip.style.display = 'none'; xh.setAttribute('visibility', 'hidden'); return; }
      xh.setAttribute('x1', X(i)); xh.setAttribute('x2', X(i)); xh.setAttribute('visibility', 'visible');
      tip.innerHTML = '<b>' + Math.round(i / (n - 1) * 100) + '% cyklu</b><br>' + main.map(function (s) { return '<span style="color:' + s.color + '">■</span> ' + esc(s.label) + ': ' + num(s.y[i]) + ' ' + esc(opt.unit) + (s.sd ? ' (±' + num(s.sd[i]) + ')' : ''); }).join('<br>');
      tip.style.display = 'block'; tip.style.left = (X(i) / W * r.width) + 'px'; tip.style.top = (Y(main[0].y[i]) / H * r.height - 10) + 'px';
    });
    svg.addEventListener('mouseleave', function () { tip.style.display = 'none'; xh.setAttribute('visibility', 'hidden'); });
  }
  function niceStep(raw) { var p = Math.pow(10, Math.floor(Math.log10(raw))), f = raw / p; return (f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10) * p; }

  // ---------- podgląd: schemat sylwetki (zastępuje nagranie w prototypie) ----------
  var SEG = [['H', 'K'], ['K', 'A'], ['A', 'MT'], ['H', 'S'], ['S', 'E'], ['E', 'W']];
  var PT_NAME = { H: 'krętarz', K: 'kolano', A: 'kostka', MT: 'V k. śródstopia', S: 'akromion', E: 'łokieć', W: 'nadgarstek' };
  function bikeSvg() {
    var R = { x: -409, y: -70 }, F = { x: 596, y: -70 }, st = { x: -159, y: -537 }, ht = { x: 383, y: -590 }, hb = { x: 434, y: -428 };
    function L(a, b, w, c) { return '<line x1="' + a.x + '" y1="' + a.y + '" x2="' + b.x + '" y2="' + b.y + '" stroke="' + (c || '#6E7A72') + '" stroke-width="' + (w || 14) + '" stroke-linecap="round"/>'; }
    return '<line x1="-1000" x2="1200" y1="265" y2="265" stroke="#3A443E" stroke-width="6"/>' +
      '<circle cx="' + R.x + '" cy="' + R.y + '" r="335" fill="none" stroke="#4A554E" stroke-width="18"/>' +
      '<circle cx="' + F.x + '" cy="' + F.y + '" r="335" fill="none" stroke="#4A554E" stroke-width="18"/>' +
      '<path d="M' + R.x + ' ' + R.y + ' L-560 265 M' + R.x + ' ' + R.y + ' L-260 265" stroke="#59645D" stroke-width="16"/>' +
      '<rect x="-470" y="240" width="160" height="30" fill="#59645D"/>' +
      L({ x: 0, y: 0 }, R) + L(R, { x: -150, y: -505 }) + L({ x: 0, y: 0 }, st, 18) + L(st, ht, 16) + L({ x: 0, y: 0 }, hb, 20) + L(ht, hb, 20) + L(hb, F, 12) +
      L(st, { x: -205, y: -695 }, 10, '#8A948C') + '<path d="M-320 -700 Q-200 -718 -70 -706 L-80 -692 Q-200 -700 -315 -688 Z" fill="#8A948C"/>' +
      L(ht, { x: 375, y: -615 }, 12) + L({ x: 375, y: -615 }, { x: 470, y: -628 }, 12, '#8A948C') +
      '<path d="M470 -628 Q560 -640 560 -690 M470 -628 Q520 -560 470 -520" stroke="#8A948C" stroke-width="10" fill="none" stroke-linecap="round"/>' +
      '<circle cx="0" cy="0" r="100" fill="none" stroke="#6E7A72" stroke-width="10"/>';
  }
  function figureSvg(p, opt) {
    var pts = p.pts, o = opt || {};
    var s = '<line x1="0" y1="0" x2="' + pts.P.x + '" y2="' + pts.P.y + '" stroke="#C9D1CB" stroke-width="14" stroke-linecap="round"/>';
    s += '<circle cx="' + (pts.S.x + 95) + '" cy="' + (pts.S.y - 100) + '" r="92" fill="none" stroke="#E9EEEA" stroke-width="8" opacity=".6"/>';
    if (o.lines !== false) SEG.forEach(function (sg) {
      var a = pts[sg[0]], b = pts[sg[1]];
      s += '<line x1="' + a.x + '" y1="' + a.y + '" x2="' + b.x + '" y2="' + b.y + '" stroke="#FFC93F" stroke-width="9" stroke-linecap="round"/>';
    });
    var m = o.metric && metric(o.metric);
    if (m && m.pts.length === 3 && o.angles !== false) {
      var a = pts[m.pts[0]], c = pts[m.pts[1]], b = pts[m.pts[2]];
      var a1 = Math.atan2(a.y - c.y, a.x - c.x), a2 = Math.atan2(b.y - c.y, b.x - c.x), r = 90;
      var d = a2 - a1; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
      var e = { x: c.x + r * Math.cos(a1 + d), y: c.y + r * Math.sin(a1 + d) };
      s += '<path d="M' + c.x + ' ' + c.y + ' L' + (c.x + r * Math.cos(a1)) + ' ' + (c.y + r * Math.sin(a1)) + ' A' + r + ' ' + r + ' 0 0 ' + (d > 0 ? 1 : 0) + ' ' + e.x + ' ' + e.y + ' Z" fill="#2C6B4F" opacity=".55"/>';
      s += '<text x="' + (c.x + 110) + '" y="' + (c.y - 20) + '" fill="#fff" font-size="56" font-family="IBM Plex Mono" font-weight="600">' + num(p[m.id], 0) + '°</text>';
    }
    if (o.points !== false) Object.keys(PT_NAME).forEach(function (k) {
      var q = pts[k], fixed = o.fixed && o.fixed[k];
      s += '<circle data-pt="' + k + '" cx="' + q.x + '" cy="' + q.y + '" r="' + (o.edit ? 26 : 17) + '" fill="' + (fixed ? '#2B57C8' : '#fff') + '" stroke="#15201A" stroke-width="5"' + (o.edit ? ' style="cursor:grab"' : '') + '><title>' + PT_NAME[k] + '</title></circle>';
      if (o.lowConf && o.lowConf.indexOf(k) >= 0) s += '<circle cx="' + q.x + '" cy="' + q.y + '" r="40" fill="none" stroke="#F0B41E" stroke-width="5" stroke-dasharray="10 8"/>';
    });
    return s;
  }

  function viewer(el, s) {
    var t = 0, playing = false, raf = null, last = 0, edit = false, fixed = {};
    var opts = { points: true, lines: true, angles: true };
    var lowConf = s.quality.ankle !== 'good' ? ['A', 'MT'] : [];
    el.innerHTML =
      '<div class="viewer"><svg viewBox="-760 -1420 1720 1700" aria-label="Schemat demonstracyjny sylwetki"></svg>' +
      '<div class="ov r t-readout"></div></div>' +
      '<div class="xs muted" style="margin-top:4px"><b>Schemat demonstracyjny</b> – w aplikacji w tym miejscu będzie nagranie z naniesionymi punktami i liniami.</div>' +
      '<div class="vctl"><button class="btn sm play">▶ Odtwórz</button><input type="range" min="0" max="100" value="0" class="scrub" aria-label="Faza cyklu"><span class="mono small t-phase" style="width:84px"></span></div>' +
      '<div class="row small">' +
      '<label class="row" style="gap:5px"><input type="checkbox" data-o="points" checked> Punkty</label>' +
      '<label class="row" style="gap:5px"><input type="checkbox" data-o="lines" checked> Linie</label>' +
      '<label class="row" style="gap:5px"><input type="checkbox" data-o="angles" checked> Kąt wybranego pomiaru</label>' +
      '<span class="spacer"></span><button class="btn sm edit">Korekta punktów</button></div>' +
      '<div class="small muted edit-note" style="display:none;margin-top:6px">Tryb korekty: zatrzymaj klatkę i przeciągnij błędny punkt. Skorygowane punkty są niebieskie. <b>W prototypie korekta nie przelicza wyników.</b></div>' +
      (lowConf.length ? '<div class="xs muted" style="margin-top:6px"><span style="color:#B98500">◌</span> przerywany pierścień = punkt o niskiej pewności śledzenia</div>' : '');
    var svg = el.querySelector('svg');
    function draw() {
      var p = RM.pose(t, s.geo);
      Object.keys(fixed).forEach(function (k) { p.pts[k] = fixed[k]; });
      svg.innerHTML = bikeSvg() + figureSvg(p, { metric: state.metric, points: opts.points, lines: opts.lines, angles: opts.angles, edit: edit, fixed: fixed, lowConf: lowConf });
      el.querySelector('.t-phase').textContent = Math.round(t * 100) + '% cyklu';
      var m = metric(state.metric);
      el.querySelector('.t-readout').textContent = m.pts.length === 3 ? m.name + ': ' + num(p[m.id], 0) + '°' : m.name;
      el.querySelector('.scrub').value = Math.round(t * 100);
    }
    function loop(ts) { if (!playing) return; if (last) t = (t + (ts - last) / 1000 * 88 / 60 / 2.5) % 1; last = ts; draw(); raf = requestAnimationFrame(loop); }
    el.querySelector('.play').onclick = function () {
      playing = !playing; this.textContent = playing ? '❚❚ Pauza' : '▶ Odtwórz'; last = 0;
      if (playing) { edit = false; syncEdit(); raf = requestAnimationFrame(loop); }
    };
    el.querySelector('.scrub').oninput = function () { t = this.value / 100; draw(); };
    $$('input[data-o]', el).forEach(function (c) { c.onchange = function () { opts[c.dataset.o] = c.checked; draw(); }; });
    function syncEdit() { el.querySelector('.edit').textContent = edit ? 'Zakończ korektę' : 'Korekta punktów'; el.querySelector('.edit-note').style.display = edit ? 'block' : 'none'; draw(); }
    el.querySelector('.edit').onclick = function () {
      edit = !edit; if (edit && playing) el.querySelector('.play').click(); syncEdit();
      if (!edit && Object.keys(fixed).length) toast('Zapisano ' + Object.keys(fixed).length + ' korekt(y) punktów (prototyp – bez przeliczenia wyników).');
    };
    var drag = null;
    function toSvg(e) { var pt = svg.createSVGPoint(); pt.x = e.clientX; pt.y = e.clientY; return pt.matrixTransform(svg.getScreenCTM().inverse()); }
    svg.addEventListener('pointerdown', function (e) { if (!edit) return; var k = e.target.getAttribute('data-pt'); if (k) { drag = k; e.preventDefault(); } });
    svg.addEventListener('pointermove', function (e) { if (!drag) return; var q = toSvg(e); fixed[drag] = { x: q.x, y: q.y }; draw(); });
    window.addEventListener('pointerup', function () { drag = null; });
    el._redraw = draw;
    draw();
    cleanup.push(function () { playing = false; cancelAnimationFrame(raf); });
  }

  // ---------- ekran: Pulpit (tylko analizy i ich efekty) ----------
  function viewDashboard() {
    var list = RM.SESSIONS.slice().sort(function (a, b) { return a.date < b.date ? 1 : -1; });
    var cards = list.map(function (s) {
      var b = bike(s.bike), body;
      if (!s.agg && s.status === 'failed') body = '<p class="small">Nie udało się przeanalizować nagrania. ' + esc(s.error.msg.split('.').slice(1).join('.').trim() || '') + '</p><a class="btn sm" href="#/analiza/' + s.id + '">Co zrobić?</a>';
      else if (!s.agg) body = '<div class="bar" style="margin:8px 0 6px"><i style="width:' + s.progress + '%"></i></div><p class="small muted">Analizujemy nagranie. Wynik pojawi się tutaj – możesz zamknąć stronę.</p><a class="btn sm" href="#/analiza/' + s.id + '">Postęp</a>';
      else {
        var F = fitOf(s), prev = prevSession(s), eff = '';
        if (prev) {
          var P = fitOf(prev), byP = {}; P.list.forEach(function (r) { byP[r.adj.id] = r; });
          var ch = RM.CHANGES.filter(function (c) { return c.bike === s.bike && c.date > prev.date && c.date <= s.date; });
          var better = F.list.filter(function (r) { return byP[r.adj.id].status !== 'ok' && r.status === 'ok'; });
          var cA = (RM.COMFORT[prev.id] || { items: {} }).items, cB = (RM.COMFORT[s.id] || { items: {} }).items;
          var comf = Object.keys(cB).filter(function (k) { return cA[k] && cB[k][0] < cA[k][0]; });
          eff = '<div class="effect"><b>Efekt zmiany</b> (' + ch.map(function (c) { return esc(c.param.toLowerCase()) + ' ' + esc(c.before) + '→' + esc(c.after) + ' ' + c.unit; }).join(', ') + '):<br>' +
            (better.length ? better.map(function (r) { return '✓ ' + esc(r.adj.name.toLowerCase()) + ' – teraz w zakresie'; }).join('<br>') : 'bez zmiany statusu regulacji') +
            (comf.length ? '<br>✓ mniej dyskomfortu: ' + comf.map(function (k) { return esc(k.toLowerCase()) + ' ' + cA[k][0] + ' → ' + cB[k][0]; }).join(', ') : '') + '</div>';
        }
        var done = RM.CHANGES.filter(function (c) { return c.before_s === s.id && F.next && c.adj === F.next.adj.id; });
        body = '<div class="row" style="gap:6px;margin:4px 0 10px">' + F.list.map(function (r) { return '<span title="' + esc(r.adj.name) + '">' + fitStatus(r.status).replace(ST_PLAIN[r.status][1], esc(r.adj.name)) + '</span>'; }).join('') + '</div>' +
          (F.next ? '<p class="next-line">' + (done.length ? '<span class="pill">wykonano ' + dateTxt(done[0].date) + '</span> ' : '<b>Następny krok:</b> ') + esc(actionText(F.next)) + '</p>' : '<p class="next-line">✓ Wszystko w zalecanych zakresach</p>') + eff +
          '<div class="row" style="margin-top:10px"><a class="btn sm pri" href="#/wyniki/' + s.id + '">Zobacz analizę</a></div>';
      }
      return '<div class="card acard"><div class="card-h" style="margin-bottom:4px"><div><h2>' + dateTxt(s.date) + ' · ' + esc(b.name) + '</h2><div class="xs muted">' + s.id + (s.agg ? ' · ' + fitOf(s).okCount + ' z 4 regulacji w zakresie' : '') + '</div></div>' + (s.agg ? '<span class="st done">Gotowe</span>' : stBadge(s.status)) + '</div>' + body + '</div>';
    }).join('');
    return '<div class="page-head"><div><div class="crumbs">ridemore.bike / fit</div><h1>Moje analizy</h1><div class="sub">Każda analiza pokazuje, co zmienić w rowerze, i czy poprzednia zmiana pomogła.</div></div>' +
      '<a class="btn pri" href="#/nowa">' + icon('plus').replace('<svg', '<svg width="16" height="16"') + ' Nowa analiza</a></div>' +
      '<div class="grid g2">' + cards + '</div>';
  }

  // ---------- zalecenia dla użytkownika (warstwa „laik”) ----------
  var ST_PLAIN = { ok: ['good', 'W porządku'], near: ['warn', 'Blisko granicy'], out: ['bad', 'Do zmiany'], unknown: ['na', 'Nie oceniono'] };
  function fitStatus(st) { var x = ST_PLAIN[st]; return '<span class="q ' + x[0] + '">' + x[1] + '</span>'; }
  function fitOf(s) { return RM_FIT.evaluateAll(s, bike(s.bike).type); }
  function prevSession(s) { return analysed().filter(function (x) { return x.bike === s.bike && x.date < s.date; }).sort(function (a, b) { return a.date < b.date ? 1 : -1; })[0]; }
  function actionText(r) { return r.text.action + ' o ok. ' + r.mm + ' mm'; }
  function rangeBar(r, small) {
    var sc = r.adj.scale, g = r.range.green, y = r.range.yellow;
    function pct(v) { return Math.max(0, Math.min(100, (v - sc[0]) / (sc[1] - sc[0]) * 100)); }
    var segs = [[sc[0], y[0], 'r'], [y[0], g[0], 'y'], [g[0], g[1], 'g'], [g[1], y[1], 'y'], [y[1], sc[1], 'r']];
    return '<div class="rbar' + (small ? ' sm' : '') + '" role="img" aria-label="' + esc(r.adj.measure + ': ' + num(r.value, 0) + ' ' + r.adj.unit + ', zakres ' + g[0] + '–' + g[1]) + '">' +
      '<div class="rbar-track">' + segs.map(function (sg) { return '<i class="' + sg[2] + '" style="width:' + (pct(sg[1]) - pct(sg[0])) + '%"></i>'; }).join('') + '</div>' +
      (r.status === 'unknown' ? '' : '<b class="rbar-mk" style="left:' + pct(r.value) + '%"></b>') +
      (small ? '' : '<div class="rbar-lbl"><span>' + esc(r.adj.low.title) + '</span><span>' + esc(r.adj.high.title) + '</span></div>') + '</div>';
  }
  function labeledFigure(s, sim) {
    var geo = Object.assign({}, s.geo, sim || {}), p = RM.pose(0.42, geo), q = p.pts;
    function lbl(pt, dx, dy, title, val) {
      var x = pt.x + dx, y = pt.y + dy;
      return '<line x1="' + pt.x + '" y1="' + pt.y + '" x2="' + (x + 150) + '" y2="' + (y + 55) + '" stroke="#9CC7AE" stroke-width="4"/>' +
        '<rect x="' + x + '" y="' + y + '" width="300" height="110" rx="18" fill="#2C6B4F" opacity=".92"/>' +
        '<text x="' + (x + 150) + '" y="' + (y + 40) + '" text-anchor="middle" fill="#D9EBE0" font-size="30" font-family="Instrument Sans">' + title + '</text>' +
        '<text x="' + (x + 150) + '" y="' + (y + 90) + '" text-anchor="middle" fill="#fff" font-size="50" font-family="IBM Plex Mono" font-weight="600">' + val + '</text>';
    }
    var f = fitOf(s).list, by = {}; f.forEach(function (r) { by[r.adj.id] = r; });
    return '<svg viewBox="-760 -1420 1720 1700" aria-label="Schemat pozycji z opisanymi pomiarami">' + bikeSvg() + figureSvg(p, { angles: false }) +
      lbl(q.K, 60, -40, 'Zgięcie kolana', num(by.saddleH.value, 0) + '°') +
      lbl(q.H, -560, -260, 'Pochylenie pleców', num(by.barHeight.value, 0) + '°') +
      lbl(q.S, 120, -120, 'Tułów–ramię', num(by.barReach.value, 0) + '°') +
      lbl(q.P, -700, -170, 'Kolano–pedał', num(by.saddleFA.value, 0) + ' mm') + '</svg>';
  }
  function adjCard(r, compact) {
    var a = r.adj, act = r.status === 'ok' ? '' : r.status === 'unknown' ? '<p class="small muted">Nagranie nie pozwala tego wiarygodnie ocenić – brak zalecenia.</p>' :
      '<p class="act">→ ' + esc(actionText(r)) + (r.mmTotal > r.mm ? ' <span class="small muted">(pierwszy krok; łącznie może być potrzebne ok. ' + r.mmTotal + ' mm)</span>' : '') + '</p>';
    return '<div class="adj"><div class="row"><h3>' + esc(a.name) + '</h3><span class="spacer"></span>' + fitStatus(r.status) + '</div>' +
      '<p class="small" style="margin:6px 0 10px">' + esc(r.status === 'ok' ? a.ok : r.status === 'unknown' ? '' : r.text.plain) + '</p>' + rangeBar(r) +
      '<div class="xs muted" style="margin-top:4px">' + esc(a.measure) + ': <b class="mono">' + (r.status === 'unknown' ? '—' : num(r.value, 0) + ' ' + a.unit) + '</b> · zalecany zakres ' + rangeTxt(r.range.green, a.unit) + '</div>' + act + '</div>';
  }
  function rangeTxt(g, u) { return g[0] < 0 ? 'od ' + signed(g[0], 0) + ' do ' + signed(g[1], 0) + ' ' + u : g[0] + '–' + g[1] + ' ' + u; }
  function viewSummary(s) {
    var b = bike(s.bike), F = fitOf(s), nx = F.next, prev = prevSession(s);
    var head = resultsHead(s, 'summary');
    var next = nx ? '<div class="card next"><div class="layer-tag">Twój następny krok</div><h2 class="big">' + esc(actionText(nx)) + '</h2>' +
      '<p>' + esc(nx.text.plain) + '</p><p class="small muted"><b>Jak to zrobić:</b> ' + esc(nx.adj.how) + '</p>' +
      '<p class="small"><b>Na co wpłynie:</b> ' + nx.adj.body.map(function (x) { return '<span class="pill">' + x + '</span>'; }).join(' ') + '</p>' +
      '<div class="row" style="margin-top:10px"><a class="btn pri" href="#/rowery/' + b.id + '">Zrobiłem – zapisz zmianę</a><a class="btn" href="#/nowa">Nagraj kontrolnie</a></div>' +
      '<p class="xs muted" style="margin-top:10px">Zmieniaj jedną rzecz naraz. Po zmianie pojeździj kilka razy i nagraj kontrolną analizę – zobaczysz, czy zmiana pomogła.</p></div>'
      : '<div class="card next"><div class="layer-tag">Twój następny krok</div><h2 class="big">Nic nie musisz zmieniać</h2><p>Wszystkie ocenione regulacje są w zalecanych zakresach. Jeśli coś Cię boli, opisz to w ocenie komfortu.</p></div>';
    var qual = s.quality.overall === 'good' ? '<span class="q good">Nagranie dobrej jakości</span>' :
      '<span class="q warn">Część pomiarów mniej pewna</span> <span class="small muted">' + (s.quality.ankle !== 'good' ? 'stopa była słabo widoczna – nie wpływa to na zalecenia poniżej.' : '') + '</span>';
    var cmp = '';
    if (prev) {
      var P = fitOf(prev), byP = {}; P.list.forEach(function (r) { byP[r.adj.id] = r; });
      var ch = RM.CHANGES.filter(function (c) { return c.bike === s.bike && c.date > prev.date && c.date <= s.date; });
      var cA = (RM.COMFORT[prev.id] || { items: {} }).items, cB = (RM.COMFORT[s.id] || { items: {} }).items;
      cmp = '<div class="card" style="margin-top:16px"><div class="card-h"><h2>Co dała zmiana</h2><span class="small muted">' + dateTxt(prev.date) + ' → ' + dateTxt(s.date) + '</span></div>' +
        (ch.length ? '<p class="small">Zmieniono: ' + ch.map(function (c) { return '<b>' + esc(c.param.toLowerCase()) + '</b> ' + esc(c.before) + ' → ' + esc(c.after) + ' ' + c.unit; }).join(', ') + '</p>' : '') +
        '<div class="tbl-wrap"><table class="tbl ba"><thead><tr><th></th><th>Przed</th><th>Po</th></tr></thead><tbody>' + F.list.map(function (r) {
          var p0 = byP[r.adj.id];
          return '<tr><td><b>' + esc(r.adj.name) + '</b></td><td>' + fitStatus(p0.status) + rangeBar(p0, true) + '</td><td>' + fitStatus(r.status) + rangeBar(r, true) + '</td></tr>';
        }).join('') + '</tbody></table></div>' +
        (Object.keys(cB).length ? '<p class="small" style="margin-top:10px"><b>Twoje odczucia:</b> ' + Object.keys(cB).filter(function (k) { return cA[k] && cA[k][0] !== cB[k][0]; }).map(function (k) { return esc(k.toLowerCase()) + ' ' + cA[k][0] + ' → ' + cB[k][0] + '/10'; }).join(' · ') + '</p>' : '') + '</div>';
    }
    return head +
      '<div class="grid g2" style="margin-top:16px"><div><div class="viewer">' + labeledFigure(s) + '</div><div class="xs muted" style="margin-top:4px"><b>Schemat demonstracyjny</b> – w aplikacji tu będzie kadr z Twojego nagrania z opisanymi pomiarami.</div>' +
      '<p class="small" style="margin-top:8px">' + qual + '</p></div>' + next + '</div>' +
      '<div class="card" style="margin-top:16px"><div class="card-h"><h2>Twoja pozycja – 4 regulacje</h2><span class="small"><b>' + F.okCount + ' z 4</b> w zalecanym zakresie</span></div>' +
      '<div class="grid g2">' + F.list.map(function (r) { return adjCard(r); }).join('') + '</div></div>' + cmp +
      '<div class="card" style="margin-top:16px" id="sim"><div class="card-h"><h2>Sprawdź, zanim zmienisz</h2><span class="xs muted">szacunek z modelu geometrycznego, nie pomiar</span></div>' +
      '<p class="small muted">Przesuń suwak, aby zobaczyć, jak zmiana roweru przesunie Twoją pozycję w zakresach.</p>' +
      '<div class="grid g2"><div class="stack">' + [['dh', 'Wysokość siodła', 'mm'], ['df', 'Siodło do tyłu (+) / przodu (−)', 'mm'], ['ds', 'Długość mostka', 'mm'], ['db', 'Wysokość kierownicy', 'mm']].map(function (x) {
        return '<div class="fld"><label>' + x[1] + ' <b class="mono" data-simv="' + x[0] + '">0 mm</b></label><input type="range" min="-30" max="30" step="5" value="0" data-sim-in="' + x[0] + '" style="accent-color:var(--accent)"></div>';
      }).join('') + '</div><div id="simOut"></div></div></div>' +
      '<div class="callout info" style="margin-top:16px"><div class="ic">i</div><div class="small">Zalecenia są szacunkiem na podstawie nagrania z boku (2D) i zakresów z publikacji oraz praktyki bike fittingu – źródła w zakładce <a href="#" data-act="details">Szczegóły</a>. Nie zastępują wizyty u bike fittera. Jeśli coś boli, a nie tylko męczy, skonsultuj się z fizjoterapeutą.</div></div>' +
      '<div style="margin-top:16px">' + demoNote('Pozycja i zalecenia wyliczone z danych demonstracyjnych i roboczego modelu dopasowania (' + RM_FIT.VERSION + ').') + '</div>';
  }
  // symulacja: wartość zmierzona + różnica z modelu geometrycznego (geo + zmiana) − (geo)
  function simulate(s, d) {
    var sin = Math.sin(73.5 * Math.PI / 180), cos = Math.cos(73.5 * Math.PI / 180);
    var g0 = Object.assign({ tilt: 42, wx: 655, wy: -760 }, s.geo);
    var g1 = Object.assign({}, g0, { hy: g0.hy - d.dh * sin, hx: g0.hx - d.dh * cos - d.df, wx: g0.wx + d.ds, wy: g0.wy - d.db, tilt: g0.tilt + d.db * 0.1 });
    function vals(g) {
      var mx = 0; for (var i = 0; i <= 100; i++) mx = Math.max(mx, RM.pose(i / 100, g).knee);
      var p = RM.pose(0.25, g), pm = RM.pose(0.5, g);
      return { knee: mx, kneePedal: p.kneePedal, shoulder: p.shoulder, torso: pm.torso };
    }
    var a = vals(g0), b = vals(g1), agg = {};
    Object.keys(s.agg).forEach(function (k) { agg[k] = s.agg[k]; });
    ['knee', 'kneePedal', 'shoulder', 'torso'].forEach(function (k) {
      var st = JSON.parse(JSON.stringify(s.agg[k].stat)), dlt = b[k] - a[k];
      Object.keys(st).forEach(function (r) { if (r !== 'rom') st[r].value += dlt; });
      agg[k] = { stat: st };
    });
    return { s: Object.assign({}, s, { agg: agg }), geo: g1 };
  }
  function renderSim(s) {
    var d = {}; $$('[data-sim-in]').forEach(function (i) { d[i.dataset.simIn] = +i.value; $('[data-simv="' + i.dataset.simIn + '"]').textContent = (i.value > 0 ? '+' : '') + i.value + ' mm'; });
    var x = simulate(s, d), F0 = fitOf(s), F1 = fitOf(x.s);
    $('#simOut').innerHTML = '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Regulacja</th><th>Teraz</th><th>Po zmianie (szacunek)</th></tr></thead><tbody>' + F1.list.map(function (r, i) {
      return '<tr data-simrow="' + r.adj.id + '"><td class="small"><b>' + esc(r.adj.name) + '</b></td><td>' + fitStatus(F0.list[i].status) + '</td><td>' + fitStatus(r.status) + rangeBar(r, true) + '</td></tr>';
    }).join('') + '</tbody></table></div>';
  }

  // ---------- ekran: Profil ----------
  function fld(label, val, unit, hint, cls) {
    return '<div class="fld ' + (cls || '') + '"><label>' + label + '</label><div class="inp"><input value="' + esc(val == null ? '' : val) + '" inputmode="decimal">' + (unit ? '<span class="u">' + unit + '</span>' : '') + '</div>' + (hint ? '<span class="hint">' + hint + '</span>' : '') + '</div>';
  }
  function viewProfile() {
    var P = RM.PROFILE, t = state.profileTab, body = '';
    if (t === 'basic') body = '<div class="form c3">' + fld('Wzrost', P.basic.height, 'cm') + fld('Masa ciała', P.basic.mass, 'kg') + fld('Długość wewnętrzna nogi', P.basic.inseam, 'cm', 'Od podłogi do krocza, boso, z poziomicą/książką między nogami.') +
      fld('Rozmiar buta', P.basic.shoe, 'EU') + fld('Rok urodzenia', P.basic.birth, '') + '<div class="fld"><label>Płeć</label><select class="sel"><option>nie podano</option><option>kobieta</option><option>mężczyzna</option></select><span class="hint">Opcjonalnie.</span></div></div>';
    if (t === 'seg') body = '<p class="small muted">Długości segmentów ciała mierzone na stojąco, między wskazanymi punktami. Służą do kontroli wiarygodności punktów wykrytych na nagraniu – nie zastępują pomiaru.</p><div class="form c3">' +
      fld('Udo', P.segments.thigh, 'cm', 'krętarz większy → nadkłykieć boczny') + fld('Podudzie', P.segments.shank, 'cm', 'nadkłykieć boczny → kostka boczna') + fld('Stopa', P.segments.foot, 'cm', 'pięta → najdłuższy palec') +
      fld('Tułów', P.segments.torso, 'cm', 'krętarz większy → akromion') + fld('Ramię', P.segments.upperArm, 'cm', 'akromion → nadkłykieć boczny k. ramiennej') + fld('Przedramię', P.segments.forearm, 'cm', 'nadkłykieć → wyrostek rylcowaty') +
      fld('Szerokość barków', P.segments.shoulders, 'cm', 'akromion ↔ akromion') + fld('Rozpiętość ramion', P.segments.armSpan, 'cm', 'koniec palców ↔ koniec palców') + '</div>';
    if (t === 'rom') body = '<p class="small muted">Zakresy ruchu z prostych testów wykonywanych samodzielnie lub z fizjoterapeutą. Wpisz wynik i stronę; aplikacja nie ocenia, czy wynik jest „dobry”.</p><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Test</th><th>Co mierzy</th><th class="num">Prawa</th><th class="num">Lewa</th><th>Jedn.</th></tr></thead><tbody>' +
      '<tr><td>Uniesienie prostej nogi (SLR)</td><td class="small muted">elastyczność tylnej taśmy uda</td><td class="num">' + P.rom.slr[0] + '</td><td class="num">' + P.rom.slrL[0] + '</td><td>°</td></tr>' +
      '<tr><td>Test wykroku przy ścianie (knee-to-wall)</td><td class="small muted">zgięcie grzbietowe stopy</td><td class="num">' + P.rom.lunge[0] + '</td><td class="num">' + P.rom.lungeL[0] + '</td><td>cm</td></tr>' +
      '<tr><td>Zgięcie stawu biodrowego</td><td class="small muted">zakres zgięcia w leżeniu</td><td class="num">' + P.rom.hipFlex[0] + '</td><td class="num">' + P.rom.hipFlex[0] + '</td><td>°</td></tr></tbody></table></div><button class="btn sm" style="margin-top:10px">+ Dodaj test</button>';
    if (t === 'pain') body = '<p class="small muted">Zgłaszane dolegliwości pomagają powiązać pomiary z odczuciami. To nie jest diagnoza – w razie bólu skonsultuj się z lekarzem lub fizjoterapeutą.</p><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Obszar</th><th>Strona</th><th>Od kiedy</th><th>Kiedy się pojawia</th><th>Uwagi</th></tr></thead><tbody>' +
      P.complaints.map(function (c) { return '<tr><td>' + esc(c.region) + '</td><td>' + c.side + '</td><td>' + c.since + '</td><td>' + esc(c.when) + '</td><td class="small muted">' + esc(c.note || '—') + '</td></tr>'; }).join('') + '</tbody></table></div><button class="btn sm" style="margin-top:10px">+ Dodaj dolegliwość</button>';
    var tabs = [['basic', 'Podstawowe'], ['seg', 'Segmenty ciała'], ['rom', 'Zakresy ruchu'], ['pain', 'Dolegliwości']];
    return '<div class="page-head"><div><div class="crumbs">Dopasowanie</div><h1>Profil rowerzysty</h1><div class="sub">' + esc(P.name) + ' · powiązany z ' + esc(P.account) + '</div></div><button class="btn pri" data-act="save">Zapisz</button></div>' +
      '<div class="grid g-main"><div class="card"><div class="tabs">' + tabs.map(function (x) { return '<button data-ptab="' + x[0] + '" class="' + (t === x[0] ? 'on' : '') + '">' + x[1] + '</button>'; }).join('') + '</div>' + body + '</div>' +
      '<div class="stack"><div class="callout info"><div class="ic">i</div><div class="small"><h3>Dane wrażliwe</h3>Wzrost, masa, zakresy ruchu i dolegliwości to dane o zdrowiu. Widzisz je tylko Ty. Nie są publikowane ani udostępniane innym użytkownikom ridemore.bike.</div></div>' +
      '<div class="card"><h3>Jak mierzyć</h3><ol class="small muted" style="padding-left:18px;margin:8px 0 0"><li>Mierz boso, rano, z pomocą drugiej osoby.</li><li>Każdy pomiar wykonaj dwukrotnie i wpisz średnią.</li><li>Punkty kostne (krętarz, nadkłykieć, kostka) znajdź palcami – te same punkty śledzi analiza wideo.</li></ol></div>' +
      demoNote('Wartości w formularzu są przykładowe. W prototypie nic nie jest zapisywane.') + '</div></div>';
  }

  // ---------- ekran: Rowery ----------
  function bikeFig(b) {
    var c = b.contact;
    return '<div class="bike-fig"><svg viewBox="-900 -900 1900 1220" aria-label="Schemat punktów kontaktu">' + bikeSvg().replace(/#4A554E|#59645D|#3A443E/g, '#B9C2BB').replace(/#6E7A72/g, '#5E6A62').replace(/#8A948C/g, '#15201A') +
      '<g font-family="IBM Plex Mono" font-size="44" font-weight="600">' +
      '<line x1="0" y1="0" x2="-213" y2="-719" stroke="#2C6B4F" stroke-width="6" stroke-dasharray="14 10"/><circle cx="-106" cy="-360" r="34" fill="#2C6B4F"/><text x="-106" y="-345" fill="#fff" text-anchor="middle">A</text>' +
      '<line x1="0" y1="-760" x2="-200" y2="-760" stroke="#2B57C8" stroke-width="6"/><line x1="0" y1="-780" x2="0" y2="40" stroke="#2B57C8" stroke-width="3" stroke-dasharray="8 8"/><circle cx="-100" cy="-800" r="34" fill="#2B57C8"/><text x="-100" y="-785" fill="#fff" text-anchor="middle">B</text>' +
      '<line x1="-200" y1="-850" x2="530" y2="-850" stroke="#D14E1E" stroke-width="6"/><circle cx="165" cy="-850" r="34" fill="#D14E1E"/><text x="165" y="-835" fill="#fff" text-anchor="middle">C</text>' +
      '<line x1="640" y1="-705" x2="640" y2="-635" stroke="#6D4AA6" stroke-width="6"/><line x1="-70" y1="-705" x2="680" y2="-705" stroke="#6D4AA6" stroke-width="3" stroke-dasharray="8 8"/><circle cx="720" cy="-670" r="34" fill="#6D4AA6"/><text x="720" y="-655" fill="#fff" text-anchor="middle">D</text></g></svg>' +
      '<div class="grid g2 small" style="padding:6px 8px;gap:4px 16px"><span><b style="color:#2C6B4F">A</b> wysokość siodła: ' + num(c.saddleH[0], 0) + ' mm</span><span><b style="color:#2B57C8">B</b> cofnięcie siodła: ' + num(c.setback[0], 0) + ' mm</span><span><b style="color:#D14E1E">C</b> siodło → kierownica: ' + num(c.reach[0], 0) + ' mm</span><span><b style="color:#6D4AA6">D</b> drop: ' + num(c.drop[0], 0) + ' mm</span></div></div>';
  }
  function kvTable(obj, labels) {
    return '<div class="tbl-wrap"><table class="tbl"><tbody>' + Object.keys(obj).map(function (k) {
      var v = obj[k];
      return '<tr><td>' + esc((labels && labels[k]) || RM.FIELD_LABELS[k] || k) + '</td><td class="num">' + (v[0] == null ? '<span class="q warn">brak</span>' : esc(typeof v[0] === 'number' ? num(v[0], v[0] % 1 ? 1 : 0) : v[0])) + '</td><td class="small muted">' + esc(v[1]) + '</td></tr>';
    }).join('') + '</tbody></table></div>';
  }
  function viewBikes(id) {
    var b = bike(id) || RM.BIKES[0], t = state.bikeTab;
    var list = RM.BIKES.map(function (x) {
      return '<a href="#/rowery/' + x.id + '" class="card" style="display:block;text-decoration:none;color:inherit;margin:0 0 10px;' + (x.id === b.id ? 'border-color:var(--accent);box-shadow:inset 0 0 0 1px var(--accent)' : '') + '"><div class="row"><b>' + esc(x.name) + '</b><span class="spacer"></span>' + (x.active ? '<span class="pill">aktywny</span>' : '') + '</div><div class="small muted">' + x.type + ' · ' + x.size + ' · ' + x.year + '</div>' + (x.incomplete ? '<div class="xs" style="color:var(--warn-ink);margin-top:4px">Brakuje części ustawień – analiza będzie możliwa, ale bez powiązania ze zmianami.</div>' : '') + '</a>';
    }).join('') + '<button class="btn sm" data-act="soon">+ Dodaj rower</button>';
    var tabs = [['contact', 'Punkty kontaktu'], ['geo', 'Geometria ramy'], ['comp', 'Komponenty']];
    var body = t === 'geo' ? kvTable(b.geometry) : t === 'comp' ? kvTable(b.components) : kvTable(b.contact, RM.CONTACT_LABELS);
    var params = Object.keys(b.contact).concat(Object.keys(b.components)).map(function (k) { return '<option>' + esc(RM.CONTACT_LABELS[k] || RM.FIELD_LABELS[k]) + '</option>'; }).join('');
    return '<div class="page-head"><div><div class="crumbs">Dopasowanie</div><h1>Moje rowery</h1><div class="sub">Typ roweru, geometria, komponenty i ustawienia punktów kontaktu. Każda zmiana ustawień trafia do historii zmian.</div></div></div>' +
      '<div class="grid" style="grid-template-columns:minmax(0,260px) minmax(0,1fr)" id="bikeGrid"><div>' + list + '</div><div class="stack">' +
      '<div class="card"><div class="card-h"><div><h2>' + esc(b.name) + '</h2><div class="small muted">' + b.type + ' · rozmiar ' + b.size + ' · rocznik ' + b.year + '</div></div><button class="btn sm" data-act="soon">Edytuj</button></div>' +
      '<div class="grid g2"><div><div class="tabs">' + tabs.map(function (x) { return '<button data-btab="' + x[0] + '" class="' + (t === x[0] ? 'on' : '') + '">' + x[1] + '</button>'; }).join('') + '</div>' + body + '</div>' + bikeFig(b) + '</div></div>' +
      '<div class="card"><div class="card-h"><h2>Zapisz zmianę ustawienia</h2><span class="xs muted">trafi do historii zmian i zostanie powiązana z kolejną sesją</span></div>' +
      '<form class="form c3" id="chgForm"><div class="fld"><label>Parametr</label><select class="sel" name="param">' + params + '</select></div>' +
      '<div class="fld"><label>Wartość przed</label><input class="txt" name="before" required></div><div class="fld"><label>Wartość po</label><input class="txt" name="after" required></div>' +
      '<div class="fld"><label>Jednostka</label><select class="sel" name="unit"><option>mm</option><option>°</option><option>—</option></select></div><div class="fld"><label>Data zmiany</label><input class="txt" type="date" name="date" value="2026-10-27"></div>' +
      '<div class="fld"><label>Kto zmienił</label><select class="sel"><option>ja</option><option>bike fitter</option><option>serwis</option></select></div>' +
      '<div class="fld full"><label>Powód zmiany</label><input class="txt" name="reason" placeholder="np. dyskomfort przodu kolana po 60 min" required></div>' +
      '<div class="row" style="grid-column:1/-1"><button class="btn pri" type="submit">Zapisz zmianę</button><span class="xs muted">Wynik zmiany zobaczysz po kolejnej sesji w „Historii zmian” i „Porównaniu sesji”.</span></div></form></div></div></div>';
  }

  // ---------- ekran: Nowa sesja (kreator) ----------
  var CHECKS = [
    'Kamera ustawiona dokładnie z boku, prostopadle do roweru (patrz schemat).',
    'Obiektyw na wysokości siodła, kamera wypoziomowana (poziomica w telefonie).',
    'Odległość 2,5–4 m; w kadrze cały rower i cała sylwetka, z zapasem.',
    'Kamera na statywie – nie trzymaj jej w ręce.',
    'Światło z przodu lub z boku; nie nagrywaj pod okno ani pod światło.',
    'Obcisły strój w kontrastowym kolorze; widoczne kolano, kostka, bark i łokieć.',
    'Znacznik skali (np. kartka z odcinkiem 200 mm) przyklejony w płaszczyźnie roweru, widoczny w kadrze.',
    'Rozgrzewka 5–10 min, następnie 45–60 s nagrania przy stałej kadencji.'
  ];
  function camDiagram() {
    return '<svg viewBox="0 0 560 230" style="width:100%;height:auto;background:var(--tint);border-radius:10px" font-family="IBM Plex Mono" font-size="12">' +
      '<text x="14" y="22" fill="#5E6A62" font-weight="600">WIDOK Z GÓRY</text>' +
      '<rect x="60" y="70" width="220" height="16" rx="8" fill="#15201A"/><text x="170" y="62" text-anchor="middle" fill="#15201A">rower na trenażerze</text>' +
      '<line x1="170" y1="92" x2="170" y2="180" stroke="#2C6B4F" stroke-width="2" stroke-dasharray="6 5"/><text x="178" y="140" fill="#2C6B4F">90°, 2,5–4 m</text>' +
      '<rect x="155" y="182" width="30" height="20" rx="4" fill="#2C6B4F"/><text x="170" y="220" text-anchor="middle" fill="#15201A">kamera</text>' +
      '<text x="314" y="22" fill="#5E6A62" font-weight="600">WIDOK Z BOKU</text>' +
      '<line x1="314" y1="200" x2="546" y2="200" stroke="#8A948C"/><circle cx="350" cy="170" r="30" fill="none" stroke="#15201A" stroke-width="3"/><circle cx="450" cy="170" r="30" fill="none" stroke="#15201A" stroke-width="3"/>' +
      '<path d="M350 170 L395 170 L385 110 M395 170 L445 115 L385 110 M445 115 L450 170" stroke="#15201A" stroke-width="3" fill="none"/>' +
      '<line x1="314" y1="108" x2="546" y2="108" stroke="#2C6B4F" stroke-dasharray="6 5"/><text x="546" y="100" text-anchor="end" fill="#2C6B4F">obiektyw ≈ wysokość siodła</text>' +
      '<rect x="398" y="128" width="20" height="10" fill="#FFC93F" stroke="#15201A"/><text x="422" y="146" fill="#15201A">znacznik 200 mm</text></svg>';
  }
  function viewWizard() {
    var w = state.wizard || (state.wizard = { step: 1, bike: 'b1', checks: {}, file: null, side: 'prawa', keep: 'delete', consent: false, grip: 'klamkomanetki', marker: 200 });
    var steps = ['Rower i ustawienia', 'Ustawienie kamery', 'Nagranie', 'Podsumowanie'];
    var body = '';
    if (w.step === 1) {
      var b = bike(w.bike);
      body = '<h2>Na którym rowerze nagrywasz?</h2><div class="grid g3" style="margin:12px 0">' + RM.BIKES.map(function (x) {
        return '<label class="card" style="cursor:pointer;margin:0;' + (w.bike === x.id ? 'border-color:var(--accent);box-shadow:inset 0 0 0 1px var(--accent)' : '') + '"><input type="radio" name="wb" value="' + x.id + '" ' + (w.bike === x.id ? 'checked' : '') + ' style="accent-color:var(--accent)"> <b>' + esc(x.name) + '</b><div class="small muted">' + x.type + ' · ' + x.size + '</div></label>';
      }).join('') + '</div>' +
        '<div class="grid g2"><div><h3>Aktualne ustawienia (' + esc(b.name) + ')</h3>' + kvTable(b.contact, RM.CONTACT_LABELS) + '</div><div class="stack">' +
        '<div class="callout info"><div class="ic">?</div><div class="small"><b>Czy coś zmieniłeś od ostatniej sesji?</b><br>Jeśli tak, zapisz zmianę przed nagraniem – dzięki temu porównanie sesji pokaże, co się zmieniło.<div style="margin-top:8px"><a class="btn sm" href="#/rowery/' + b.id + '">Zapisz zmianę ustawienia</a></div></div></div>' +
        '<div class="fld"><label>Chwyt podczas nagrania</label><select class="sel" data-w="grip"><option' + (w.grip === 'klamkomanetki' ? ' selected' : '') + '>klamkomanetki</option><option>chwyt dolny</option><option>chwyt górny</option><option>chwyt MTB</option></select><span class="hint">Kąt łokcia i pochylenie tułowia zależą od chwytu – porównuj sesje z tym samym chwytem.</span></div></div></div>';
    }
    if (w.step === 2) {
      var done = CHECKS.filter(function (_, i) { return w.checks[i]; }).length;
      body = '<div class="grid g2"><div><h2>Jak ustawić kamerę</h2><p class="small muted">Dobre nagranie jest ważniejsze niż model analizy. Każdy punkt poniżej ogranicza konkretny błąd pomiaru.</p>' + camDiagram() +
        '<div class="callout warn" style="margin-top:12px"><div class="ic">!</div><div class="small"><b>Ograniczenia nagrania 2D.</b> Kamera z boku widzi rzut sylwetki na płaszczyznę. Znacznik skali pozwala przeliczyć piksele na milimetry tylko w płaszczyźnie znacznika – nie usuwa błędów perspektywy. Kąty mierzone są dla strony zwróconej do kamery.</div></div></div>' +
        '<div><h3>Lista kontrolna przed nagraniem <span class="pill">' + done + ' / ' + CHECKS.length + '</span></h3>' + CHECKS.map(function (c, i) { return '<label class="chk"><input type="checkbox" data-chk="' + i + '" ' + (w.checks[i] ? 'checked' : '') + '><span class="small">' + c + '</span></label>'; }).join('') +
        '<p class="xs muted">Opcjonalnie: naklejki-markery na krętarzu, kolanie, kostce, barku i łokciu poprawiają dokładność śledzenia.</p></div></div>';
    }
    if (w.step === 3) {
      body = '<div class="grid g2"><div><h2>Prześlij nagranie</h2>' +
        '<div class="drop ' + (w.file ? (w.file.err ? 'err' : 'ok') : '') + '" style="margin-top:10px"><p><b>' + (w.file ? esc(w.file.name) : 'Wybierz plik MP4 lub MOV') + '</b></p><p class="small muted">' + (w.file ? (w.file.err ? esc(w.file.err) : num(w.file.size / 1048576, 1) + ' MB · format poprawny') : 'Limit prototypu: 500 MB, 15–120 s. Docelowe limity zależą od hostingu (do potwierdzenia w Etapie 2).') + '</p>' +
        '<input type="file" id="vfile" accept=".mp4,.mov,video/mp4,video/quicktime"></div>' +
        '<div class="form" style="margin-top:14px"><div class="fld"><label>Strona zwrócona do kamery</label><div class="seg" data-seg="side">' + ['prawa', 'lewa'].map(function (s) { return '<button type="button" class="' + (w.side === s ? 'on' : '') + '" data-v="' + s + '">' + s + '</button>'; }).join('') + '</div></div>' +
        '<div class="fld"><label>Długość znacznika skali</label><div class="inp"><input data-w="marker" value="' + w.marker + '"><span class="u">mm</span></div></div></div></div>' +
        '<div class="stack"><div class="card" style="background:var(--tint)"><h3>Co stanie się z nagraniem</h3><p class="small muted" style="margin-top:4px">Nagranie zawiera Twój wizerunek. Jest przechowywane poza publicznym katalogiem serwera i dostępne tylko dla Ciebie.</p>' +
        '<label class="chk"><input type="radio" name="keep" value="delete" ' + (w.keep === 'delete' ? 'checked' : '') + '><span class="small"><b>Usuń nagranie po analizie</b> (domyślnie). Zachowamy tylko wyniki pomiarów.</span></label>' +
        '<label class="chk"><input type="radio" name="keep" value="keep" ' + (w.keep === 'keep' ? 'checked' : '') + '><span class="small"><b>Zachowaj nagranie</b> – do ręcznej korekty punktów i ponownej analizy nowszą wersją algorytmu. Możesz je usunąć w każdej chwili.</span></label>' +
        '<p class="xs muted">Jeśli analiza się nie powiedzie, plik jest przechowywany do 7 dni, aby można było ponowić zadanie, a potem usuwany automatycznie (propozycja polityki – do akceptacji).</p></div>' +
        '<label class="chk"><input type="checkbox" data-w="consent" ' + (w.consent ? 'checked' : '') + '><span class="small">Zgadzam się na przetwarzanie nagrania w celu analizy pozycji.</span></label></div></div>';
    }
    if (w.step === 4) {
      var bb = bike(w.bike);
      body = '<h2>Sprawdź i wyślij</h2><table class="tbl" style="max-width:640px;margin-top:10px"><tbody>' +
        '<tr><td>Rower</td><td>' + esc(bb.name) + '</td></tr><tr><td>Wysokość siodła</td><td>' + num(bb.contact.saddleH[0], 0) + ' mm</td></tr><tr><td>Chwyt</td><td>' + esc(w.grip) + '</td></tr>' +
        '<tr><td>Lista kontrolna</td><td>' + Object.keys(w.checks).filter(function (k) { return w.checks[k]; }).length + ' / ' + CHECKS.length + '</td></tr>' +
        '<tr><td>Plik</td><td>' + (w.file ? esc(w.file.name) : '—') + '</td></tr><tr><td>Strona do kamery</td><td>' + w.side + '</td></tr><tr><td>Znacznik skali</td><td>' + esc(w.marker) + ' mm</td></tr>' +
        '<tr><td>Nagranie po analizie</td><td>' + (w.keep === 'delete' ? 'usunąć' : 'zachować') + '</td></tr></tbody></table>' +
        '<div style="margin-top:14px">' + demoNote('W prototypie plik nie jest nigdzie wysyłany. Przycisk otworzy symulację ekranu „Analiza w toku”.') + '</div>';
    }
    var can = w.step === 1 || (w.step === 2 && CHECKS.every(function (_, i) { return w.checks[i]; })) || (w.step === 3 && w.file && !w.file.err && w.consent) || w.step === 4;
    var why = w.step === 2 ? 'Zaznacz wszystkie punkty listy kontrolnej.' : w.step === 3 ? 'Wybierz poprawny plik i zaznacz zgodę.' : '';
    return '<div class="page-head"><div><div class="crumbs">Analiza</div><h1>Nowa sesja</h1><div class="sub">Nagranie jazdy na trenażerze, kamera z boku.</div></div></div>' +
      '<ol class="steps" style="padding:0">' + steps.map(function (s, i) { return '<li class="' + (i + 1 < w.step ? 'done' : i + 1 === w.step ? 'on' : '') + '">' + s + '</li>'; }).join('') + '</ol>' +
      '<div class="card">' + body + '<hr class="sep"><div class="row">' + (w.step > 1 ? '<button class="btn" data-act="wback">← Wstecz</button>' : '') + '<span class="spacer"></span>' +
      (can ? '' : '<span class="small muted">' + why + '</span>') +
      (w.step < 4 ? '<button class="btn pri" data-act="wnext" ' + (can ? '' : 'disabled') + '>Dalej →</button>' : '<button class="btn pri" data-act="wsend">Wyślij do analizy</button>') + '</div></div>';
  }

  // ---------- ekran: Analiza w toku ----------
  var STAGES = ['Przyjęcie pliku i walidacja (format, długość, rozdzielczość, fps)', 'Oczekiwanie w kolejce', 'Wykrywanie punktów anatomicznych', 'Śledzenie punktów i uzupełnianie krótkich braków', 'Kalibracja skali i poziomu', 'Wykrywanie cykli pedałowania', 'Obliczanie kątów i parametrów', 'Kontrola jakości', 'Zapis wyników' ];
  function viewJob(id) {
    var s = session(id);
    if (!s) { s = state.jobs[id]; }
    if (!s) return viewNotFound();
    if (s.agg) { location.hash = '#/wyniki/' + s.id; return ''; }
    var j = state.jobs[id] || (state.jobs[id] = { id: id, status: s.status, stage: s.status === 'failed' ? 2 : (s.stage || 0), progress: s.progress || 0, scenario: s.status === 'failed' ? 'fail' : 'ok', attempt: 1, error: s.error, bike: s.bike, date: s.date });
    var list = STAGES.map(function (st, i) {
      var cls = i < j.stage ? 'ok' : i === j.stage ? (j.status === 'failed' ? 'err' : j.status === 'running' || j.status === 'pending' ? 'run' : 'ok') : '';
      if (j.status === 'warn' && i === 7) cls = 'wr';
      if ((j.status === 'done' || j.status === 'warn') && i <= 8 && cls === '') cls = 'ok';
      var mark = cls === 'ok' ? '✓' : cls === 'err' ? '×' : cls === 'wr' ? '!' : i + 1;
      return '<li><span class="dot ' + cls + '">' + mark + '</span><span class="small' + (cls ? '' : ' muted') + '">' + st + '</span></li>';
    }).join('');
    var err = j.status === 'failed' ? '<div class="callout bad" style="margin-top:14px"><div class="ic">×</div><div class="small"><h3>Analiza nieudana · ' + esc(j.error.code) + '</h3>' + esc(j.error.msg) + '<br><b>Co zrobić:</b> nagraj ponownie przy świetle z przodu lub z boku; upewnij się, że cała sylwetka jest w kadrze. Próba ' + esc(j.error.attempt) + '.' +
      '<div class="row" style="margin-top:10px"><button class="btn sm" data-act="retry">Ponów analizę tego samego pliku</button><a class="btn sm pri" href="#/nowa">Nagraj ponownie</a></div></div></div>' : '';
    var fin = j.status === 'done' || j.status === 'warn' ? '<div class="callout ' + (j.status === 'warn' ? 'warn' : 'info') + '" style="margin-top:14px"><div class="ic">' + (j.status === 'warn' ? '!' : '✓') + '</div><div class="small"><h3>' + ST_LABEL[j.status] + '</h3>' + (j.status === 'warn' ? 'Część pomiarów ma ograniczoną wiarygodność (np. punkt stopy szacowany na bucie). Szczegóły zobaczysz w sekcji jakości.' : 'Wszystkie pomiary przeszły kontrolę jakości.') + '<div class="row" style="margin-top:10px"><a class="btn sm pri" href="#/wyniki/S-0007">Otwórz wyniki (przykład S-0007)</a></div></div></div>' : '';
    return '<div class="page-head"><div><div class="crumbs">Analiza</div><h1>Analiza w toku</h1><div class="sub">' + esc(j.id) + ' · ' + dateTxt(j.date) + ' · ' + esc(bike(j.bike).name) + '</div></div><a class="btn" href="#/pulpit">← Wróć do pulpitu</a></div>' +
      '<div class="grid g-main"><div class="card"><div class="card-h"><h2>Stan zadania</h2>' + stBadge(j.status) + '</div>' +
      '<div class="bar"><i id="jobbar" style="width:' + (j.status === 'failed' ? j.progress : j.status === 'done' || j.status === 'warn' ? 100 : j.progress) + '%;' + (j.status === 'failed' ? 'background:var(--danger)' : '') + '"></i></div>' +
      '<p class="small muted" style="margin:6px 0 10px">' + (j.status === 'running' || j.status === 'pending' ? 'Analiza działa na serwerze. Możesz zamknąć tę stronę – wynik pojawi się na pulpicie.' : '') + '</p>' +
      '<ul class="joblist">' + list + '</ul>' + err + fin + '</div>' +
      '<div class="stack"><div class="card"><h3>Szczegóły zadania</h3><table class="tbl small" style="margin-top:6px"><tbody>' +
      '<tr><td>Identyfikator</td><td class="mono">job-' + esc(j.id.toLowerCase()) + '</td></tr><tr><td>Próba</td><td>' + j.attempt + ' z 3</td></tr>' +
      '<tr><td>Model / algorytm</td><td class="muted">— (prototyp, brak modelu)</td></tr><tr><td>Limit czasu zadania</td><td>do ustalenia (Etap 2)</td></tr>' +
      '<tr><td>Nagranie po analizie</td><td>usunąć</td></tr></tbody></table></div>' +
      '<div class="card"><h3>Symulacja stanów (tylko prototyp)</h3><p class="xs muted">Pozwala obejrzeć każdy stan zadania.</p><div class="row" style="margin-top:8px">' +
      '<button class="btn sm" data-sim="ok">Przebieg prawidłowy</button><button class="btn sm" data-sim="warn">Z ostrzeżeniami</button><button class="btn sm danger" data-sim="fail">Błąd</button></div></div>' +
      demoNote('Postęp jest symulowany w przeglądarce; nie odpowiada żadnej rzeczywistej analizie.') + '</div></div>';
  }
  function runJob(id) {
    var j = state.jobs[id]; if (!j || (j.status !== 'running' && j.status !== 'pending')) return;
    var h = setInterval(function () {
      if (j.status === 'pending') { j.status = 'running'; render(); return; }
      j.progress = Math.min(100, j.progress + 3);
      var stage = Math.min(8, Math.floor(j.progress / 11.2));
      if (j.scenario === 'fail' && stage >= 3) { j.status = 'failed'; j.stage = 3; j.error = { code: 'E_TRACKING_LOST', msg: 'Utracono śledzenie kolana i kostki w ponad 40% klatek – noga przesłonięta lub poza kadrem.', attempt: j.attempt + ' z 3' }; clearInterval(h); render(); return; }
      if (j.progress >= 100) { j.status = j.scenario === 'warn' ? 'warn' : 'done'; j.stage = 9; clearInterval(h); render(); return; }
      if (stage !== j.stage) { j.stage = stage; render(); } else { var b = $('#jobbar'); if (b) b.style.width = j.progress + '%'; }
    }, 450);
    cleanup.push(function () { clearInterval(h); });
  }

  // ---------- ekran: Wyniki ----------
  function repCell(s, m) {
    var lvl = s.quality[m.id];
    if (m.na || lvl === 'na') return { rep: '<span class="muted small">niedostępne – wymaga nagrania z przodu</span>', range: '—', sd: '—' };
    var st = s.agg[m.id].stat, r0 = m.reps[0], u = m.unit;
    var val = num(st[r0[0]].value) + ' ' + u;
    if (lvl === 'bad') return { rep: '<span class="muted small">pomiar niewiarygodny – wartość ukryta</span>', range: '—', sd: '—' };
    return {
      rep: (lvl === 'warn' ? '≈ ' : '') + '<b>' + val + '</b><div class="xs muted">' + esc(r0[1]) + '</div>',
      range: num(st.min.value) + ' – ' + num(st.max.value) + ' ' + u + '<div class="xs muted">ROM ' + num(st.rom.value) + ' ' + u + '</div>',
      sd: '±' + num(st[r0[0]].sd) + ' ' + u
    };
  }
  function resultsHead(s, tab) {
    var b = bike(s.bike);
    var opts = analysed().map(function (x) { return '<option value="' + x.id + '"' + (x.id === s.id ? ' selected' : '') + '>' + dateTxt(x.date) + ' · ' + esc(bike(x.bike).name) + '</option>'; }).join('');
    return '<div class="page-head"><div><div class="crumbs"><a href="#/pulpit">Moje analizy</a> / ' + s.id + '</div><h1>Analiza pozycji · ' + dateTxt(s.date) + '</h1><div class="sub">' + esc(b.name) + ' · ' + esc(s.settingsNote) + '</div></div>' +
      '<div class="row"><select class="sel" id="sessSel" style="width:auto">' + opts + '</select><a class="btn" href="#/komfort/' + s.id + '">Oceń komfort</a></div></div>' +
      '<div class="tabs"><button data-rtab="summary" class="' + (tab === 'summary' ? 'on' : '') + '">Podsumowanie</button><button data-rtab="details" class="' + (tab === 'details' ? 'on' : '') + '">Szczegóły (dla bike fittera)</button></div>';
  }
  function viewResults(id) {
    var s = session(id);
    if (!s) return viewNotFound();
    if (!s.agg) { location.hash = '#/analiza/' + s.id; return ''; }
    if (state.resTab !== 'details') return viewSummary(s);
    var b = bike(s.bike), Qs = s.quality;
    var opts = analysed().map(function (x) { return '<option value="' + x.id + '"' + (x.id === s.id ? ' selected' : '') + '>' + x.id + ' · ' + dateTxt(x.date) + '</option>'; }).join('');
    var rows = RM.METRICS.map(function (m) {
      var c = repCell(s, m);
      return '<tr class="click' + (state.metric === m.id ? ' sel' : '') + (m.na ? ' dim' : '') + '" data-metric="' + m.id + '"><td><b>' + m.name + '</b> <button class="btn ghost sm" data-def="' + m.id + '" title="Definicja" style="padding:0 4px">ⓘ</button></td><td>' + c.rep + '</td><td class="num">' + c.range + '</td><td class="num">' + c.sd + '</td><td>' + qBadge(m.na ? 'na' : Qs[m.id], true) + '</td></tr>';
    }).join('') + '<tr class="dim"><td colspan="5" class="small">Strona ' + (s.side === 'prawa' ? 'lewa' : 'prawa') + ': niemierzona – niewidoczna z kamery. Aby ją zmierzyć, nagraj drugą sesję z kamerą po przeciwnej stronie.</td></tr>';
    var rejected = s.cycles.filter(function (c) { return !c.valid; });
    var cad = s.cycles.filter(function (c) { return c.valid; }).map(function (c) { return c.cadence; }).sort(function (a, b) { return a - b; });
    var m = metric(state.metric);
    return resultsHead(s, 'details') +
      '<div class="row" style="margin-bottom:12px"><a class="btn sm" href="#/porownanie">Porównanie sesji (pomiary)</a><button class="btn sm" disabled title="Etap 6">Eksport CSV/JSON</button><span class="small muted">Widok dla bike fittera: kąty, definicje, jakość śledzenia i cykle.</span></div>' +
      demoNote('Przebiegi i wartości wygenerowano z modelu geometrycznego w przeglądarce. To <b>nie jest</b> wynik analizy wideo ani modelu AI.') +

      // 1. jakość
      '<section class="card layer quality" style="margin-top:16px"><div class="layer-tag">1 · Jakość i ograniczenia pomiaru</div>' +
      '<div class="grid g4"><div class="kpi"><span class="l">Ocena ogólna</span><span>' + qBadge(Qs.overall) + '</span></div>' +
      '<div class="kpi"><span class="v">' + Qs.framesOk + '<small>%</small></span><span class="l">klatek z wiarygodnymi punktami</span></div>' +
      '<div class="kpi"><span class="v">' + s.valid + '<small> / ' + s.cycles.length + '</small></span><span class="l">cykli zaakceptowanych</span></div>' +
      '<div class="kpi"><span class="v">' + num(s.calibration.scale, 2) + '<small> px/mm</small></span><span class="l">skala ze znacznika · kamera przechylona ' + num(s.calibration.tilt) + '°</span></div></div>' +
      '<div style="margin-top:14px"><div class="row xs muted" style="justify-content:space-between"><span>Jakość śledzenia w czasie nagrania (' + s.fragment + ')</span><span><span class="q good">OK</span> <span class="q warn">ograniczona</span> <span class="q bad">odrzucona</span></span></div>' +
      '<div class="qstrip" style="margin-top:6px">' + s.qstrip.map(function (q, i) { return '<i class="' + q + '" title="sekunda ' + (i + 1) + '"></i>'; }).join('') + '</div></div>' +
      (s.warnings.length ? '<ul class="small" style="margin:12px 0 0;padding-left:18px">' + s.warnings.map(function (w) { return '<li>' + esc(w) + '</li>'; }).join('') + '</ul>' : '') +
      '<p class="xs muted" style="margin-top:10px">Model estymacji pozy: — · wersja algorytmu: — · format wyników: — <i>(prototyp – w aplikacji zapisywane przy każdej analizie)</i>. Nagranie: ' + s.res + ', ' + s.fps + ' fps, ' + (s.keepVideo ? 'zachowane na życzenie użytkownika' : 'usunięte po analizie') + '.</p></section>' +

      // 2. pomiar
      '<section class="card layer measure" style="margin-top:16px"><div class="layer-tag">2 · Pomiar wyliczony z nagrania</div>' +
      '<div class="grid" style="grid-template-columns:minmax(0,1fr) minmax(0,1.45fr)" id="resGrid"><div id="viewer"></div>' +
      '<div><p class="small" style="margin-bottom:6px">Strona <b>' + s.side + '</b> (zwrócona do kamery) · wartości z ' + s.valid + ' zaakceptowanych cykli</p><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Pomiar</th><th>Wartość repr.<br><span style="text-transform:none;font-weight:500">mediana z cykli</span></th><th class="num">Zakres w cyklu</th><th class="num">Zmienność<br><span style="text-transform:none;font-weight:500">SD cykli</span></th><th>Jakość</th></tr></thead><tbody>' + rows + '</tbody></table></div>' +
      '<p class="xs muted" style="margin-top:8px">„≈” – pomiar o ograniczonej wiarygodności. Kliknij wiersz, aby zobaczyć przebieg w cyklu i kąt na schemacie; ⓘ – definicja geometryczna.</p></div></div>' +
      '<hr class="sep"><div class="card-h"><div><h3>' + m.name + ' w cyklu pedałowania</h3><div class="xs muted">' + (m.na ? 'Brak danych.' : 'Średnia z ' + s.valid + ' cykli ± odchylenie standardowe. 0% = korba w górnym martwym punkcie (GMP), DMP = dolny martwy punkt.') + '</div></div>' +
      '<label class="row small" style="gap:6px"><input type="checkbox" id="showCycles" ' + (state.showCycles ? 'checked' : '') + '> pokaż pojedyncze cykle</label></div>' +
      (m.na ? '<div class="empty">' + esc(m.def) + '</div>' : (Qs[m.id] === 'warn' ? '<div class="callout warn" style="margin-bottom:10px"><div class="ic">!</div><div class="small">Ograniczona wiarygodność: ' + esc(s.warnings[1] || 'niska pewność punktów') + '</div></div>' : '') +
        '<div class="chart" id="cycleChart"></div><div class="legend"><span><i style="border-color:#2C6B4F"></i>średnia</span><span><i class="band" style="background:#2C6B4F;opacity:.18"></i>± 1 SD</span>' + (state.showCycles ? '<span><i style="border-color:#8A948C"></i>pojedyncze cykle</span>' : '') + '</div>') + '</section>' +

      // 3. cykle
      '<section class="card" style="margin-top:16px"><div class="card-h"><h2>Wykryte cykle</h2><span class="small muted">kadencja (mediana): ' + cad[cad.length >> 1] + ' obr./min</span></div>' +
      '<div class="cycles">' + s.cycles.map(function (c) { return '<i class="' + (c.valid ? '' : 'rej') + '" title="Cykl ' + c.n + (c.valid ? ' · ' + c.cadence + ' obr./min' : ' · odrzucony: ' + c.reason) + '"></i>'; }).join('') + '</div>' +
      '<table class="tbl small" style="margin-top:10px;max-width:640px"><thead><tr><th>Cykl</th><th>Powód odrzucenia</th></tr></thead><tbody>' + rejected.map(function (c) { return '<tr><td class="mono">#' + c.n + '</td><td>' + esc(c.reason) + '</td></tr>'; }).join('') + '</tbody></table>' +
      '<p class="xs muted">Cykl wyznaczany z trajektorii osi pedału: od jednego najwyższego położenia do kolejnego. Każdy cykl jest normalizowany do 0–100%.</p></section>' +

      // 4–5. interpretacja i zalecenia
      '<div class="grid g2" style="margin-top:16px"><section class="card layer interp"><div class="layer-tag">3 · Interpretacja (opis, nie ocena)</div>' + interpretation(s) + '</section>' +
      '<section class="card layer reco"><div class="layer-tag">4 · Zalecenia – reguły modelu dopasowania ' + RM_FIT.VERSION + '</div><table class="tbl small"><thead><tr><th>Regulacja</th><th>Pomiar</th><th>Zakres</th><th>Źródło</th></tr></thead><tbody>' +
      RM_FIT.ADJUSTMENTS.map(function (a) { var r = RM_FIT.rangeFor(a, b.type); return '<tr><td><b>' + esc(a.name) + '</b></td><td>' + esc(a.measure) + '</td><td class="num">' + rangeTxt(r.green, a.unit) + '</td><td class="xs muted">' + esc(a.source) + '</td></tr>'; }).join('') +
      '</tbody></table><p class="xs muted">Liczba mm w zaleceniu to szacunek z modelu geometrycznego, zaokrąglony do 5 mm i ograniczony do 10 mm na jeden krok. Zakresy robocze – do weryfikacji w Etapie 4.</p></section></div>' +

      '<div class="callout info" style="margin-top:16px"><div class="ic">i</div><div class="small"><h3>Ograniczenia analizy 2D</h3><ul style="margin:4px 0 0;padding-left:18px"><li>Kąty to rzut na płaszczyznę obrazu; skręcenie kończyny lub kamery zmienia wynik.</li><li>Kalibracja znacznikiem daje skalę tylko w płaszczyźnie znacznika i nie usuwa perspektywy.</li><li>Punkty anatomiczne są estymowane z obrazu (często przez ubranie i buty) – to nie jest pomiar laboratoryjny.</li><li>Porównuj sesje nagrane w tych samych warunkach: ta sama kamera, odległość, chwyt i kadencja.</li></ul></div></div>';
  }
  function interpretation(s) {
    var k = s.agg.knee, iMax = k.mean.indexOf(Math.max.apply(null, k.mean)), h = s.agg.hip, iMin = h.mean.indexOf(Math.min.apply(null, h.mean));
    return '<ul class="small" style="margin:0;padding-left:18px">' +
      '<li>Największy wyprost kolana (' + num(k.stat.max.value) + '°) występuje przy ok. ' + iMax + '% cyklu, tuż za dolnym martwym punktem.</li>' +
      '<li>Biodro jest najbardziej zamknięte (' + num(h.stat.min.value) + '°) przy ok. ' + iMin + '% cyklu, w pobliżu górnego martwego punktu.</li>' +
      '<li>Zmienność między cyklami kąta kolana wynosi ±' + num(k.stat.max.sd) + '° – różnice mniejsze niż ta wartość trudno odróżnić od zmienności pomiaru.</li>' +
      '<li>Kąt stawu skokowego i pozycja stopy mają ' + (s.quality.ankle === 'good' ? 'dobrą' : 'ograniczoną') + ' wiarygodność w tej sesji.</li></ul>' +
      '<p class="xs muted" style="margin-top:8px">Aplikacja nie stosuje progów „prawidłowej pozycji” bez źródła naukowego. Wynik warto czytać razem z oceną komfortu.</p>';
  }

  // ---------- ekran: Porównanie ----------
  function viewCompare() {
    var A = session(state.cmpA || 'S-0006'), B = session(state.cmpB || 'S-0007');
    var opts = function (sel) { return analysed().map(function (x) { return '<option value="' + x.id + '"' + (x.id === sel ? ' selected' : '') + '>' + x.id + ' · ' + dateTxt(x.date) + '</option>'; }).join(''); };
    var changes = RM.CHANGES.filter(function (c) { return c.bike === B.bike && c.date > A.date && c.date <= B.date; });
    var rows = RM.METRICS.filter(function (m) { return !m.na; }).map(function (m) {
      var r = m.reps[0][0], a = A.agg[m.id].stat[r], b = B.agg[m.id].stat[r], d = b.value - a.value;
      var qa = A.quality[m.id], qb = B.quality[m.id], noisy = Math.abs(d) < Math.max(a.sd, b.sd);
      var note = (qa !== 'good' || qb !== 'good') ? 'Ostrożnie: ograniczona wiarygodność w jednej z sesji.' : noisy ? 'Różnica mniejsza niż zmienność między cyklami.' : '';
      return '<tr class="click' + (state.cmpMetric === m.id ? ' sel' : '') + '" data-cmetric="' + m.id + '"><td><b>' + m.name + '</b><div class="xs muted">' + esc(m.reps[0][1]) + '</div></td><td class="num">' + num(a.value) + ' ' + m.unit + '</td><td class="num">' + num(b.value) + ' ' + m.unit + '</td><td class="num"><b>' + signed(d) + '</b> ' + m.unit + '</td><td class="num xs">±' + num(a.sd) + ' / ±' + num(b.sd) + '</td><td>' + qBadge(qa, true) + ' ' + qBadge(qb, true) + '</td><td class="small muted">' + note + '</td></tr>';
    }).join('');
    var cA = RM.COMFORT[A.id] || { items: {} }, cB = RM.COMFORT[B.id] || { items: {} };
    var regs = RM.REGIONS.filter(function (r) { return cA.items[r] || cB.items[r]; });
    var m = metric(state.cmpMetric);
    return '<div class="page-head"><div><div class="crumbs">Analiza</div><h1>Porównanie sesji</h1><div class="sub">Co się zmieniło w ustawieniach, w pomiarach i w odczuciach.</div></div>' +
      '<div class="row"><select class="sel" id="cmpA" style="width:auto">' + opts(A.id) + '</select><span class="muted">→</span><select class="sel" id="cmpB" style="width:auto">' + opts(B.id) + '</select></div></div>' +
      demoNote('Obie sesje pochodzą z danych demonstracyjnych.') +
      '<div class="grid g-main" style="margin-top:16px"><div class="card"><div class="card-h"><h2>Zmiany ustawień między sesjami</h2><a class="btn sm ghost" href="#/historia">Historia zmian</a></div>' +
      (changes.length ? '<div class="tbl-wrap"><table class="tbl"><thead><tr><th>Data</th><th>Parametr</th><th class="num">Przed</th><th class="num">Po</th><th>Powód</th></tr></thead><tbody>' + changes.map(function (c) { return '<tr><td>' + dateTxt(c.date) + '</td><td>' + esc(c.param) + '</td><td class="num">' + esc(c.before) + ' ' + c.unit + '</td><td class="num">' + esc(c.after) + ' ' + c.unit + '</td><td class="small muted">' + esc(c.reason) + '</td></tr>'; }).join('') + '</tbody></table></div>' : '<div class="empty small">Brak zapisanych zmian między tymi sesjami.</div>') + '</div>' +
      '<div class="card"><h2>Warunki nagrania</h2><div class="tbl-wrap"><table class="tbl small" style="margin-top:8px"><thead><tr><th></th><th>' + A.id + '</th><th>' + B.id + '</th></tr></thead><tbody>' +
      '<tr><td>Rower</td><td>' + esc(bike(A.bike).name) + '</td><td>' + esc(bike(B.bike).name) + '</td></tr><tr><td>Strona</td><td>' + A.side + '</td><td>' + B.side + '</td></tr>' +
      '<tr><td>Nagranie</td><td>' + A.res + ' · ' + A.fps + ' fps</td><td>' + B.res + ' · ' + B.fps + ' fps</td></tr><tr><td>Przechył kamery</td><td>' + num(A.calibration.tilt) + '°</td><td>' + num(B.calibration.tilt) + '°</td></tr>' +
      '<tr><td>Jakość ogólna</td><td>' + qBadge(A.quality.overall, true) + '</td><td>' + qBadge(B.quality.overall, true) + '</td></tr></tbody></table></div></div></div>' +
      '<div class="card" style="margin-top:16px"><div class="card-h"><h2>Różnice w pomiarach</h2><span class="xs muted">wartości reprezentatywne (mediana z cykli); kliknij wiersz, aby nałożyć przebiegi</span></div><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Pomiar</th><th class="num">' + A.id + '</th><th class="num">' + B.id + '</th><th class="num">Δ</th><th class="num">SD</th><th>Jakość</th><th>Uwagi</th></tr></thead><tbody>' + rows + '</tbody></table></div>' +
      '<hr class="sep"><h3>' + m.name + ' – nałożone przebiegi</h3><div class="chart" id="cmpChart" style="margin-top:8px"></div><div class="legend"><span><i class="dash" style="border-color:var(--ser-a)"></i>' + A.id + ' · ' + dateTxt(A.date) + '</span><span><i style="border-color:var(--ser-b)"></i>' + B.id + ' · ' + dateTxt(B.date) + '</span><span><i class="band" style="background:#8A948C;opacity:.3"></i>± 1 SD</span></div></div>' +
      '<div class="card" style="margin-top:16px"><h2>Komfort</h2><p class="xs muted">0 = brak dyskomfortu, 10 = najsilniejszy.</p><div class="tbl-wrap"><table class="tbl" style="max-width:720px"><thead><tr><th>Obszar</th><th class="num">' + A.id + '</th><th class="num">' + B.id + '</th><th class="num">Δ</th><th>Moment pojawienia się</th></tr></thead><tbody>' +
      regs.map(function (r) { var a = cA.items[r] ? cA.items[r][0] : 0, b = cB.items[r] ? cB.items[r][0] : 0; return '<tr><td>' + esc(r) + '</td><td class="num">' + a + '</td><td class="num">' + b + '</td><td class="num">' + signed(b - a, 0) + '</td><td class="small muted">' + esc((cA.items[r] || [])[3] || '—') + ' → ' + esc((cB.items[r] || [])[3] || '—') + '</td></tr>'; }).join('') + '</tbody></table></div></div>';
  }

  // ---------- ekran: Komfort ----------
  function viewComfort(id) {
    var s = session(id) || session('S-0007');
    var cur = RM.COMFORT[s.id] || (RM.COMFORT[s.id] = { ride: '', items: {} });
    var prevId = { 'S-0007': 'S-0006' }[s.id], prev = prevId ? RM.COMFORT[prevId].items : {};
    var opts = analysed().map(function (x) { return '<option value="' + x.id + '"' + (x.id === s.id ? ' selected' : '') + '>' + x.id + ' · ' + dateTxt(x.date) + '</option>'; }).join('');
    var rows = RM.REGIONS.map(function (r) {
      var it = cur.items[r] || [0, 'obie', '', RM.ONSETS[0]], dis = it[0] === 0 ? ' disabled' : '';
      return '<div class="comfort-row" data-region="' + esc(r) + '"><div><b class="small">' + esc(r) + '</b>' + (prev[r] ? '<div class="xs muted">poprzednio: ' + prev[r][0] + '</div>' : '') + '</div>' +
        '<input type="range" class="sc" min="0" max="10" step="1" value="' + it[0] + '" aria-label="Ocena: ' + esc(r) + '"><span class="score">' + it[0] + '</span>' +
        '<div class="seg" data-side>' + ['lewa', 'prawa', 'obie'].map(function (sd) { return '<button type="button" class="' + (it[1] === sd ? 'on' : '') + '" data-v="' + sd + '"' + dis + '>' + sd + '</button>'; }).join('') + '</div>' +
        '<div class="row comfort-loc" style="gap:6px"><input class="txt" placeholder="lokalizacja" value="' + esc(it[2]) + '" data-loc' + dis + ' style="padding:6px 8px"><select class="sel" data-onset' + dis + ' style="padding:6px 8px">' + RM.ONSETS.map(function (o) { return '<option' + (o === it[3] ? ' selected' : '') + '>' + o + '</option>'; }).join('') + '</select></div></div>';
    }).join('');
    return '<div class="page-head"><div><div class="crumbs">Dopasowanie</div><h1>Komfort jazdy</h1><div class="sub">Oceń jazdę na ustawieniach z sesji pomiarowej. Ocena komfortu to Twoje odczucie – aplikacja go nie interpretuje medycznie.</div></div>' +
      '<div class="row"><select class="sel" id="comfSel" style="width:auto">' + opts + '</select><button class="btn pri" data-act="saveComfort">Zapisz ocenę</button></div></div>' +
      '<div class="grid g-main"><div class="card"><div class="card-h"><h2>Obszary ciała</h2><span class="xs muted">0 = brak dyskomfortu · 10 = najsilniejszy, uniemożliwia jazdę</span></div>' +
      '<div class="row xs muted hide-sm" style="display:grid;grid-template-columns:minmax(150px,1.2fr) minmax(160px,2fr) auto minmax(120px,1fr) minmax(130px,1fr);gap:12px;border-bottom:1px solid var(--hair-strong);padding-bottom:6px"><span>OBSZAR</span><span>OCENA 0–10</span><span></span><span>STRONA CIAŁA</span><span>LOKALIZACJA · MOMENT POJAWIENIA SIĘ</span></div>' + rows + '</div>' +
      '<div class="stack"><div class="card"><h3>Jazda, której dotyczy ocena</h3><div class="form" style="grid-template-columns:1fr;margin-top:8px">' +
      '<div class="fld"><label>Ustawienia</label><input class="txt" value="' + esc(bike(s.bike).name + ' · ' + s.settingsNote) + '" readonly></div>' +
      '<div class="fld"><label>Rodzaj i czas jazdy</label><input class="txt" value="' + esc(cur.ride) + '" placeholder="np. 90 min, trenażer, jazda w tlenie"></div>' +
      '<div class="fld"><label>Uwagi</label><textarea class="txt" rows="3" placeholder="np. dyskomfort mija po zmianie chwytu"></textarea></div></div></div>' +
      '<div class="callout info"><div class="ic">i</div><div class="small">Oceniaj po jeździe trwającej co najmniej tyle, ile zwykle. Porównanie komfortu ma sens dla podobnych jazd (czas, intensywność).</div></div>' +
      demoNote('Oceny są przykładowe; zapis działa tylko do odświeżenia strony.') + '</div></div>';
  }

  // ---------- ekran: Historia zmian ----------
  function viewHistory() {
    var f = state.histBike || 'all';
    var rows = RM.CHANGES.filter(function (c) { return f === 'all' || c.bike === f; }).map(function (c) {
      var res = '<span class="muted small">brak kolejnego pomiaru</span>';
      var A = c.before_s && session(c.before_s), B = c.after_s && session(c.after_s);
      if (B && B.status === 'failed') res = '<span class="small">' + stBadge('failed') + ' <a href="#/analiza/' + B.id + '">' + B.id + '</a> – brak wiarygodnego pomiaru</span>';
      else if (B && !B.agg) res = '<span class="small">' + stBadge(B.status) + ' <a href="#/analiza/' + B.id + '">' + B.id + '</a></span>';
      else if (A && B && A.agg && B.agg && c.metric) {
        var m = metric(c.metric), d = B.agg[c.metric].stat[c.rep].value - A.agg[c.metric].stat[c.rep].value;
        var ca = RM.COMFORT[A.id].items[c.comfort], cb = RM.COMFORT[B.id].items[c.comfort];
        var adjR = c.adj && RM_FIT.ADJUSTMENTS.filter(function (x) { return x.id === c.adj; })[0];
        var st = adjR ? fitStatus(RM_FIT.evaluate(adjR, A, bike(c.bike).type).status) + ' → ' + fitStatus(RM_FIT.evaluate(adjR, B, bike(c.bike).type).status) + '<br>' : '';
        res = '<div class="small">' + st + '<a href="#/wyniki/' + B.id + '">' + B.id + '</a>: ' + m.name.toLowerCase() + ' ' + (c.rep === 'max' ? 'maks.' : 'min.') + ' <span class="mono">' + signed(d) + m.unit + '</span>' +
          (ca && cb ? '<br>komfort „' + esc(c.comfort) + '”: ' + ca[0] + ' → ' + cb[0] : '') + '</div>';
      }
      return '<tr><td>' + dateTxt(c.date) + '</td><td class="small">' + esc(bike(c.bike).name) + '</td><td><b>' + esc(c.param) + '</b></td><td class="num">' + esc(c.before) + '</td><td class="num">' + esc(c.after) + '</td><td>' + esc(c.unit || '—') + '</td><td class="small muted">' + esc(c.reason) + '</td><td>' + res + '</td></tr>';
    }).join('');
    return '<div class="page-head"><div><div class="crumbs">Dopasowanie</div><h1>Historia zmian</h1><div class="sub">Każda zmiana ustawień z powodem i wynikiem kolejnego pomiaru.</div></div>' +
      '<div class="row"><select class="sel" id="histBike" style="width:auto"><option value="all">Wszystkie rowery</option>' + RM.BIKES.map(function (b) { return '<option value="' + b.id + '"' + (f === b.id ? ' selected' : '') + '>' + esc(b.name) + '</option>'; }).join('') + '</select><a class="btn pri" href="#/rowery/b1">+ Zapisz zmianę</a></div></div>' +
      '<div class="card"><div class="tbl-wrap"><table class="tbl"><thead><tr><th>Data</th><th>Rower</th><th>Parametr</th><th class="num">Przed</th><th class="num">Po</th><th>Jedn.</th><th>Powód</th><th>Wynik kolejnego pomiaru</th></tr></thead><tbody>' + rows + '</tbody></table></div>' +
      '<p class="xs muted" style="margin-top:10px">Wynik kolejnego pomiaru pokazuje różnicę względem sesji sprzed zmiany. Kilka zmian naraz utrudnia przypisanie efektu jednej z nich.</p></div>' +
      '<div style="margin-top:16px">' + demoNote('Historia zawiera przykładowe zmiany. Zmiany zapisane w prototypie znikają po odświeżeniu strony.') + '</div>';
  }

  function viewNotFound() { return '<div class="empty">Nie znaleziono ekranu. <a href="#/pulpit">Wróć do pulpitu</a></div>'; }

  // ---------- definicja pomiaru (panel boczny) ----------
  function openDef(id) {
    var m = metric(id), p = RM.pose(0.45);
    var pts = m.pts.length ? m.pts : [];
    var fig = pts.length >= 2 ? '<svg viewBox="-450 -1350 1150 1250" style="width:100%;background:#1B231E;border-radius:10px;margin:10px 0">' + figureSvg(p, { metric: id, lines: true, points: true }) + '</svg>' : '';
    $('#drawer').innerHTML = '<div class="row"><h2>' + m.name + '</h2><span class="spacer"></span><button class="btn sm" data-act="closeDef">Zamknij</button></div>' +
      '<p class="small muted" style="margin-top:6px">Płaszczyzna strzałkowa (widok z boku, 2D) · jednostka: ' + m.unit + '</p>' + fig +
      '<h3>Definicja</h3><p class="small">' + esc(m.def) + '</p><h3>Punkty anatomiczne</h3><ol class="small" style="padding-left:18px">' + m.points.map(function (x) { return '<li>' + esc(x) + '</li>'; }).join('') + '</ol>' +
      '<h3>Wzór</h3><p class="mono small" style="background:var(--tint);padding:8px 10px;border-radius:8px">' + esc(m.formula) + '</p>' +
      (m.reps.length ? '<h3>Wartości raportowane</h3><ul class="small" style="padding-left:18px">' + m.reps.map(function (r) { return '<li>' + esc(r[1]) + '</li>'; }).join('') + '<li>Zmienność: odchylenie standardowe między zaakceptowanymi cyklami</li></ul>' : '') +
      '<p class="xs muted">Definicje są robocze i zostaną zatwierdzone w Etapie 4 (pomiary biomechaniczne) razem z testami matematycznymi.</p>';
    $('#drawer').classList.add('open'); $('#drawerBg').classList.add('open');
  }
  function closeDef() { $('#drawer').classList.remove('open'); $('#drawerBg').classList.remove('open'); }

  // ---------- router ----------
  function route() { return (location.hash || '#/pulpit').replace(/^#\/?/, '') || 'pulpit'; }
  function render() {
    cleanup.forEach(function (f) { f(); }); cleanup = [];
    var r = route(), parts = r.split('/'), html;
    renderNav(r);
    switch (parts[0]) {
      case 'pulpit': html = viewDashboard(); break;
      case 'profil': html = viewProfile(); break;
      case 'rowery': html = viewBikes(parts[1]); break;
      case 'nowa': html = viewWizard(); break;
      case 'analiza': html = viewJob(parts[1] || 'S-0008'); break;
      case 'wyniki': html = viewResults(parts[1] || 'S-0007'); break;
      case 'porownanie': html = viewCompare(); break;
      case 'komfort': html = viewComfort(parts[1] || 'S-0007'); break;
      case 'historia': html = viewHistory(); break;
      default: html = viewNotFound();
    }
    if (html === '') return;
    app.innerHTML = html;
    afterRender(parts);
  }
  function afterRender(parts) {
    var p0 = parts[0];
    if (p0 === 'wyniki' && $('#sim')) {
      var ss = session(parts[1] || 'S-0007');
      $$('[data-sim-in]').forEach(function (i) { i.oninput = function () { renderSim(ss); }; });
      renderSim(ss);
      $('#sessSel').onchange = function () { location.hash = '#/wyniki/' + this.value; };
    }
    if (p0 === 'wyniki' && $('#viewer')) {
      var s = session(parts[1] || 'S-0007');
      viewer($('#viewer'), s);
      var el = $('#cycleChart'), m = metric(state.metric);
      if (el) {
        var ser = [];
        if (state.showCycles) s.cycles.filter(function (c) { return c.valid; }).forEach(function (c) { ser.push({ y: c.curves[m.id], color: '#8A948C', thin: true, label: 'cykl' }); });
        ser.push({ y: s.agg[m.id].mean, sd: s.agg[m.id].sd, color: '#2C6B4F', label: 'średnia' });
        lineChart(el, { series: ser, unit: m.unit, title: m.name });
      }
      var cs = $('#showCycles'); if (cs) cs.onchange = function () { state.showCycles = cs.checked; render(); };
      $('#sessSel').onchange = function () { location.hash = '#/wyniki/' + this.value; };
    }
    if (p0 === 'porownanie') {
      var A = session(state.cmpA || 'S-0006'), B = session(state.cmpB || 'S-0007'), cm = metric(state.cmpMetric);
      lineChart($('#cmpChart'), { unit: cm.unit, title: cm.name, series: [
        { y: A.agg[cm.id].mean, sd: A.agg[cm.id].sd, color: '#D14E1E', dash: true, label: A.id },
        { y: B.agg[cm.id].mean, sd: B.agg[cm.id].sd, color: '#2B57C8', label: B.id }] });
      $('#cmpA').onchange = function () { state.cmpA = this.value; render(); };
      $('#cmpB').onchange = function () { state.cmpB = this.value; render(); };
    }
    if (p0 === 'analiza') runJob(parts[1] || 'S-0008');
    if (p0 === 'nowa') {
      var w = state.wizard, f = $('#vfile');
      if (f) f.onchange = function () {
        var file = f.files[0]; if (!file) return;
        var ext = (file.name.split('.').pop() || '').toLowerCase(), err = null;
        if (['mp4', 'mov'].indexOf(ext) < 0) err = 'Nieobsługiwany format „.' + ext + '”. Dozwolone: MP4, MOV.';
        else if (file.size > 500 * 1048576) err = 'Plik jest większy niż 500 MB.';
        w.file = { name: file.name, size: file.size, err: err }; render();
      };
    }
    if (p0 === 'komfort') {
      $('#comfSel').onchange = function () { location.hash = '#/komfort/' + this.value; };
      $$('.comfort-row').forEach(function (row) {
        var rng = row.querySelector('input[type=range]');
        rng.oninput = function () {
          row.querySelector('.score').textContent = rng.value;
          $$('[data-side] button, [data-loc], [data-onset]', row).forEach(function (x) { x.disabled = rng.value === '0'; });
        };
      });
    }
    if (p0 === 'historia') $('#histBike').onchange = function () { state.histBike = this.value; render(); };
    if (p0 === 'rowery') {
      var form = $('#chgForm');
      if (form) form.onsubmit = function (e) {
        e.preventDefault(); var fd = new FormData(form), b = bike(parts[1]) || RM.BIKES[0];
        RM.CHANGES.unshift({ date: fd.get('date'), bike: b.id, param: fd.get('param'), before: fd.get('before'), after: fd.get('after'), unit: fd.get('unit') === '—' ? '' : fd.get('unit'), reason: fd.get('reason'), before_s: 'S-0007', after_s: null });
        toast('Zmiana zapisana w historii (prototyp – do odświeżenia strony).'); form.reset();
      };
    }
    window.scrollTo(0, 0);
  }

  // delegacja kliknięć
  document.addEventListener('click', function (e) {
    var t = e.target.closest('[data-rtab],[data-def],[data-metric],[data-cmetric],[data-href],[data-act],[data-sim],[data-ptab],[data-btab],[data-seg] button,[data-side] button,[data-chk],input[name=wb],input[name=keep],input[data-w=consent]');
    if (!t) return;
    if (t.dataset.def) { e.stopPropagation(); openDef(t.dataset.def); return; }
    if (t.dataset.metric) { state.metric = t.dataset.metric; var y = window.scrollY; render(); window.scrollTo(0, y); return; }
    if (t.dataset.cmetric) { state.cmpMetric = t.dataset.cmetric; var y2 = window.scrollY; render(); window.scrollTo(0, y2); return; }
    if (t.dataset.href) { location.hash = t.dataset.href; return; }
    if (t.dataset.rtab) { state.resTab = t.dataset.rtab; render(); return; }
    if (t.dataset.ptab) { state.profileTab = t.dataset.ptab; render(); return; }
    if (t.dataset.btab) { state.bikeTab = t.dataset.btab; render(); return; }
    if (t.dataset.chk != null && state.wizard) { state.wizard.checks[t.dataset.chk] = t.checked; render(); return; }
    if (t.name === 'wb') { state.wizard.bike = t.value; render(); return; }
    if (t.name === 'keep') { state.wizard.keep = t.value; return; }
    if (t.dataset.w === 'consent') { state.wizard.consent = t.checked; render(); return; }
    if (t.parentNode.dataset.seg === 'side') { state.wizard.side = t.dataset.v; render(); return; }
    if (t.parentNode.hasAttribute('data-side')) { $$('button', t.parentNode).forEach(function (b) { b.classList.toggle('on', b === t); }); return; }
    if (t.dataset.sim) {
      var id = route().split('/')[1] || 'S-0008', j = state.jobs[id];
      Object.assign(j, { status: 'pending', stage: 0, progress: 0, scenario: t.dataset.sim, error: null }); render(); return;
    }
    var a = t.dataset.act;
    if (a === 'save') toast('Prototyp: dane profilu nie są zapisywane.');
    if (a === 'soon') toast('Funkcja zaprojektowana – formularz pojawi się w kolejnym etapie.');
    if (a === 'closeDef') closeDef();
    if (a === 'details') { e.preventDefault(); state.resTab = 'details'; render(); }
    if (a === 'wnext') { state.wizard.step++; render(); }
    if (a === 'wback') { state.wizard.step--; render(); }
    if (a === 'wsend') {
      var nid = 'S-' + ('000' + (9 + Object.keys(state.jobs).filter(function (k) { return !session(k); }).length)).slice(-4);
      state.jobs[nid] = { id: nid, status: 'pending', stage: 0, progress: 0, scenario: 'warn', attempt: 1, bike: state.wizard.bike, date: '2026-10-27' };
      state.wizard = null; location.hash = '#/analiza/' + nid;
    }
    if (a === 'retry') { var jid = route().split('/')[1], jj = state.jobs[jid]; Object.assign(jj, { status: 'pending', stage: 0, progress: 0, scenario: 'ok', attempt: jj.attempt + 1, error: null }); render(); }
    if (a === 'saveComfort') {
      var sid = route().split('/')[1] || 'S-0007', items = {};
      $$('.comfort-row').forEach(function (row) {
        var v = +row.querySelector('input[type=range]').value; if (!v) return;
        var on = row.querySelector('[data-side] .on');
        items[row.dataset.region] = [v, on ? on.dataset.v : 'obie', row.querySelector('[data-loc]').value, row.querySelector('[data-onset]').value];
      });
      RM.COMFORT[sid] = Object.assign(RM.COMFORT[sid] || {}, { items: items });
      toast('Ocena komfortu zapisana (prototyp – do odświeżenia strony).');
    }
  });
  document.addEventListener('change', function (e) { var t = e.target; if (t.dataset && t.dataset.w && state.wizard && t.dataset.w !== 'consent') state.wizard[t.dataset.w] = t.value; });
  $('#drawerBg').addEventListener('click', closeDef);
  window.addEventListener('hashchange', render);
  render();
})();
