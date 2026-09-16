---
id: E25/F01/US01/T01
type: task
story: us-01-ask-chip-role-gate
wave: w18
status: live
target_repo: raffa-web
---

# task-01-ask-chip-role-gate — Hide admin-gated Ask chips from a non-Admin

## Coding objective
Thread the server-derived `role` (already at
`web/src/components/shell/AppShell.tsx:40`, passed to `RailNav` at `:50` but not
to `GlobalAskBar` at `:59`) into `GlobalAskBar`, and — in
`web/src/components/ask-bar/askSuggestions.ts`
(`suggestionsFromCapabilityCatalog`, `getAskBarCopy`) and the Ask screen's own
suggestion path (`web/src/routes/ask/askViewModel.ts#suggestionsFor`) — drop
any capability-chip whose catalog entry carries `roleGate !== "any"` when the
role is not Admin (`roleGate` is on `CapabilityBody`,
`web/src/api/generated/schema.ts:692`). This is presentation only:
`GET /api/capabilities` (`CapabilitiesEndpointExtensions.cs:49`) stays un-gated
and identical for both roles (ADR-022 S16-11). Cite the IA anchor
`inputs/design/prototypes/raffa-v2/ia-v2.md:92-115` (roles on the pilot path).

## Parent story AC covered
- AC-1 non-Admin sees no `roleGate !== "any"` chip; Admin does.
- AC-2 capabilities GET identical for both roles — presentation only.
- AC-3 `role` threaded from `AppShell.tsx`, not re-derived.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/components/shell/AppShell.tsx | pass `role` into `GlobalAskBar` |
| web/src/components/ask-bar/GlobalAskBar.tsx | accept `role`, filter chips |
| web/src/components/ask-bar/askSuggestions.ts | drop `roleGate !== "any"` chips for non-Admin |
| web/src/routes/ask/askViewModel.ts | `suggestionsFor` drops admin-gated chips for non-Admin |

## Context the implementer needs

**Closes: NW-74**

- **Architecture decisions in force**: ADR-022 S16-11 + ADR-012 w17 cl 40 (presentation, never a security fix); the catalog is static and tenant-free (ADR-024 w16 cl 2).
- **Do not touch**: `CapabilitiesEndpointExtensions.cs` / the capabilities endpoint (un-gated by rule); `client.ts`.

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving a non-Admin drops an admin-gated chip and an Admin keeps it (and the capabilities GET body is byte-identical)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | non-Admin drops `roleGate==="admin"` chip; Admin keeps it | `web/src/components/ask-bar/askSuggestions.test.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F01/US01/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-01-ask-chip-role-gate/us-01-ask-chip-role-gate/tasks/task-01-ask-chip-role-gate.md
  produces: [ask-chip-role-gate]
  depends_on: []
  effort: S
  layer: frontend
  status: live
```
