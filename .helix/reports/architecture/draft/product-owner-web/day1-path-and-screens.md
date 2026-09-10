# Product Owner — Web delta (independent lane)

Author: product-owner seat
Scope: this pass only. ADR-001…017 and epic-01…05 are **done** and are cited, not reopened.
Ground truth: `inputs/web-integration-brief.md`, `inputs/product-spec.md` §16/§20,
`reports/context/web-integration-mandate.md`, `reports/context/product-context.md`,
and the Claude Design handoff under `inputs/design/` (`ia.md`, `screens.md`,
`design-system.md`, `prototypes/day1-demo.html`).

This lane maps spec §16 R0–R4 and §20 Day-1 to **screens**, not endpoints. It
contains no backend capabilities, no new stack, no module redesign.

---

## 1. The bar: §16 and §20 are user outcomes, delivered in the browser

Spec §16 definitions of success and §20 Definition-of-V1-done are phrased as
procurement-user outcomes ("upload and ask", "does not miss renewal windows",
"a new proposal can be assessed in minutes"). An API endpoint is *not* a
delivered screen. This pass exists because E02–E05 decomposed `layer: backend`;
after E05 a reviewer on `demo` could have a complete API and zero portfolio,
Contract 360, Ask Raffa, renewals, savings, or quote-check UI.

**Success for this pass (product-owner owned):** a procurement user on Azure
`demo`, after the last web slice is promoted (`demo-v*`, ADR-016), completes
§20 **in the browser** against the Claude Design prototype (`inputs/design/prototypes/day1-demo.html`).
Not a localhost `config.json` shell, not a raw Swagger page.

---

## 2. Screen inventory — §16 R0–R4 → screens

Every §16 row must have a screen (or a named non-goal against spec §1.2). The
inventory below is the product-owner view; the screen detail lives in
`inputs/design/prototypes/screens.md` (ux-ui-designer owns the 1:1 annex). I
concur with that annex and cite it — I do not re-author the pixels.

| §16 release | Definition of success | Screen(s) | Prototype ref |
|---|---|---|---|
| R0 — Foundation | Secure workspace ingests documents | Sign-in → workspace · Members & roles · Upload → document status · audit read-back | screens §1, §2, §3 |
| R1 — Contract Intelligence | Upload, ask reliable questions | Portfolio · Contract 360 · Review/correct · Ask Raffa + citations | screens §4–§7 |
| R2 — Renewals | Don't miss material renewal windows | Renewal pipeline · insight card · action | screens §8 |
| R3 — Savings | Quantify credible savings | Home (Savings KPIs + opportunities) | screens §9 |
| R4 — Quote Check | Proposal assessed in minutes | Quote check stepper (Extract → Assessment → Target → Negotiation) + outcome | screens §10 |

**No release row is a non-goal.** Every R0–R4 row maps to at least one screen.
(Spec §1.2 non-goals — CLM authoring, e-signature, PO/invoice, sourcing/RFP,
ERP replacement — are *features absent from the product*, not §16 rows; they
are already enforced by the §16 table itself and need no screen.)

---

## 3. §20 Day-1 ladder → one clickable browser path

`inputs/design/prototypes/ia.md` already records a single clickable Day-1 path.
As product owner I confirm it satisfies the letter of §20, step by step:

| §20 step | Browser screen | Day-1 path position |
|---|---|---|
| Create a workspace and invite Procurement users | Sign-in → workspace · Members & roles | 1–2 |
| Upload a portfolio of contracts; classify/extract/structure | Upload → document status (processing → needs_review → ready) | 3 |
| Ask reliable questions with source evidence | Ask Raffa + numbered citations (+ one abstain) | after review |
| See renewal and cancellation deadlines | Portfolio attention strip · Contract 360 · Renewal pipeline | mid-path |
| See relevant contract/commercial risks | Contract 360 › Risks · Overview "Top risks" | mid-path |
| See market benchmarks where available | Contract 360 › Benchmark · Quote check assessment | mid–late |
| See prioritized savings opportunities | Home (Savings KPIs + opportunities) | after renewal action |
| Upload quote → line-level assessment → target → strategy | Quote check stepper | late |
| Record outcome, track realized savings | Quote outcome → Home "Savings Realized" updates | end |
| Use outcome as permissioned learning data | recorded outcome (fed to learning; no separate screen required) | end |

The single navigable flow is: **sign in → pick workspace → invite Procurement →
upload → processing → needs_review → review critical fields → Contract 360 →
Ask (citations + one abstain) → Renewals → act → Home shows opportunity → Quote
check → record outcome → Home Savings Realized updates.** This is the
integration-smoke script for the last web-wave story.

---

## 4. Roles on the Day-1 path (product-owner concur)

`ia.md` models two Day-1 roles; I concur and do not add a third nav variant:

- **Workspace Admin** — everything + Workspace & members.
- **Procurement** — everything except member management (sees "request access").

Legal / Finance / read-only exist in the permission model but are **not**
separate nav variants in V1. This matches spec §3.1 and does not re-open the
role model (an ADR-001…017 concern).

---

## 5. e06+ cut — which screens are in, and in what order

The brief proposes an epic-06 cut; as product owner I **approve the proposed
six-slice sequence** with one refinement: the last slice must carry the named
Day-1 integration-smoke story (not be implicit).

| Wave | Screen set | §16/§20 coverage |
|---|---|---|
| e06 | Design system + app shell (nav rail, auth gate, workspace switch, global Ask bar; `config.json` already real) | shell supports all rows |
| e07 | R0 UI — invite/roles, upload, document status, audit read-back | R0; §20 steps 1–3 |
| e08 | R1 UI — portfolio + filters, Contract 360 (10 tabs), review/correct, Ask Raffa + citations/abstain | R1; §20 "ask with evidence", risks |
| e09 | R2 UI — renewal pipeline, insight card, action | R2; §20 "don't miss windows" |
| e10 | R3 UI — savings KPIs + opportunities list | R3; §20 "prioritized savings" |
| e11 | R4 UI — quote check stepper + negotiation + outcome → Savings Realized **+ `us-final-integration` (browser Day-1 on `demo`)** | R4; §20 remainder |

Order follows the R0→R4 user ladder from §16 — no capability UI is cut. The
shell (e06) must land first because every later screen renders inside it.

**Cut decision (product-owner):** all six waves are **in**. Nothing in the
mandate authorizes deferring a §16 row or §20 step. If delivery capacity forces
a split, the only acceptable re-bucket is moving e10 (R3 savings) after e11
(R4 quote check) *only if* the integration-smoke stays last — I do not
recommend it; savings-opportunity creation is a prerequisite of the quote
outcome → Savings Realized step in the Day-1 path.

---

## 6. Open questions I carry (for the table, not self-answered here)

1. **`us-final-integration` ownership** — must be a `layer: web` story whose
   "done" is the §20 browser walk on `demo`, not `dotnet test`. Delivery-manager
   confirms the slice calendar places it last.
2. **Thin API gaps** — I assert zero product-blocking gaps because the
   designer mapped screens to existing §7–§12 endpoints; software-architect
   confirms or names them. If named, they are additive `layer: backend` thin
   tasks and must not delay the shell (e06) or the R0 UI (e07).
3. **`config.json` realness** — the mandate says it is "already real" (E01/F07).
   I treat it as the auth/workspace gate exists; client-architect confirms no
   MSAL/config delta blocks e06.

---

## 7. What I explicitly did NOT do

- No ADR authored (ux-ui-designer owns ADR-018+; I concur at the table).
- No workitems/slices/plan files written.
- No backend capabilities added; no module redesign.
- No protected file touched (`product-context.md`, `locked-decisions.md`,
  ADR-001…017, epic-01…05, `wave-spec.execution.yaml`, e01–e05, `slice.current.yaml`).
- No application code.
