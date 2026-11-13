import { Page, expect } from '@playwright/test';

export const VIEWPORTS = [
  { name: '1440x900', width: 1440, height: 900 },
  { name: '1280x800', width: 1280, height: 800 },
  { name: '1920x1080', width: 1920, height: 1080 },
];

/** Neutralise everything that is not design: caret, spinner, animations. */
export async function stabilise(page: Page) {
  await page.addStyleTag({
    content: `*{caret-color:transparent!important;animation:none!important;transition:none!important}
              [data-no-i18n]{visibility:hidden!important}`, // demo bar etc. — not part of the product
  });
  await page.evaluate(() => (document as any).fonts.ready);
}

export const btn = (page: Page, name: RegExp | string) => page.getByRole('button', { name }).first();
export const tab = (page: Page, name: string) => page.getByRole('link', { name }).or(page.getByRole('button', { name })).first();

/** Demo-bar control: hidden by stabilise() (visibility:hidden), so it is clicked by event, not by pointer. */
export const demo = (page: Page, name: RegExp) =>
  page.locator('[data-no-i18n] button').filter({ hasText: name }).first().dispatchEvent('click');

/**
 * Approved deviation D5 (PARITY.md): Oggi shows a case step only inside the company's lead time
 * (Settings → Company rules; default 120 days), the prototype shows every case. A fixture reset empties
 * the company rules, so on the app the run sets the lead time to its maximum (365 days) after the reset:
 * the app then lists what the prototype lists. No-op on the prototype (no APP_URL, no such API).
 */
export async function parityLeadTime(page: Page) {
  if (!process.env.APP_URL) return;
  const api = process.env.API_URL ?? 'http://localhost:5180';
  const headers = { 'X-Raffa-Fixture-User': 'luca', 'X-Tenant-Id': '' };
  const session = await page.request.get(`${api}/api/fixtures/session`);
  headers['X-Tenant-Id'] = (await session.json()).tenantId;
  const r = await page.request.put(`${api}/api/company-rules`, { headers, data: { text: 'Parity run: widest lead time.', startWindowDays: 365 } });
  expect(r.ok(), 'lead time set for the parity run').toBeTruthy();
  // The screen shown after the reset keeps its feed: leave it, so the next visit reads the new lead time.
  await page.getByRole('link', { name: 'Rinnovi' }).first().click();
  await page.waitForTimeout(500);
}

export type Step = { shot: string; run: (p: Page) => Promise<void> };

/** The golden click-path = the screens in /screenshots. IT, scenario "Da zero" then "Con dati". Same order and names. */
export const FLOW_ZERO: Step[] = [
  { shot: '01-oggi-punto-zero', run: async () => {} },
  { shot: '02-lettura-documento', run: async p => { await btn(p, /Carica il primo contratto/).click(); } },
  { shot: '03-revisione-campo-1', run: async p => { await expect(btn(p, /Accetta: è corretto/)).toBeVisible({ timeout: 8000 }); } },
  { shot: '04-revisione-campo-2', run: async p => { await btn(p, /Accetta: è corretto/).click(); } },
  { shot: '05-contratto-validato', run: async p => { await btn(p, /Accetta: è corretto/).click(); } },
  // Approved deviation D11 (PARITY.md): a validated contract no longer offers "Torna a Oggi" (it moves on
  // to the next contract, or back to Contratti), so the path reaches Oggi through its tab, on the prototype too.
  { shot: '06-oggi-focus-card', run: async p => { await tab(p, 'Oggi').click(); } },
  { shot: '07-rinnovo-intervista', run: async p => { await btn(p, /Me ne occupo io/).click(); } },
  { shot: '08-rinnovo-scheda-aperta', run: async p => { await btn(p, /^Scheda:/).click(); } },
  { shot: '09-rinnovi-lista', run: async p => { await tab(p, 'Rinnovi').click(); } },
  { shot: '10-contratti-lista', run: async p => { await tab(p, 'Contratti').click(); } },
  { shot: '11-contratto-decisioni', run: async p => { await p.locator('[data-screen-label="Contratti"] .r-row, [data-screen-label="Contratti"] a').first().click(); } },
  { shot: '12-contratto-completo', run: async p => { await btn(p, /^Tutto il contratto/).click(); } },
  // Approved deviation D10 (PARITY.md): the Risultati tab is Spesa e risparmi, on the prototype too (deviations.ts).
  { shot: '13-risultati-senza-obiettivo', run: async p => { await tab(p, 'Spesa e risparmi').click(); } },
  { shot: '14-risultati-obiettivo-bozza', run: async p => { await btn(p, /Imposta un obiettivo/).click(); await btn(p, /^Voglio risparmiare il 20%/).click(); } },
  { shot: '15-risultati-obiettivo-attivo', run: async p => { await btn(p, /Sì, è questo/).click(); } },
  { shot: '16-chiedi-conversazioni', run: async p => { await tab(p, 'Chiedi').click(); } },
  { shot: '17-chiedi-archiviata', run: async p => { await btn(p, /^Archiviate/).click(); await btn(p, /Zoom · esito/).click(); } },
];

export const FLOW_FULL: Step[] = [
  { shot: '18-oggi-con-dati', run: async p => { await demo(p, /^Con dati$/); await p.waitForTimeout(700); await parityLeadTime(p); await tab(p, 'Oggi').click(); } },
  { shot: '19-rinnovi-con-dati', run: async p => { await tab(p, 'Rinnovi').click(); } },
  { shot: '20-rinnovo-docusign', run: async p => { await tab(p, 'Oggi').click(); await btn(p, /Fai ora/).click(); } },
  { shot: '21-rinnovo-panoramica', run: async p => { await btn(p, /Rivedi la panoramica/).click(); } },
  { shot: '22-offerta-verifica', run: async p => { await tab(p, 'Chiedi').click(); await btn(p, /Confronta un/).click(); await btn(p, /Databricks/).click(); } },
  { shot: '23-offerta-confronto', run: async p => { await btn(p, /Conferma i dati letti/).click(); } },
  { shot: '24-offerta-decisione', run: async p => { await btn(p, /Vai alla decisione/).click(); } },
  { shot: '25-oggi-en', run: async p => { await demo(p, /^EN$/); await tab(p, 'Today').click(); } },
  { shot: '26-oggi-guida-chiusa', run: async p => { await demo(p, /^IT$/); await p.locator('[data-screen-label="Guida Raffa"] >> text=Nascondi').click(); await tab(p, 'Oggi').click(); } },
];
