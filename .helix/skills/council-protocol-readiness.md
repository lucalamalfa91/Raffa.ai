# Council protocol — demo-readiness (6 seats)

Seats: `product-owner-readiness`, `software-architect-readiness`,
`client-architect-readiness`, `cloud-architect-readiness`,
`delivery-manager-readiness`, `security-architect-readiness`.

Read `inputs/demo-readiness-brief.md`. Do not re-open ADR-001…021.

Each producer writes a draft under `reports/architecture/draft/<seat-id>/`.
The table must produce `reports/audit/demo-readiness-gaps.md`. ADR-022 only
if a Day-1 decision is needed (proposed: fixture seed + `X-Tenant-Id` until
ADR-010).

```
VOTE: APPROVE
```

APPROVE when the gap file exists and e10 does not duplicate e05/e09/e06–e08.
