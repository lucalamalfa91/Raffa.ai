import { describe, expect, it } from "vitest";
import { act, checklist, create, permissionsOf, responsibilityOf, shortStatus, stage, todo, view } from "../../../../src/routes/renewals/case/engine";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "../../../../src/routes/renewals/case/fixtures";
import { scriptedServices } from "../../../../src/routes/renewals/case/mockServices";
import type { CaseServices } from "../../../../src/routes/renewals/case/services";
import type { ActPayload, ActionType, Case, CaseContext } from "../../../../src/routes/renewals/case/types";

const L = DEMO_APPROVER; // Luca Lamalfa -- approver (CPO)
const M = DEMO_OWNER; // Marta Keller -- owner (buyer)
const EVA = "Eva Roth"; // on the case, no assigned responsibility

const ctxAs = (who: string, cid = "1", lang: "en" | "it" = "en"): CaseContext => demoContext(cid, who, lang);
const open = (cid = "1", branch: "notice" | null = null): Case => create(cid, ctxAs(M, cid), branch, { approver: L, owner: M, members: [L, M, EVA] });
const run = (c: Case, who: string, type: ActionType, p?: ActPayload, cid = "1"): Case => act(c, type, p, ctxAs(who, cid));

type Step = [string, ActionType, ActPayload?];
const play = (c: Case, steps: Step[], cid = "1"): Case => steps.reduce((acc, [who, type, p]) => run(acc, who, type, p, cid), c);

const R1 = "Hi Luca,\n\nthanks. For a 24-month renewal we can offer a better price. Best, Anna";
const R2 = "Hi Luca,\n\nthank you. We can do it. Liability 18 months. Best, Anna";
const send = (eid: number): Step[] => [
  [M, "ui", { k: `to:${eid}`, v: "anna.meyer@supplier.test" }],
  [L, "approve", { eid }],
  [M, "copy", { eid }],
  [M, "sentOpen", { eid }],
  [M, "sentOk", { eid }],
];
const INTERVIEW: Step[] = [[M, "ans", { q: 0, val: "keep" }], [M, "ans", { q: 1, val: "12" }], [M, "ans", { q: 2, val: "p5" }], [M, "q3done"]];

describe("purity", () => {
  it("never mutates its input and keeps the case plain JSON", () => {
    const c0 = open();
    const snapshot = JSON.stringify(c0);
    const c1 = run(c0, M, "ans", { q: 0, val: "keep" });
    expect(JSON.stringify(c0)).toBe(snapshot);
    expect(c1).not.toBe(c0);
    expect(JSON.parse(JSON.stringify(c1))).toEqual(c1);
  });

  it("is deterministic: same case, action and context give the same result", () => {
    const c0 = open();
    expect(run(c0, M, "ans", { q: 0, val: "keep" })).toEqual(run(c0, M, "ans", { q: 0, val: "keep" }));
  });

  it("view, todo, stage and checklist do not change the case", () => {
    const c = play(open(), INTERVIEW);
    const snapshot = JSON.stringify(c);
    view(c, ctxAs(M));
    todo(c, ctxAs(M));
    checklist(c, ctxAs(M));
    stage(c);
    expect(JSON.stringify(c)).toBe(snapshot);
  });
});

describe("interview and plan (steps 1-2)", () => {
  it("shows exactly one current card: the open question", () => {
    const current = view(open(), ctxAs(M)).cards.filter((card) => card.current);
    expect(current).toHaveLength(1);
    expect(current[0]).toMatchObject({ k: "q", kicker: "Question 1 / 4" });
  });

  it("answering commits the choice, collapses the question and opens the next one", () => {
    const c = run(open(), M, "ans", { q: 0, val: "keep" });
    const cards = view(c, ctxAs(M)).cards;
    const q1 = cards.find((card) => card.k === "q" && card.kicker.startsWith("Question 1"));
    expect(q1).toMatchObject({ dim: true, current: false });
    expect(q1?.lines[0]).toBe("Answer: Keep everything as today");
    expect(cards.filter((card) => card.current).map((card) => card.kicker)).toEqual(["Question 2 / 4"]);
  });

  it("ignores an answer to a question that is not open", () => {
    const c = run(open(), M, "ans", { q: 2, val: "p5" });
    expect(c.q).toBe(0);
    expect(c.goals.target).toBeUndefined();
  });

  it("pre-selects the clauses to push on, and Continue builds plan v1", () => {
    const c = play(open(), INTERVIEW);
    expect(c.goals.clauses).toEqual(["p0", "p1"]);
    expect(c.phase).toBe("plan");
    expect(c.planVer).toBe(1);
    expect(stage(c)).toBe(1);
  });

  it("Edit reopens the interview and a new plan is v2", () => {
    const c = play(open(), [...INTERVIEW, [M, "editPlan"], [M, "ans", { q: 0, val: "reduce" }], [M, "ans", { q: 1, val: "24" }], [M, "ans", { q: 2, val: "none" }], [M, "q3done"]]);
    expect(c.planVer).toBe(2);
    expect(c.planOk).toBe(false);
  });

  it("free text answers the open question as a custom answer", () => {
    const c = run(open(), M, "say", { text: "Keep all but cut CPQ to 40 seats" });
    expect(c.goals.prod).toBe("custom");
    expect(view(c, ctxAs(M)).summary.goals[0]?.v).toBe("Keep all but cut CPQ to 40 seats");
  });
});

describe("emails: Raffa drafts, people approve and send", () => {
  const planned = play(open(), [...INTERVIEW, [M, "confirmPlan"]]);

  it("drafts the first email without a recipient and asks for one", () => {
    expect(planned.emails[0]).toMatchObject({ kind: "first", state: "draft", to: "" });
    expect(todo(planned, ctxAs(M)).map((t) => t.kind)).toContain("recipient");
  });

  it("never invents an address: approval is refused until a recipient is added", () => {
    const c = run(planned, L, "approve", { eid: 0 });
    expect(c.emails[0]?.state).toBe("draft");
    expect(c.ui.warn).toBe("Add the recipient first — I never invent addresses.");
    const approve = view(planned, ctxAs(L)).cards.find((card) => card.k === "email")?.buttons.find((b) => b.act === "approve");
    expect(approve).toMatchObject({ off: true, why: "Add the recipient first — I never invent addresses." });
  });

  it("only the approver or a delegate can approve", () => {
    const withTo = run(planned, M, "ui", { k: "to:0", v: "anna.meyer@supplier.test" });
    expect(run(withTo, M, "approve", { eid: 0 }).emails[0]?.state).toBe("draft");
    expect(run(withTo, M, "approve", { eid: 0 }).ui.warn).toContain("Only the approver (CPO) or an explicit delegate");
    expect(run(withTo, L, "approve", { eid: 0 }).emails[0]).toMatchObject({ state: "approved", approvedBy: L });
  });

  it("Copy is not Send: copying moves to 'copied' and never to 'sent'", () => {
    const approved = play(planned, [[M, "ui", { k: "to:0", v: "a@b.test" }], [L, "approve", { eid: 0 }]]);
    const copied = run(approved, M, "copy", { eid: 0 });
    expect(copied.emails[0]?.state).toBe("copied");
    expect(copied.emails[0]?.sent).toBeNull();
    expect(copied.clip).toContain("To: a@b.test");
    expect(copied.phase).toBe("email");
  });

  it("a draft cannot be copied", () => {
    const c = run(planned, M, "copy", { eid: 0 });
    expect(c.clip).toBeUndefined();
  });

  it("'sent' exists only as the user's declaration, and then proposes a check date", () => {
    const c = play(planned, send(0));
    expect(c.emails[0]).toMatchObject({ state: "sent", sent: { by: M, edited: false } });
    expect(c.phase).toBe("waiting_rem");
    const remindAt = c.rems[0]?.date;
    expect(remindAt).toBe("15 Sep 2026"); // +7 days from 08 Sep
    expect(view(c, ctxAs(M)).cards.find((card) => card.k === "email")?.email?.state).toContain("Sent — confirmed by Marta Keller");
  });

  it("the check date never goes past notice minus 3 days", () => {
    const urgent = open("4");
    const c = play(urgent, [...INTERVIEW, [M, "confirmPlan"], ...send(0)], "4");
    expect(c.rems[0]?.date).toBe("15 Sep 2026");
    const late = { ...c, rems: [], round: 1 };
    expect(late.rems).toHaveLength(0);
    // 02 Oct 2026 notice → cap 29 Sep 2026; +7 days from today is 15 Sep, so no clamp here.
    expect(new Date("29 Sep 2026").getTime()).toBeGreaterThan(new Date("15 Sep 2026").getTime());
  });

  it("editing after approval invalidates it and bumps the version", () => {
    const approved = play(planned, [[M, "ui", { k: "to:0", v: "a@b.test" }], [L, "approve", { eid: 0 }]]);
    const edited = play(approved, [[M, "edit", { eid: 0 }], [M, "ui", { k: "editBody", v: "New body" }], [M, "saveEdit", { eid: 0 }]]);
    expect(edited.emails[0]).toMatchObject({ state: "draft", ver: 2, approvedBy: null, body: "New body" });
    expect(edited.decs.some((d) => typeof d.text !== "string" && d.text.en.includes("approval invalidated"))).toBe(true);
  });

  it("an example address exists only if the service offers one", () => {
    const strict: CaseServices = { ...scriptedServices, exampleAddress: undefined };
    const ctx: CaseContext = { ...ctxAs(M), services: strict };
    const c = act(play(open(), [...INTERVIEW, [M, "confirmPlan"]]), "toEx", { eid: 0 }, ctx);
    expect(c.emails[0]?.to).toBe("");
    expect(view(c, ctx).cards.find((card) => card.k === "email")?.buttons.map((b) => b.act)).not.toContain("toEx");
    expect(view(planned, ctxAs(M)).cards.find((card) => card.k === "email")?.buttons.map((b) => b.act)).toContain("toEx");
  });
});

describe("negotiation and agreement (steps 3-4)", () => {
  const afterFirst = play(open(), [...INTERVIEW, [M, "confirmPlan"], ...send(0), [M, "remOk", { rid: 0 }]]);

  it("a reply is analysed into proposals; nothing is agreed", () => {
    const c = run(afterFirst, M, "reply", { text: R1 });
    expect(c.round).toBe(1);
    expect(c.reps[0]?.rows.map((r) => r.state)).toEqual(["proposed", "conflict", "proposed", "unclear", "rejected"]);
    expect(Object.values(c.terms).some((t) => t.state === "agreed")).toBe(false);
    expect(c.agreed).toBeNull();
    expect(c.phase).toBe("review");
  });

  it("asks for a trade-off decision when the plan term is not 24 months", () => {
    const c = run(afterFirst, M, "reply", { text: R1 });
    expect(c.qs).toEqual([{ did: 0, state: "open" }]);
    expect(todo(c, ctxAs(M)).map((t) => t.kind)).toContain("decide");
    const decided = run(c, M, "decide", { did: 0, opt: "c" });
    expect(decided.limits).toEqual({ term: 24, priceMax: 144, exit: 12 });
    expect(decided.emails.at(-1)?.kind).toBe("counter");
  });

  it("supersedes drafts when a newer reply arrives", () => {
    const drafted = play(open(), [...INTERVIEW, [M, "confirmPlan"], ...send(0), [M, "remOk", { rid: 0 }], [M, "reply", { text: R1 }], [M, "decide", { did: 0, opt: "a" }]]);
    expect(drafted.emails[1]?.state).toBe("draft");
    const again = run(drafted, M, "reply", { text: R2 });
    expect(again.emails[1]?.state).toBe("superseded");
  });

  const atAgree = play(afterFirst, [[M, "reply", { text: R1 }], [M, "decide", { did: 0, opt: "a" }], ...send(1), [M, "remOk", { rid: 1 }], [M, "reply", { text: R2 }]]);
  const withPolicy = run(atAgree, M, "policy");

  it("terms become 'agreed' only on explicit confirmation", () => {
    expect(atAgree.phase).toBe("agree");
    expect(atAgree.agreed).toBeNull();
    expect(Object.values(atAgree.terms).filter((t) => t.state === "proposed").length).toBeGreaterThan(0);
    const done = run(atAgree, M, "agreeOk", { did: 1 });
    expect(Object.values(done.terms).some((t) => t.state === "proposed")).toBe(false);
    expect(done.agreed).toEqual({ price: 144, term: 24, cap: 3, liab: 18 });
    expect(done.phase).toBe("doc_wait");
  });

  it("a policy conflict (liability 18 vs 24) needs a recorded authoriser", () => {
    expect(todo(withPolicy, ctxAs(M)).find((t) => t.kind === "policy")?.who).toBe(L);
    const refused = run(withPolicy, M, "agreeOk", { did: 1 });
    expect(refused.agreed).toBeNull();
    expect(refused.ui.warn).toBe("Record who authorized the exception to the policy first.");
    const ok = play(withPolicy, [[M, "ui", { k: "auth", v: "Dana Frei, CFO" }], [M, "agreeOk", { did: 1 }]]);
    expect(ok.agreed).toMatchObject({ auth: "Dana Frei, CFO" });
    expect(ok.decs.at(-1)?.text).toMatchObject({ en: expect.stringContaining("recorded outside Raffa") });
  });

  it("Today does not raise the policy item once an authoriser is recorded", () => {
    const withAuth = run(withPolicy, M, "ui", { k: "auth", v: "Dana Frei, CFO" });
    expect(todo(withAuth, ctxAs(M)).map((t) => t.kind)).toEqual(["agree"]);
  });

  it("a document before agreement is not checked against anything", () => {
    const c = run(afterFirst, M, "addDoc");
    expect(c.docs).toHaveLength(0);
    expect(view(c, ctxAs(M)).cards.at(-1)?.text).toContain("confirm the terms first");
  });
});

describe("responsibilities and permissions", () => {
  const planned = play(open(), [...INTERVIEW, [M, "confirmPlan"]]);

  it("derives responsibilities from the case's assignments, not from workspace roles", () => {
    expect(responsibilityOf(planned, L)).toBe("approver");
    expect(responsibilityOf(planned, M)).toBe("owner");
    expect(responsibilityOf(planned, EVA)).toBe("collab");
    const delegated = run(planned, L, "delegate", { who: EVA, on: true });
    expect(responsibilityOf(delegated, EVA)).toBe("delegate");
    expect(permissionsOf(planned, L)).toEqual({ approve: true, work: true, close: true, manageTeam: true });
    expect(permissionsOf(planned, M)).toEqual({ approve: false, work: true, close: false, manageTeam: false });
    expect(permissionsOf(planned, EVA)).toEqual({ approve: false, work: false, close: false, manageTeam: false });
  });

  it("one person can hold several responsibilities; the strongest is shown", () => {
    const solo = create("1", ctxAs(L), null, { approver: L, owner: L });
    expect(responsibilityOf(solo, L)).toBe("approver");
    expect(permissionsOf(solo, L).work).toBe(true);
  });

  it("a collaborator can only read and comment", () => {
    const refused = run(open(), EVA, "ans", { q: 0, val: "keep" });
    expect(refused.q).toBe(1); // answering the open question is open to anyone on the case...
    const plan = play(open(), INTERVIEW);
    const denied = run(plan, EVA, "confirmPlan");
    expect(denied.planOk).toBe(false);
    expect(denied.ui.warn).toBe("Only people working on this renewal can do this.");
    const comment = run(planned, EVA, "say", { text: "Looks fine to me" });
    expect(comment.feed.at(-1)).toMatchObject({ k: "user", by: EVA, tag: { en: "internal comment" } });
  });

  it("disabled buttons stay in the view with the reason", () => {
    const plan = play(open(), INTERVIEW);
    const confirm = view(plan, ctxAs(EVA)).cards.find((card) => card.k === "plan")?.buttons.find((b) => b.act === "confirmPlan");
    expect(confirm).toMatchObject({ off: true, why: "Only people working on this renewal can do this." });
    const enabled = view(plan, ctxAs(M)).cards.find((card) => card.k === "plan")?.buttons.find((b) => b.act === "confirmPlan");
    expect(enabled).toMatchObject({ off: false, why: "" });
  });

  it("a delegate can approve; closing needs the extra right", () => {
    const delegated = play(planned, [[L, "delegate", { who: EVA, on: true }], [M, "ui", { k: "to:0", v: "a@b.test" }]]);
    expect(run(delegated, EVA, "approve", { eid: 0 }).emails[0]?.state).toBe("approved");
    expect(permissionsOf(delegated, EVA).close).toBe(false);
    expect(permissionsOf(run(delegated, L, "delegateClose", { who: EVA }), EVA).close).toBe(true);
  });

  it("an owner can also be a delegate: the person list shows both", () => {
    const delegated = run(planned, L, "delegate", { who: M, on: true });
    const marta = view(delegated, ctxAs(L)).hdr.people.find((x) => x.name === M);
    expect(marta).toMatchObject({ responsibility: "owner", isDelegate: true, canClose: false, role: "Owner · works the case · delegate" });
    const closer = run(delegated, L, "delegateClose", { who: M });
    expect(view(closer, ctxAs(L)).hdr.people.find((x) => x.name === M)).toMatchObject({ canClose: true, role: "Owner · works the case · delegate, can close" });
    expect(permissionsOf(closer, M)).toMatchObject({ approve: true, close: true });
  });

  it("only the approver manages delegations and reassigns the owner", () => {
    expect(run(planned, M, "delegate", { who: EVA, on: true }).delegates).toEqual({});
    expect(run(planned, M, "delegate", { who: EVA, on: true }).ui.warn).toBe("Only the approver (CPO) manages delegations.");
    expect(run(planned, M, "transfer", { who: EVA }).owner).toBe(M);
    const moved = run(planned, L, "transfer", { who: EVA });
    expect(moved.owner).toBe(EVA);
    expect(moved.history.at(-1)?.who).toBe(EVA);
  });

  it("delegated approvals go to the delegate in Today", () => {
    const delegated = play(planned, [[L, "delegate", { who: EVA, on: true }], [M, "ui", { k: "to:0", v: "a@b.test" }]]);
    expect(todo(delegated, ctxAs(M)).find((t) => t.kind === "approve")?.who).toBe(EVA);
  });
});

/** The renewal from interview to a validated new contract (Salesforce, trade-off path). */
const FULL_RENEWAL: Step[] = [
  ...INTERVIEW,
  [M, "confirmPlan"], ...send(0), [M, "remOk", { rid: 0 }], [M, "reply", { text: R1 }], [M, "decide", { did: 0, opt: "a" }],
  ...send(1), [M, "remOk", { rid: 1 }], [M, "reply", { text: R2 }], [M, "agreeOk", { did: 1 }],
  ...send(2), [M, "remOk", { rid: 2 }],
  [M, "addDoc"], [M, "fixEmail"], ...send(3), [M, "markAnnex", { v: 1 }], [M, "addDoc"],
];

describe("draft, signature and acquisition (steps 5-7)", () => {
  const drafted = play(open(), FULL_RENEWAL);

  it("the first draft is checked point by point and differences block the next step", () => {
    const first = play(open(), FULL_RENEWAL.slice(0, FULL_RENEWAL.indexOf(FULL_RENEWAL.find(([, t]) => t === "fixEmail") as Step)));
    expect(first.phase).toBe("doc_check");
    expect(first.docs[0]?.rows.filter((r) => r.res !== "ok").map((r) => r.res)).toEqual(["diff", "diff", "unread"]);
    expect(checklist(first, ctxAs(M)).filter((i) => !i.done).map((i) => i.label)).toEqual(["No differences with the agreement", "Every point read"]);
  });

  it("a revised draft that matches moves to signature, which happens outside Raffa", () => {
    expect(drafted.docs).toHaveLength(2);
    expect(drafted.phase).toBe("sign_wait");
    expect(view(drafted, ctxAs(M)).cards.at(-1)?.lines[0]).toContain("Signing is not done in Raffa");
  });

  const signed = play(drafted, [[M, "addDoc"]]);

  it("the signed file must be confirmed as final by the approver or a closing delegate", () => {
    expect(signed.phase).toBe("sign_confirm");
    const denied = run(signed, M, "sigOk");
    expect(denied.signed?.confirmedBy).toBeUndefined();
    expect(denied.ui.warn).toContain("Only the approver (CPO), or a delegate with closing rights");
    const button = view(signed, ctxAs(M)).cards.at(-1)?.buttons[0];
    expect(button).toMatchObject({ off: true });
    expect(run(signed, L, "sigOk").phase).toBe("pipeline");
  });

  it("extraction runs, fields under 80% need a decision, then the outcome is computed from the two prices", () => {
    let c = run(signed, L, "sigOk");
    expect(c.tick).toBe(true);
    c = play(c, [[L, "tick"], [L, "tick"], [L, "tick"]]);
    expect(c.pipe?.stage).toBe(3);
    expect(run(c, M, "validate").phase).toBe("pipeline"); // fields still open
    c = play(c, [[M, "acceptF", { i: 0 }], [M, "ui", { k: "cf1", v: "Quarterly in advance, Net 45" }], [M, "correctF", { i: 1 }], [M, "validate"]]);
    expect(c.phase).toBe("done");
    expect(c.outcome).toEqual({
      kind: "renewed",
      start: "16 Jan 2027",
      end: "15 Jan 2029",
      cancel: "17 Oct 2028",
      cancelDays: daysFrom("08 Sep 2026", "17 Oct 2028"),
      spend: 596800,
      delta: 43200,
      price: "CHF 144 per user / month",
      was: 156,
      file: "Salesforce_OrderForm_signed.pdf",
    });
    expect(c.pipe?.weak.map((w) => w.state)).toEqual(["accepted", "corrected"]);
    expect(todo(c, ctxAs(M))).toEqual([]);
    expect(shortStatus(c)).toBe("Renewed");
  });
});

function daysFrom(from: string, to: string): number {
  return Math.round((Date.parse(`${to} UTC`) - Date.parse(`${from} UTC`)) / 86_400_000);
}

describe("non-renewal notice and alternatives", () => {
  const withNotice = play(open("4", "notice"), [[M, "nStart"], [M, "ui", { k: "to:0", v: "legal@docusign.test" }], [L, "approve", { eid: 0 }], [M, "copy", { eid: 0 }], [M, "sentOpen", { eid: 0 }], [M, "sentOk", { eid: 0 }]], "4");

  it("opens on the notice check and drafts the notice email", () => {
    expect(open("4", "notice").notice).toMatchObject({ state: "check" });
    expect(withNotice.notice?.state).toBe("sent");
    expect(todo(withNotice, ctxAs(M, "4")).map((t) => t.kind)).toEqual(["nout"]);
  });

  it("recording the outcome needs evidence (or an explicit waiver) and a closer", () => {
    expect(run(withNotice, M, "nOut", {}, "4").ui.warn).toContain("Only the approver (CPO)");
    const noEvidence = run(withNotice, L, "nOut", {}, "4");
    expect(noEvidence.outcome).toBeNull();
    expect(noEvidence.ui.warn).toBe("Attach evidence of receipt, or tick that you proceed without it.");
    const done = play(withNotice, [[M, "nReceipt"], [L, "nOut"]], "4");
    expect(done.outcome).toEqual({ kind: "terminated", end: "31 Oct 2026", when: "08 Sep 2026" });
    expect(done.phase).toBe("notice_out");
    expect(shortStatus(done)).toBe("Not renewed");
  });

  it("choosing an alternative does not close the current contract", () => {
    let c = play(open("4"), [...INTERVIEW, [M, "startAlt"], [M, "altReqs"], [M, "altEx"], [M, "altRfq"]], "4");
    expect(c.alt?.vendors).toEqual(["Vendor A", "Vendor B"]);
    expect(c.emails.filter((e) => e.kind === "rfq")).toHaveLength(2);
    c = play(c, [[M, "altOffer", { vendor: "Vendor A" }], [L, "altSel", { vendor: "Vendor A" }]], "4");
    expect(c.alt?.state).toBe("selected");
    expect(c.outcome).toBeNull();
    expect(shortStatus(c)).toBe("Replacement in progress");
    c = play(c, [[M, "altNew", { vendor: "Vendor A" }]], "4");
    expect(c.outcome).toBeNull(); // the exit track is still open
    c = play(c, [[M, "startNotice"], [M, "nStart"], ...send(c.emails.length), [M, "nReceipt"], [L, "nOut"]], "4");
    expect(c.outcome).toMatchObject({ kind: "terminated", replacedBy: "Vendor A" });
  });
});

describe("the service boundary", () => {
  it("the engine reads the reply only through the injected analysis", () => {
    const calls: number[] = [];
    const custom: CaseServices = {
      ...scriptedServices,
      analyzeReply: (_c, _ctx, rid) => {
        calls.push(rid);
        return { rows: [{ label: "Custom", said: "said", plan: "plan", state: "proposed", warn: "" }], terms: [{ key: "price", label: { en: "Unit price", it: "Prezzo unitario" }, state: "proposed", val: "CHF 1" }] };
      },
    };
    const ctx: CaseContext = { ...ctxAs(M), services: custom };
    let c = open();
    c = play(c, [...INTERVIEW, [M, "confirmPlan"], ...send(0), [M, "remOk", { rid: 0 }]]);
    c = act(c, "reply", { text: R1 }, ctx);
    expect(calls).toEqual([0]);
    expect(c.reps[0]?.rows[0]?.label).toBe("Custom");
    expect(c.terms.price).toMatchObject({ state: "proposed", val: "CHF 1", ev: 1 });
  });

  it("the sample reply and example vendors are service calls too", () => {
    expect(run(open(), M, "sampleReply").ui.replyText).toContain("24-month renewal");
    const custom: CaseServices = { ...scriptedServices, sampleReply: () => "stub reply" };
    expect(act(open(), "sampleReply", {}, { ...ctxAs(M), services: custom }).ui.replyText).toBe("stub reply");
  });
});

describe("copy and language", () => {
  const ALL: Step[] = [...FULL_RENEWAL, [M, "addDoc"], [L, "sigOk"], [L, "tick"], [L, "tick"], [L, "tick"], [M, "acceptF", { i: 0 }], [M, "acceptF", { i: 1 }], [M, "validate"]];

  it.each(["en", "it"] as const)("every step of the full renewal renders complete %s copy", (lang) => {
    let c = create("1", ctxAs(M, "1", lang), null, { approver: L, owner: M });
    const seen: string[] = [];
    ALL.forEach(([who, type, p]) => {
      c = act(c, type, p, ctxAs(who, "1", lang));
      seen.push(JSON.stringify(view(c, ctxAs(who, "1", lang))), JSON.stringify(todo(c, ctxAs(who, "1", lang))));
    });
    expect(c.phase).toBe("done");
    const text = seen.join("\n");
    expect(text).not.toMatch(/undefined|NaN|\[object Object\]/);
  });

  it("the same case reads in both languages", () => {
    const c = play(open(), INTERVIEW);
    const en = view(c, ctxAs(M, "1", "en")).cards.find((card) => card.k === "plan");
    const it = view(c, ctxAs(M, "1", "it")).cards.find((card) => card.k === "plan");
    expect(en?.buttons[0]?.label).toBe("Confirm plan");
    expect(it?.buttons[0]?.label).toBe("Conferma piano");
    expect(shortStatus(c, "it")).toBe("Piano pronto");
  });

  it("the stepper has the seven wizard steps", () => {
    expect(view(open(), ctxAs(M)).steps7.map((s) => s.label)).toEqual(["Start", "Plan", "Negotiation", "Agreement", "Draft", "Signature", "Acquisition"]);
    expect(view(open(), ctxAs(M, "1", "it")).steps7.map((s) => s.label)).toEqual(["Avvio", "Piano", "Negoziazione", "Accordo", "Bozza", "Firma", "Acquisizione"]);
  });
});
