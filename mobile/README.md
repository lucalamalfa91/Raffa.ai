# raffa-mobile

React Native (Expo) + TypeScript app for Raffa (ADR-013). This is a
**non-gating** lane: V1 is web-first, and neither this app nor a store
release is required for any R0–R4 wave or the Day-1 `demo` (see
`.github/workflows/mobile.yml`, which runs with `continue-on-error: true`
at both the job and step level so a failure here can never block `dev`/
`demo` promotion — parent story `us-01-mobile-scaffold` AC-3).

## Status

- Task T01 (scaffold): Expo + TypeScript app, native redirect scheme
  (`raffa://callback`, AC-1) and per-environment config plumbing
  (`src/config/`).
- Task T02 (this task): wires the OIDC Authorization Code + PKCE flow (AC-2)
  against Entra ID (`src/auth/`), using `getNativeRedirectUri()` /
  `getMobileEnvConfig()` from `src/config/`. `App.tsx` is a minimal
  sign-in/sign-out shell proving the flow end to end, mirroring
  `web/src/App.tsx`.

## Requirements

- Node.js + npm (Expo SDK 57 / React Native 0.86 / TypeScript ~6.0).
- No native Android Studio / Xcode toolchain is required to develop —
  `npm start` runs the app in Expo Go or a simulator.

## Getting started

```bash
npm ci
cp .env.example .env   # then fill in the per-environment values
npm start
```

## Scripts

| Script | What it does |
|---|---|
| `npm run typecheck` / `npm run build` | `tsc --noEmit` — no native bundling step exists without EAS credentials (out of scope for V1; ADR-013: no store release for R0–R4), so type-checking is this app's CI-provable "build". |
| `npm test` | Runs the Jest (`jest-expo` preset) unit tests under `tests/`. |
| `npm start` / `npm run android` / `npm run ios` / `npm run web` | Expo dev server. |

## Config (no secrets in the bundle)

Per ADR-013 ("Mobile reads API base URL + OIDC authority from per-environment
config, exactly as `web/` does; no client secret is ever placed in the
app/bundle") and ADR-010 (PKCE public clients carry no client secret), the
app reads four non-secret, per-environment values from `EXPO_PUBLIC_*`
build-time env vars — see `.env.example` and `src/config/env.ts`:

- `EXPO_PUBLIC_API_BASE_URL` — backend API origin for this environment.
- `EXPO_PUBLIC_OIDC_AUTHORITY` — Entra ID issuer for this environment's tenant.
- `EXPO_PUBLIC_OIDC_CLIENT_ID` — the public-client application ID (no secret).
- `EXPO_PUBLIC_OIDC_API_SCOPES` — comma-separated API scopes requested at
  sign-in (ADR-010 placeholder `Raffa.Read`/`Raffa.Write`-shaped values,
  pending the API surface being fixed — `reports/open-questions.md`
  OQ-client-007). The standard `openid`/`profile`/`offline_access` scopes are
  added automatically (`src/auth/oidcConfig.ts`) and should not be listed here.

`getMobileEnvConfig()` fails fast (throws, naming the missing variable) if
any of the four is unset, rather than silently defaulting and pointing the
app at the wrong environment.

## Native redirect scheme

`app.json`'s `expo.scheme` is `raffa`, which is also Entra ID's registered
native reply URL for the public client (ADR-010). `src/config/redirectUri.ts`
builds this as `raffa://callback` (`getNativeRedirectUri()`), read from
`app.json` rather than duplicated as a literal.

## OIDC Authorization Code + PKCE (`src/auth/`)

`expo-auth-session` drives the flow against the Entra ID authority named in
runtime config — the Expo-idiomatic, Expo-Go-compatible equivalent of how
`web/` uses `@azure/msal-browser`, without adding a native-module toolchain
dependency (ADR-013: Expo "drastically lowers native toolchain friction").

- `src/auth/oidcConfig.ts` — pure, unit-tested functions that build the
  `AuthRequestConfig` (client id, native redirect URI, scopes, PKCE + S256
  challenge method) and the token-exchange `AccessTokenRequestConfig`
  (authorization code + PKCE `code_verifier`, never a client secret).
- `src/auth/useOidcAuth.ts` — the hook that actually opens the system browser
  (`AuthRequest.promptAsync`) and exchanges the returned code for tokens
  (`AuthSession.exchangeCodeAsync`). Thin by design and not unit-tested
  directly (same boundary `web/src/App.tsx` draws around `@azure/msal-react`):
  it composes `oidcConfig.ts` (tested) with `expo-auth-session`'s own request
  and PKCE plumbing (that library's responsibility to test).
- `App.tsx` — a minimal sign-in/sign-out shell using `useOidcAuth()`, tested
  via `tests/App.test.tsx` by mocking `useOidcAuth` (mirrors
  `web/tests/App.test.tsx` mocking `@azure/msal-react`).

### Known gap: token persistence and refresh

Exchanged tokens live only in React state (`useOidcAuth.ts`); there is no
`expo-secure-store` persistence and no silent refresh via
`AuthSession.refreshAsync` yet, so the session does not survive an app
restart. Out of this task's scope (config + PKCE wiring, AC-2); tracked as a
follow-up alongside the actual product screens that will need a persisted
session.
