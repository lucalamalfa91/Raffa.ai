import { test, Page } from '@playwright/test';
import { FLOW_FULL, FLOW_ZERO, VIEWPORTS, stabilise } from './flow';

/** Captures the golden screenshots FROM THE REFERENCE PROTOTYPE. Run once, commit the PNGs. */
const URL = process.env.REFERENCE_URL!; // e.g. http://localhost:4173/AskRaffa%20V4.dc.html

for (const vp of VIEWPORTS) {
  test(`reference goldens @ ${vp.name}`, async ({ browser }) => {
    const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, locale: 'it-IT', reducedMotion: 'reduce' });
    const page: Page = await ctx.newPage();
    await page.addInitScript(() => { localStorage.setItem('raffa4.mode', 'zero'); localStorage.setItem('raffa4.lang', 'it'); });
    await page.goto(URL);
    await stabilise(page);
    for (const s of [...FLOW_ZERO, ...FLOW_FULL]) {
      await s.run(page);
      await page.waitForTimeout(700);
      await stabilise(page);
      await page.screenshot({ path: `goldens/${vp.name}/${s.shot}.png`, clip: { x: 0, y: 36, width: vp.width, height: vp.height - 36 }, animations: 'disabled' });
    }
    await ctx.close();
  });
}
