import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * V2 Portfolio CSS (`contigo-v2/markup.html` PORTFOLIO block: the table's own inline instance
 * values and column widths) plus the shared V2 screen chrome added to components.css for every
 * module screen. Same "read the stylesheet, assert the rule body" shape the other *.css.test.ts
 * files in this repo use -- these are the values a reviewer would otherwise measure by eye.
 */
function readCss(relativePath: string): string {
  return readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), "utf-8");
}

/** The declaration block of the first rule whose selector list equals `selector` exactly. */
function ruleBodyFor(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = new RegExp(`(?:^|\\n)${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  if (match === null) throw new Error(`No rule for ${selector}`);
  return match[1];
}

const contractsCss = readCss("../../../src/routes/contracts/contracts.css");
const componentsCss = readCss("../../../src/styles/components.css");

describe("contracts.css (V2 Portfolio table vs markup.html's own inline instance values)", () => {
  it("gives the table the export's instance values: 13px, tabular-nums, 760px minimum", () => {
    const body = ruleBodyFor(contractsCss, ".portfolio-table");
    expect(body).toMatch(/min-width:\s*760px/);
    expect(body).toMatch(/font-size:\s*13px/);
    expect(body).toMatch(/font-variant-numeric:\s*tabular-nums/);
  });

  it("pins the export's column widths: Supplier 14%, Annual spend 13%, Ends 12%, Give notice by 14%, Start 10%, Auto/Risk 8%, Status 12%", () => {
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-supplier")).toMatch(/width:\s*14%/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-spend")).toMatch(/width:\s*13%/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-ends")).toMatch(/width:\s*12%/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-notice")).toMatch(/width:\s*14%/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-start")).toMatch(/width:\s*10%/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-auto,\n.portfolio-table .portfolio-col-risk")).toMatch(/width:\s*8%/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table .portfolio-col-status")).toMatch(/width:\s*12%/);
  });

  it("makes rows read as clickable (cg-row hover tint) and keeps the supplier cell bold with the reserved 10px bar padding", () => {
    expect(ruleBodyFor(contractsCss, ".portfolio-table tbody tr.portfolio-row")).toMatch(/cursor:\s*pointer/);
    expect(ruleBodyFor(contractsCss, ".portfolio-table tbody tr.portfolio-row:hover")).toMatch(/background:\s*var\(--color-neutral-200\)/);
    const supplier = ruleBodyFor(contractsCss, ".portfolio-cell-supplier");
    expect(supplier).toMatch(/font-weight:\s*var\(--weight-body-strong\)/);
    expect(supplier).toMatch(/padding-left:\s*10px/);
  });

  it("truncates the contract cell with an ellipsis and keeps the day count at 11px, as the export does", () => {
    const contract = ruleBodyFor(contractsCss, ".portfolio-cell-contract");
    expect(contract).toMatch(/text-overflow:\s*ellipsis/);
    expect(contract).toMatch(/white-space:\s*nowrap/);
    expect(ruleBodyFor(contractsCss, ".portfolio-notice-days")).toMatch(/font-size:\s*11px/);
  });

  it("no longer carries the Day-1 filter-chip or attention-strip composites", () => {
    expect(contractsCss).not.toMatch(/\.portfolio-filters/);
    expect(contractsCss).not.toMatch(/\.attention-cell/);
  });
});

describe("components.css (shared V2 screen chrome used by Portfolio, Renewals, Quote check, Savings, Members)", () => {
  it("draws the screen header as the export does: flex, full 28px 32px 16px pad, 2px divider, 13px neutral summary", () => {
    const header = ruleBodyFor(componentsCss, ".screen-header");
    expect(header).toMatch(/display:\s*flex/);
    expect(header).toMatch(/padding:\s*28px 32px 16px/);
    expect(header).toMatch(/border-bottom:\s*2px solid var\(--color-divider\)/);
    const summary = ruleBodyFor(componentsCss, ".screen-header-summary");
    expect(summary).toMatch(/font-size:\s*13px/);
    expect(summary).toMatch(/color:\s*var\(--color-neutral-600\)/);
  });

  it("gives the reroute block the export's own 48px 32px padding and 520px measure", () => {
    const reroute = ruleBodyFor(componentsCss, ".screen-reroute");
    expect(reroute).toMatch(/padding:\s*48px 32px/);
    expect(reroute).toMatch(/max-width:\s*520px/);
  });

  it("keeps the critical row tinted accent-100 with a 3px accent bar (the urgent portfolio row reuses it)", () => {
    const body = ruleBodyFor(componentsCss, ".table tr.row-critical");
    expect(body).toMatch(/background:\s*var\(--color-accent-100\)/);
    expect(body).toMatch(/inset 3px 0 0 var\(--color-accent\)/);
  });
});
