// Run the opt-in BrowserPlaytestHostTests first; see README.md in this directory.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const checkOnboardingStyles = require('./onboarding-style-playtest.cjs');
const checkCreationStyles = require('./creation-style-playtest.cjs');
const signal = process.env.RPG_BROWSER_PLAYTEST_SIGNAL || path.join(__dirname, 'obj/browser-host.txt');
const base = fs.readFileSync(signal, 'utf8').trim();

(async () => {
  const browsers = await Promise.all(['chrome', 'msedge'].map(channel => chromium.launch({ channel, headless: true })));
  const pages = [];
  const errors = [];
  try {
    for (let i = 0; i < browsers.length; i++) {
      const context = await browsers[i].newContext({ viewport: { width: 1600, height: 1000 } });
      await context.addCookies([{ name: 'PlaytestPlayer', value: i ? 'bob' : 'alice', url: base }]);
      const page = await context.newPage();
      page.on('pageerror', error => errors.push(error.message));
      await checkOnboardingStyles(page, base, i ? 'edge' : 'chrome');
      await page.goto(`${base}/lobby?campaignId=campaign&characterId=${i ? 'b' : 'a'}`);
      await page.getByRole('button', { name: 'Click when Ready' }).waitFor({ timeout: 45000 });
      await page.waitForFunction(() => document.querySelector('.connection-status')?.innerText.trim() === 'Connected');
      pages.push(page);
    }
    const [alice, bob] = pages;
    await alice.getByRole('button', { name: 'Click when Ready' }).click();
    await bob.getByRole('button', { name: 'Click when Ready' }).click();
    await Promise.all(pages.map(page => page.waitForURL('**/game?**')));
    await Promise.all(pages.map(page => page.getByRole('button', { name: 'Leave campaign' }).waitFor()));
    await Promise.all(pages.map(page => page.waitForFunction(() => document.body.innerText.includes('The action is complete.') && document.querySelector('.status-completed'))));
    for (const [page, own, other] of [[alice, 'Alice', 'Bob'], [bob, 'Bob', 'Alice']]) {
      await page.locator('.narrative-tabs').getByRole('button', { name: 'Character', exact: true }).click();
      const story = await page.locator('.narrative-content').innerText();
      assert.ok(story.includes(`${own}'s clue.`));
      assert.ok(!story.includes(`${other}'s clue.`));
      assert.ok(!(await page.locator('body').innerText()).includes('Hidden villain'));
      assert.equal(await page.getByRole('button', { name: 'GM Only' }).count(), 0);
    }
    console.log('PASS: lobby-to-game transfer and private narrative filtering in Chrome and Edge');

    await bob.locator('.user-input input').fill('Open the door');
    await bob.getByRole('button', { name: 'Send', exact: true }).click();
    assert.equal(await bob.getByRole('button', { name: 'Waiting...', exact: true }).isDisabled(), true);
    await alice.locator('.user-input input').fill('Examine the room');
    await alice.getByRole('button', { name: 'Send', exact: true }).click();
    await bob.context().setOffline(true);
    await bob.waitForTimeout(1000);
    await bob.context().setOffline(false);
    await bob.waitForFunction(() => document.querySelector('.connection-status')?.innerText.trim() === 'Connected', { timeout: 30000 });
    await Promise.all(pages.map(page => page.waitForFunction(() => {
      const responses = [...document.querySelectorAll('.ai-message')];
      return responses.filter(e => e.innerText.includes('The action is complete.')).length === 3;
    })));
    assert.equal(await bob.locator('.user-message').filter({ hasText: 'Open the door' }).count(), 1);
    assert.equal(await alice.locator('.user-message').filter({ hasText: 'Examine the room' }).count(), 1);
    console.log('PASS: interleaved actions, disabled duplicate controls, and recovery during streaming');

    const before = await alice.locator('.ai-message').count();
    await alice.goto(`${base}/startup`);
    const retained = await (await alice.request.get(`${base}/api/characters/a`)).json();
    assert.equal(retained.campaignId ?? retained.CampaignId, 'campaign');
    await alice.goto(`${base}/lobby?campaignId=campaign&characterId=a`);
    await alice.waitForURL('**/game?**');
    await alice.waitForFunction(count => document.querySelectorAll('.ai-message').length === count, before);
    console.log('PASS: ordinary navigation preserves membership and rejoin does not introduce the AI again');

    for (const page of pages) {
      const scope = 'campaign';
      await page.locator('.user-input input').fill('Unsent thought');
      await page.reload();
      await page.waitForFunction(() => document.querySelector('.connection-status')?.innerText.trim() === 'Connected');
      await page.waitForFunction(() => document.querySelector('.user-input input')?.value === 'Unsent thought');
      await page.locator('.user-input input').fill('');
    }
    console.log('PASS: account/scoped composer text survives refresh in Chrome and Edge');
    await alice.goto(`${base}/campaign-creation?worldOnly=true`);
    await alice.waitForFunction(() => { const button = document.querySelector('.btn-save'); return button && !button.disabled; });
    const resumedUrl = alice.url();
    assert.ok(resumedUrl.includes('sessionId='));
    const messagesBeforeRefresh = await alice.locator('.ai-message').count();
    await alice.locator('.user-input input').fill('Unsent world detail');
    await alice.reload();
    await alice.waitForFunction(() => { const button = document.querySelector('.btn-save'); return button && !button.disabled; });
    assert.equal(alice.url(), resumedUrl);
    assert.equal(await alice.locator('.ai-message').count(), messagesBeforeRefresh);
    await alice.waitForFunction(() => document.querySelector('.user-input input')?.value === 'Unsent world detail');
    const draftId = new URL(resumedUrl).searchParams.get('sessionId');
    const listed = await (await alice.request.get(`${base}/api/sessions`)).json();
    assert.ok(listed.some(d => d.sessionId === draftId));
    await alice.goto(`${base}/startup`);
    const draftCard = alice.locator('.character-card').filter({ hasText: 'Generated playtest world' }).filter({ has: alice.getByRole('button', { name: 'Resume', exact: true }) });
    await draftCard.getByRole('button', { name: 'Resume', exact: true }).click();
    await alice.waitForFunction(() => { const button = document.querySelector('.btn-save'); return button && !button.disabled; });
    assert.equal(new URL(alice.url()).searchParams.get('sessionId'), draftId);
    console.log('PASS: durable world draft discovery and resume preserve draft, transcript, and composer without a second introduction');
    await alice.goto(`${base}/campaign-creation?worldOnly=true`);
    await alice.getByRole('button', { name: 'Save World', exact: true }).waitFor();
    await alice.waitForFunction(() => { const button = document.querySelector('.btn-save'); return button && !button.disabled; });
    const completedWorldUrl = alice.url();
    await alice.getByRole('button', { name: 'Save World', exact: true }).click();
    await alice.waitForURL('**/startup');
    await alice.goto(completedWorldUrl);
    await alice.locator('.creation-progress > summary').click();
    await alice.getByRole('button', { name: 'Continue to saved result' }).waitFor();
    assert.equal(await alice.getByRole('button', { name: 'Save World', exact: true }).isDisabled(), true);
    assert.equal(await alice.locator('.user-input input').isDisabled(), true);
    console.log('PASS: completed creation recovery shows saved result and disables regeneration/finalization');
    await alice.goto(`${base}/campaign-creation?worldOnly=true&worldId=world`);
    await alice.getByRole('button', { name: 'Save Changes', exact: true }).waitFor();
    await alice.waitForFunction(() => { const button = document.querySelector('.btn-save'); return button && !button.disabled; });
    await alice.getByRole('button', { name: 'Save Changes', exact: true }).click();
    await alice.waitForURL('**/startup');
    await alice.goto(`${base}/campaign-creation?worldOnly=false&worldId=world`);
    await alice.getByRole('button', { name: 'Create Campaign', exact: true }).waitFor();
    await alice.waitForFunction(() => { const button = document.querySelector('.btn-save'); return button && !button.disabled; });
    const campaignDraftUrl = alice.url();
    const autosaved = alice.waitForResponse(r => r.url().includes('/campaign-draft') && r.request().method() === 'POST');
    await alice.locator('.campaign-form-section input[type=text]').first().fill('Recovered campaign name');
    assert.equal((await autosaved).status(), 200);
    await alice.reload();
    await alice.getByRole('button', { name: 'Create Campaign', exact: true }).waitFor();
    await alice.waitForFunction(() => document.querySelector('.campaign-form-section input[type=text]')?.value === 'Recovered campaign name');
    assert.equal(alice.url(), campaignDraftUrl);
    await alice.getByRole('button', { name: 'Create Campaign', exact: true }).click();
    await alice.waitForURL('**/startup');
    console.log('PASS: Save World, Save Changes, and Create Campaign through the real UI');

    await alice.goto(`${base}/character-creation`);
    await alice.getByRole('button', { name: 'Finish Character', exact: true }).waitFor();
    await alice.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Finish Character' && !b.disabled));
    const characterResponse = alice.waitForResponse(r => r.url().includes('/finalize-character') && r.request().method() === 'POST');
    await alice.getByRole('button', { name: 'Finish Character', exact: true }).click();
    const characterResult = await (await characterResponse).json();
    await alice.waitForURL('**/startup');
    const savedCharacter = await (await alice.request.get(`${base}/api/characters/${characterResult.characterId ?? characterResult.CharacterId}`)).json();
    assert.ok((savedCharacter.inventory ?? savedCharacter.Inventory).length >= 2);
    assert.equal(savedCharacter.playerId ?? savedCharacter.PlayerId, 'alice');
    console.log('PASS: Finish Character persists ownership and equipment through the real UI');

    for (const page of pages) {
    // Improve-next regressions: orientation, structured intent, stopping, scroll pinning and onboarding.
    await page.goto(`${base}/startup`);
    await page.getByRole('button', { name: 'Continue Adventure', exact: true }).click();
    await page.waitForURL('**/game?**');
    await page.getByRole('button', { name: 'Journal', exact: false }).click();
    assert.ok((await page.locator('body').innerText()).includes('Open the gate'));
    assert.equal(await page.locator('.adventure-overview').count(), 1);
    assert.equal(await page.locator('.action-picker, .context-action').count(), 0);
    await page.locator('.user-input input').fill('Request a party rest.');
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.waitForFunction(() => document.body.innerText.includes('Request a party rest.') && !document.querySelector('.btn-cancel') && !!document.querySelector('.btn-submit'));
    await page.locator('.user-input input').fill('Stop this response');
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.waitForFunction(() => [...document.querySelectorAll('.ai-message')].some(e => e.textContent.includes('Beginning the action.') && e.querySelector('.streaming-cursor')) || document.querySelector('.status-processing'));
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await page.getByRole('button', { name: 'I reviewed the current state', exact: true }).waitFor();
    assert.equal(await page.locator('.user-input input').isEnabled(), true);
    await page.getByRole('button', { name: 'I reviewed the current state', exact: true }).click();
    await page.reload();
    await page.locator('.user-input input').waitFor();
    assert.ok((await page.locator('body').innerText()).includes('Stopped. Applied effects remain'));
    // A shorter chat viewport makes the scroll boundary deterministic on both browser engines.
    await page.locator('.chat-messages').evaluate(el => { el.style.maxHeight = '160px'; el.style.minHeight = '160px'; el.scrollTop = 0; el.dispatchEvent(new Event('scroll')); });
    await page.locator('.user-input input').fill('Scroll regression');
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('button', { name: 'New Messages', exact: false }).waitFor();
    assert.ok(await page.locator('.chat-messages').evaluate(el => el.scrollTop < 5));
    await page.getByRole('button', { name: 'New Messages', exact: false }).click();
    await page.waitForFunction(() => { const el = document.querySelector('.chat-messages'); return el.scrollHeight - el.scrollTop - el.clientHeight < 50; });
    await page.waitForFunction(() => !document.querySelector('.btn-cancel') && !!document.querySelector('.btn-submit'));
    console.log('PASS: overview/journal, free-text Rest request, active cancellation/review/reload and conditional scrolling');
    }

    await alice.goto(`${base}/startup`);
    await alice.getByRole('button', { name: 'Join with Code', exact: true }).click();
    await alice.locator('.adventure-entry input').fill('PLAYTEST');
    await alice.getByRole('button', { name: 'Join Adventure', exact: true }).click();
    await alice.waitForFunction(() => document.body.innerText.includes('Added Recovery playtest'));
    await alice.getByRole('button', { name: 'Start New Adventure', exact: true }).click();
    await alice.waitForURL('**/adventure-setup');
    await alice.getByRole('button', { name: 'Haunted frontier', exact: true }).click();
    await alice.getByRole('button', { name: 'Save World', exact: true }).waitFor();
    const quickUrl = alice.url();
    await alice.reload();
    await alice.waitForFunction(() => { const b = document.querySelector('.btn-save'); return b && !b.disabled; });
    assert.equal(alice.url(), quickUrl);
    assert.equal(await alice.locator('.user-message').count(), 1);
    await alice.getByRole('button', { name: 'Save World', exact: true }).click();
    await alice.waitForURL('**/adventure-setup?worldId=*');
    await alice.getByRole('button', { name: 'Create Campaign', exact: true }).click();
    await alice.waitForFunction(() => { const b = document.querySelector('.btn-save'); return b && !b.disabled; });
    await alice.getByRole('button', { name: 'Create Campaign', exact: true }).click();
    await alice.waitForURL('**/adventure-setup?campaignId=*');
    await alice.getByRole('button', { name: 'Warrior', exact: true }).click();
    await alice.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Finish Character' && !b.disabled));
    await alice.getByRole('button', { name: 'Finish Character', exact: true }).click();
    await alice.waitForURL('**/lobby?**');
    assert.ok((await alice.locator('.setup-progress').innerText()).includes('Ready up'));
    console.log('PASS: Continue/Join and Quick Create with refresh, review/save and world-to-campaign-to-character-to-lobby progress');

    await bob.getByRole('button', { name: 'Leave campaign' }).click();
    await bob.waitForURL('**/startup');
    const withdrawn = await (await bob.request.get(`${base}/api/characters/b`)).json();
    assert.equal(withdrawn.campaignId ?? withdrawn.CampaignId, '');
    for (let i = 0; i < pages.length; i++) await checkCreationStyles(pages[i], base, i ? 'edge' : 'chrome');
    assert.deepEqual(errors, []);
    console.log('PASS: explicit withdrawal persists character unassignment; no browser exceptions');
  } catch (error) {
    for (let i = 0; i < pages.length; i++) {
      console.error(`Browser ${i}: ${await pages[i].locator('body').innerText()}`);
      await pages[i].screenshot({ path: path.join(__dirname, `obj/playtest-failure-${i}.png`) });
    }
    throw error;
  } finally {
    await Promise.all(browsers.map(browser => browser.close()));
    fs.rmSync(signal, { force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
