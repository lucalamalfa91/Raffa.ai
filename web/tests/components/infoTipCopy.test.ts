import { describe, expect, it } from "vitest";
import {
  buildDocumentsFilterTip,
  buildRailContractsTip,
  buildReviewConfidenceTip,
  SCREEN_GUIDES,
  TIPS,
  type TipCopy,
} from "../../src/components/infoTipCopy";

describe("infoTipCopy", () => {
  const every: [string, TipCopy][] = [...Object.entries(SCREEN_GUIDES), ...Object.entries(TIPS)];

  it("gives every tip a question for a name and at least one line of plain text", () => {
    for (const [key, tip] of every) {
      expect(tip.label, key).not.toBe("");
      expect(tip.lines.length, key).toBeGreaterThan(0);
      for (const line of tip.lines) expect(line.trim(), key).not.toBe("");
    }
  });

  it("never names two screen guides the same, so each trigger is told apart", () => {
    const labels = Object.values(SCREEN_GUIDES).map((guide) => guide.label);
    expect(new Set(labels).size).toBe(labels.length);
  });

  it("says why the contract screens are dimmed until the first validated contract, and what the number means after", () => {
    expect(buildRailContractsTip(false).lines.join(" ")).toMatch(/dimmed until your first contract is validated/);
    expect(buildRailContractsTip(true).lines.join(" ")).toMatch(/how many validated contracts/);
  });

  it("explains the Not added chip only while it is on screen", () => {
    expect(buildDocumentsFilterTip(false).lines.join(" ")).not.toContain("Not added");
    expect(buildDocumentsFilterTip(true).lines.join(" ")).toContain("Not added");
  });

  it("names the workspace's own auto-accept bar, and says so plainly while it is unknown", () => {
    expect(buildReviewConfidenceTip(90).lines.join(" ")).toContain("At 90% or more a value is accepted automatically");
    expect(buildReviewConfidenceTip(null).lines.join(" ")).not.toMatch(/\d+%/);
  });
});
