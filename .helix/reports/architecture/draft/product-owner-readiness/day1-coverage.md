# Day-1 coverage (product-owner-readiness)

Oracle: spec Day-1 / §20 walked in the browser on Azure `demo` after `demo-v*`.
“API exists” is not done.

| §20 step | API | Screen | Data on `demo` | Class |
|----------|-----|--------|----------------|-------|
| Sign-in | Entra on SPA; API still `X-Tenant-Id` | e06 shell | Entra public client in `config.json` | WAVE_COVERED (e06) + ADR-022 |
| Invite Procurement | Identity/workspace API (e01) | e06 members | empty until operator invites | WAVE_COVERED (e06) |
| Upload portfolio | Documents API (e01) | e06 upload | none until upload | WAVE_COVERED (e06) |
| Extract / classify | e02 + AI Gateway | status readback e06 | **needs live Foundry/OCR or fixture gateway** | E10 if CA has no AI/OCR vars |
| Contract 360 | e02 | e07 | extracted rows | WAVE_COVERED (e07) |
| Ask + citations + one abstain | Chat API (e02) | e07 | embeddings / chunks | WAVE_COVERED (e07) |
| Renewal action | e03 | e08 | derived renewals | WAVE_COVERED (e08) |
| Savings (fixture) | e04 fixture adapter | e08 | **not seeded on Flexible Server** | E10 |
| Quote check | e05 | e08 | uploaded quote | WAVE_COVERED (e05+e08) |
| Record outcome / Home realized | e05 outcomes | e08 Home | persisted outcome | WAVE_COVERED (e08) |

`VOTE: APPROVE`
