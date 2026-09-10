# Claude Design brief — Raffa web V2 (pilot path)

Use **Claude Design** (`/design`, DesignSync, claude.ai/design). Do **not** invent a
pixel system as a coding agent and call it done.

## Product

Raffa is an AI-native Procurement Intelligence Platform (web-first, ADR-012:
React + TS + Vite SPA, Entra OIDC PKCE, Azure Static Web Apps). Backend R0–R4
is treated as done. This pass designs the **user-visible web** only.

Headline (sign-in hero): **Your contracts. Your savings. Nothing missed.**
Four pillars shown beneath it — Contract Intelligence (what you bought),
Renewal Intelligence (when to act), Savings Intelligence (where to save),
New Purchase Quote Check (before you buy).

Roles in the pilot path: **Workspace Admin** vs **Procurement**. Legal / Finance /
read-only exist in spec but are not required as full nav variants.

## V2 principles (what changed from Day-1)

- **Ask Raffa is the home.** Sign-in lands on Ask. The same Ask bar (square
  mark, full-width input, quiet suggestion links beneath) sits on top of every
  screen; asking from any screen always opens a **new chat** in Ask Raffa.
- **Two-tier navigation.** Primary: Ask Raffa (with recent conversations
  nested under it, resume by click, "+ New chat") and Documents. Secondary
  "From your contracts": Portfolio, Renewals, Quote check — greyed with reroute
  empty states until the first document is validated. **No Home screen.**
- **Ask is system-aware.** Benchmark / competitor / "in linea" questions route
  to Quote check (with action buttons); unknown suppliers route to upload;
  "what can you do" lists the modules. Structured facts vs clause retrieval;
  every answer cites page + section or abstains ("cannot determine reliably").
- **Documents shows only what needs the user.** Default filter "Needs your
  attention" (processing / needs review / failed); completed documents are
  hidden behind "All documents". Empty-attention state: "Nothing needs you
  right now."
- **Review is a state of Documents**, not a screen: only fields < 80% by
  default; "Show all 41 extracted facts" on demand; accept / correct with
  evidence panel; "Mark as validated" unlocks Ask, Portfolio, Renewals.
- **Contract 360 has no tabs.** A logical flow: (1) three answers in one band —
  *Where you can save* (estimate + lever), *When you must move* (notice
  deadline, days left), *What to do* (action, rationale, primary button +
  "Assign to a colleague"); (2) *Why* — the 2–3 clauses behind it, click for
  original wording (citations from Ask land here); (3) *Details ▾* collapsed —
  key terms, documents in family, facts still to decide. Starting the action
  opens an inline **negotiation tracker** (status, owner, target, deadline,
  4-step checklist, "Track it in Renewals", Undo).
- Wording: never "knowledge base". Say "validated contracts".

## Required screens (one clickable pilot path)

1. Sign-in (Entra / MSAL) → workspace picker → **Ask Raffa (home)**
2. Documents — onboarding empty state ("First your contracts. Then your
   questions."), upload / drop, processing stages, attention filter
3. Review state (confidence: >95% accept, 80–95% flag, <80% you decide)
4. Ask Raffa — new chat, resume, citations, abstain, routing actions
5. Contract 360 — answers band, clauses/proof, details, negotiation tracker
6. Portfolio (spec §8.1 columns, "More columns" on demand)
7. Renewals — priority list + insight + action (spec §9.3), shared status
   with Contract 360 tracker
8. Quote check (spec §11–12) — reached from nav or from Ask routing
9. Workspace & members — invite / role (admin vs procurement)

Include empty, error, and loading states on the pilot path. Demo strip at top
with 5 pilot acts (Sign in → Upload → Review weak facts → Ask → Follow a
citation) and a Reset button. No marketing landing.

Tweaks: `fixtures` (none | seeded — 3 validated contracts), `uploadOutcome`
(default completed), `role` (default admin).

## Export onto disk (this repo)

Write under `.helix/inputs/design/`:

```
design-system.md
ia.md
screens.md
brief-v2.md
prototypes/
  raffa-v2.html           (standalone, all screens, demo strip)
  raffa-day1.html         (archived V1, reference only)
```

Update `README.md` with the Claude Design **project name + URL**
(`https://claude.ai/design/...`) so Helix council and later `/design-sync` can
find it.

Cite spec §16 / §20 in `screens.md`. ADRs 012/013 stay locked (no new SPA stack).
