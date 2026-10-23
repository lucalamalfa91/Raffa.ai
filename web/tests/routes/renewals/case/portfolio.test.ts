import { describe, expect, it } from "vitest";
import { seedDemoCases } from "../../../../src/routes/renewals/case/demoCases";
import { addDays } from "../../../../src/routes/renewals/case/dates";
import { DEMO_APPROVER, DEMO_OWNER, DEMO_TODAY } from "../../../../src/routes/renewals/case/fixtures";
import {
  EMPTY_QUEUE,
  applyKpi,
  bandOf,
  buildKpis,
  buildQueue,
  buildSummaries,
  groupByBand,
  groupByKind,
  headline,
  inQueueScope,
  inScope,
  isNoticeUrgent,
  miniBars,
  rowActions,
  snoozedItems,
  visibleQueue,
} from "../../../../src/routes/renewals/case/portfolio";
import { translator } from "../../../../src/i18n/translate";

const L = DEMO_APPROVER;
const M = DEMO_OWNER;
const t = translator("en");
const cases = seedDemoCases();

describe("renewals portfolio", () => {
  const list = buildSummaries(cases, L, "en");

  it("lists every contract, nearest notice deadline first", () => {
    expect(list.map((x) => [x.supplier, x.noticeDays])).toEqual([
      ["DocuSign", 24],
      ["Salesforce", 40],
      ["Microsoft", 204],
      ["AWS", 266],
    ]);
  });

  it("knows the wizard step, status and owner of each case, and says so when there is none", () => {
    const byName = Object.fromEntries(list.map((x) => [x.supplier, x]));
    expect(byName.DocuSign).toMatchObject({ hasCase: true, stage: 2, status: "Email to approve", owner: M });
    expect(byName.Microsoft).toMatchObject({ stage: 0, status: "Interview" });
    expect(byName.AWS).toMatchObject({ stage: 3, status: "Terms to confirm" });
    expect(byName.Salesforce).toMatchObject({ hasCase: false, stage: -1, status: "Not started", owner: "No owner" });
  });

  it("computes the days to the end of the term from today's date", () => {
    expect(list.find((x) => x.supplier === "Salesforce")?.endsInDays).toBe(129);
    expect(list.find((x) => x.supplier === "DocuSign")?.endsInDays).toBe(53);
  });

  it("translates the status", () => {
    expect(buildSummaries(cases, L, "it").find((x) => x.supplier === "AWS")?.status).toBe("Condizioni da confermare");
  });

  it("draws seven bars: done, the current step, then nothing; none before a case starts", () => {
    expect(miniBars({ stage: 2, done: false })).toEqual(["done", "done", "now", "todo", "todo", "todo", "todo"]);
    expect(miniBars({ stage: -1, done: false })).toEqual(Array(7).fill("todo"));
    expect(miniBars({ stage: 6, done: true })).toEqual(Array(7).fill("done"));
  });

  it("marks a deadline within 45 days as urgent, unless the case is closed", () => {
    expect(isNoticeUrgent({ noticeDays: 45, done: false })).toBe(true);
    expect(isNoticeUrgent({ noticeDays: 46, done: false })).toBe(false);
    expect(isNoticeUrgent({ noticeDays: 3, done: true })).toBe(false);
  });

  it("'Mine' is what you own or what waits on you; 'Unassigned' is what has no case", () => {
    expect(inScope(list, "team")).toHaveLength(4);
    expect(inScope(list, "unassigned").map((x) => x.supplier)).toEqual(["Salesforce"]);
    const marta = buildSummaries(cases, M, "en");
    expect(inScope(marta, "mine").map((x) => x.supplier).sort()).toEqual(["AWS", "DocuSign", "Microsoft"]);
    // For the approver: the email to approve, the policy exception, and the deadline nobody owns.
    expect(inScope(list, "mine").map((x) => x.supplier).sort()).toEqual(["AWS", "DocuSign", "Salesforce"]);
  });

  it("headline counts open and not started", () => {
    expect(headline(list, t)).toBe("3 renewals open · 1 not started · sorted by notice deadline");
    expect(headline(list.slice(0, 2), t)).toContain("1 renewal open");
  });
});

describe("KPI strip", () => {
  const list = buildSummaries(cases, L, "en");
  const kpis = buildKpis(list, t);

  it("has the five cells in order, with counts, and only urgent ones turn red", () => {
    expect(kpis.map((k) => [k.key, k.label, k.value, k.urgent])).toEqual([
      ["n30", "Notice in 30 days", 1, true],
      ["n90", "Notice in 90 days", 2, false],
      ["open", "Not started", 1, false],
      ["neg", "In negotiation", 3, false],
      ["closed", "Closed", 0, false],
    ]);
  });

  it("an urgent cell at zero is not red", () => {
    expect(buildKpis([], t)[0]).toMatchObject({ value: 0, urgent: false });
  });

  it("each cell filters the list; no key leaves it whole", () => {
    expect(applyKpi(list, "n30").map((x) => x.supplier)).toEqual(["DocuSign"]);
    expect(applyKpi(list, "n90").map((x) => x.supplier)).toEqual(["DocuSign", "Salesforce"]);
    expect(applyKpi(list, "open").map((x) => x.supplier)).toEqual(["Salesforce"]);
    expect(applyKpi(list, "neg")).toHaveLength(3);
    expect(applyKpi(list, "closed")).toEqual([]);
    expect(applyKpi(list, null)).toHaveLength(4);
  });
});

describe("action queue", () => {
  const queue = buildQueue(cases, L, "en");

  it("collects what every case needs, plus deadlines nobody owns, nearest first", () => {
    expect(queue.map((x) => [x.supplier, x.kind, x.dueDays])).toEqual([
      ["DocuSign", "approve", 24],
      ["Salesforce", "unassigned", 40],
      ["Microsoft", "plan", 204],
      ["AWS", "policy", 266],
    ]);
  });

  it("a contract with no case inside 120 days is an item for the approver; outside it is not", () => {
    const unassigned = queue.find((x) => x.kind === "unassigned");
    expect(unassigned).toMatchObject({ supplier: "Salesforce", who: L, hasCase: false, phase: "Not started", mine: true });
    expect(unassigned?.why).toBe("Auto-renews for 12 months if nothing happens by 18 Oct 2026. Start the renewal and assign it.");
    const far = buildQueue({}, L, "en").filter((x) => x.kind === "unassigned").map((x) => x.supplier);
    expect(far).toEqual(["DocuSign", "Salesforce"]); // Microsoft (204 d) and AWS (266 d) are beyond 120 days
  });

  it("each item says where its case is, in words", () => {
    expect(queue.find((x) => x.supplier === "DocuSign")?.phase).toBe("Step 3 / 7 · Negotiation");
  });

  it("scopes: yours, the team's, or unowned", () => {
    expect(inQueueScope(queue, "mine").every((x) => x.mine)).toBe(true);
    expect(inQueueScope(queue, "team").every((x) => !x.mine && x.hasCase)).toBe(true);
    expect(inQueueScope(queue, "unassigned").map((x) => x.supplier)).toEqual(["Salesforce"]);
  });

  it("snoozed and finished items leave the queue; a snooze ends when its date passes", () => {
    const first = queue[0];
    if (!first) throw new Error("empty queue");
    const snoozed = { snoozed: { [first.key]: addDays(DEMO_TODAY, 3) }, dismissed: {} };
    expect(visibleQueue(queue, snoozed, DEMO_TODAY).map((x) => x.key)).not.toContain(first.key);
    expect(snoozedItems(queue, snoozed, DEMO_TODAY).map((x) => [x.key, x.until])).toEqual([[first.key, addDays(DEMO_TODAY, 3)]]);
    expect(visibleQueue(queue, snoozed, addDays(DEMO_TODAY, 3)).map((x) => x.key)).toContain(first.key);
    const done = { snoozed: {}, dismissed: { [first.key]: true as const } };
    expect(visibleQueue(queue, done, DEMO_TODAY)).toHaveLength(queue.length - 1);
    expect(snoozedItems(queue, done, DEMO_TODAY)).toEqual([]);
    expect(visibleQueue(queue, EMPTY_QUEUE, DEMO_TODAY)).toHaveLength(queue.length);
  });

  it("falls into three bands by days to the deadline", () => {
    expect([bandOf(30), bandOf(31), bandOf(120), bandOf(121)]).toEqual(["now", "soon", "soon", "later"]);
    const bands = groupByBand(queue, t);
    expect(bands.map((b) => [b.label, b.items.length])).toEqual([["Within 30 days", 1], ["31–120 days", 1], ["Later", 2]]);
  });

  it("groups by kind of work for the action queue view", () => {
    expect(groupByKind(queue, t).map((g) => [g.label, g.items.map((x) => x.supplier)])).toEqual([
      ["Approvals", ["DocuSign", "AWS"]],
      ["Plans & notices", ["Microsoft"]],
      ["Deadlines without owner", ["Salesforce"]],
    ]);
  });
});

describe("what a row offers", () => {
  const queue = buildQueue(cases, L, "en");
  const unassigned = queue.find((x) => x.kind === "unassigned");
  const approve = queue.find((x) => x.kind === "approve");

  it("the approver starts an unowned renewal and assigns it, or takes it", () => {
    if (!unassigned) throw new Error("no unassigned item");
    const a = rowActions(unassigned, L, undefined, t);
    expect(a.primary).toEqual({ do: "startAssign", label: "Start & assign to Marta", assignTo: M });
    expect(a.secondary).toEqual({ do: "startSelf", label: "I’ll handle it" });
    expect(a.delegate).toBeNull();
  });

  it("anyone else just starts it", () => {
    const marta = buildQueue(cases, M, "en").find((x) => x.kind === "unassigned");
    if (!marta) throw new Error("no unassigned item");
    expect(rowActions(marta, M, undefined, t)).toMatchObject({ primary: { do: "startSelf", label: "Start renewal" }, secondary: null });
  });

  it("opens the case 'at this step' when it waits on you, otherwise just opens it", () => {
    if (!approve) throw new Error("no approval item");
    expect(rowActions(approve, L, cases["4"], t).primary.label).toBe("Open at this step");
    expect(rowActions({ ...approve, who: M }, L, cases["4"], t).primary.label).toBe("Open the case");
  });

  it("only the approver can delegate an approval, and to the owner", () => {
    if (!approve) throw new Error("no approval item");
    expect(rowActions(approve, L, cases["4"], t).delegate).toEqual({ label: "Delegate to Marta", do: "delegate", who: M });
    expect(rowActions(approve, M, cases["4"], t).delegate).toBeNull();
  });

  it("the approver can hand other work to the owner, unless the owner already has it", () => {
    const plan = buildQueue(cases, L, "en").find((x) => x.kind === "plan");
    if (!plan) throw new Error("no plan item");
    expect(rowActions({ ...plan, owner: L }, L, cases["2"], t).delegate).toEqual({ label: "Reassign to Marta", do: "transfer", who: M });
    expect(rowActions(plan, L, cases["2"], t).delegate).toBeNull();
  });
});
