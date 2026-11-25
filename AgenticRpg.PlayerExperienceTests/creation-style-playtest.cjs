const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

module.exports = async function checkCreationStyles(page, base, channel) {
  const output = path.join(__dirname, 'obj/style-screenshots');
  fs.mkdirSync(output, { recursive: true });
  const routes = [
    ['campaign-form', '/campaign-creation?worldOnly=false&worldId=world&journey=true', '.campaign-creation', '.creation-content'],
    ['world-creation', '/campaign-creation?worldOnly=true&journey=true&starter=Classic%20fantasy', '.campaign-creation', '.creation-content'],
    ['character-creation', '/character-creation?journey=true&starter=Warrior', '.character-creation-container', '.split-container']
  ];
  for (const [width, height] of [[1440, 900], [1280, 720], [390, 844], [320, 740]]) {
    await page.setViewportSize({ width, height });
    for (const [name, route, rootSelector, panelsSelector] of routes) {
      await page.goto(`${base}${route}`);
      await page.locator(`${rootSelector} .creation-progress > summary`).click();
      await page.locator(`${rootSelector} .creation-status`).filter({ hasText: 'Draft saved' }).waitFor();
      await page.locator(`${rootSelector} .setup-progress`).waitFor();
      await page.locator('.runtime-connection').filter({ hasText: /^\s*Connected\s*$/ }).waitFor();
      const expandedPanelHeight = await page.locator(panelsSelector).evaluate(panel => panel.getBoundingClientRect().height);
      await page.locator(`${rootSelector} .creation-progress > summary`).click();
      assert.equal(await page.locator(`${rootSelector} .setup-progress`).isVisible(), false, 'Setup details start collapsed');
      assert.ok(Math.abs(await page.locator(panelsSelector).evaluate(panel => panel.getBoundingClientRect().height) - expandedPanelHeight) < 1,
        `${channel} ${name} ${width}: opening setup details must not consume panel height`);
      const layout = await page.locator(rootSelector).evaluate((root, selector) => {
        const panels = root.querySelector(selector);
        const feedback = document.querySelectorAll('.creation-progress');
        const rect = panels.getBoundingClientRect();
        return { feedbackInside: [...feedback].every(element => root.contains(element)),
          statusColor: getComputedStyle(root.querySelector('.creation-status')).color,
          statusBackground: getComputedStyle(root.querySelector('.creation-progress')).backgroundColor,
          horizontalOverflow: document.documentElement.scrollWidth > innerWidth + 1,
          documentOverflow: document.documentElement.scrollHeight > innerHeight + 1,
          panelsBottom: rect.bottom, viewport: innerHeight, panelsHeight: rect.height,
          gridColumns: getComputedStyle(panels).gridTemplateColumns.split(' ').length,
          worldWidth: root.querySelector('.world-panel')?.getBoundingClientRect().width,
          panelsWidth: rect.width };
      }, panelsSelector);
      assert.equal(layout.feedbackInside, true);
      assert.equal(layout.statusColor, 'rgb(224, 242, 241)');
      assert.equal(layout.statusBackground, 'rgb(17, 24, 39)');
      assert.equal(layout.horizontalOverflow, false, `${channel} ${name} ${width}: no horizontal overflow`);
      assert.equal(layout.documentOverflow, false, `${channel} ${name} ${width}: the page must budget for its progress/header`);
      assert.ok(layout.panelsBottom <= layout.viewport + 1 && layout.panelsHeight >= 160);
      if (name === 'campaign-form') {
        assert.equal(layout.gridColumns, 1);
        assert.ok(Math.abs(layout.worldWidth - layout.panelsWidth) < 2, 'Campaign form must use available width');
        await page.locator('.world-panel').evaluate(panel => panel.scrollTop = panel.scrollHeight);
        assert.equal(await page.locator('.btn-save').evaluate(button => {
          const rect = button.getBoundingClientRect();
          return rect.top >= 0 && rect.bottom <= innerHeight;
        }), true, 'Save control remains reachable by scrolling');
        await page.locator('.world-panel').evaluate(panel => panel.scrollTop = 0);
      }
      if (name !== 'campaign-form') {
        const transcriptHeight = await page.locator('.chat-messages').evaluate(messages => messages.getBoundingClientRect().height);
        assert.ok(transcriptHeight >= height * .35, `${channel} ${name} ${width}: transcript needs useful viewport space (${transcriptHeight}px)`);
        await page.locator('.suggested-actions summary').waitFor();
        assert.ok(await page.locator('.suggested-actions').evaluate(element => element.getBoundingClientRect().height) <= 50,
          'Long suggestions must occupy only one compact row');
        const beforeSuggestions = await page.locator('.chat-messages').evaluate(element => element.getBoundingClientRect().height);
        await page.locator('.suggested-actions summary').click();
        assert.equal(await page.locator('.suggested-action-btn').count(), 4);
        assert.equal(await page.locator('.chat-messages').evaluate(element => element.getBoundingClientRect().height), beforeSuggestions,
          'Opening suggestions must overlay the transcript, not shrink it');
        await page.locator('.suggested-actions summary').click();
        const controlsFit = await page.locator('.user-input').evaluate(composer => {
          const bounds = composer.getBoundingClientRect();
          return [...composer.querySelectorAll('button, .input-field')].every(control => {
            const rect = control.getBoundingClientRect();
            return rect.left >= bounds.left && rect.right <= bounds.right + 1 && rect.bottom <= innerHeight + 1;
          });
        });
        assert.equal(controlsFit, true, `${name} ${width}: composer controls must not be clipped`);
        const initialTextareaCount = await page.locator('.user-input textarea').count();
        await page.locator('.input-toggle-button').click();
        assert.notEqual(await page.locator('.user-input textarea').count(), initialTextareaCount);
        const expandedFits = await page.locator('.user-input').evaluate(composer => {
          const bounds = composer.getBoundingClientRect();
          return [...composer.querySelectorAll('button, .input-field')].every(control => {
            const rect = control.getBoundingClientRect();
            return rect.left >= bounds.left && rect.right <= bounds.right + 1 && rect.bottom <= innerHeight + 1;
          });
        });
        assert.equal(expandedFits, true, `${name} ${width}: expanded composer controls must fit`);
        await page.locator('.input-toggle-button').click();
      } else {
        await page.locator('.campaign-advanced summary').click();
        await page.locator('.model-toggle').waitFor();
        assert.equal(await page.locator('.model-toggle').evaluate(button => getComputedStyle(button).backgroundColor), 'rgb(17, 24, 39)');
      }
      // Let message fade-in finish before inspecting the saved screenshots.
      await page.waitForTimeout(350);
      if (width !== 320) await page.screenshot({ path: path.join(output, `${channel}-${name}-${width}.png`), fullPage: true });
    }
  }
  await page.setViewportSize({ width: 1600, height: 1000 });
  console.log(`PASS: ${channel} compact setup overlay, transcript height, creation theme, viewport layout and scrollable Save at desktop/390px/320px`);
};

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
          await module.exports(page, base, channel);
        } finally { await browser.close(); }
      }
    } finally { fs.rmSync(signal, { force: true }); }
  })().catch(error => { console.error(error); process.exitCode = 1; });
}
