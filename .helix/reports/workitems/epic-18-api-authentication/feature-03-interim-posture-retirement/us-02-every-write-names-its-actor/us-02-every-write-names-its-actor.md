---
id: us-02
type: user-story
parent: feature-03
wave: w15
status: active
---

# us-02-every-write-names-its-actor — Every write names its actor

## Story

As a **workspace Admin reading the audit trail**, I want every row to name the
person who caused it, so that the trail is evidence rather than decoration.

## Acceptance criteria

- [ ] AC-1 The `UnattributedActor = "unattributed"` constant is **deleted**, not made unreachable.
- [ ] AC-2 The nine service-layer sites that hardcode it — and take no actor from the request at all — receive the resolved identity instead.
- [ ] AC-3 The three divergent behaviours for an absent caller identity collapse to **401**: today the same absent header produces 401 on every `ICallerIdentity` consumer, 400 on conversations, and a silent `"unattributed"` audit row on nine write paths.
- [ ] AC-4 No `CreatedBy`, `CorrectedBy` or audit row can be written with a placeholder actor — proven by a test, not by inspection.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-api-validates-the-token (`E18/F01/US01/T01`, w15) | with the token regime an unauthenticated request is already a 401, which is what makes the constant unreachable — and therefore safe to delete |

## Architecture decisions in force

- **ADR-011** — an audit row that cannot name its actor is a gap in the audit posture; every write names its actor.
- **ADR-022** — the last interim fallback retires.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Thread the resolved actor into nine services; delete the constant | M | **queued — W16** |

## Council decisions carried into this story

Not at the w15 table — **queued**. Evidence:
`reports/context/waves/w15-requirements.md` §2, NW-32 row. The point the w15
table made that binds this story is delivery-manager's, in the **NW-05** row: the
nine service paths that default the actor are exactly why "web deploys before
backend" was refused — that ordering *"can land **writes attributed to nobody**
— the exact rows NW-32 exists to delete"*.

**This is a deliberate deletion, not a side effect.** With NW-05 an
unauthenticated request is a 401 and the constant becomes unreachable at the
endpoint — but the nine service-layer defaults would **survive** as the value
written to `CreatedBy` / `CorrectedBy` and to audit rows.

## Open questions

- Whether historic rows carrying `"unattributed"` are backfilled or left. **Assumption in force**: left, and labelled in the audit UI as pre-attribution rather than rewritten — a backfill would invent an actor the system never observed.
