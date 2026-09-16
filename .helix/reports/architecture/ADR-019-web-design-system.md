# ADR-019 — Web design system (tokens, type, colour, components)

- **Status**: accepted
- **Date**: 2026-09-04
- **Deciders**: ux-ui-designer (draft), council-close
- **Locked citations**: "Visual design method: Claude Design is mandatory" (web-integration-brief §2, §5); "ADR-012 is locked — no new frontend-framework ADR unless a defect blocks" (brief §2). Both respected; this ADR adopts, does not re-pick, a stack.

## Context and problem statement

The web pass must deliver the user-visible ladder with a UI that matches the
Claude Design prototype, "not a localhost config.json shell and not a raw
Swagger page" (brief §7). A design system is therefore a decision, not a
backlog garnish: without locked tokens, type, colour, components, and semantic
mappings, the Code implementer will invent a divergent visual language
screen-by-screen and the `demo` result will not match the prototype that
defines §20 success.

The design system has already been authored in Claude Design and exported to
disk under `inputs/design/prototypes/design-system.md` (text dump) and bound in
the interactive prototype `inputs/design/prototypes/day1-demo.html`. This ADR
adopts that export verbatim as the single visual source of truth and states the
implementation rule: consume the exported `styles.css` tokens, do not fork them.

## Decision drivers

- **Visual fidelity to §20** — the acceptance "matches the Claude Design
  prototype" is only checkable if tokens are shared, not re-derived.
- **AI-assisted implementation** — a small Claude Code team needs a small,
  deterministic token set it can consume mechanically (via `/design-sync` where
  enabled, or the markdown dump where not).
- **Accessibility baseline** — contrast, keyboard, empty states are part of the
  seat's ownership (brief §4) and must be encoded in the system, not left to
  per-screen judgement.

## Considered options

1. **Adopt the exported Modernist system verbatim** (chosen).
2. **Adopt values but re-author them as an in-repo JSON/CSS Kit** owned by the
   code repo.
3. **Design a new token set** in prose during council chat.

## Decision outcome

**Chosen: Option 1** — adopt the Claude Design export
(`inputs/design/prototypes/design-system.md`, bound system folder
`_ds/modernist-*`) as the authoritative design system, with
`inputs/design/prototypes/design-system.md` as the text dump and
`inputs/design/prototypes/day1-demo.html` as the executable reference. The
system is architectural/ink-on-ground: flat, 0px corner radius, flush-left
alignment, one accent colour used sparingly.

### Consequences

- **Good**: one authoritative source already on disk; implementer consumes
  tokens rather than inventing them; semantic mappings (confidence → tag, risk →
  colour, abstain → block) are pre-specified so AI answers don't drift.
- **Bad**: the accent colour (`--color-accent-100/200` as critical-row tint and
  sign-in ground) is deliberately opinionated; any stakeholder wanting a
  "friendlier" rounded/card UI will not get it from this system.
- **Neutral**: the system names a specific bound folder inside Claude Design
  (`_ds/modernist-…`); that binding matters only to `/design-sync`, not to the
  static markdown dump.

## Token set (locked)

| Token | Value | Use |
|---|---|---|
| --color-bg | #f3f2f2 | page ground |
| --color-surface | #eae9e9 | panels, inputs, side panes |
| --color-text | #201e1d | ink |
| --color-accent | #ec3013 | primary action, critical marker |
| --color-accent-100/200 | #fff2ef / #ffe0d9 | critical row tint, sign-in ground + grid |
| --color-accent-700 | #ae1800 | accent body text (≥4.5:1) |
| --color-neutral-200…900 | ramp | hovers, selected rows, muted text, stepper |
| --color-divider | ink @ 40% | 2px section rules, 1px row rules |
| --font-heading / --font-body | Archivo | 800 headings, 400/600 body |
| --space-1…8 | 4–32px | spacing scale |
| --shadow-* | tuned | dialogs only (avoid in-app) |

## Type scale (app)

- Screen title `h2` 32px/800; kicker `h6` 13px uppercase, accent (release tag).
- KPI number 26px/800; key-fact number 20–22px/800.
- Table body 13px; table header 11px uppercase letter-spaced.
- Micro meta 11–12px `--color-neutral-600`.

## Component catalogue (locked)

`.btn` (primary/secondary/ghost/block, labels flush-left) · `.tag` (neutral/
accent/outline) · `.table` (2px header, 1px row rules, hover tint) · `.input` ·
`.field > label` · `.radio + .dot` · `.seg` · `.card` (recommendation/provenance
only) · `.hr` (2px) · skeleton bars · attention/threshold strip · detail pane ·
"Facts vs AI" separation. Icons: Lucide, inline SVG, currentColor, 1.5 stroke,
square caps.

## Semantic mapping (locked — drives answer quality, not just colour)

| Meaning | Treatment |
|---|---|
| Confidence > 95% | `.tag-neutral` "Accepted · 97%" |
| Confidence 80–95% | `.tag-accent` "Flagged · 88%" |
| Confidence < 80% | `.tag-outline` "Review · 71%" — blocks consequential use |
| Status completed/Ready | `.tag-neutral` |
| Status needs_review | `.tag-outline` |
| Status failed | `.tag-accent` |
| Risk High | `.tag-accent`; Medium/Low `.tag-neutral` |
| Deadline ≤ 45 d | date in `--color-accent-700`, weight 600 |
| Abstain | 2px accent left rule + `--color-accent-100` block |

These mappings are a **product decision expressed as UI**: they carry the
confidence thresholds from spec §7.3 (>95% accept, 80–95% flag, <80% require
review) directly onto the screen. The decomposer must not treat colour as
decoration — it is the confidence/risk contract rendered.

## Accessibility baseline (locked)

- Contrast: body `#201e1d` on `#f3f2f2` (>12:1); accent-text uses
  `--color-accent-700` `#ae1800` (≥4.5:1) instead of raw `--color-accent`.
- Keyboard: all interactive controls are native (`button`, `input`, `radio`, `a`);
  the Review "Mark as validated" CTA is `disabled` until gating is met, with a
  visible reason, not a hidden control.
- Empty/error copy is plain-language and names the failing job, never a raw stack
  trace (see ADR-018 empty/error/loading).
- No colour-only semantics: every tag/urgency indicator is paired with a text
  label ("Flagged", "High risk", "≤45 d").

## Implications for the decomposition

- New `layer: web` tasks must consume `inputs/design/prototypes/design-system.md`
  and `day1-demo.html`; they must **not** invent a parallel visual language.
- The e06 design-system story ports the token set + component catalogue into
  `web/` as the shared sheet (consuming `styles.css` tokens, not forking values).
- Every screen story cites its prototype file and the semantic mappings above.
- Passata 2 implementer mounts a `claude-design` skill; prefer `/design-sync`
  when the CLI offers it (brief §5.3), else the markdown dump is authoritative.

## Assumptions

- The bound Modernist system folder (`_ds/modernist-…`) is available to the
  Claude Design project referenced in `inputs/design/README.md`; if it is not,
  the `design-system.md` text dump remains the fallback source of truth.
- Claude Code on the implementer path has Design enabled (brief §5.3); otherwise
  the markdown handoff is used and the round-trip is lost. Tracked in
  reports/open-questions.md.

## Amendment (2026-09-10, wave w14 — a status vocabulary for people, and one rule generalised)

Serves **NW-58** (member and invitation states on screen 10) and the copy half
of **NW-24** / **NW-09** (screen 1). Written by ux-ui-designer, owner of this
ADR (`INDEX.md:52`). Every section above stays in force. This footer **adds one
group of rows to the Semantic mapping and generalises one accessibility rule**;
it changes **no token, no component and no confidence threshold**.

**1. The Semantic mapping gains people-and-invitation states.** Every row of
`## Semantic mapping (locked)` today describes a *document* or an *extracted
fact*. A workspace member is neither, and w14 puts three of them on screen:

| Meaning | Treatment | Text label |
|---|---|---|
| Member is `Active` | `.tag-neutral` | "Active" |
| Invitation is live (`Invited`) | `.tag-accent` | "Invited" |
| Invitation has lapsed (`Expired`) | `.tag-outline` | "Expired" |

The first two are the export's own choices (`app.jsx:133-134`) and already ship
(`memberViewModel.ts:91-93`). **`Expired → .tag-outline` is the only new mapping
in this wave, and it is derived rather than invented**: `.tag-outline` is
already this system's "needs your decision" treatment — `Status needs_review` in
the table above, the same mapping in code at `semantics.ts:75-76`, and
`Review · N%` at `semantics.ts:44`. A lapsed invitation is precisely a row
waiting on an Admin's decision, so it inherits that treatment instead of
claiming a new one.

Two constraints from the sections above are restated because a task will
otherwise reach for colour: the **one-accent rule** holds — `Invited` and
`Expired` are told apart by their **label and their row action**, never by a
second accent — and `## Accessibility baseline`'s "no colour-only semantics"
applies unchanged, so each of the three tags ships its text label.

**2. The disabled-CTA rule is a system rule, not a rule about one button.**
`:119-121` states it for the Review "Mark as validated" CTA: the control is
`disabled` until gating is met, **with a visible reason, not a hidden control**.
w14 needs the identical shape for a different button — the last Workspace
Admin's `Remove`, which the server answers with 409 (ADR-025). This footer
records that the rule governs **gated destructive and consequential actions**
generally, of which "Mark as validated" was the first instance. Hiding the
control reads as a broken product; letting the click fail reads as an unreliable
one. The visible reason is a `.hint` on the row, and it names **what to do
next**, not what went wrong.

**3. No new token and no new component — the catalogue is not extended.**
Everything w14 renders is already in `## Component catalogue (locked)`: `.btn`
(primary/secondary/ghost/block), `.tag` (neutral/accent/outline), `.table`,
`.input`, `.field > label`, `.radio + .dot`, and skeleton bars. Two utilities
this wave leans on — `.micro-meta` and `.hint` — are **implemented classes in
`web/src`, not catalogue entries** (`.micro-meta` appears above only as a
type-scale row at `:85`). They are used as they exist; neither is promoted into
the catalogue here.

**4. Destructive confirmation is inline, never a dialog.** `--shadow-*` is
marked "dialogs only (avoid in-app)" at `:78`, and the locked catalogue contains
no dialog component. The two destructive affordances w14 adds — revoking an
invitation and removing a member — therefore confirm **in the row**: the actions
cell swaps to the question, two buttons and a one-line consequence. This needs
nothing the catalogue lacks, which is exactly why no component is added.

**5. One role vocabulary, two key spaces, and unmodelled roles pass through.**
A person's role now appears in three places: the rail
(`RailNav.tsx:149`, via `WORKSPACE_ROLE_LABEL` at `navItems.ts:32-35`, keyed on
the lowercase wire role), the members table (`memberRoleLabel` via
`INVITE_ROLE_LABEL` at `memberViewModel.ts:18-21`, keyed on the backend enum),
and — new this wave — the workspace picker's row tag. **The two maps emit
identical strings** ("Workspace Admin", "Procurement"): they are two key spaces
over **one vocabulary**, and they must not be allowed to diverge — if a third
role is ever modelled, both gain it in the same change. Where a role is *not*
modelled (the backend also accepts Legal / Finance / ReadOnly —
`memberViewModel.ts:9-11` — while V1 offers two and NW-54 is deferred), the
label **passes the server's own value through**, as `memberRoleLabel:87` already
does. It is never blank, and **never substituted with a modelled label**: a
client parse that maps an unmodelled role to least privilege for *affordances*
(ADR-012 w14 footer) must not also rewrite what the screen **calls** that
person, or the product tells a Legal member they are Procurement. Permissions
degrade to least privilege; **labels do not degrade at all**.

**6. What this footer deliberately does not touch.** The three confidence rows
(`:100-102`, `>95 / 80–95 / <80`) are left exactly as they are, and this footer
must **not** be read as ratifying them: `semantics.ts:9-13` cites a HITL
decision of 2026-09-10 ("≥90% is auto-accepted … Supersedes the earlier §7.3
bands"), so the body above is already stale against an operator ruling.
Reconciling it is **NW-65, queued to W17**; appending an unrelated group of rows
here neither closes nor pre-empts that.

## Amendment (2026-09-13, wave w15 — one semantic row for a refused document; no token, no component)

Serves **NW-27**. Written by ux-ui-designer, owner of this ADR (`INDEX.md:52`).
Every section above stays in force. This footer adds **one row** to the Semantic
mapping and changes **no token, no type-scale row, no component and no confidence
threshold**. It is deliberately the smallest footer this seat writes in w15:
three of the four items it owns need nothing from the design system at all.

**1. The Semantic mapping gains the refused document.**

| Meaning | Treatment | Text label |
|---|---|---|
| Document refused at admission (`Rejected`) | `.tag-outline` | **Not added** |

**Derived, not invented** — treatment and label already ship together on the card
this wave retires: `UploadResultCard.tsx:24` is
`<span class="tag tag-outline">Not added</span>`. It is also the correct
derivation from the table above rather than a free choice. `.tag-accent` is
`Status failed` (`:105`): a **Raffa-side** failure the user can retry. A refusal
is not a failure — it is a **decision about the file**, and `.tag-outline` is
already this system's "this row is about a decision" treatment: `Status
needs_review` (`:104`), `Review · 71%` (`:102`, "blocks consequential use"), and
w14's `Expired` by exactly this reasoning. The **one-accent rule** holds, and
`## Accessibility baseline`'s no-colour-only rule is satisfied by the text label,
as it is for every row above.

**2. The compiler will not ask for the second half of this edit, and that is why
it is in the ADR and not only in the task.** `documentTable.ts:97-99` is
`getStatusTag(status as DocumentStatus)` — an **unchecked cast**. `DocumentStatus`
(`semantics.ts:52`) is
`"completed" | "ready" | "needs_review" | "failed" | "processing"` and has no
`"rejected"`. A task that adds `"rejected"` to `RowStatus` (`documentTable.ts:79`)
and stops there **compiles clean**, and `getStatusTag`'s exhaustive switch falls
through to `undefined` — a blank tag or a runtime crash, never a build error.
**The row above is therefore a two-file edit**: `semantics.ts:52` and `:61-73`
(add `"rejected"` → `{ variant: "outline", label: "Not added" }`) **and**
`documentTable.ts:79` and `:81-93`. A task must cite both. This is the only place
in this wave's design surface where the type system does not protect the change.

**3. No new component — for three surfaces that each look like they need one.**

(a) The Documents filter gains a **third** `<button aria-pressed>` inside the
existing `.seg` (catalogued at `:91`; `AttentionFilter.tsx:18-32` is already "a
controlled pair of native `<button aria-pressed>` toggles"). A third child is a
**catalogue use, not an extension**.

(b) NW-69's invite pane is `.btn-secondary`, `.micro-meta`, `.input` and
paragraphs — all already available. w14 clause 3's note stands: `.micro-meta` and
`.hint` are **implemented classes in `web/src`, not catalogue entries**, and they
are used exactly as they exist. No status treatment is needed that the map above
lacks, so NW-69 adds no row.

(c) **The invitation email consumes token *values*, never the system** (ADR-020's
w15 footer, surface 12). None of this system's mechanism survives an email
client: no external stylesheet, no CSS custom property, and **no web font** —
`--font-heading / --font-body` is **Archivo** (`:76`), which a mail client will
not load. Any colour is therefore an **inline literal of a value from
`## Token set (locked)`** — `--color-text` `#201e1d`, `--color-accent` `#ec3013`,
`--color-accent-700` `#ae1800` — never a variable reference, never a new value,
and never a near-miss picked to look right in one client. That is
"consume tokens, do not fork them" honoured in the only way an email allows, and
it is recorded here so that the first surface Raffa sends **outside the browser**
does not quietly become a second design system nobody owns.

## Amendment (2026-09-14, wave w15 — re-entry round: one treatment, two producers, and a third site the compiler will not ask for)

Continues the **2026-09-13 w15 footer** above (`:232-294`, clauses 1–3), every
clause of which stays in force. Serves **NW-27** and **NW-61**. Written by
ux-ui-designer, owner of this ADR (`INDEX.md:52`). **No token, no type-scale row,
no component and no confidence threshold changes here either** — and the Semantic
mapping gains **no new row**.

**4. The refusal treatment is keyed on the reading, not on where the fact came
from.** Clause 1's row (`Rejected` → `.tag-outline` **Not added**) now also serves
a refusal that never reached the server: the browser-side oversize check and a
413/415, which ADR-020 w15 round-3 §6 renders as a **local row** rather than as
the retired card. Same tag, same label, deliberately indistinguishable — the
user's fact is identical and the difference between the two producers is ours.

**The consequence is a third edit site, and it is the dangerous one.**
Clause 2 established that adding `"rejected"` to `RowStatus` while forgetting
`semantics.ts:52` compiles clean (`documentTable.ts:97-99` is an unchecked cast)
and yields a blank tag. The local row adds a site that fails **worse**:
`DocumentStatusTable.tsx:94-96` maps a local entry with a ternary —
`getRowStatusTag(entry.phase === "failed" ? "failed" : "processing")` — so widening
`LocalUploadEntry.phase` with `"rejected"` and stopping there also **compiles
clean**, and renders a refused file as **Processing**, indefinitely, with
`Uploading…` beneath it (`:105`). A blank tag is a visible defect; a confident
wrong status is a lie the user has no way to catch. So the two-file edit of clause
2 is a **three-site** edit — `semantics.ts:52` / `:61-73`, `documentTable.ts:79` /
`:81-93`, and `DocumentStatusTable.tsx:94-96` (with `:90`, `:97`, `:105`) — and
**not one of the three fails a build if it is forgotten**. A task must cite all
three; the check is a grep, not a green build.

**5. The paused-updates notice adds nothing to this system.** ADR-020 w15 round-3
§8's stopped-poll notice is `.hint` + `.btn-secondary` — the pair
`DocumentStatusTable.tsx:100-103` already ships for the failed row — and on the
tier surfaces it is a **secondary beside** the empty block's existing primary CTA,
which is the block's shape, not an extension of it. It is **never** `.tag-accent`
and never an alert treatment: nothing has failed, so the one-accent rule (clause
1, `:249-254`) holds and `## Accessibility baseline`'s no-colour-only rule is
satisfied by the sentence itself.

**6. Unchanged.** `## Token set (locked)`, `## Type scale (app)`,
`## Component catalogue (locked)`, `## Accessibility baseline (locked)`, the three
confidence rows, the w14 footer, and w15 clauses 1–3.

## Amendment (2026-09-15, wave w17 — one bar, three decisions, and an accent released from confidence)

Continues the **w15 re-entry footer** above (`:296-337`, clauses 4–6), the w15
footer (`:232-294`, clauses 1–3) and the w14 footer (`:146-230`), every clause of
which stays in force **except the evidence claim corrected in clause 10**;
numbering continues from them. Written by **ux-ui-designer**, owner of this ADR
(`INDEX.md:52`). Serves **NW-71, NW-66, NW-65, NW-64, NW-62, NW-72, NW-63**.
Baseline `d3d2d24`.

This is the **first footer since this ADR was accepted that changes the Semantic
mapping's confidence rows**; every earlier one stated explicitly that it did not,
and the w14 footer's §6 deferred them to this wave by name. **No token, no
type-scale row and no component changes here**: the whole wave is assembled from
the locked catalogue.

**7. The three confidence rows become two bands and a decision vocabulary.** The
screen no longer renders a *band it computed*; it renders the **server's
persisted decision** (ADR-003 w17 clause 1; ADR-012 w17 clause 34 — the web never
recomputes it). Rows `:100-102` are replaced by:

| Server decision | Treatment | Label |
|---|---|---|
| `auto_accepted` | `.tag-neutral` | `Accepted automatically · NN%` |
| `human_accepted` | `.tag-neutral` | `Accepted by you` — **no percentage** |
| `review_required` | `.tag-outline` | `Review · NN%` — blocks consequential use |

- **`human_accepted` carries no percentage.** A human decision has no confidence.
  Printing the model's score beside a person's judgement asserts that the
  judgement is itself uncertain, which is false, and it is the same class of
  error as clause 8's rounding: a figure placed next to a word that denies it.
- **The two accepted states are distinguished by the label, never by the
  variant.** They share `.tag-neutral` deliberately. ADR-001 w17 clause 8 requires
  them to stay distinguishable; `## Accessibility baseline` (`:124-125`) requires
  the **text** to be the carrier; and OQ-w17-cl-02 has just found a live defect
  (`contract360ViewModel.ts:575`) where a screen infers a *decision* from a
  *variant*. Minting a third variant to separate them would build the next
  instance of that defect into the system on purpose.
- **`.tag-accent` leaves confidence entirely.** Row `:101` ("Flagged · 88%") is
  retired, so accent is reserved for `failed` (`:105`), High risk (`:106`), the
  invited/critical markers — and nothing else. The one-accent rule
  (`design-system.md:9`, w15 clause 5) gets **stronger**, not weaker: the middle
  band was accent's largest consumer, on four surfaces (`WhyClauses.tsx:56`,
  `FactTable.tsx:35`, `contract360ViewModel.ts:574`, `reviewViewModel.ts:282`).
  Retiring it is the whole point of moving to one bar.
- **Blocking**: `isConfidenceBlocking` (`semantics.ts:48-49`, `< 80`) is replaced
  by **"not accepted"** (ADR-001 w17 clause 2). The design-system consequence is
  only that the disabled CTA keeps its **visible reason** (`:119-121`), and that
  reason now counts `review_required` fields instead of naming a threshold.
- **Vocabulary fence**: `officialized` is an **ADR word** and never appears on a
  screen. The screen says accepted, you decide, or nothing.

**8. Every displayed confidence floors; it never rounds.** `semantics.ts:33`
currently applies `Math.round` before building **every** label. At one bar that
is not a cosmetic choice: a `review_required` field at `0.895` renders
**`Review · 90%`** — the bar's own number printed beside the word that denies it.
A tag that contradicts itself inside one string is not a rounding artefact; it is
the product telling the user two things at once.

- Rule: `0.895 → 89%`, `0.90 → 90%`, `0.999 → 99%`. **A figure is never rounded
  up to a value the fact does not hold.**
- This is **presentation only** — the decision stays the server's, taken on the
  raw stored double with no rounding before the compare (ADR-024 w17 clause A1).
  The two rulings compose: the server decides unrounded, the screen floors.
- It is a **one-line code change in `semantics.ts`**, which single-writer
  constraint 2 gives **NW-71** — not a copy note. Recorded as a token-level rule
  because two seats found the same line from two directions in the same round
  (this seat from the semantic mapping, client-architect from the call site,
  OQ-w17-002 / ADR-001 w17 clause 2).
- Test-locked: `tests/styles/semantics.test.ts:15-37` is rewritten by that task.

**9. The clause risk enum is relabelled into product words — and it is a
label-only change, on one function.** Product-owner's clause 6 supplies the
words: Critical/High → **Push to change**, Medium → **Worth raising**, Low →
**Standard terms**. This seat's lane proposed a different four-word set; **the
words are product-owner's and the lane's set is withdrawn**. What this seat rules
is the treatment, and the answer is *nothing moves*:

- **Semantic-mapping row `:106` is unchanged** (Risk High `.tag-accent`;
  Medium/Low `.tag-neutral`). `getClauseRiskTag`
  (`contract360ViewModel.ts:255-259`) **already** computes `High || Critical →
  accent`, everything else → `neutral`, with the **label** carrying the meaning,
  and its own docstring (`:254`) already says *"Text first, colour only as
  emphasis."* The variant expression stays byte-identical; only the strings
  change. Medium and Low keep sharing `.tag-neutral`, distinguished by their
  words — the precedent is already in the file, and `:124-125` requires exactly
  that.
- **Scope fence, and it dissolves a collision.** The relabel touches
  `getClauseRiskTag` in `contract360ViewModel.ts` **only**.
  `semantics.ts:getRiskTag` (`:91-96` — "High risk" / "Medium risk" / "Low risk")
  is a **different function on different surfaces** and is **not** renamed:
  renaming it would silently change Portfolio and Renewals rows that no item in
  this wave touches, **and** it would put NW-66 into `semantics.ts`, which
  constraint 2 reserves for NW-71. The fence is what keeps that file
  single-writer without spending a phase.
- **A null risk level stays null.** `:256` returns `null` when the clause carries
  no risk level and **must keep doing so**: "Standard terms" is a statement about
  the clause, so synthesising it for a clause whose risk was never determined
  invents precisely the kind of fact this wave exists to stop (w14 clause 3,
  ADR-001 w17 clause 5).
- **Legend**: the three words are stated once as `.micro-meta` in the hint slot
  the component already has (`WhyClauses.tsx:29`). Because the words no longer
  name a risk level, the screen must say what they mean; a vocabulary the user
  has to infer is a vocabulary that means nothing.

**10. Correction — the w14 footer §6 reached the right conclusion on evidence
that is not on main.** §6 (`:224-230`) declines to ratify the three confidence
rows on the grounds that *"`semantics.ts:9-13` cites a HITL decision of
2026-09-10 ('≥90% is auto-accepted … Supersedes the earlier §7.3 bands')"*.

**On `d3d2d24` that docstring says no such thing**, verified first-hand at this
table: `semantics.ts:9-13` cites this ADR's *"Semantic mapping (locked)"* and
`design-system.md`'s, and states that **both carry spec §7.3's thresholds** (>95
accept, 80–95 flag, <80 review). There is **no `90`**, no HITL date and no
supersession sentence anywhere in the file. The 90 % comment exists only in an
uncommitted operator stash, which is not an oracle.

- **§6's conclusion was right and its evidence was not.** The bands *were* stale;
  the authority is the **2026-09-10 HITL ruling itself**, carried by the raw file
  and `w17-requirements.md`, not by a code comment.
- The body is **not edited** (append-only): §6's text stands and a reader is sent
  here. Recorded because an implementer following §6's citation would find no such
  line and could reasonably conclude the staleness claim was invented — and then
  "restore" the three bands.
- §6 also names **NW-65** as the item that would reconcile them. The
  reconciliation is delivered by **NW-71** (the threshold and the vocabulary) and
  **NW-65** (the layout that stops rendering the tag on Contract 360), so the
  trace from §6 lands on real tasks.

**11. Where each treatment may appear after this wave.** **No confidence tag
renders on Contract 360 at all** (ADR-001 w17 clause 6): every fact that screen
shows is officialized and carries page · section. So on a 360 fact row
`.tag-neutral` means **leverage**, and on Review it means **accepted** — one
treatment, two screens, two meanings, and **no screen where both meanings are in
play at once**. That last clause is the load-bearing one: a shared treatment
across screens is safe only while it holds, so the next item that puts a
confidence tag back on Contract 360 must return to this clause first.
`.tag-outline` keeps `needs_review` (`:104`), `Not added` (w15 clause 1) and now
`review_required` (clause 7).

**12. Unchanged.** `## Token set (locked)`, `## Type scale (app)`,
`## Component catalogue (locked)` — **no new component this wave**: the clause
"specchietto" is the existing `ClauseHighlight`, the fourth KPI cell is the
existing cell shape, and the viewer is composed from `.btn-ghost`, `.table` and
surface tokens — `## Accessibility baseline (locked)`, rows `:103-108`, the w14
footer apart from clause 10's correction, and w15 clauses 1–6. This footer
creates **no new ADR** and supersedes nothing.
