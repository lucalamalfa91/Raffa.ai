import { afterEach, describe, expect, it } from "vitest";
import { msg, translate, trackMissingItalian } from "../../src/i18n/translate";
import { act, checklist, create, todo, view } from "../../src/routes/renewals/case/engine";
import { seedDemoCases } from "../../src/routes/renewals/case/demoCases";
import { DEMO_APPROVER as L, DEMO_OWNER as M, demoContext } from "../../src/routes/renewals/case/fixtures";
import type { ActPayload, ActionType, Case } from "../../src/routes/renewals/case/types";

const EVA = "Eva Roth";
type Step = [string, ActionType, ActPayload?];

/** Every message the case shows in Italian, for each person on it, after every step. */
function journeyMissing(cid: string, branch: "notice" | null, steps: Step[]): string[] {
  const missing = new Set<string>();
  trackMissingItalian((id) => missing.add(id));
  const show = (c: Case) => {
    for (const who of [L, M, EVA]) {
      const ctx = demoContext(cid, who, "it");
      view(c, ctx);
      todo(c, ctx);
      checklist(c, ctx);
    }
  };
  let c = create(cid, demoContext(cid, M, "it"), branch, { approver: L, owner: M, members: [L, M, EVA] });
  show(c);
  for (const [who, type, payload] of steps) {
    c = act(c, type, payload, demoContext(cid, who, "it"));
    show(c);
  }
  return [...missing].sort();
}

const R1 = "Hi Luca,\n\nthanks. For a 24-month renewal we can offer a better price. Best, Anna";
const R2 = "Hi Luca,\n\nthank you. We can do it. Liability 18 months. Best, Anna";
const send = (eid: number): Step[] => [
  [M, "ui", { k: `to:${eid}`, v: "anna.meyer@supplier.test" }],
  [L, "approve", { eid }],
  [M, "copy", { eid }],
  [M, "sentOpen", { eid }],
  [M, "sentOk", { eid }],
];

afterEach(() => trackMissingItalian(null));

describe("the renewal case in Italian", () => {
  it("notices a message without Italian", () => {
    const missing: string[] = [];
    trackMissingItalian((id) => missing.push(id));
    expect(translate("it", "No such message in the catalogue")).toBe("No such message in the catalogue");
    expect(missing).toEqual(["No such message in the catalogue"]);
    expect(msg("Done").id).toBe("Done");
  });

  it("shows the case in Italian", () => {
    const ctx = demoContext("1", M, "it");
    const c = create("1", ctx, null, { approver: L, owner: M, members: [L, M] });
    expect(view(c, ctx).steps7.map((s) => s.label)[0]).toBe("Avvio");
    expect(view(c, demoContext("1", M, "en")).steps7.map((s) => s.label)[0]).toBe("Start");
  });

  it("has Italian at every step of a renewal, through to the signed contract", () => {
    const missing = journeyMissing("1", null, [
      [M, "ans", { q: 0, val: "keep" }],
      [M, "ans", { q: 1, val: "12" }],
      [M, "ans", { q: 2, val: "p5" }],
      [M, "q3done"],
      [M, "confirmPlan"],
      ...send(0),
      [M, "remOk", { rid: 0 }],
      [M, "reply", { text: R1 }],
      [M, "say", { text: "Please compare" }],
      [L, "say", { text: "OK" }],
      [EVA, "say", { text: "Looks fine" }],
      [M, "reply", { text: R2 }],
    ]);
    expect(missing).toEqual([]);
  });

  it("has Italian on the notice branch", () => {
    const missing = journeyMissing("2", "notice", [[M, "ans", { q: 0, val: "end" }], [M, "q3done"]]);
    expect(missing).toEqual([]);
  });

  it("has Italian for every demo case as seeded", () => {
    const missing = new Set<string>();
    trackMissingItalian((id) => missing.add(id));
    for (const [cid, c] of Object.entries(seedDemoCases())) {
      for (const who of [L, M, EVA]) {
        const ctx = demoContext(cid, who, "it");
        view(c, ctx);
        todo(c, ctx);
        checklist(c, ctx);
      }
    }
    expect([...missing].sort()).toEqual([]);
  });
});
