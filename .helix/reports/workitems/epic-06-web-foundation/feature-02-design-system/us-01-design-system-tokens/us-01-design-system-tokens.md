---
id: us-01
type: user-story
parent: feature-02
wave: 6
status: active
---

# us-01-design-system-tokens — Design system tokens + components

## Story

As a **web implementer**, I want the Raffa design system in `web/` as a shared
token sheet and component catalogue, so all screens share one visual language
(not a divergent one per screen).

## Acceptance criteria

- [ ] AC-1 Tokens and component classes ported from `inputs/design/prototypes/design-system.md` (consume `styles.css`, do not fork values).
- [ ] AC-2 Semantic mappings (confidence/risk/abstain) encoded, not colour-only.
- [ ] AC-3 Accessibility baseline honoured (contrast, native controls, disabled CTA with reason).

## Definition of done

- [ ] Shared sheet committed under `web/`; `npm run build` succeeds.
- [ ] honours ADR-019.

## Dependencies

| Depends on | Why |
|------------|-----|
| ADR-019 (design system) | source values |

## Architecture decisions in force

- ADR-019 (adopt export verbatim), ADR-012.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Port design system tokens + components | M | phase-02 |

## Council decisions carried into this story

Adopt Modernist export verbatim; do not invent a parallel visual language.
