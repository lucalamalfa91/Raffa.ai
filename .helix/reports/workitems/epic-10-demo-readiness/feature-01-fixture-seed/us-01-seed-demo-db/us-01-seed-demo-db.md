---
id: us-01
type: user-story
parent: feature-01
wave: 10
status: active
---

# us-01-seed-demo-db — Seed fixture data on `contigo_demo`

## Acceptance criteria

- [ ] AC-1 A checked-in job/script inserts fixture benchmark data and at
      least one savings opportunity (plus supporting contract row if
      required by FKs) into `contigo_demo`.
- [ ] AC-2 The same job can target `contigo_dev` optionally.
- [ ] AC-3 Seed does not call `MigrateAsync()`. It assumes e09 schema.
- [ ] AC-4 Seed does not disable RLS for the API identity.
