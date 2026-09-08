# Contigo V2 prototype — unpacked sources

`../Contigo V2 Prototype.html` is a Claude Design **bundled page**: the real
app lives inside gzip + base64 payloads (`<script type="__bundler/manifest">`)
and unpacks only in a browser. A coding agent cannot search it. This folder
is the same prototype, unpacked, so ADR-024, epic-13 tasks and reviewers can
cite it by file and string.

| File | What it is | Search it for |
|------|------------|---------------|
| `app.jsx` | The prototype's React logic (`class Component extends DCLogic`): fixture contracts, evidence, `renderVals()` (every value bound in the markup), the `ask()` intent engine, upload simulation, review flow, Contract 360 tracker. | `primaryNav`, `kbNav`, `askOffReason`, `askScope`, `chipsFor`, `ask(text,scope)`, `startUpload`, `finishReview`, `steps360`, `unknownSupplier` |
| `markup.html` | The screen markup with the template DSL: `{{ value }}` bindings to `renderVals()`, `<sc-if value="{{ … }}">`, `<sc-for list="{{ … }}" as="…">`, `sc-camel-on-click="{{ handler }}"`. Screen blocks are guarded by `scr.<name>` (`documents`, `ask`, `portfolio`, `renewals`, `home`, `quote`, `workspace`, `c360`) and `isSignin`. | "From your contracts", "+ New chat", "First your contracts. Then your questions.", "Nothing needs you right now.", "Ask needs at least one validated contract.", "Mark as validated", "Track it in Renewals" |
| `styles.css` | The Modernist design-system CSS as shipped in the bundle (tokens, `.btn-*`, `.tag-*`, `.input`, `.card`, states). Same system as `../design-system.md` (ADR-019); bundled fonts are referenced as `<bundled-font-…>`. | `--color-accent`, `.tag-outline`, `.btn-primary`, `.empty-state` |
| `ia-v2.md` | Information architecture V2: roles, two-tier navigation, route map, object → screen map, cross-links, Ask intents, pilot path. Authored from `app.jsx` + `markup.html`. | — |
| `screens-v2.md` | Screen inventory V2 with states and verbatim copy, mapped to spec §16 / §20 and to the requirements (`inputs/requirements.md`). | — |

## How the pieces fit

```
Contigo V2 Prototype.html  ──unpack──▶  app.jsx (logic)  +  markup.html (view)  +  styles.css
                                              │
                        renderVals() returns every {{ name }} the markup binds
                        ask(text, scope) is the behavioural oracle for Ask intents
```

Tweaks exposed by the bundle (`data-props`): `fixtures` (`none` | `seeded` — 3
validated contracts), `role` (`admin` | `procurement`), `uploadOutcome`
(`needs_review` | `completed` | `failed`).

## Rules for citing the design

- Cite the bundled file **and** the unpacked file: "`Contigo V2 Prototype.html`
  (unpacked: `contigo-v2/markup.html`, search *Nothing needs you right now.*)".
- Copy and measurements come from these files, not from memory. When the
  markup and `app.jsx` disagree with `inputs/requirements.md`, the
  requirements win (they record the HITL decisions of 2026-09-08) and the
  divergence is named in the task.
- Re-unpack after a new export: run the same split (manifest → gunzip →
  `template` script → `<style>` / `text/x-dc` blocks). Do not hand-edit
  `app.jsx` / `markup.html` / `styles.css`.
