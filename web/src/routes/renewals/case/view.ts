/**
 * `view(case, ctx)` -- everything the UI renders for a case: the chat cards (each tagged with its
 * wizard step), the header, the stepper, checklist, timeline, terms summary and team. Pure: it
 * derives the view from the case and its context and never changes either.
 *
 * Buttons that the viewer may not press are still returned, with `off: true` and the reason in `why`,
 * so the UI can show them disabled next to the reason instead of hiding them.
 */

import { checklist } from "./agenda";
import { addDays, daysBetween } from "./dates";
import {
  EMAIL_KIND,
  EXAMPLES,
  NO_APPROVE,
  NO_CLOSE,
  NO_WORK,
  PHASES,
  POLICY,
  STEPS,
  answerLabel,
  canApprove,
  canClose,
  canWork,
  clausesOf,
  figures,
  liabilityConflict,
  outlookTable,
  permissionsOf,
  priceLabel,
  questionDef,
  responsibilityOf,
  shortStatus,
  stage,
} from "./model";
import { chf } from "./text";
import { msg, resolveText, translator, type Lang, type Msg, type Text } from "../../../i18n/translate";
import type {
  Card,
  CardButton,
  CardChip,
  CardInput,
  CardRow,
  Case,
  CaseContext,
  CaseHeader,
  CasePerson,
  CaseView,
  Email,
  FeedItem,
  Responsibility,
  TableCell,
  TermView,
  TimelineEntry,
} from "./types";

interface ButtonOptions {
  primary?: boolean;
  off?: boolean;
  why?: string;
}

function button(label: string, act: CardButton["act"], p: CardButton["p"] = {}, o: ButtonOptions = {}): CardButton {
  // The reason only exists for a button that is switched off, so the UI can render `why` unconditionally.
  return { label, act, p, primary: !!o.primary, off: !!o.off, why: o.off ? (o.why ?? "") : "" };
}

function blankCard(it: FeedItem): Card {
  return {
    id: it.id,
    k: it.k,
    st: it.st ?? 0,
    by: it.by,
    at: it.at,
    plain: false,
    text: "",
    kicker: "",
    title: "",
    lines: [],
    rows: [],
    table: null,
    chips: [],
    buttons: [],
    inputs: [],
    email: null,
    fields: [],
    steps: [],
    note: "",
    pre: "",
    tag: "",
    tone: "",
    current: false,
    dim: false,
    banner: "",
  };
}

const RESPONSIBILITY_LABEL: Record<Responsibility, Msg> = {
  approver: msg("Approver"),
  owner: msg("Owner"),
  delegate: msg("Delegate"),
  collab: msg("Collaborator"),
};

export function view(c: Case, ctx: CaseContext): CaseView {
  const lang: Lang = ctx.lang;
  const tr = translator(lang);
  const T = (x: Text | null | undefined): string => resolveText(x, lang);
  const v = ctx.viewer;
  const C = ctx.contract;
  const f = figures(ctx);
  const ap = canApprove(c, v);
  const wk = canWork(c, v);
  const cc = canClose(c, v);
  const whyWork = T(NO_WORK);
  const clauses = clausesOf(ctx);
  const terms = c.terms;
  const st = stage(c);

  const kindTitle = (e: Email): string => T(EMAIL_KIND[e.kind].title);
  const kindNoun = (e: Email): string => T(EMAIL_KIND[e.kind].noun);

  /* ---------- cards ---------- */
  const cards: Card[] = [];
  c.feed.forEach((it) => {
    if (it.thread && c.thread && it.thread !== c.thread) return;
    const d = blankCard(it);
    const i = it.i ?? 0;

    switch (it.k) {
      case "say":
        d.text = T(it.text);
        break;
      case "user":
        d.text = T(it.text);
        d.tag = it.tag ? T(it.tag) : "";
        break;

      case "q": {
        const q = questionDef(i, ctx);
        const current = c.q === i && c.phase === "interview";
        d.kicker = tr("Question {n} / 4", { n: i + 1 });
        d.title = q.text;
        d.lines = [q.hint];
        if (current) {
          d.chips = q.opts.map(
            (o): CardChip => ({
              label: o[1],
              act: q.multi ? "toggle" : "ans",
              p: q.multi ? { k: o[0] } : { q: i, val: o[0] },
              on: q.multi ? (c.goals.clauses ?? []).includes(o[0]) : false,
            }),
          );
          if (q.multi) {
            d.buttons = [button(tr("Continue"), "q3done", {}, { primary: true })];
            if (!c.goals.policy) d.buttons.push(button(tr("Add company policy (sample)"), "policy"));
            d.note = tr("Or type another clause below.");
          } else {
            d.note = tr("Or type your own answer below.");
          }
          d.current = true;
        } else {
          d.dim = true;
          d.lines = [tr("Answer: {answer}", { answer: answerLabel(c, ctx, i) })];
        }
        break;
      }

      case "outlook":
        d.kicker = tr("Before you start");
        d.title = tr("What you can expect to renegotiate");
        d.lines = [
          tr("Read from your contract, clause by clause, against common practice for this kind of agreement. This is general practice, not a market benchmark — the supplier may differ."),
        ];
        d.table = outlookTable(ctx);
        d.pin = c.phase === "interview" || c.phase === "plan";
        break;

      case "policy": {
        d.kicker = tr("Company policy added");
        d.title = "Procurement Policy v3.2";
        d.rows = [
          { k: tr("Scope"), v: tr("Software contracts above CHF 100k") },
          ...Object.values(POLICY).flatMap((rule): CardRow[] => (rule ? [{ k: rule.bind ? tr("Binding") : tr("Guidance"), v: T(rule.text) }] : [])),
        ];
        d.note = tr("No policy on file would not mean compliance has been checked. Raffa shows policy rules separately from its own proposals.");
        break;
      }

      case "plan": {
        const ver = it.ver ?? 0;
        const current = ver === c.planVer && !c.planOk && c.phase === "plan";
        const g = c.goals;
        d.kicker = tr("Plan v{ver}", { ver });
        d.title = tr("Renewal plan — {supplier}", { supplier: C.supplier });
        d.rows = [
          { k: tr("Situation"), v: tr("Ends {end} · notice by {cancel} · {chf} / yr · uplift {uplift}", { end: C.end, cancel: C.cancel, chf: chf(C.spend), uplift: C.uplift }) },
          { k: tr("Products"), v: answerLabel(c, ctx, 0) },
          { k: tr("Term"), v: answerLabel(c, ctx, 1) },
          { k: tr("Opening request"), v: answerLabel(c, ctx, 2) },
        ];
        clauses
          .filter((x) => (g.clauses ?? []).includes(x.k))
          .forEach((x) => {
            const rule = g.policy ? POLICY[x.lk] : undefined;
            const tag = rule ? ` · ${rule.bind ? tr("policy (binding)") : tr("policy (guidance)")}` : "";
            d.rows.push({ k: x.type, v: `${x.ask} · ${x.src}${tag}`, w: 600 });
          });
        d.rows.push({ k: tr("Hypotheses"), v: tr("The supplier may trade price for a longer term — I will ask for your limit when it happens.") });
        d.rows.push({ k: tr("Unknown"), v: tr("Usage data (not in the contract) · external market prices (not available, not used)") });
        if (current) {
          d.current = true;
          d.buttons = [button(tr("Confirm plan"), "confirmPlan", {}, { primary: true, off: !wk, why: whyWork }), button(tr("Edit"), "editPlan")];
        } else {
          d.dim = ver !== c.planVer;
        }
        if (ver === c.planVer) {
          if (!c.notice) d.buttons.push(button(tr("Prepare non-renewal notice"), "startNotice"));
          if (!c.alt) d.buttons.push(button(tr("Evaluate alternatives"), "startAlt"));
        }
        break;
      }

      case "email": {
        const e = c.emails[it.eid ?? -1];
        if (!e) return;
        const es = e.state;
        const superseded = es === "superseded";
        const current = !superseded && es !== "sent";
        const stateLabel: Record<Email["state"], string> = {
          draft: tr("Draft — to approve"),
          approved: tr("Approved — to send"),
          copied: tr("Copied — send to be confirmed"),
          sent: tr("Sent — confirmed by {by} on {at}", { by: e.sent?.by, at: e.sent?.at }),
          superseded: tr("Superseded — a newer reply arrived"),
        };
        d.kicker = `${kindTitle(e)} · v${e.ver}`;
        d.title = e.subject;
        d.dim = superseded;
        d.current = current;
        const order: Email["state"][] = ["draft", "approved", "copied", "sent"];
        d.steps = [tr("Draft"), tr("Approved"), tr("Copied"), tr("Sent (declared)")].map((label, n) => ({
          label,
          on: !superseded && order.indexOf(es) >= n,
          now: !superseded && order.indexOf(es) === n,
        }));
        d.email = {
          eid: e.id,
          to: e.to,
          toMissing: !e.to && current,
          toKey: `to:${e.id}`,
          state: stateLabel[es],
          body: e.body,
          attach: e.attach,
          editing: c.ui.edit === e.id && current,
          editBody: c.ui.editBody ?? "",
          showBody: !superseded,
          sentNote: e.sent?.edited
            ? tr("The text actually sent was edited after approval; it was confirmed afterwards and may differ from the approved version.")
            : "",
          attachNote: e.attach.length
            ? tr("Attach in your mailbox: {attach} — the Copy command does not include attachments.", { attach: e.attach.join(", ") })
            : "",
        };
        if (current) {
          if (!e.to && ctx.services.exampleAddress) d.buttons.push(button(tr("Use example address"), "toEx", { eid: e.id }));
          if (c.ui.edit === e.id) {
            d.buttons.push(button(tr("Save changes"), "saveEdit", { eid: e.id }, { primary: true }), button(tr("Cancel"), "cancelEdit"));
          } else if (c.ui.sentOpen === e.id) {
            const inputs: CardInput[] = [
              { label: tr("Recipient"), value: e.to, ui: `to:${e.id}`, type: "text" },
              { label: tr("Date and time sent"), value: c.ui.sentDate ?? "", ui: "sentDate", type: "text" },
              { label: tr("I edited the text in my mailbox"), value: !!c.ui.sentEdited, ui: "sentEdited", type: "check" },
            ];
            if (c.ui.sentEdited) inputs.push({ label: tr("Paste the text actually sent"), value: c.ui.sentText ?? "", ui: "sentText", type: "area" });
            d.inputs = inputs;
            d.buttons = [button(tr("Confirm sent"), "sentOk", { eid: e.id }, { primary: true, off: !wk, why: whyWork }), button(tr("Cancel"), "sentCancel")];
            d.note = tr("Raffa records your declaration of sending; it does not verify delivery or reading.");
          } else {
            if (es === "draft") {
              d.buttons.push(
                button(tr("Approve"), "approve", { eid: e.id }, {
                  primary: true,
                  off: !ap || !e.to,
                  why: !ap ? T(NO_APPROVE) : tr("Add the recipient first — I never invent addresses."),
                }),
              );
            }
            if (es === "approved" || es === "copied") {
              d.buttons.push(
                button(es === "copied" ? tr("Copy again") : tr("Copy email"), "copy", { eid: e.id }, { primary: es === "approved" }),
                button(tr("I sent it"), "sentOpen", { eid: e.id }, { primary: es === "copied" }),
              );
            }
            d.buttons.push(button(tr("Edit"), "edit", { eid: e.id }));
            if (es === "approved" || es === "copied") {
              d.note = tr("Approved by {approvedBy}. Any edit invalidates this approval. Copy does not mean sent.", { approvedBy: e.approvedBy });
            }
          }
        }
        break;
      }

      case "rem": {
        const r = c.rems[it.rid ?? -1];
        if (!r) return;
        d.kicker = tr("Check date");
        if (r.state === "proposed") {
          d.title = tr("When should I remind you to check for a reply?");
          d.lines = [tr("Proposed: {date}. Your contractual notice date stays {cancel} — the check date never replaces it.", { date: r.date, cancel: C.cancel })];
          d.inputs = [{ label: tr("Check date"), value: c.ui.remDate ?? r.date, ui: "remDate", type: "text" }];
          d.buttons = [button(tr("Confirm date"), "remOk", { rid: r.rid }, { primary: true, off: !wk, why: whyWork })];
          d.current = true;
        } else if (r.state === "set") {
          d.title = tr("Check set for {date}", { date: r.date });
          d.lines = [tr("Active even with the chat closed. It posts an app notice linked to this case — not a message to the supplier.")];
          d.current = true;
          d.tone = "wait";
        } else if (r.state === "due") {
          d.title = tr("Did you receive a reply?");
          d.lines = [tr("No reply has been registered in Raffa.")];
          d.buttons = [button(tr("Paste reply"), "openReply", {}, { primary: true }), button(tr("Prepare follow-up"), "followup"), button(tr("Change date"), "remChange")];
          d.current = true;
        } else {
          d.title = tr("Check {date} — closed", { date: r.date });
          d.dim = true;
          d.lines = [tr("Cancelled or updated after a new reply or follow-up.")];
        }
        break;
      }

      case "reply": {
        const r = c.reps[it.rid ?? -1];
        if (!r) return;
        d.kicker = tr("Supplier reply pasted · round {round}", { round: r.round });
        d.title = tr("Original text kept");
        d.pre = r.text;
        d.note = tr("Pasted by {by} on {at}. The original stays next to Raffa’s interpretation.", { by: r.by, at: r.at });
        d.dim = true;
        break;
      }

      case "ana": {
        const r = c.reps[it.rid ?? -1];
        if (!r) return;
        d.kicker = tr("Analysis · reply {round}", { round: r.round });
        d.title = tr("What changed");
        const statusLabel = {
          proposed: tr("Proposed"),
          unclear: tr("Unclear"),
          rejected: tr("Rejected"),
          conflict: tr("Conflict"),
        };
        d.table = {
          cols: [tr("Term⟦condition⟧"), tr("Supplier says"), tr("Your plan"), tr("Status")],
          layout: [{ min: 110, fr: 1 }, { min: 160, fr: 1.8 }, { min: 130, fr: 1.2 }, { min: 90, fr: 0.7 }],
          rows: r.rows.map((x): TableCell[] => [
            { v: x.label, w: 600 },
            { v: x.said + (x.warn ? ` — ${x.warn}` : ""), ...(x.warn && { tone: "alert" as const }) },
            { v: x.plan },
            { v: statusLabel[x.state], w: 600, ...(x.state !== "proposed" && { tone: "alert" as const }) },
          ]),
        };
        d.lines = [
          r.round === 1
            ? tr("Nothing is agreed yet: a supplier proposal is not an agreement. Payment terms are unclear — I will ask for clarification in the counter-proposal.")
            : tr("These are proposals backed by the supplier’s written reply. They become “agreed” only when you confirm them."),
        ];
        d.note = tr("Prototype: the reading of the reply is scripted for the demo scenario.");
        break;
      }

      case "dec": {
        const q = c.qs[it.did ?? -1];
        if (!q) return;
        d.kicker = tr("Decision needed");
        d.title = tr("The discount is tied to 24 months. What is your limit?");
        if (q.state === "open") {
          const price24 = priceLabel(f, ctx.services.counterPrice(ctx, 24));
          d.lines = [
            tr("Your plan asked for {answerLabel}. A longer commitment means less flexibility at the next renewal; a shorter one means no discount was offered. My only comparison is your current price ({priceLabel}) — no external benchmark.", { answerLabel: answerLabel(c, ctx, 1), priceLabel: priceLabel(f, f.p0) }),
          ];
          const options: Array<["a" | "b" | "c", string]> = [
            ["a", tr("Accept up to 24 months if unit price ≤ {price24}", { price24 })],
            ["b", tr("Stay at 12 months — decline the discount")],
            ["c", tr("24 months with an exit option at month 12")],
          ];
          d.chips = options.map(([opt, label]): CardChip => ({ label, act: "decide", p: { did: q.did, opt }, on: false, off: !wk }));
          d.current = true;
          d.note = wk ? "" : tr("Owner or delegate decides.");
        } else {
          d.dim = true;
          d.lines = [tr("Decision: {decision} · {by}", { decision: q.label ?? "", by: q.by ?? "" })];
        }
        break;
      }

      case "agree": {
        const q = c.qs[it.did ?? -1];
        if (!q) return;
        const conflict = liabilityConflict(c);
        d.kicker = tr("Confirm terms");
        d.title = tr("Mark these as agreed?");
        const withUnit = (key: string, val: string | null | undefined): string => {
          if (!val) return "—";
          if (key === "term") return tr("{n} months", { n: val });
          if (key === "liab") return tr("{n} months of fees", { n: val });
          return val;
        };
        d.rows = Object.entries(terms)
          .filter(([, t]) => t.state === "proposed" || t.state === "agreed")
          .map(([key, t]): CardRow => ({
            k: T(t.label),
            v: `${withUnit(key, t.val)} · ${t.state === "agreed" ? tr("agreed") : tr("proposed in reply {ev}", { ev: t.ev })}`,
            w: 600,
          }));
        const missing = Object.values(terms)
          .filter((t) => t.state === "open")
          .map((t) => T(t.label));
        if (missing.length) {
          d.rows.push({ k: tr("Not covered by this reply"), v: tr("{terms} — still open, not part of this confirmation.", { terms: missing.join(", ") }), tone: "muted" });
        }
        if (q.state === "agree") {
          d.current = true;
          d.lines = [tr("“Agreed” needs the written exchange as evidence and your confirmation. Silence is not acceptance.")];
          if (conflict) {
            d.banner = tr("Liability 18 months conflicts with Procurement Policy v3.2 (binding, min. 24). A deviation needs authorization from the competent role — collected outside Raffa in this release.");
            d.inputs = [{ label: tr("Exception authorized by (name / role)"), value: c.ui.auth ?? "", ui: "auth", type: "text" }];
          }
          d.buttons = [button(tr("Confirm as agreed"), "agreeOk", { did: q.did }, { primary: true, off: !wk, why: whyWork })];
        } else {
          d.dim = true;
          d.lines = [tr("Confirmed as agreed by {by}", { by: q.by })];
        }
        break;
      }

      case "doc": {
        const doc = c.docs[(it.v ?? 0) - 1];
        if (!doc) return;
        const latest = it.v === c.docs.length;
        const diffs = doc.rows.filter((x) => x.res === "diff").length;
        const unread = doc.rows.filter((x) => x.res === "unread").length;
        const matching = doc.rows.filter((x) => x.res === "ok" || x.res === "manual").length;
        d.kicker = tr("Contract draft v{n}", { n: it.v ?? 0 });
        d.title = doc.file;
        d.banner = tr("Draft — not an active contract. It does not count as spend and does not trigger a new renewal.");
        const resultLabel = { ok: tr("Matches"), diff: tr("Differs"), unread: tr("Not read"), manual: tr("Manual") };
        d.table = {
          cols: [tr("Point"), tr("Agreed"), tr("In the draft"), tr("Result"), tr("Source")],
          layout: [{ min: 110, fr: 1 }, { min: 130, fr: 1.3 }, { min: 150, fr: 1.6 }, { min: 80, fr: 0.6 }, { min: 70, fr: 0.5 }],
          rows: doc.rows.map((x): TableCell[] => {
            const bad = x.res === "diff" || x.res === "unread";
            return [
              { v: x.label, w: 600 },
              { v: x.agreed },
              { v: x.draft, ...(bad && { tone: "alert" as const }) },
              { v: resultLabel[x.res], w: 600, ...(bad && { tone: "alert" as const }) },
              { v: x.src, tone: "muted" },
            ];
          }),
        };
        d.lines = [
          [
            tr(diffs === 1 ? "{n} difference" : "{n} differences", { n: diffs }),
            tr(unread === 1 ? "{n} point not read" : "{n} points not read", { n: unread }),
            tr("{n} match", { n: matching }),
          ].join(" · "),
        ];
        if (latest && c.phase === "doc_check") {
          d.current = true;
          if (diffs) d.buttons.push(button(tr("Prepare correction email"), "fixEmail", {}, { primary: true }));
          if (unread) d.buttons.push(button(tr("Mark annex verified manually"), "markAnnex", { v: it.v }, { off: !wk, why: whyWork }));
          d.note = tr("A revised draft restarts the check. When a new version arrives, add it with “Add contract”.");
        } else if (!latest) {
          d.dim = true;
        } else {
          d.lines.push(tr("Every point was read and matches the agreed position."));
        }
        break;
      }

      case "signwait":
        d.kicker = tr("Signature");
        d.title = tr("Ready to sign — outside Raffa");
        d.lines = [tr("Signing is not done in Raffa and I do not certify signatures. When it is signed, add the signed contract.")];
        if (c.phase === "sign_wait" || c.phase === "sign_declared") {
          d.current = true;
          d.buttons = [button(tr("Add signed contract (sample)"), "addDoc", {}, { primary: true })];
          if (c.phase === "sign_wait") d.buttons.push(button(tr("Signature declared — file missing"), "signDeclared"));
          else d.banner = tr("Signature declared — document to acquire. Task open.");
        } else {
          d.dim = true;
        }
        break;

      case "sig": {
        if (!c.signed) return;
        d.kicker = tr("Signed document");
        d.title = c.signed.file;
        d.lines = [tr("Compared with the last verified draft (v{length}): same content on all 8 points. No differences to document.", { length: c.docs.length })];
        if (c.phase === "sign_confirm") {
          d.current = true;
          d.lines.push(tr("Confirm that this is the final signed version required by your process. A file name containing “signed” is not enough."));
          d.buttons = [button(tr("Confirm as final signed document"), "sigOk", {}, { primary: true, off: !cc, why: T(NO_CLOSE) })];
          if (!cc) d.note = tr("Waiting for {owner} or a delegate with closing rights.", { owner: c.owner });
        } else {
          d.dim = true;
          d.lines.push(tr("Confirmed by {confirmedBy}.", { confirmedBy: c.signed.confirmedBy }));
        }
        break;
      }

      case "pipe": {
        const pipe = c.pipe;
        if (!pipe) return;
        const stages = [tr("Upload"), tr("Processing"), tr("Extraction"), tr("Review"), tr("Validation")];
        d.kicker = tr("Negotiation completed · new contract in review");
        d.title = tr("Standard pipeline — same one, inside the chat");
        d.steps = stages.map((label, n) => ({ label, on: pipe.stage >= n, now: pipe.stage === n }));
        if (pipe.stage >= 3) {
          d.lines = [
            tr("{total} facts extracted · {aboveThreshold} above threshold · {length} below 80% for you to decide. An extraction error never reopens the signed negotiation.", { total: pipe.facts.total, aboveThreshold: pipe.facts.aboveThreshold, length: pipe.weak.length }),
          ];
          d.fields = pipe.weak.map((w, n) => ({
            name: T(w.name),
            val: w.val,
            conf: `${w.conf}%`,
            state: w.state,
            open: w.state === "open",
            doneLabel: w.state === "accepted" ? tr("Accepted") : w.state === "corrected" ? tr("Corrected") : "",
            inKey: `cf${n}`,
            inVal: String(c.ui[`cf${n}`] ?? ""),
            i: n,
          }));
          if (pipe.stage === 3) {
            d.current = true;
            d.buttons = [
              button(tr("Mark as validated"), "validate", {}, {
                primary: true,
                off: pipe.weak.some((x) => x.state === "open"),
                why: tr("Decide both fields first"),
              }),
            ];
          }
        } else {
          d.lines = [tr("Processing the signed contract…")];
        }
        break;
      }

      case "out": {
        const o = c.outcome;
        if (o?.kind !== "renewed") return;
        d.kicker = tr("Outcome");
        d.title = tr("Renewed — new contract scheduled");
        d.rows = [
          { k: tr("Starts"), v: o.start },
          { k: tr("Term ends"), v: o.end },
          { k: tr("Next notice deadline"), v: o.cancel },
          { k: tr("New price"), v: tr("{price} (was {was})", { price: o.price, was: o.was }) },
          { k: tr("Annual change vs previous (same quantities)"), v: `${o.delta > 0 ? "−" : "+"}${chf(Math.abs(o.delta))}`, w: 600 },
          { k: tr("Previous contract"), v: tr("stays active until {end}", { end: C.end }) },
        ];
        d.lines = [
          tr("Signature, validation and start date are separate events. The change is computed from the two contracts’ prices — not a market saving. The next cycle is planned from the validated dates."),
        ];
        d.buttons = [button(tr("Open Contract 360"), "nav", { to: "c360" }), button(tr("See in Renewals"), "nav", { to: "renewals" })];
        break;
      }

      case "ncheck": {
        const days = daysTo(ctx, C.cancel);
        const clause = ctx.detail.push.find((x) => /renew/i.test(x.type)) ?? ctx.detail.push[0];
        d.kicker = tr("Non-renewal notice");
        d.title = tr("What the contract requires");
        d.rows = [
          { k: tr("Clause"), v: clause?.src ?? "—" },
          { k: tr("Deadline"), v: tr("{date} · {n} days left", { date: C.cancel, n: days }), w: 600 },
          { k: tr("Effective end"), v: C.end },
          { k: tr("Form"), v: tr("Written notice. The contract lists no email address for notices — recipient to be confirmed.") },
          { k: tr("Tasks"), v: tr("1 · Email notice (draft below) · 2 · Registered letter if required — confirm with Legal") },
        ];
        if (days < 0) d.banner = tr("The window appears expired or contradictory. I cannot promise an effective notice — verify with Legal.");
        if (c.notice?.state === "check") {
          d.current = true;
          d.buttons = [button(tr("Prepare the notice email"), "nStart", {}, { primary: true })];
        } else {
          d.dim = true;
        }
        break;
      }

      case "nrec": {
        const notice = c.notice;
        if (!notice) return;
        d.kicker = tr("Notice communicated");
        d.title = tr("Status: notice communicated — outcome to verify");
        d.lines = [tr("Sending the email is not the same as the contract ending. Attach the supplier’s confirmation, then record the outcome.")];
        if (notice.state === "sent") {
          d.current = true;
          if (notice.evidence) {
            d.lines.push(tr("Evidence attached: {evFile}", { evFile: notice.evFile }));
          } else {
            d.buttons.push(button(tr("Add receipt (sample)"), "nReceipt"));
            d.inputs = [{ label: tr("Proceed without evidence"), value: !!c.ui.noEv, ui: "noEv", type: "check" }];
          }
          d.buttons.push(button(tr("Record outcome: not renewed"), "nOut", {}, { primary: true, off: !cc, why: T(NO_CLOSE) }));
        } else {
          d.dim = true;
        }
        break;
      }

      case "nout": {
        const exitTasks = ctx.detail.obligationsSupplier.filter((x) => /return|data|offboard/i.test(x.text)).map((x) => `${x.text} (${x.when})`);
        d.kicker = tr("Outcome");
        d.title = tr("Not renewed — notice");
        d.rows = [
          { k: tr("Effective end"), v: C.end },
          { k: tr("Successor"), v: c.alt?.chosen ?? tr("none — nothing activated") },
          { k: tr("Exit tasks still open"), v: exitTasks.join(" · ") || tr("Return of data per the agreement") },
        ];
        d.buttons = [button(tr("Open Contract 360"), "nav", { to: "c360" })];
        break;
      }

      case "areq": {
        const alt = c.alt;
        if (!alt) return;
        d.kicker = tr("Alternatives");
        d.title = tr("Essential requirements");
        d.lines = [
          tr("From your contract and plan. Needed from {addDays}. I compare only candidates you name and offers you upload — I do not search the market.", { addDays: addDays(C.end, 1) }),
        ];
        if (alt.state === "req") {
          d.current = true;
          d.chips = alt.reqs.map((label): CardChip => ({ label, act: "ui", p: { k: "noop", v: 1 }, on: true }));
          d.buttons = [button(tr("Confirm requirements"), "altReqs", {}, { primary: true })];
        } else {
          d.dim = true;
          d.rows = alt.reqs.map((r): CardRow => ({ k: "•", v: r }));
        }
        break;
      }

      case "acand": {
        const alt = c.alt;
        if (!alt) return;
        d.kicker = tr("Alternatives");
        d.title = tr("Which vendors should I ask?");
        d.lines = [alt.vendors.length ? tr("Candidates: {names}", { names: alt.vendors.join(", ") }) : tr("No candidate yet.")];
        if (alt.state === "cand") {
          d.current = true;
          d.inputs = [{ label: tr("Vendor name"), value: c.ui.altName ?? "", ui: "altName", type: "text" }];
          d.buttons = [
            button(tr("Add vendor"), "altAdd"),
            ...(ctx.services.exampleVendors().length ? [button(tr("Add two examples"), "altEx")] : []),
            button(tr("Prepare RFQ emails"), "altRfq", {}, { primary: true, off: !alt.vendors.length, why: tr("Add at least one vendor") }),
          ];
        } else {
          d.dim = true;
        }
        break;
      }

      case "aoff": {
        const offer = c.alt?.offers[it.vendor ?? ""];
        if (!offer || offer === true) return;
        d.kicker = tr("Offer · {vendor}", { vendor: it.vendor ?? "" });
        d.title = tr("Declared by the vendor — specific evidence, not a market benchmark");
        d.rows = [
          { k: tr("Price"), v: f.p0 < 1000 ? `CHF ${offer.price} per ${f.unit}` : `${chf(offer.total)} / yr` },
          { k: tr("Term"), v: tr("{n} months", { n: offer.term }) },
          { k: tr("Requirements covered"), v: offer.cover },
          { k: tr("Migration"), v: offer.mig },
          { k: tr("Missing data"), v: offer.gap, tone: "alert" },
        ];
        break;
      }

      case "acmp": {
        const alt = c.alt;
        if (!alt) return;
        const names = Object.keys(alt.offers).filter((k) => !k.endsWith("_rfq"));
        d.kicker = tr("Comparison");
        d.title = tr("Offers vs your current contract");
        const priceOf = (price: number, total: number): string => (f.p0 < 1000 ? `CHF ${price} per ${f.unit}` : `${chf(total)} / yr`);
        d.table = {
          cols: [tr("Option"), tr("Price"), tr("Term"), tr("Coverage"), tr("Migration"), tr("Missing")],
          layout: [{ min: 100, fr: 1 }, { min: 130, fr: 1.2 }, { min: 70, fr: 0.6 }, { min: 70, fr: 0.6 }, { min: 80, fr: 0.7 }, { min: 130, fr: 1.2 }],
          rows: [
            [
              { v: tr("Current · {supplier}", { supplier: C.supplier }), w: 600 },
              { v: priceOf(f.p0, f.p0) },
              { v: tr("to {end}", { end: C.end }) },
              { v: "—" },
              { v: "—" },
              { v: tr("uplift {uplift}", { uplift: C.uplift }) },
            ],
            ...names.flatMap((n): TableCell[][] => {
              const o = alt.offers[n];
              if (!o || o === true) return [];
              return [[{ v: n, w: 600 }, { v: priceOf(o.price, o.total) }, { v: tr("{n} mo", { n: o.term }) }, { v: o.cover }, { v: o.mig }, { v: o.gap, tone: "alert" }]];
            }),
          ],
        };
        d.lines = [tr("Costs are what each vendor declared, for the lines they quoted. Transition effort and continuity matter: where data is missing I say so instead of estimating.")];
        if (alt.state === "compare") {
          d.current = true;
          d.buttons = names.map((n) => button(tr("Select {n}", { n }), "altSel", { vendor: n }, { primary: true, off: !cc, why: T(NO_CLOSE) }));
          d.note = tr("Add the other vendor’s offer from its thread to compare more.");
        } else {
          d.dim = true;
        }
        break;
      }

      case "asel": {
        const alt = c.alt;
        if (!alt) return;
        d.kicker = tr("Replacement");
        d.title = tr("{chosen} selected — the current contract is NOT closed", { chosen: alt.chosen });
        d.rows = [
          { k: tr("Exit track"), v: alt.exitDone ? tr("Done — not renewed") : tr("Open — send notice to {supplier}", { supplier: C.supplier }), w: 600, ...(!alt.exitDone && { tone: "alert" as const }) },
          { k: tr("New agreement"), v: alt.newDone ? tr("Signed contract added") : tr("Open — add the signed contract"), w: 600, ...(!alt.newDone && { tone: "alert" as const }) },
        ];
        d.lines = [
          tr("The case closes as “replacement” only when the previous contract’s obligations are resolved or assigned, and the new contract is acquired. No implicit termination."),
        ];
        if (!alt.exitDone || !alt.newDone) {
          d.current = true;
          if (!alt.exitDone && !c.notice) d.buttons.push(button(tr("Prepare non-renewal notice"), "startNotice", {}, { primary: true }));
          if (!alt.newDone) d.buttons.push(button(tr("Add new vendor contract (sample)"), "altNew", { vendor: alt.chosen ?? "" }));
        }
        break;
      }

      case "asel2":
        d.kicker = tr("New vendor");
        d.title = tr("New contract received — enters the standard pipeline once");
        d.lines = [tr("Extraction and review of {newFile} run like any other contract. It stays linked to this case and to the replaced contract.", { newFile: c.alt?.newFile })];
        d.dim = true;
        break;
    }
    d.plain = it.k === "say" || it.k === "user";
    cards.push(d);
  });

  /* ---------- header ---------- */
  const rem = c.rems.find((x) => x.state === "set" || x.state === "due");
  const currentEmail = [...c.emails].reverse().find((e) => e.state !== "sent" && e.state !== "superseded" && e.kind !== "notice" && e.kind !== "rfq");
  const approverOrDelegate = Object.keys(c.delegates)[0] ?? c.approver;
  const phase = PHASES[c.phase] ?? PHASES.email;
  let nextText: Msg = phase.next;
  let nextWho = phase.who;
  if (c.phase === "email" && currentEmail) {
    if (currentEmail.state === "draft" && !currentEmail.to) {
      nextText = msg("Add the recipient");
      nextWho = "owner";
    } else if (currentEmail.state === "draft") {
      nextText = msg("Approve the email");
      nextWho = "approver";
    } else {
      nextText = msg("Copy, send and confirm");
      nextWho = "owner";
    }
  }
  const nextActor =
    nextWho === "approver" ? approverOrDelegate : nextWho === "supplier" ? tr("Supplier") : nextWho === "you" ? tr("You") : nextWho === "owner" ? c.owner : "—";
  const nextLine = `${T(nextText)}${rem && c.phase === "waiting" ? ` · ${tr("check {date}", { date: rem.date })}` : ""}`;

  let status = T(phase.status);
  if (c.outcome?.kind === "terminated") {
    status = c.alt?.chosen ? tr("Not renewed · replaced by {name}", { name: c.alt.chosen }) : tr("Not renewed");
  }

  const tracks: string[] = [];
  if (c.notice) {
    const label = { check: tr("to prepare"), email: tr("email in progress"), sent: tr("communicated — outcome to verify"), out: tr("not renewed") }[c.notice.state];
    tracks.push(tr("Notice: {state}", { state: label }));
  }
  if (c.alt) {
    const label = {
      req: tr("requirements"),
      cand: tr("candidates"),
      rfq: tr("RFQ sent"),
      compare: tr("comparing"),
      selected: tr("selected {chosen}", { chosen: c.alt.chosen }),
    }[c.alt.state];
    tracks.push(tr("Alternatives: {state}", { state: label }));
  }

  const renewed = c.outcome?.kind === "renewed" ? c.outcome : null;
  const people: CasePerson[] = c.members.map((name) => {
    const responsibility = responsibilityOf(c, name);
    // A delegation is independent of the main responsibility: the owner can also be a delegate.
    const delegated = !!c.delegates[name];
    const closes = !!c.delegates[name]?.close;
    const role =
      responsibility === "approver"
        ? tr("Approver · CPO")
        : responsibility === "owner"
          ? delegated
            ? closes
              ? tr("Owner · works the case · delegate, can close")
              : tr("Owner · works the case · delegate")
            : tr("Owner · works the case")
          : delegated
            ? closes
              ? tr("Delegate · can close")
              : tr("Delegate")
            : tr("Collaborator");
    return {
      name,
      role,
      responsibility,
      isOwner: responsibility === "owner" || responsibility === "approver",
      isDelegate: delegated,
      canClose: closes,
      viewing: name === v,
    };
  });
  const responsibility = responsibilityOf(c, v);

  const hdr: CaseHeader = {
    title: tr("Renewal · {supplier} · {contract} {from}–{to}", { supplier: C.supplier, contract: C.name.split(" — ")[0] ?? C.name, from: C.start.slice(-4), to: C.end.slice(-4) }),
    supplier: C.supplier,
    contract: C.name,
    status,
    next: `${nextActor === "—" ? "" : `${nextActor} — `}${nextLine}`,
    approver: c.approver,
    noticeBy: renewed ? renewed.cancel : C.cancel,
    noticeDays: renewed ? renewed.cancelDays : daysTo(ctx, C.cancel),
    end: renewed ? tr("{end} (new contract; previous ends {previous})", { end: renewed.end, previous: C.end }) : C.end,
    owner: c.owner,
    tracks,
    warn: c.ui.warn ?? "",
    people,
    youRole: T(RESPONSIBILITY_LABEL[responsibility]),
  };

  /* ---------- summary ---------- */
  const termStateLabel = { open: tr("Open"), proposed: tr("Proposed"), agreed: tr("Agreed"), rejected: tr("Rejected"), unclear: tr("Unclear") };
  const termsView: TermView[] = Object.values(terms).map((t) => ({ label: T(t.label), val: t.val || "—", state: termStateLabel[t.state], status: t.state }));
  const summary: CaseView["summary"] = {
    goals: [
      { k: tr("Products"), v: c.goals.prod ? answerLabel(c, ctx, 0) : "—" },
      { k: tr("Term"), v: c.goals.term ? answerLabel(c, ctx, 1) : "—" },
      { k: tr("Target"), v: c.goals.target !== undefined ? answerLabel(c, ctx, 2) : "—" },
      { k: tr("Clauses"), v: c.goals.clauses ? answerLabel(c, ctx, 3) : "—" },
      { k: tr("Plan"), v: c.planVer ? (c.planOk ? tr("v{ver} · confirmed", { ver: c.planVer }) : tr("v{ver} · to confirm", { ver: c.planVer })) : "—" },
    ],
    terms: termsView,
    decisions: [...c.decs].reverse().map((x) => ({ by: x.by, at: x.at, text: T(x.text) })),
    refs: tr(c.signed ? "{emails} emails · {replies} replies · {drafts} drafts · 1 signed" : "{emails} emails · {replies} replies · {drafts} drafts", {
      emails: c.emails.length,
      replies: c.reps.length,
      drafts: c.docs.length,
    }),
  };

  /* ---------- actions, threads, demo shortcuts ---------- */
  const closed = !!c.outcome || c.phase === "pipeline";
  const actions: CaseView["actions"] = [];
  if ((!closed && c.emails.some((e) => e.state === "sent")) || c.round > 0) actions.push({ label: tr("Paste reply"), act: "openReply" });
  if (!closed && c.agreed) actions.push({ label: tr("Add contract"), act: "addDoc" });
  if (!c.planOk && !c.goals.policy && c.phase !== "interview") actions.push({ label: tr("Add policy"), act: "policy" });

  const threads: CaseView["threads"] =
    c.alt && c.alt.vendors.length && ["rfq", "compare", "selected"].includes(c.alt.state)
      ? c.alt.vendors.map((t) => ({ t, on: c.thread === t, offer: !c.alt?.offers[t] && c.alt?.state !== "selected", hasOffer: !!c.alt?.offers[t] }))
      : [];
  const demo: CaseView["demo"] = !closed && c.rems.some((r) => r.state === "set") ? [{ label: tr("Jump to the check date"), act: "ff" }] : [];

  /* ---------- "since your last visit" ---------- */
  const sinceLabel: Partial<Record<FeedItem["k"], string>> = {
    email: tr("prepared an email"),
    user: tr("wrote in the chat"),
    reply: tr("pasted a supplier reply"),
    ana: tr("analysed the reply"),
    dec: tr("asked for a decision"),
    agree: tr("proposed terms to confirm"),
    doc: tr("checked a draft"),
    sig: tr("received the signed file"),
    plan: tr("prepared the plan"),
    rem: tr("proposed a check date"),
    say: tr("posted an update"),
    policy: tr("added a policy"),
  };
  let since: CaseView["since"] = null;
  if (c.ui.since) {
    const byUser: Record<string, string[]> = {};
    c.ui.since.items.forEach((x) => {
      const label = sinceLabel[x.k] ?? tr("updated the case");
      const list = (byUser[x.by] ??= []);
      if (!list.includes(label)) list.push(label);
    });
    since = { n: c.ui.since.n, lines: Object.entries(byUser).map(([who, labels]) => `${who}: ${labels.join(", ")}`) };
  }

  /* ---------- timeline ---------- */
  const kindLabel: Partial<Record<FeedItem["k"], string>> = {
    email: tr("Email"),
    reply: tr("Supplier reply"),
    ana: tr("Analysis"),
    dec: tr("Decision asked"),
    agree: tr("Terms to confirm"),
    doc: tr("Draft checked"),
    sig: tr("Signed file"),
    plan: tr("Plan"),
    rem: tr("Check date"),
    out: tr("Outcome"),
    nrec: tr("Notice"),
    nout: tr("Outcome"),
    pipe: tr("Acquisition"),
    policy: tr("Policy"),
    ncheck: tr("Notice check"),
    areq: tr("Alternatives"),
    acand: tr("Alternatives"),
    aoff: tr("Offer"),
    acmp: tr("Comparison"),
    asel: tr("Replacement"),
    signwait: tr("Signature"),
  };
  const emailStateLabel: Record<Email["state"], string> = {
    draft: tr("draft"),
    approved: tr("approved"),
    copied: tr("copied"),
    sent: tr("sent"),
    superseded: tr("superseded"),
  };
  const timeline: TimelineEntry[] = [];
  c.feed.forEach((it) => {
    if (it.k === "q" || it.k === "outlook") return;
    let label: string;
    let text: string;
    if (it.k === "say") {
      label = "Raffa.ai";
      text = T(it.text).split(/(?<=[.!?])\s/)[0] ?? "";
    } else if (it.k === "user") {
      const own = T(it.text);
      label = it.by;
      text = own.length > 90 ? `${own.slice(0, 88)}…` : own;
    } else if (it.k === "email") {
      const e = c.emails[it.eid ?? -1];
      label = kindLabel.email ?? "Email";
      text = e ? `${kindNoun(e)} v${e.ver} — ${emailStateLabel[e.state]}` : "";
    } else {
      label = kindLabel[it.k] ?? it.k;
      text = sinceLabel[it.k] ?? "";
    }
    timeline.push({ at: it.at.slice(0, 6), time: it.at.slice(-5), by: it.by, label, text, mine: it.by === v });
  });
  timeline.reverse();

  const upcoming: CaseView["upcoming"] = c.outcome
    ? []
    : [
        { date: addDays(C.cancel, -30), label: tr("Go / no-go call: renew, renegotiate or exit") },
        { date: C.cancel, label: tr("Notice deadline") },
        { date: C.end, label: tr("Term ends") },
      ]
        .map((x) => ({ ...x, days: daysTo(ctx, x.date) }))
        .filter((x) => x.days >= 0)
        .map((x) => ({ date: x.date, label: x.label, when: tr("{n} days", { n: x.days }) }));

  const emailsList: CaseView["emailsList"] = c.emails.map((e) => ({
    label: `${kindNoun(e)} · v${e.ver}`,
    state: { draft: tr("Draft"), approved: tr("Approved"), copied: tr("Copied"), sent: tr("Sent"), superseded: tr("Superseded") }[e.state],
    pending: e.state === "draft" || e.state === "approved" || e.state === "copied",
  }));
  const docsList: CaseView["docsList"] = [
    ...c.docs.map((d) => {
      const open = d.rows.filter((x) => x.res === "diff" || x.res === "unread").length;
      return { label: d.file, state: open ? tr("{n} open points", { n: open }) : tr("matches"), open: open > 0, final: false };
    }),
    ...(c.signed ? [{ label: c.signed.file, state: c.signed.confirmedBy ? tr("final · confirmed") : tr("to confirm"), open: false, final: true }] : []),
  ];

  return {
    outlook: outlookTable(ctx),
    timeline,
    upcoming,
    stage: st,
    steps7: STEPS.map((x, n) => ({ label: T(x), n: n + 1, done: n < st, now: n === st })),
    check: checklist(c, ctx),
    example: T(EXAMPLES[st]),
    since,
    emailsList,
    docsList,
    hdr,
    summary,
    cards,
    actions,
    threads,
    demo,
    reply: { open: !!c.ui.replyOpen, text: c.ui.replyText ?? "", from: [...c.emails].reverse().find((e) => e.to)?.to ?? "—" },
    shortStatus: shortStatus(c, lang),
    permissions: permissionsOf(c, v),
    responsibility,
  };
}

function daysTo(ctx: CaseContext, date: string): number {
  return daysBetween(ctx.today, date);
}
