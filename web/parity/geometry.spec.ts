import { test, expect } from '@playwright/test';
import { tab, btn, stabilise } from './flow';

/** Computed-style assertions: values screenshots cannot prove. All numbers come from README + STYLE-INVENTORY.md. */
const URL = process.env.APP_URL!;
const css = (loc: any, prop: string) => loc.evaluate((el: Element, p: string) => getComputedStyle(el).getPropertyValue(p), prop);

test.use({ viewport: { width: 1440, height: 900 }, locale: 'it-IT' });

test('shell', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  const nav = page.locator('header, [role="banner"]').first();
  expect(await css(nav, 'border-bottom-width')).toBe('2px');
  expect(await css(nav, 'border-bottom-color')).toBe('rgb(32, 30, 29)');
  const guide = page.locator('[data-screen-label="Guida Raffa"]');
  expect(Math.round((await guide.boundingBox())!.width)).toBe(300);
  expect(await css(guide, 'background-color')).toBe('rgb(32, 30, 29)');
  expect(await css(guide, 'border-top-width')).toBe('3px');
  expect(await css(page.locator('body'), 'font-family')).toContain('Archivo');
});

test('oggi focus card', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  const card = page.locator('[data-screen-label="Focus"]');
  expect(await css(card, 'border-top-width')).toBe('2px');
  expect(await css(card, 'padding-top')).toBe('28px');
  expect(await css(card, 'padding-left')).toBe('32px');
  expect(await css(card, 'background-color')).toBe('rgb(255, 255, 255)');
  // never more than 3 buttons in the card
  expect(await card.getByRole('button').count()).toBeLessThanOrEqual(3);
});

test('square corners everywhere', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  const radii = await page.evaluate(() => [...document.querySelectorAll('*')].map(e => getComputedStyle(e).borderTopLeftRadius).filter(r => r !== '0px'));
  expect(radii).toEqual([]);
});

test('case step: at most 3 visible buttons, rest under "Altro"', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  await tab(page, 'Oggi').click(); await btn(page, /Fai ora/).click();
  const step = page.locator('[data-screen-label="Passo corrente"]');
  const visible = await step.getByRole('button').filter({ hasNotText: /Altro|Meno/ }).count();
  expect(visible).toBeLessThanOrEqual(3);
});

test('chiedi: both headers share one 56px line', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  await tab(page, 'Chiedi').click();
  const left = page.locator('[data-screen-label="Conversazioni"] > div').first();
  const right = page.locator('[data-screen-label="Chiedi"] > div').last().locator('> div').first();
  const [a, b] = [await left.boundingBox(), await right.boundingBox()];
  expect(Math.round(a!.height)).toBe(56); expect(Math.round(b!.height)).toBe(56);
  expect(Math.round(a!.y)).toBe(Math.round(b!.y));
});

test('risultati: goal bar collapsed by default, slim', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  await tab(page, 'Risultati').click();
  const bar = page.locator('[data-screen-label="Obiettivo"]');
  expect((await bar.boundingBox())!.height).toBeLessThan(60);
  await expect(bar.getByRole('textbox')).toHaveCount(0);
});

test('no market benchmark / no estimates anywhere', async ({ page }) => {
  await page.goto(`${URL}/?now=2026-09-08&fixture=full`); await stabilise(page);
  for (const name of ['Oggi', 'Rinnovi', 'Contratti', 'Risultati', 'Chiedi']) {
    await tab(page, name).click();
    const text = (await page.locator('body').innerText()).toLowerCase();
    expect(text).not.toMatch(/benchmark di mercato attivo|sopra mercato|sotto mercato|mediana|p25|p75|risparmio stimato/);
  }
});
