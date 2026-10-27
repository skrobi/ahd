// Test dymny prototypu: każdy ekran, desktop 1440 px i wąski 390 px.
// Uruchomienie: node ridemore-fit/tests/smoke.js [katalog_na_zrzuty]
const { chromium } = require('playwright');
const path = require('path');
const url = 'file://' + path.resolve(__dirname, '../prototyp/index.html');
const shots = process.argv[2];
const routes = ['pulpit', 'profil', 'rowery/b1', 'nowa', 'analiza/S-0008', 'analiza/S-0005', 'wyniki/S-0007', 'wyniki/S-0006', 'porownanie', 'komfort/S-0007', 'historia'];
const sizes = { desktop: [1440, 900], waski: [390, 844] };
let fail = 0;
function check(ok, msg) { if (!ok) { fail++; console.log('  BŁĄD: ' + msg); } }

(async () => {
  const browser = await chromium.launch();
  for (const [name, [w, h]] of Object.entries(sizes)) {
    const page = await browser.newPage({ viewport: { width: w, height: h } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => { if (m.type() === 'error' && !/fonts\.g/.test(m.text())) errors.push(m.text()); });
    for (const r of routes) {
      await page.goto(url + '#/' + r);
      await page.waitForTimeout(250);
      const info = await page.evaluate(() => ({
        overflow: document.documentElement.scrollWidth - window.innerWidth,
        h1: (document.querySelector('main h1') || {}).textContent || '',
        demo: !!document.querySelector('.demo-bar')
      }));
      console.log(`${name} #/${r}: "${info.h1}" overflow=${info.overflow}px`);
      check(info.h1, `${r}: brak nagłówka`);
      check(info.overflow <= 1, `${r} (${name}): poziome przewijanie ${info.overflow}px`);
      check(info.demo, `${r}: brak paska danych demonstracyjnych`);
      if (shots) await page.screenshot({ path: path.join(shots, `${name}-${r.replace('/', '_')}.png`), fullPage: true });
    }
    check(errors.length === 0, `${name}: błędy JS: ${errors.join(' | ')}`);
    await page.close();
  }

  // scenariusze interakcji (desktop)
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const errs = []; page.on('pageerror', e => errs.push(e.message));
  // 1. kreator: nie da się przejść dalej bez listy kontrolnej; zły format pliku blokuje
  await page.goto(url + '#/nowa');
  await page.click('[data-act=wnext]');
  check(await page.isDisabled('[data-act=wnext]'), 'kreator: krok 2 powinien blokować „Dalej” bez listy kontrolnej');
  for (let i = 0; i < 8; i++) await page.check(`[data-chk="${i}"]`);
  check(!(await page.isDisabled('[data-act=wnext]')), 'kreator: po zaznaczeniu listy „Dalej” powinno działać');
  await page.click('[data-act=wnext]');
  await page.setInputFiles('#vfile', { name: 'film.avi', mimeType: 'video/x-msvideo', buffer: Buffer.from('x') });
  check(await page.isVisible('.drop.err'), 'kreator: plik .avi powinien zostać odrzucony');
  await page.setInputFiles('#vfile', { name: 'jazda.mp4', mimeType: 'video/mp4', buffer: Buffer.from('x') });
  await page.check('[data-w=consent]');
  check(await page.isVisible('.drop.ok'), 'kreator: plik .mp4 powinien zostać przyjęty');
  await page.click('[data-act=wnext]');
  await page.click('[data-act=wsend]');
  check((await page.evaluate(() => location.hash)).startsWith('#/analiza/S-0009'), 'kreator: wysłanie powinno otworzyć ekran analizy');
  await page.waitForTimeout(16000);
  check(await page.isVisible('.st.warn'), 'analiza: symulacja powinna zakończyć się stanem „z ostrzeżeniami”');
  // 2. błąd i ponowienie
  await page.goto(url + '#/analiza/S-0005');
  check(await page.isVisible('.callout.bad'), 'analiza S-0005: powinien być widoczny błąd');
  await page.click('[data-act=retry]');
  check((await page.textContent('.joblist').catch(() => '')).length > 0, 'ponowienie: lista etapów');
  check((await page.textContent('main')).includes('2 z 3'), 'ponowienie: licznik prób powinien wzrosnąć');
  // 3a. podsumowanie dla użytkownika: zalecenia prostym językiem
  await page.goto(url + '#/wyniki/S-0006');
  check((await page.textContent('.card.next')).includes('Podnieś siodło o ok. 10 mm'), 'podsumowanie S-0006: zalecenie podniesienia siodła');
  await page.goto(url + '#/wyniki/S-0007');
  check((await page.textContent('.card.next')).includes('Skróć mostek o ok. 10 mm'), 'podsumowanie S-0007: zalecenie skrócenia mostka');
  check((await page.textContent('main')).includes('Co dała zmiana'), 'podsumowanie S-0007: porównanie przed/po');
  check(!(await page.textContent('main')).includes('Kąt kolana'), 'podsumowanie nie powinno pokazywać tabeli kątów');
  const simBefore = await page.textContent('[data-simrow=barReach]');
  await page.fill('[data-sim-in=ds]', '-10'); await page.dispatchEvent('[data-sim-in=ds]', 'input');
  const simAfter = await page.textContent('[data-simrow=barReach] td:nth-child(3)');
  check(simBefore.includes('Blisko granicy') && simAfter.includes('W porządku'), 'symulacja: krótszy mostek powinien przenieść odległość do kierownicy w zakres');
  await page.goto(url + '#/pulpit');
  check((await page.textContent('main')).includes('teraz w zakresie'), 'pulpit: efekt zmiany siodła');
  check(!(await page.textContent('main')).includes('wykonano 25.10.2026'), 'pulpit: zmiana bloków nie jest wykonaniem zalecenia „skróć mostek”');
  // 3b. szczegóły: wybór pomiaru, definicja, tor kolana niedostępny
  await page.goto(url + '#/wyniki/S-0007');
  await page.click('[data-rtab=details]');
  await page.click('tr[data-metric=hip]');
  check((await page.textContent('main')).includes('Kąt biodra w cyklu'), 'wyniki: wybór pomiaru powinien zmienić wykres');
  await page.click('[data-def=knee]');
  check(await page.isVisible('#drawer.open'), 'wyniki: definicja powinna się otworzyć');
  check((await page.textContent('#drawer')).includes('Nadkłykieć boczny'), 'definicja: punkty anatomiczne');
  await page.click('[data-act=closeDef]');
  const kt = await page.textContent('tr[data-metric=kneeTrack]');
  check(kt.includes('niedostępne'), 'wyniki: tor kolana powinien być oznaczony jako niedostępny');
  const ankle = await page.textContent('tr[data-metric=ankle]');
  check(ankle.includes('≈') && ankle.includes('Ograniczona'), 'wyniki: kąt stawu skokowego oznaczony jako ograniczona wiarygodność');
  // 4. spójność kątów schematu z definicją geometryczną
  const geo = await page.evaluate(() => {
    const a = RM.angleAt({ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 });
    const b = RM.angleAt({ x: 0, y: 0 }, { x: 1, y: 0 }, { x: -1, y: 0 });
    const c = RM.angleToHorizontal({ x: 0, y: 0 }, { x: 1, y: -1 });
    return [a, b, c];
  });
  check(Math.abs(geo[0] - 90) < 1e-9 && Math.abs(geo[1] - 180) < 1e-9 && Math.abs(geo[2] - 45) < 1e-9, 'geometria: angleAt/angleToHorizontal ' + geo);
  // 5. porównanie i historia
  await page.goto(url + '#/porownanie');
  check((await page.textContent('main')).includes('Wysokość siodła'), 'porównanie: zmiana siodła między sesjami');
  await page.goto(url + '#/historia');
  check((await page.textContent('main')).includes('brak wiarygodnego pomiaru'), 'historia: nieudana sesja po zmianie mostka');
  check(errs.length === 0, 'interakcje: błędy JS: ' + errs.join(' | '));
  await browser.close();
  console.log(fail ? `\nWYNIK: ${fail} błąd(ów)` : '\nWYNIK: wszystkie sprawdzenia OK');
  process.exit(fail ? 1 : 0);
})();
