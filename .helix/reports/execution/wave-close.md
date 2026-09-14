# Wave close — `wave-next-w15`

- **When**: 2026-09-14T02:53:30.318225+00:00
- **Product repo**: `C:\Users\luca.la-malfa\source\repos\contigo`
- **Origin**: `https://github.com/lucalamalfa91/contigo.git`
- **PR**: https://github.com/lucalamalfa91/Raffa.ai/pull/103
- **Open points**: 1

## Commits on `integration` not on `origin/main`

- `e976549 E16/F02/US02/T01: wip - Raffa.Storage project, blob adapter moved out of Raffa.Api`
- `d2a6c88 Merge branch 'wave/E16-F02-US01-T01' into integration`
- `42fe06e E16/F01/US01/T01: fix -- reword comments so the DoD's literal RootManageSharedAccessKey/servicebus greps pass clean`
- `6356740 E16/F01/US01/T01: docs -- infra/README.md layout tree, resource names, w15 Service Bus/ACS/Graph section, known gaps`
- `8da4647 E16/F01/US01/T01: wip -- wire communication/servicebus/containerapps/keyvault/identity in both env roots`
- `c41d835 E16/F01/US01/T01: wip -- containerapps: KEDA scale rule, max_replicas, ACS + AzureAd + ServiceBus env vars`
- `4a56481 E16/F01/US01/T01: wip -- identity module Graph permission, optional_claims, tenant_id output`
- `edd9c4a E16/F01/US01/T01: wip -- servicebus RBAC lifecycle fix, keyvault acs-connection secret complete`
- `7066502 E16/F01/US01/T01: wip -- servicebus subscription+RBAC, new communication module`
- `bbe0c57 E16/F02/US01/T01: wip -- claim-race and schema-shape tests`
- `20d73c4 E16/F02/US01/T01: wip -- schema, entities, and claim store`
- `3846767 Merge helix/w15 (w15 plan, ADR-027, three epics, process speed/reliability fixes) into main`
- `dbacd64 process(next-wave): arm MAF compaction, halve the table rounds, cap the lane drafts, double the fan-out degree`
- `69b9dd5 plan(w15): council decisions, ADR-027, three epics and the 11-task wave file`
- `3720543 plan(w15): raw input -- upload feels instant (NW-27/61) + invite end to end (NW-67/68/69) + the W15 queue`

## Delivery per task

| Task | Branch | Own paths | On integration | Salvage tags | Verdict |
|---|---|---|---|---|---|
| `E16/F01/US01/T01` | `wave/E16-F01-US01-T01` | 19 | yes | — | delivered |
| `E16/F02/US01/T01` | `wave/E16-F02-US01-T01` | 13 | yes | `salvage/E16-F02-US01-T01/1`, `salvage/E16-F02-US01-T01/2`, `salvage/E16-F02-US01-T01/3` | delivered |
| `E18/F01/US01/T01` | `wave/E18-F01-US01-T01` | 8 | no | — | **committed, not on integration** |
| `E16/F02/US02/T01` | `wave/E16-F02-US02-T01` | 13 | no | `salvage/E16-F02-US02-T01/1`, `salvage/E16-F02-US02-T01/2` | **committed, not on integration** |
| `E18/F01/US02/T01` | missing | — | — | — | **undelivered** |
| `E16/F02/US03/T01` | `wave/E16-F02-US03-T01` | 0 | yes | — | **undelivered** (no own commits) |
| `E17/F01/US01/T01` | missing | — | — | — | **undelivered** |
| `E16/F03/US01/T01` | missing | — | — | — | **undelivered** |
| `E17/F02/US01/T01` | missing | — | — | — | **undelivered** |
| `E17/F03/US01/T01` | missing | — | — | — | **undelivered** |
| `E16/F04/US01/T01` | missing | — | — | — | **undelivered** |

## Open points

1. Undelivered tasks — no committed work on their `wave/*` branch beyond its fork point, or work not on `integration`: `E18/F01/US01/T01`, `E16/F02/US02/T01`, `E18/F01/US02/T01`, `E16/F02/US03/T01`, `E17/F01/US01/T01`, `E16/F03/US01/T01`, `E17/F02/US01/T01`, `E17/F03/US01/T01`, `E16/F04/US01/T01`. Do not treat the wave as complete. A dead turn's uncommitted files are on the `salvage/<task>/<n>` tags in the table below: `git show --stat <tag>`, then `git checkout <tag> -- <paths>` on a branch from `integration`, finish, commit with the task id.

## How to read Studio

Green on `execution-fanout` means the orchestration finished (`failed_task_ids` empty). With `fan_out.require_delivery` that also means every live task carried committed work on its branch; the table above is the independent audit of that claim (engine and hook measure the same fork-point diff). Green does **not** mean a PR exists, and it does **not** mean there were zero warnings. `on_orchestration_stop` is observation-only (fail-open): a hook error is recorded and the wave still completes. This file is the close record; HITL is the human channel when open points exist.
