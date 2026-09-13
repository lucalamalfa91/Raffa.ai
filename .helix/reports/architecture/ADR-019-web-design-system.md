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
