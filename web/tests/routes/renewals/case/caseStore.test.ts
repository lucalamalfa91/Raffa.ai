import { describe, expect, it, vi } from "vitest";
import { CASE_STORE_KEY, createCaseStore, type StoreEffects } from "../../../../src/routes/renewals/case/caseStore";
import { seedDemoCases } from "../../../../src/routes/renewals/case/demoCases";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "../../../../src/routes/renewals/case/fixtures";

const L = DEMO_APPROVER;
const M = DEMO_OWNER;

function memoryStorage(initial?: string) {
  const data = new Map<string, string>(initial === undefined ? [] : [[CASE_STORE_KEY, initial]]);
  return {
    data,
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
  };
}

function fakeEffects() {
  const later: Array<{ run: () => void; ms: number }> = [];
  const effects: StoreEffects = { copyToClipboard: vi.fn(), later: (run, ms) => void later.push({ run, ms }) };
  return { effects, later };
}

describe("case store", () => {
  it("starts from the seed and opens a new case for a contract that has none", () => {
    const store = createCaseStore({ storage: null, seed: seedDemoCases, effects: fakeEffects().effects });
    expect(Object.keys(store.getState().cases).sort()).toEqual(["2", "3", "4"]);
    store.open("1", demoContext("1", M), { approver: L, owner: M });
    expect(store.getState().cases["1"]).toMatchObject({ cid: "1", owner: M, approver: L, phase: "interview" });
  });

  it("opening an existing case records a visit instead of recreating it", () => {
    const store = createCaseStore({ storage: null, seed: seedDemoCases, effects: fakeEffects().effects });
    const before = store.getState().cases["4"];
    store.open("4", demoContext("4", L), { approver: L, owner: L });
    const after = store.getState().cases["4"];
    expect(after?.owner).toBe(M); // not reassigned
    expect(after?.feed).toHaveLength(before?.feed.length ?? -1);
    expect(after?.ui.since?.n).toBeGreaterThan(0); // Luca sees what Marta did
  });

  it("opening on the notice branch adds the notice check to an existing case", () => {
    const store = createCaseStore({ storage: null, seed: seedDemoCases, effects: fakeEffects().effects });
    store.open("2", demoContext("2", L), { approver: L, owner: L, branch: "notice" });
    expect(store.getState().cases["2"]?.notice).toMatchObject({ state: "check" });
  });

  it("applies actions through the engine and ignores unknown contracts", () => {
    const store = createCaseStore({ storage: null, seed: seedDemoCases, effects: fakeEffects().effects });
    store.act("2", "toggle", { k: "r0" }, demoContext("2", M));
    expect(store.getState().cases["2"]?.goals.clauses).toContain("r0");
    const snapshot = store.getState();
    store.act("99", "toggle", { k: "r0" }, demoContext("2", M));
    expect(store.getState()).toBe(snapshot);
  });

  it("notifies subscribers, and stops after unsubscribe", () => {
    const store = createCaseStore({ storage: null, seed: seedDemoCases, effects: fakeEffects().effects });
    const listener = vi.fn();
    const off = store.subscribe(listener);
    store.act("2", "toggle", { k: "r0" }, demoContext("2", M));
    expect(listener).toHaveBeenCalledTimes(1);
    off();
    store.act("2", "toggle", { k: "r0" }, demoContext("2", M));
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("copy puts the email on the clipboard and the case never keeps it", () => {
    const { effects } = fakeEffects();
    const store = createCaseStore({ storage: null, seed: seedDemoCases, effects });
    const approvedDocusign = demoContext("4", L);
    store.act("4", "decide", undefined, approvedDocusign); // already decided in the seed: a no-op
    store.act("4", "approve", { eid: 1 }, approvedDocusign);
    store.act("4", "copy", { eid: 1 }, demoContext("4", M));
    expect(effects.copyToClipboard).toHaveBeenCalledWith(expect.stringContaining("Subject: Re: eSignature Business Pro — counter-proposal"));
    const stored = store.getState().cases["4"];
    expect(stored?.clip).toBeUndefined();
    expect(stored?.emails[1]?.state).toBe("copied"); // copied, never sent
  });

  it("runs the simulated extraction one tick at a time", () => {
    const { effects, later } = fakeEffects();
    const store = createCaseStore({ storage: null, seed: () => ({}), effects });
    const L_ctx = demoContext("1", L);
    store.open("1", L_ctx, { approver: L, owner: L });
    const steps: Array<[string, Parameters<typeof store.act>[1], Parameters<typeof store.act>[2]?]> = [
      [L, "ans", { q: 0, val: "keep" }], [L, "ans", { q: 1, val: "24" }], [L, "ans", { q: 2, val: "none" }], [L, "q3done"], [L, "confirmPlan"],
      [L, "ui", { k: "to:0", v: "a@b.test" }], [L, "approve", { eid: 0 }], [L, "copy", { eid: 0 }], [L, "sentOpen", { eid: 0 }], [L, "sentOk", { eid: 0 }], [L, "remOk", { rid: 0 }],
      [L, "sampleReply"], [L, "reply"],
      [L, "ui", { k: "to:1", v: "a@b.test" }], [L, "approve", { eid: 1 }], [L, "copy", { eid: 1 }], [L, "sentOpen", { eid: 1 }], [L, "sentOk", { eid: 1 }], [L, "remOk", { rid: 1 }],
      [L, "sampleReply"], [L, "reply"], [L, "agreeOk", { did: 0 }],
      [L, "ui", { k: "to:2", v: "a@b.test" }], [L, "approve", { eid: 2 }], [L, "copy", { eid: 2 }], [L, "sentOpen", { eid: 2 }], [L, "sentOk", { eid: 2 }],
      [L, "addDoc"], [L, "fixEmail"], [L, "ui", { k: "to:3", v: "a@b.test" }], [L, "approve", { eid: 3 }], [L, "copy", { eid: 3 }], [L, "sentOpen", { eid: 3 }], [L, "sentOk", { eid: 3 }],
      [L, "markAnnex", { v: 1 }], [L, "addDoc"], [L, "addDoc"],
    ];
    for (const [who, type, p] of steps) store.act("1", type, p, demoContext("1", who));
    expect(store.getState().cases["1"]?.phase).toBe("sign_confirm");
    expect(later).toHaveLength(0);
    store.act("1", "sigOk", {}, L_ctx);
    expect(store.getState().cases["1"]?.pipe?.stage).toBe(0);
    expect(later).toHaveLength(1);
    expect(later[0]?.ms).toBe(900);
    later.shift()?.run();
    expect(store.getState().cases["1"]?.pipe?.stage).toBe(1);
    later.shift()?.run();
    later.shift()?.run();
    expect(store.getState().cases["1"]?.pipe?.stage).toBe(3);
    expect(later).toHaveLength(0); // review is where the user takes over
  });
});

describe("case store: the person's own queue", () => {
  const make = (storage = memoryStorage()) => createCaseStore({ storage, seed: seedDemoCases, effects: fakeEffects().effects });

  it("snoozes and finishes items, and brings a snoozed one back", () => {
    const store = make();
    store.snooze("4:approve:x", "11 Sep 2026");
    store.dismiss("1:unassigned");
    expect(store.getState().queue).toEqual({ snoozed: { "4:approve:x": "11 Sep 2026" }, dismissed: { "1:unassigned": true } });
    store.unsnooze("4:approve:x");
    expect(store.getState().queue.snoozed).toEqual({});
  });

  it("snoozing an item that was marked done brings it back to snoozed, not done", () => {
    const store = make();
    store.dismiss("a");
    store.snooze("a", "11 Sep 2026");
    expect(store.getState().queue).toEqual({ snoozed: { a: "11 Sep 2026" }, dismissed: {} });
  });

  it("keeps the queue across a reload, and reset clears it", () => {
    const storage = memoryStorage();
    make(storage).dismiss("1:unassigned");
    const reloaded = make(storage);
    expect(reloaded.getState().queue.dismissed).toEqual({ "1:unassigned": true });
    reloaded.reset();
    expect(reloaded.getState().queue).toEqual({ snoozed: {}, dismissed: {} });
  });

  it("changing a case does not touch the queue state", () => {
    const store = make();
    store.dismiss("x");
    store.act("2", "toggle", { k: "r0" }, demoContext("2", M));
    expect(store.getState().queue.dismissed).toEqual({ x: true });
  });
});

describe("case store persistence", () => {
  it("persists every change and restores it in a new store", () => {
    const storage = memoryStorage();
    const first = createCaseStore({ storage, seed: seedDemoCases, effects: fakeEffects().effects });
    first.act("2", "toggle", { k: "r0" }, demoContext("2", M));
    expect(JSON.parse(storage.data.get(CASE_STORE_KEY) ?? "{}").cases["2"].goals.clauses).toContain("r0");
    const second = createCaseStore({ storage, seed: () => ({}), effects: fakeEffects().effects });
    expect(second.getState().cases["2"]?.goals.clauses).toContain("r0");
  });

  it.each([
    ["not JSON", "{oops"],
    ["no cases", JSON.stringify({ cases: {} })],
    ["a case without a feed", JSON.stringify({ cases: { 1: { cid: "1" } } })],
  ])("falls back to the seed when the saved data is %s", (_label, saved) => {
    const store = createCaseStore({ storage: memoryStorage(saved), seed: seedDemoCases, effects: fakeEffects().effects });
    expect(Object.keys(store.getState().cases).sort()).toEqual(["2", "3", "4"]);
  });

  it("keeps working when storage throws", () => {
    const broken = {
      getItem: () => {
        throw new Error("blocked");
      },
      setItem: () => {
        throw new Error("quota");
      },
      removeItem: () => {
        throw new Error("blocked");
      },
    };
    const store = createCaseStore({ storage: broken, seed: seedDemoCases, effects: fakeEffects().effects });
    store.act("2", "toggle", { k: "r0" }, demoContext("2", M));
    expect(store.getState().cases["2"]?.goals.clauses).toContain("r0");
    expect(() => store.reset()).not.toThrow();
  });

  it("reset returns to the seeded demo and clears what was saved", () => {
    const storage = memoryStorage();
    const store = createCaseStore({ storage, seed: seedDemoCases, effects: fakeEffects().effects });
    store.open("1", demoContext("1", M), { approver: L, owner: M });
    store.reset();
    expect(Object.keys(store.getState().cases).sort()).toEqual(["2", "3", "4"]);
  });
});
