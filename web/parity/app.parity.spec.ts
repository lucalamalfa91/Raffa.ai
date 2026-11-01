import { test, expect, Page } from '@playwright/test';
import { FLOW_FULL, FLOW_ZERO, VIEWPORTS, stabilise } from './flow';

/** Runs the same click-path on the REAL app and compares with the goldens captured from the prototype. */
const URL = process.env.APP_URL!;

for (const vp of VIEWPORTS) {
  test(`app parity @ ${vp.name}`, async ({ browser }) => {
    const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, locale: 'it-IT', reducedMotion: 'reduce' });
    const page: Page = await ctx.newPage();
    await page.addInitScript(() => { localStorage.setItem('raffa4.lang', 'it'); });
    await page.goto(`${URL}/?now=2026-09-08&fixture=zero`); // the app must honour both
    await stabilise(page);
    for (const s of [...FLOW_ZERO, ...FLOW_FULL]) {
      await s.run(page);
      await page.waitForTimeout(700);
      await stabilise(page);
      await expect(page).toHaveScreenshot(`${vp.name}/${s.shot}.png`, {
        clip: { x: 0, y: 36, width: vp.width, height: vp.height - 36 }, // same clip as the goldens (demo bar excluded)
        maxDiffPixelRatio: 0.002, threshold: 0.1, animations: 'disabled',
      });
    }
    await ctx.close();
  });
}
// Snapshots dir must point at ../goldens: set `snapshotPathTemplate: '{testDir}/../goldens/{arg}{ext}'` in playwright.config.ts.
