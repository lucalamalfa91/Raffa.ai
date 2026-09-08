# Visual fidelity mandate — epic-11 / e11

Delta only. ADR-019 already locked Modernist. This wave implements the
compiled prototype, not a new design system.

## Oracle

- Artifact: https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58
- Export: `inputs/design/prototypes/day1-demo.html` (+ `screens.md`, `design-system.md`)
- Live: https://mango-pond-061bc6d1e.6.azurestaticapps.net/

## Scope

**In:** CSS/markup/tests for screens that already ship on `dev` (sign-in +
picker, 224px shell, global Ask bar, Documents, Portfolio, Contract 360,
Review) plus hiding the E01 `/health` probe from the canvas.

**Out:** building missing screens (Home, Ask chat, Members, Renewals, Quote
check) — e08 / recoveries. Out: backend, Terraform, `wave-spec.execution.yaml`,
e01–e10, `slice.current.yaml`.

## Success

An operator opening `dev` sees the mockup's sign-in (accent-100 panel, 52px
clamp north-star, red-square lockup, "Sign in" + Entra mark) and a full-bleed
shell, not a debug line and a timid 32px poster.
