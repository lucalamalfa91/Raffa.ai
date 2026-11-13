import { Page, expect } from '@playwright/test';

/**
 * Approved deviation D9 (PARITY.md): Contratti, Rinnovi, Risultati and the Chiedi intents changed
 * structure (data tables, one KPI tile grid, evenly padded tiles), so the prototype's pixels no longer
 * describe them. On these shots the run asserts the rules D9 exists for instead of comparing pixels:
 * every cell of a table column starts (and ends) on the same x, the KPI tiles of one row put their
 * values on one line, bar rows start and end on the same x, no text is cut and nothing scrolls sideways.
 * Every other shot is still compared with the prototype pixel for pixel.
 */
export const D9_SHOTS: ReadonlySet<string> = new Set([
  '09-rinnovi-lista',
  '10-contratti-lista',
  '13-risultati-senza-obiettivo',
  '14-risultati-obiettivo-bozza',
  '15-risultati-obiettivo-attivo',
  '16-chiedi-conversazioni',
  '19-rinnovi-con-dati',
]);

/**
 * Approved deviation D11 (PARITY.md): Revisione puts the contract page beside the field, explains how
 * the value was read, and once validated moves on to the next contract instead of offering "Torna a
 * Oggi". The prototype's pixels no longer describe these shots either, so they get the same layout
 * rules (no sideways scroll, no cut text) instead of a pixel comparison.
 */
export const D11_SHOTS: ReadonlySet<string> = new Set([
  '03-revisione-campo-1',
  '04-revisione-campo-2',
  '05-contratto-validato',
]);

/** The layout faults on the screen in view; empty when the D9 rules hold. */
export async function alignmentFaults(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const faults: string[] = [];
    const round = (n: number) => Math.round(n);
    const doc = document.documentElement;
    if (doc.scrollWidth > doc.clientWidth + 1) faults.push(`the page scrolls sideways (${doc.scrollWidth} > ${doc.clientWidth})`);
    const main = document.querySelector('.v4-main');
    if (main && main.scrollWidth > main.clientWidth + 1) faults.push(`the main area overflows (${main.scrollWidth} > ${main.clientWidth})`);

    // Tables: one left and one right edge per column (a stacked, narrow table has no columns to check).
    document.querySelectorAll('.dt').forEach((table, t) => {
      const rows = [...table.querySelectorAll<HTMLElement>('.dt-row')].filter((r) => getComputedStyle(r).display !== 'none');
      if (rows.length < 2 || getComputedStyle(rows[rows.length - 1]).gridTemplateColumns.split(' ').length <= 2) return;
      const columns = new Map<number, { left: Set<number>; right: Set<number> }>();
      for (const row of rows) {
        [...row.children].forEach((cell, i) => {
          if (cell.classList.contains('dt-span') || (cell.classList.contains('dt-end') && !cell.textContent?.trim())) return;
          const box = cell.getBoundingClientRect();
          if (box.width === 0 && box.height === 0) return; // hidden in this run (e.g. [data-parity-hide])
          const c = columns.get(i) ?? { left: new Set<number>(), right: new Set<number>() };
          c.left.add(round(box.left));
          c.right.add(round(box.right));
          columns.set(i, c);
        });
      }
      columns.forEach((c, i) => {
        if (c.left.size > 1) faults.push(`table ${t}, column ${i}: cells start at ${[...c.left].join(', ')}`);
        if (c.right.size > 1) faults.push(`table ${t}, column ${i}: cells end at ${[...c.right].join(', ')}`);
      });
    });

    // KPI tiles: the values (and the notes) of one row of tiles share one top.
    for (const selector of ['.s4-num-v', '.s4-kpi-v', '.s4-kpi-sub']) {
      const byRow = new Map<number, Set<number>>();
      document.querySelectorAll<HTMLElement>(selector).forEach((el) => {
        const tile = el.closest('.s4-num, .s4-kpi');
        if (!tile) return;
        const row = round(tile.getBoundingClientRect().top);
        const tops = byRow.get(row) ?? new Set<number>();
        tops.add(round(el.getBoundingClientRect().top));
        byRow.set(row, tops);
      });
      byRow.forEach((tops) => {
        if (tops.size > 1) faults.push(`${selector}: one row of tiles puts its values at ${[...tops].join(', ')}`);
      });
    }

    // Bar rows: every bar of a chart starts and ends on the same x.
    for (const selector of ['.s4-by .s4-stack', '.s4-lever .s4-bar', '.s4-proof .s4-bar']) {
      const bars = [...document.querySelectorAll(selector)].map((el) => el.getBoundingClientRect());
      if (bars.length < 2) continue;
      if (new Set(bars.map((b) => round(b.left))).size > 1 || new Set(bars.map((b) => round(b.right))).size > 1) {
        faults.push(`${selector}: bars do not share their start and end`);
      }
    }

    // Cut text: a leaf element that hides text overflowing it (screen-reader-only labels excepted).
    document.querySelectorAll<HTMLElement>('.v4-main *').forEach((el) => {
      if (el.children.length > 0 || !el.textContent?.trim() || el.matches('.dt-label, .visually-hidden')) return;
      const style = getComputedStyle(el);
      if ((style.overflow === 'hidden' || style.overflowX === 'hidden') && el.scrollWidth > el.clientWidth + 1) {
        faults.push(`text cut: "${el.textContent.trim().slice(0, 40)}"`);
      }
    });
    return faults;
  });
}

/** Soft-asserts the D9 rules on the screen in view, so the report lists every fault of the run. */
export async function expectAligned(page: Page, shot: string) {
  expect.soft(await alignmentFaults(page), `${shot}: D9 layout rules`).toEqual([]);
}
