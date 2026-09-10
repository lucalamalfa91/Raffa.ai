# Raffa — visual fidelity (mockup vs `dev`)

Operator complaint (2026-09-07): live `dev` SWA looks nothing like the Claude
Design mockup. E06/F06 removed the `40rem` cap and stopped there. Tokens exist;
the compiled prototype's layout, type, and chrome do not.

## Oracle (pixel reference)

1. **Operator URL:** https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58
2. **Local export (authoritative for values):** `inputs/design/prototypes/day1-demo.html`
3. **Dumps:** `design-system.md`, `screens.md`, `ia.md` in the same folder.
4. **ADRs in force:** ADR-018, ADR-019 (Modernist verbatim — do not fork), ADR-020.

Council / auditor cannot open claude.ai. Treat (2) as the executable reference.

## Live target

https://mango-pond-061bc6d1e.6.azurestaticapps.net/

Evidence from 2026-09-07: bundle `index-CYNvgndl.css` already has
`signin-screen{grid-template-columns:1fr 1fr}` and no `max-width:40rem` on
`main`. The remaining gap is **not width**. It is type, colour, composition,
chrome, and missing screens.

## What this wave must do

Reproduce the mockup faithfully on shipped screens that already exist
(sign-in, shell, Ask **bar**, Documents, Portfolio, Contract 360, Review).
Do **not** rebuild Home / Ask screen / Members / Renewals / Quote (those are
e08 + recoveries). After e08, a follow-up restyle is allowed; it is out of
e11 live tasks.

Do **not** invent a parallel visual language. Copy prototype values
(inline styles in `day1-demo.html`) into `web/src` CSS/markup.

## Never write

`wave-spec.execution.yaml`, `wave-spec.web.yaml`, `wave-spec.schema.yaml`,
`wave-spec.readiness.yaml`, `slices/e01.yaml`–`e10.yaml`, `slice.current.yaml`,
ADR-001…022, epic-01…10.

## Passata 2

After HITL on the gap report:

```
./run.ps1 -Max -Slice e11 -o execution-fanout
```

Launch **before** e08 so new screens inherit corrected chrome. Do not run e08
and e11 in parallel (F04 Day-1 walk vs CSS ownership).
