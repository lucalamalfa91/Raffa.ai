# Claude Design handoff — ux-ui-designer (web delta, wave 6+)

Status: **complete — export present on disk**, no HITL block at this lane.

`HITL_CLAUDE_DESIGN:` condition checked: **not triggered**. The operator
already ran the Claude Design export before this pass launched. `inputs/design/`
is fully populated (verified via `list_dir`), so this lane records the handoff
and closes — it does **not** OBJECT.

## What exists on disk (verified)

```
inputs/design/
  README.md                        # project name + URL, export layout
  _claude-design-brief.md          # the brief given to Claude Design
  prototypes/
    day1-demo.html                 # 361KB clickable Day-1 path (the prototype)
    design-system.md               # tokens, type, colour, components (text dump)
    ia.md                          # sitemap + user flows + route map
    screens.md                     # inventory ↔ spec §16 / §20
```

- **Project link** (from `inputs/design/README.md`):
  `https://claude.ai/design/p/325f13ce-8fe3-4212-b22c-2ffd1700435e?file=Raffa+Day-1.dc.html`
- **Design system identity**: Modernist, bound folder
  `_ds/modernist-584f2982-aad7-48d1-aef0-a80897b0b5e4/` (per design-system.md).

## Brief §5.1 minimum prototype coverage — check

| Required (brief §5.1) | Present in prototype? |
|---|---|
| Sign-in (Entra) → workspace | screens.md §1 |
| Invite / role (admin vs procurement) | screens.md §2 |
| Upload → document status | screens.md §3 |
| Portfolio list + filters | screens.md §4 |
| Contract 360 (clauses, evidence, confidence) | screens.md §5 |
| Review / correction | screens.md §6 |
| Ask + citations / abstain | screens.md §7 |
| Renewal pipeline + insight + action | screens.md §8 |
| Savings KPIs + list | screens.md §9 |
| Quote extract → assess → target → negotiate | screens.md §10 |

All ten are authored. Empty/error/loading states are specified in
`design-system.md` §States and referenced per-screen in `screens.md`.

## Pointers written by this seat

The three ADRs above cite these paths as their source of truth:

- `ADR-information-architecture.md` → `inputs/design/prototypes/ia.md`
- `ADR-design-system.md` → `inputs/design/prototypes/design-system.md`
- `ADR-screen-inventory.md` → `inputs/design/prototypes/screens.md`

No prose-only colour list was written as a substitute. This lane is not a
"second client-architect": it owns IA, design system, screen inventory,
interaction patterns, and the accessibility baseline, all sourced from the
Claude Design export.

## Handoff to later phases

- **Decomposer**: each `layer: web` story must cite the prototype file it
  implements (`inputs/design/prototypes/day1-demo.html` and its
  `screens.md`/`ia.md`/`design-system.md` entries).
- **Passata 2 implementer**: mount a `claude-design` skill; prefer
  `/design-sync` when the CLI offers it (brief §5.3); otherwise the markdown
  dumps remain authoritative. Do not fork token values into the SPA — consume
  `styles.css` tokens.
- **Product-owner + council-gate**: this satisfies the "prototypes/ not empty"
  close gate; no OBJECT is raised on the export's existence.

## Open items (tracked in reports/open-questions.md, not a halt)

1. Is the bound Modernist system folder (`_ds/modernist-…`) reachable from the
   Claude Code implementer's Design-enabled account? If not, the
   `design-system.md` text dump is the fallback source of truth.
2. Does Entra return role/permission claims usable client-side to gate
   `/workspace/members` (Admin vs Procurement)? If not, the non-admin
   "request access" state must be server-driven.
3. Azure Static Web Apps client-side routing fallback for `/contracts/:id` and
   `/quotes/:id` — handed to cloud-architect.
