import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// vite.config.ts sets `test.css: false`, so component tests under jsdom never apply the real
// cascade -- there is no computed style to assert against at render time (see
// web/tests/styles/layout.test.ts's own header comment). This suite proves the V2 CSS source
// itself, the same convention V1's own documents.css.test.ts / signin.css.test.ts already set.
function readSource(relativePath: string): string {
  const raw = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8");
  return raw.replace(/\/\*[\s\S]*?\*\//g, "");
}

function ruleBodyFor(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`));
  if (!match) {
    throw new Error(`No "${selector} { ... }" rule found.`);
  }
  return match[1];
}

describe("documents.css (task E13/F09/US01/T03, web-documents-v2)", () => {
  const css = readSource("../../../src/routes/documents/documents.css");

  it("onboarding headline uses the heading font/weight at the export's clamp size", () => {
    const body = ruleBodyFor(css, ".documents-onboarding-headline");
    expect(body).toMatch(/font-family:\s*var\(--font-heading\)/);
    expect(body).toMatch(/font-size:\s*clamp\(30px,\s*3\.4vw,\s*46px\)/);
  });

  it("filenames wrap at word/character-run boundaries, not one glyph per line", () => {
    expect(ruleBodyFor(css, ".upload-result-filename")).toMatch(/overflow-wrap:\s*anywhere/);
    expect(ruleBodyFor(css, ".document-status-table-filename")).toMatch(/overflow-wrap:\s*anywhere/);
    expect(ruleBodyFor(css, ".document-status-table-link")).toMatch(/overflow-wrap:\s*anywhere/);
  });

  it("dropzone is dashed, and the onboarding/list variants each carry their own density", () => {
    expect(ruleBodyFor(css, ".upload-dropzone")).toMatch(/border:\s*2px dashed var\(--color-divider\)/);
    expect(ruleBodyFor(css, ".upload-dropzone--onboarding")).toMatch(/padding:\s*22px 24px/);
    expect(ruleBodyFor(css, ".upload-dropzone--list")).toMatch(/padding:\s*14px 18px/);
  });

  it("does not reintroduce a boxed .card on the 'Not added' card (ADR-019: recommendation/provenance only)", () => {
    const body = ruleBodyFor(css, ".upload-result-card");
    expect(body).not.toMatch(/\bbackground\b|\bbox-shadow\b/);
  });

  it("the row progress bar is a bare 4px fill, no label baked into the bar itself", () => {
    expect(ruleBodyFor(css, ".document-row-progress")).toMatch(/height:\s*4px/);
    expect(ruleBodyFor(css, ".document-row-progress-fill")).toMatch(/background:\s*var\(--color-accent\)/);
  });

  it("the row grid's Next-step column is right-aligned, matching the export's own flex-end action cell", () => {
    const rule = css.match(/\.document-status-table th:nth-child\(4\),\s*\.document-status-table td:nth-child\(4\)\s*\{([^}]*)\}/);
    expect(rule).not.toBeNull();
    expect(rule![1]).toMatch(/text-align:\s*right/);
  });
});
