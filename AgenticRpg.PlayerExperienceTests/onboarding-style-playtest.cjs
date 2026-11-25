const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Real computed styles catch missing isolated assets and global cascade regressions.
module.exports = async function checkOnboardingStyles(page, base, channel) {
  const output = path.join(__dirname, 'obj/style-screenshots');
  fs.mkdirSync(output, { recursive: true });
  for (const width of [1440, 390, 320]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`${base}/startup`);
    await page.getByRole('button', { name: 'Start New Adventure', exact: true }).waitFor();
    await page.getByRole('button', { name: 'Join with Code', exact: true }).click();
    await page.getByPlaceholder('Someone actually invited you?').waitFor();
    await checkPage(page, '.startup-menu');
    const cards = await page.locator('.menu-content > .menu-section').evaluateAll(cards => cards.map(card => {
      const css = getComputedStyle(card);
      return { bottomBorder: parseFloat(css.borderBottomWidth), padding: parseFloat(css.paddingBottom) };
    }));
    assert.ok(cards.length > 1);
    assert.ok(cards.every(card => card.bottomBorder >= 1 && card.padding >= 16), 'Model menu CSS must not strip startup card borders/padding');
    if (width !== 320) await page.screenshot({ path: path.join(output, `${channel}-startup-${width}.png`), fullPage: true });
    for (const query of ['', '?worldId=world', '?campaignId=campaign']) {
      await page.goto(`${base}/adventure-setup${query}`);
      await page.locator('.setup-panel').first().waitFor();
      await page.waitForFunction(() => !document.querySelector('.setup-status'));
      await checkPage(page, '.adventure-setup');
      const progress = await page.locator('.setup-progress').evaluate(element => getComputedStyle(element).backgroundColor);
      assert.equal(progress, 'rgb(17, 24, 39)');
      if (!query && width !== 320) await page.screenshot({ path: path.join(output, `${channel}-setup-${width}.png`), fullPage: true });
    }
  }
  await page.setViewportSize({ width: 1600, height: 1000 });
  console.log(`PASS: ${channel} onboarding themes, controls and card cascade at desktop, 390px and 320px`);
};

async function checkPage(page, selector) {
  const styles = await page.locator(selector).evaluate(root => {
    const css = getComputedStyle(root);
    const controls = [...root.querySelectorAll('.onboarding-control, .onboarding-field')].map(element => {
      const style = getComputedStyle(element);
      return { height: element.getBoundingClientRect().height, bg: style.backgroundColor, color: style.color };
    });
    return { background: css.backgroundImage, color: css.color, controls,
      overflowing: document.documentElement.scrollWidth > innerWidth + 1 };
  });
  assert.ok(styles.background.includes('linear-gradient'));
  assert.equal(styles.color, 'rgb(224, 242, 241)');
  assert.ok(styles.controls.length > 0);
  assert.ok(styles.controls.every(control => control.height >= 44 && control.bg !== 'rgb(239, 239, 239)' && control.bg !== 'rgb(255, 255, 255)'));
  assert.equal(styles.overflowing, false, `No horizontal overflow on ${selector}`);
}

// Also usable on its own while the opt-in host is running.
if (require.main === module) {
  const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
  const signal = process.env.RPG_BROWSER_PLAYTEST_SIGNAL || path.join(__dirname, 'obj/browser-host.txt');
  const base = fs.readFileSync(signal, 'utf8').trim();
  (async () => {
    try {
      for (const channel of ['chrome', 'msedge']) {
        const browser = await chromium.launch({ channel, headless: true });
        try {
          const context = await browser.newContext();
          await context.addCookies([{ name: 'PlaytestPlayer', value: 'alice', url: base }]);
          const page = await context.newPage();
          const errors = [];
          page.on('pageerror', error => errors.push(error.message));
          await module.exports(page, base, channel);
          // Authenticated Home redirects to Startup; check its public view in an anonymous context.
          const publicContext = await browser.newContext();
          const publicPage = await publicContext.newPage();
          await publicPage.goto(`${base}/`);
          await publicPage.locator('.begin-adventure').waitFor();
          assert.equal(await publicPage.locator('.begin-adventure').evaluate(button => getComputedStyle(button).backgroundColor), 'rgb(112, 196, 211)');
          await publicContext.close();
          for (const route of ['/game?campaignId=campaign&characterId=a', '/campaign-creation?worldOnly=true', '/character-creation']) {
            await page.goto(`${base}${route}`);
            await page.getByRole('button', { name: 'Open Advanced Settings' }).waitFor();
            const fit = await page.locator('.menu-button').evaluate(button => button.getBoundingClientRect().width >= 140 && button.scrollWidth <= button.clientWidth);
            assert.ok(fit, `Advanced Settings label must fit on ${route}`);
            assert.equal(await page.locator('link[rel="stylesheet"]').evaluateAll(links => links.filter(link => /\/app(?:\.[a-z0-9]+)?\.css(?:\?|$)/i.test(link.href)).length), 1);
            await page.getByRole('button', { name: 'Open Advanced Settings' }).click();
            await page.locator('.model-menu-content').waitFor();
            assert.ok(await page.locator('.model-menu-content .menu-section').last().evaluate(section => parseFloat(getComputedStyle(section).paddingBottom) === 0));
          }
          assert.deepEqual(errors, []);
          console.log(`PASS: ${channel} Home, labelled settings controls and model dialog styles`);
        } finally { await browser.close(); }
      }
    } finally { fs.rmSync(signal, { force: true }); }
  })().catch(error => { console.error(error); process.exitCode = 1; });
}
