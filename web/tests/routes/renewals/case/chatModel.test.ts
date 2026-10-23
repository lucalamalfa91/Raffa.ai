import { describe, expect, it } from "vitest";
import { splitFeed } from "../../../../src/routes/renewals/case/chat/chatModel";
import { act, create, view } from "../../../../src/routes/renewals/case/engine";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "../../../../src/routes/renewals/case/fixtures";
import { seedDemoCases } from "../../../../src/routes/renewals/case/demoCases";

const L = DEMO_APPROVER;
const M = DEMO_OWNER;
const fresh = () => create("1", demoContext("1", M), null, { approver: L, owner: M });

describe("splitFeed: current step and history", () => {
  it("a new case shows Raffa's opening line and the first question, with no history", () => {
    const v = view(fresh(), demoContext("1", M));
    const split = splitFeed(v.cards, v.steps7);
    expect(split.history).toEqual([]);
    expect(split.current.map((c) => c.k)).toEqual(["say", "q"]);
    expect(split.current.at(-1)?.current).toBe(true);
  });

  it("never puts the 'what to expect' table in the chat", () => {
    const v = view(fresh(), demoContext("1", M));
    expect(v.cards.some((c) => c.k === "outlook")).toBe(true);
    const split = splitFeed(v.cards, v.steps7);
    expect([...split.byId.values()].some((c) => c.k === "outlook")).toBe(false);
  });

  it("answered questions move to history, grouped by the wizard step they belong to", () => {
    let c = fresh();
    c = act(c, "ans", { q: 0, val: "keep" }, demoContext("1", M));
    c = act(c, "ans", { q: 1, val: "12" }, demoContext("1", M));
    const v = view(c, demoContext("1", M));
    const split = splitFeed(v.cards, v.steps7);
    expect(split.history).toHaveLength(1);
    expect(split.history[0]).toMatchObject({ n: 1, label: "Start" });
    expect(split.history[0]?.rows.map((r) => r.label)).toEqual([
      "Question 1 / 4 · First: what do you want to do with the products and quantities?",
      "Question 2 / 4 · Term: how long should the renewed contract run?",
    ]);
    expect(split.current.filter((x) => x.current).map((x) => x.kicker)).toEqual(["Question 3 / 4"]);
  });

  it("an answer echoed by its question is not repeated as a history row", () => {
    let c = fresh();
    c = act(c, "ans", { q: 0, val: "keep" }, demoContext("1", M));
    c = act(c, "ans", { q: 1, val: "12" }, demoContext("1", M));
    const v = view(c, demoContext("1", M));
    expect(v.cards.some((x) => x.k === "user")).toBe(true);
    const rows = splitFeed(v.cards, v.steps7).history.flatMap((g) => g.rows);
    expect(rows.some((r) => r.label.startsWith("Marta:"))).toBe(false);
  });

  it("what the user wrote on their own stays in history, and Raffa's chatter does not", () => {
    let c = fresh();
    c = act(c, "say", { text: "Keep all but cut CPQ to 40 seats" }, demoContext("1", M));
    c = act(c, "ans", { q: 1, val: "12" }, demoContext("1", M));
    const v = view(c, demoContext("1", M));
    const rows = splitFeed(v.cards, v.steps7).history.flatMap((g) => g.rows).map((r) => r.label);
    expect(rows.some((r) => r.includes("Keep all but cut CPQ"))).toBe(false); // echoed by the answered question
    expect(rows.some((r) => r.startsWith("Raffa.ai:"))).toBe(false);
  });

  it("history spans several wizard steps and keeps an email's state as the row note", () => {
    const seeded = seedDemoCases()["4"];
    if (!seeded) throw new Error("demo case missing");
    const v = view(seeded, demoContext("4", L));
    const split = splitFeed(v.cards, v.steps7);
    expect(split.history.map((g) => g.label)).toEqual(["Start", "Plan", "Negotiation"]);
    const email = split.history.flatMap((g) => g.rows).find((r) => r.label.startsWith("First email"));
    expect(email?.note).toMatch(/^Sent — confirmed by Marta Keller/);
    expect(split.current.some((c) => c.k === "email" && c.current)).toBe(true);
  });

  it("when nothing is waiting on the user, the last card is the current one", () => {
    const c = act(fresh(), "ans", { q: 0, val: "keep" }, demoContext("1", M));
    const v = view({ ...c, phase: "done" }, demoContext("1", M));
    const split = splitFeed(v.cards.map((x) => ({ ...x, current: false })), v.steps7);
    expect(split.current.length).toBeGreaterThan(0);
    expect(split.current.at(-1)?.id).toBe(v.cards.filter((x) => x.k !== "outlook").at(-1)?.id);
  });

  it("handles an empty feed", () => {
    expect(splitFeed([], [])).toMatchObject({ current: [], history: [] });
  });
});
