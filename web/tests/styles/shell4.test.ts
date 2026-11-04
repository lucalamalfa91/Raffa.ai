import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

/** AskRaffa 4.0 shell CSS carries the prototype's values (AskRaffa V4.dc.html lines 26-47, 309-318). */
function source(relativePath: string): string {
  return readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8").replace(/\/\*[\s\S]*?\*\//g, "");
}
function rule(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = css.match(new RegExp(`(?:^|\\n)\\s*${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) throw new Error(`No "${selector} { ... }" rule found.`);
  return match[1];
}

describe("shell4.css", () => {
  const css = source("../../src/components/shell/shell4.css");

  it("nav: 2px ink bottom rule, 40px gutters; active tab = 3px accent bottom border", () => {
    expect(rule(css, ".v4-nav")).toMatch(/border-bottom:\s*2px solid var\(--color-text\)/);
    expect(rule(css, ".v4-nav")).toMatch(/padding:\s*0 40px/);
    expect(rule(css, ".v4 .v4-tab")).toMatch(/border-bottom:\s*3px solid transparent/);
    expect(rule(css, ".v4 .v4-tab.is-active")).toMatch(/border-bottom-color:\s*var\(--color-accent\)/);
  });

  it("main grid: minmax(0,1fr) 300px with the guide, minmax(0,1fr) without", () => {
    expect(rule(css, ".v4-body")).toMatch(/grid-template-columns:\s*minmax\(0, 1fr\) 300px/);
    expect(rule(css, ".v4-body.is-guide-closed")).toMatch(/grid-template-columns:\s*minmax\(0, 1fr\);/);
  });

  it("guide: ink ground, 3px accent top border, 18/20/24 padding", () => {
    const guide = rule(css, ".v4-guide");
    expect(guide).toMatch(/background:\s*var\(--color-text\)/);
    expect(guide).toMatch(/border-top:\s*3px solid var\(--color-accent\)/);
    expect(guide).toMatch(/padding:\s*18px 20px 24px/);
  });
});

describe("ds-modernist.css", () => {
  const css = source("../../src/styles/ds-modernist.css");

  it("v4 root: white ground, #f3f2f2 surface, 100vw/100vh column, 14px/1.5 body", () => {
    const root = rule(css, ".v4");
    expect(root).toMatch(/--color-bg:\s*#ffffff/);
    expect(root).toMatch(/--color-surface:\s*#f3f2f2/);
    expect(root).toMatch(/width:\s*100vw/);
    expect(root).toMatch(/height:\s*100vh/);
    expect(root).toMatch(/font-size:\s*15px/);
    expect(root).toMatch(/line-height:\s*1\.5/);
  });

  it("buttons are the DS ones: 1px border, centred, title weight (D8), square", () => {
    const btn = rule(css, ".v4 .btn");
    expect(btn).toMatch(/border:\s*1px solid transparent/);
    expect(btn).toMatch(/justify-content:\s*center/);
    expect(btn).toMatch(/font-weight:\s*var\(--weight-title\)/);
    expect(btn).toMatch(/border-radius:\s*0/);
  });

  it("is imported after the older component sheet so it wins inside .v4", () => {
    const index = source("../../src/styles/index.css");
    expect(index.indexOf("ds-modernist.css")).toBeGreaterThan(index.indexOf("components.css"));
  });
});
