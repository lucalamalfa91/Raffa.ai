import { test, expect, Page } from '@playwright/test';
import { FLOW_FULL, FLOW_ZERO, VIEWPORTS, stabilise } from './flow';
import { D9_SHOTS, D11_SHOTS, expectAligned } from './alignment';

/** Runs the same click-path on the REAL app and compares with the goldens captured from the prototype. */
const URL = process.env.APP_URL!;

for (const vp of VIEWPORTS) {
  test(`app parity @ ${vp.name}`, async ({ browser }) => {
    const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, locale: 'it-IT', reducedMotion: 'reduce' });
    const page: Page = await ctx.newPage();
    await page.addInitScript(() => { localStorage.setItem('raffa4.lang', 'it'); sessionStorage.setItem('raffa4.parity', '1'); });
    await page.goto(`${URL}/today?now=2026-09-08&fixture=zero`); // fixture mode: scenario 'Da zero', scenario clock
    await stabilise(page);
    // Approved deviations D4 and D6 (PARITY.md): the Settings tab and the language switch + account menu
    // are not in the prototype; the run hides them so the rest of the nav still compares pixel for pixel.
    await page.addStyleTag({ content: '[data-tab="settings"], [data-parity-hide], .p4-email-card, .p4-split > .artifact-panel{display:none!important} .p4-email-inline{display:block!important}' });
    for (const s of [...FLOW_ZERO, ...FLOW_FULL]) {
      await s.run(page);
      await page.waitForTimeout(700);
      await stabilise(page);
      // Approved deviation D9 (PARITY.md): these screens changed structure, so the run checks the
      // layout rules D9 exists for (alignment.ts) instead of the prototype's pixels. Same for D11 (Revisione).
      if (D9_SHOTS.has(s.shot) || D11_SHOTS.has(s.shot)) {
        await expectAligned(page, `${vp.name}/${s.shot}`);
        continue;
      }
      // Soft: every shot is compared and reported, a mismatch does not stop the click-path.
      await expect.soft(page).toHaveScreenshot([vp.name, `${s.shot}.png`], {
        clip: { x: 0, y: 36, width: vp.width, height: vp.height - 36 }, // same clip as the goldens (demo bar excluded)
        maxDiffPixelRatio: 0.002, threshold: 0.1, animations: 'disabled',
      });
    }
    await ctx.close();
  });
}
