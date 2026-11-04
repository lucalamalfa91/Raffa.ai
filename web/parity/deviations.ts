/**
 * Approved deviations from the reference prototype (PARITY.md "Approved deviations").
 * The product owner asked for them after the 4.0 release; the app implements them in its CSS, and
 * the goldens are captured from the prototype with the SAME rules applied, so parity still proves
 * "prototype + approved deviations", pixel for pixel:
 *   D1  body text in Inter — superseded by D8                    → (none)
 *   D2  every font size up to 14px is one pixel larger            → inline styles + stylesheet rules
 *   D3  no max-width at or above 900px: screens use the window    → inline styles + stylesheet rules
 *   D8  IBM Plex Sans for headings and body; weight 800 becomes   → --font-heading / --font-body, and
 *       700 at 18px and up, 600 below (as the app's tokens)          inline styles + stylesheet rules
 *   D10 the Risultati tab is Spesa e risparmi (EN Spend and      → the nav tab's label
 *       savings)
 * D9 (data tables, KPI tile grid, page header, CHF format) changes the screens' structure, which no
 * style rule can reproduce on the prototype: see PARITY.md for how those screens are compared.
 * Runs in the page (addInitScript) and keeps re-applying as the prototype re-renders.
 */
export const APPROVED_DEVIATIONS = `(() => {
  const FONT = '"IBM Plex Sans", "Helvetica Neue", Arial, system-ui, sans-serif';
  const fix = (style, mark) => {
    const fs = style.getPropertyValue('font-size');
    const m = /^(\\d+(?:\\.\\d+)?)px$/.exec(fs.trim());
    if (m && mark.fs !== fs) {
      const v = parseFloat(m[1]);
      if (v <= 14) { const next = (v + 1) + 'px'; style.setProperty('font-size', next, style.getPropertyPriority('font-size')); mark.fs = next; }
    }
    // D8: Archivo 800 → IBM Plex Sans 700 (18px and up, or a heading rule without a size) / 600.
    const fw = style.getPropertyValue('font-weight').trim();
    if ((fw === '800' || fw === 'var(--font-heading-weight)') && !mark.fw) {
      const size = /^(\\d+(?:\\.\\d+)?)px$/.exec(style.getPropertyValue('font-size').trim());
      const heading = size ? parseFloat(size[1]) >= 18 : /(^|[\\s,])h[1-4]\\b/.test(mark.sel || '');
      style.setProperty('font-weight', heading ? '700' : '600', style.getPropertyPriority('font-weight'));
      mark.fw = true;
    }
    const mw = /^(\\d+)px$/.exec(style.getPropertyValue('max-width').trim());
    if (mw && parseInt(mw[1], 10) >= 900) style.setProperty('max-width', 'none', style.getPropertyPriority('max-width'));
  };
  // D10: the nav tab (a button with a 3px bottom bar: its label, then the badge) is renamed; the
  // screen's own heading is not touched (Risultati is a D9 screen, compared by its layout rules).
  const RENAMED = { 'Risultati': 'Spesa e risparmi', 'Results': 'Spend and savings' };
  const renameTabs = () => {
    document.querySelectorAll('button').forEach((b) => {
      if (b.style.borderBottomWidth !== '3px' || !b.firstChild) return;
      const first = b.firstChild;
      const text = first.nodeType === 3 ? first : first.childNodes.length === 1 && first.firstChild.nodeType === 3 ? first.firstChild : null;
      const next = text && RENAMED[text.data.trim()];
      if (next) text.data = next;
    });
  };
  const marks = new WeakMap();
  const markOf = (k) => { let m = marks.get(k); if (!m) { m = {}; marks.set(k, m); } return m; };
  const doneSheets = new WeakSet();
  const walkRules = (rules) => { for (const r of rules) { if (r.style) { const m = markOf(r); m.sel = r.selectorText; fix(r.style, m); } if (r.cssRules) walkRules(r.cssRules); } };
  const apply = () => {
    for (const sheet of document.styleSheets) {
      if (doneSheets.has(sheet)) continue;
      try { if (sheet.cssRules.length) { walkRules(sheet.cssRules); doneSheets.add(sheet); } } catch (e) { /* cross-origin (fonts) */ }
    }
    document.querySelectorAll('[style]').forEach((el) => {
      const m = markOf(el);
      if (m.fs && el.style.fontSize !== m.fs) m.fs = undefined; // re-rendered: original value is back
      if (m.fw && el.style.fontWeight === '800') m.fw = undefined;
      fix(el.style, m);
    });
    renameTabs();
  };
  const start = () => {
    const st = document.createElement('style');
    st.textContent = "@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:ital,wght@0,400;0,500;0,600;0,700;1,400&display=swap'); :root{--font-heading:" + FONT + " !important;--font-body:" + FONT + " !important}";
    document.head.appendChild(st);
    let queued = false;
    new MutationObserver(() => { if (!queued) { queued = true; queueMicrotask(() => { queued = false; apply(); }); } })
      .observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['style'] });
    apply();
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
  window.addEventListener('load', apply);
})();`;
