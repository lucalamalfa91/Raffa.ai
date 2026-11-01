/**
 * Approved deviations from the reference prototype (PARITY.md "Approved deviations").
 * The product owner asked for them after the 4.0 release; the app implements them in its CSS, and
 * the goldens are captured from the prototype with the SAME rules applied, so parity still proves
 * "prototype + approved deviations", pixel for pixel:
 *   D1  body text in Inter (headings stay Archivo 800)            → --font-body
 *   D2  every font size up to 14px is one pixel larger            → inline styles + stylesheet rules
 *   D3  no max-width at or above 900px: screens use the window    → inline styles + stylesheet rules
 * Runs in the page (addInitScript) and keeps re-applying as the prototype re-renders.
 */
export const APPROVED_DEVIATIONS = `(() => {
  const FONT = '"Inter", system-ui, sans-serif';
  const fix = (style, mark) => {
    const fs = style.getPropertyValue('font-size');
    const m = /^(\\d+(?:\\.\\d+)?)px$/.exec(fs.trim());
    if (m && mark.fs !== fs) {
      const v = parseFloat(m[1]);
      if (v <= 14) { const next = (v + 1) + 'px'; style.setProperty('font-size', next, style.getPropertyPriority('font-size')); mark.fs = next; }
    }
    const mw = /^(\\d+)px$/.exec(style.getPropertyValue('max-width').trim());
    if (mw && parseInt(mw[1], 10) >= 900) style.setProperty('max-width', 'none', style.getPropertyPriority('max-width'));
  };
  const marks = new WeakMap();
  const markOf = (k) => { let m = marks.get(k); if (!m) { m = {}; marks.set(k, m); } return m; };
  const doneSheets = new WeakSet();
  const walkRules = (rules) => { for (const r of rules) { if (r.style) fix(r.style, markOf(r)); if (r.cssRules) walkRules(r.cssRules); } };
  const apply = () => {
    for (const sheet of document.styleSheets) {
      if (doneSheets.has(sheet)) continue;
      try { if (sheet.cssRules.length) { walkRules(sheet.cssRules); doneSheets.add(sheet); } } catch (e) { /* cross-origin (fonts) */ }
    }
    document.querySelectorAll('[style]').forEach((el) => {
      const m = markOf(el);
      if (m.fs && el.style.fontSize !== m.fs) m.fs = undefined; // re-rendered: original value is back
      fix(el.style, m);
    });
  };
  const start = () => {
    const st = document.createElement('style');
    st.textContent = "@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap'); :root{--font-body:" + FONT + " !important}";
    document.head.appendChild(st);
    let queued = false;
    new MutationObserver(() => { if (!queued) { queued = true; queueMicrotask(() => { queued = false; apply(); }); } })
      .observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['style'] });
    apply();
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
  window.addEventListener('load', apply);
})();`;
