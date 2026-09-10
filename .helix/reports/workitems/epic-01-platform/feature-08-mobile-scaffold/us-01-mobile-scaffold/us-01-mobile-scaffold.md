---
id: us-01
type: user-story
parent: feature-08
wave: R0
status: active
---

# us-01-mobile-scaffold — Mobile scaffold + OIDC

## Story

As a **backend engineer**, I want the React Native scaffold with OIDC PKCE wired,
so that the `mobile/` folder is present without blocking delivery.

## Acceptance criteria

- [ ] AC-1 Expo + TypeScript scaffold with `raffa://callback` native redirect.
- [ ] AC-2 OIDC Authorization Code + PKCE against Entra ID (no client secret).
- [ ] AC-3 Non-blocking: failure does not block `dev`/`demo` promotion.

## Definition of done

- [ ] `expo` build succeeds; OIDC config loads from per-env injection.
- [ ] honours ADR-013, ADR-010.

## Dependencies

| Depends on | Why |
|------------|-----|
| feature-04 (deployable API) | API origin |

## Architecture decisions in force

- ADR-013 (Expo RN), ADR-010 (PKCE).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Expo scaffold | M | phase-24 |
| task-02 | OIDC PKCE config | S | phase-25 |

## Council decisions carried into this story

Non-gating mobile lane; no store release for R0–R4.

## Open questions

- none.
