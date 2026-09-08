# `demo-v*` promotion runbook + SWA config smoke

- **Task**: E10/F03/US01/T01 (parent story `us-01-demo-v-smoke`, AC-1/AC-2/AC-3)
- **Decisions in force**: ADR-016 (promotion dev → demo), ADR-012 (web
  stack / "config, not code"), ADR-022 (Day-1 demo may keep `X-Tenant-Id`
  + fixture seed; ADR-010 remains the post-Day-1 host target)
- **Status**: mechanism already live and proven. `demo-v1` and `demo-v3`
  both promoted successfully before this task existed; this runbook
  documents the repeatable procedure and adds a standing verification
  check (`scripts/check_demo_swa_config.py`,
  `.github/workflows/demo-config-check.yml`) for every future promotion.

## Scope (AC-3)

This is infra smoke only — it proves the promotion *mechanism* and the
SPA's runtime config wiring. It does not rebuild or re-verify the e08
final-integration screens, and it does not apply schema (e09 owns that).
"The first `demo-v*`" below means the first tag-triggered promotion this
mechanism ever ran, not a new promotion cut by this task.

## What already exists (reused, not reinvented)

Per the task text ("Reuse `demo-promote.yml` / `web.yml`; do not invent a
second promotion path"), every moving part below was built by earlier
tasks and is verified live in this runbook, not re-authored:

| Piece | File | Built by |
|---|---|---|
| Tag trigger + approval gate + reused per-folder jobs | `.github/workflows/demo-promote.yml` | task E01/F03/US03/T01 |
| Per-environment deploy + `config.json` injection | `.github/workflows/web.yml`, `scripts/write_web_runtime_config.py` | tasks us-02 (web CI) + fix/web-runtime-config |
| `demo` GitHub Environment required reviewers | `scripts/apply_demo_environment_reviewers.py` | task E01/F03/US03/T02 |
| Static Web App resource | `infra/modules/staticwebapp`, wired into `infra/environments/demo/main.tf` | task E01/F03/US01/T01-shaped web-hosting infra (closed OQ-impl-004) |

This task's own additions are the runbook itself and the read-only
verification check:

| Piece | File |
|---|---|
| Live config.json checker (pure logic unit-tested, network path proven live below) | `scripts/check_demo_swa_config.py` |
| Its tests | `tests/test_check_demo_swa_config.py` |
| Optional, on-demand CI wrapper (`workflow_dispatch`, no Azure credential, no deploy) | `.github/workflows/demo-config-check.yml` |

## Preconditions (verified live 2026-09-07/08, `gh` as `lucalamalfa91`)

```
$ gh api repos/lucalamalfa91/contigo/environments/demo
{
  "name": "demo",
  "protection_rules": [{
    "type": "required_reviewers",
    "prevent_self_review": false,
    "reviewers": [{"type": "User", "reviewer": {"login": "lucalamalfa91", "id": 57912352}}]
  }],
  "deployment_branch_policy": null
}
```

The `demo` GitHub Environment exists with exactly the required-reviewers
gate ADR-016/OQ-DM-002 call for (product-owner + security-architect, both
resolving to the sole repo-owner account on this personal-account repo,
per `scripts/apply_demo_environment_reviewers.py`'s own docstring).

## Runbook — cutting a `demo-v*` promotion

1. **Confirm `main` has what you want to promote.** The commit must
   already be on `main` (merged PR) — `demo-promote.yml`'s
   `verify-tag-on-main` job hard-fails the run otherwise (ADR-016/ADR-014:
   "a tag ... on `main`", enforced with `git merge-base --is-ancestor`
   since GitHub's `push.tags` trigger cannot filter tags by branch).

2. **Tag and push** (next sequence number after the highest existing
   `demo-v*` tag — `git tag -l "demo-v*"` to check):

   ```bash
   git tag demo-v<N> <sha-on-main>   # or HEAD if main is already checked out
   git push origin demo-v<N>
   ```

   No `workflow_dispatch` exists for this on purpose — ADR-016 considered
   and rejected manual dispatch (its Option 2) because it has no immutable
   reference to *what* was promoted. The tag is that reference.

3. **`demo-promote.yml` runs automatically** on the tag push:
   `verify-tag-on-main` → `demo-promotion` (records tag/commit/actor to
   the run summary, itself gated on `environment: demo`) → `promote-infra`
   → `promote-backend` + `promote-web` (each *also* gated on
   `environment: demo` inside the reused `infra.yml`/`backend.yml`/
   `web.yml` jobs).

4. **Approve the pending deployment.** GitHub → this run → "Review
   pending deployments" → select `demo` → Approve. One approval on any of
   the `environment: demo` jobs does not blanket-approve the others in the
   same run; expect to approve `demo-promotion`, `promote-infra`'s apply
   job, `promote-backend`'s deploy job, and `promote-web`'s deploy job
   separately as each becomes pending (this is exactly what `demo-promote.yml`'s
   own header comment names `demo-promotion` for — one clear top-level
   "promoting to demo" entry alongside the per-folder ones).

5. **Verify AC-2 — the SWA `config.json` check** (this task's addition):
   grab the deployed hostname from the `promote-web` job's own "Project
   deployed to `https://<host>`" log line (Azure Static Web Apps CLI
   prints it at the end of the deploy step), then either:

   ```bash
   python scripts/check_demo_swa_config.py --host <swa-host> --environment demo
   ```

   or dispatch `.github/workflows/demo-config-check.yml` with
   `target_environment: demo` and `swa_hostname: <swa-host>` from the
   Actions UI. Exit `0` / `[PASS]` means `config.json` is real
   (non-localhost, non-placeholder) and points at `demo`'s own API and
   OIDC client, never `dev`'s. Exit `1` / `[FAIL]` lists exactly which
   field is wrong.

## Recorded evidence — this mechanism, already run three times

`demo-v*` tags already exist on this repo from before this task (git log,
verified 2026-09-07/08):

| Tag | Commit | Run | Result |
|---|---|---|---|
| `demo-v1` | `a4e564c` "Merge pull request #19 …fix/web-runtime-config" | [33865035471](https://github.com/lucalamalfa91/contigo/actions/runs/33865035471) | ✅ success (2026-09-04T10:49:22Z) |
| `demo-v2` | `22c474f` "Merge pull request #20 …fix/demo-promote-deploy-if" | [33865595161](https://github.com/lucalamalfa91/contigo/actions/runs/33865595161) | ⏹ cancelled (2m49s — superseded while chasing the `promote-web`/`deploy-if` fix that landed as `demo-v3`) |
| `demo-v3` | `2db5734` "Merge pull request #22 …fix/acr-pull-registry" | [33879692679](https://github.com/lucalamalfa91/contigo/actions/runs/33879692679) | ✅ success (2026-09-04T13:43:54Z) — **current live `demo`** |

All three tags are confirmed ancestors of `origin/main` (`git merge-base
--is-ancestor demo-vN origin/main`, exit 0 for each). `demo-v3`'s own job
graph (`gh run view 33879692679 --json jobs`) shows every gated job
(`demo promotion approval`, `promote infra / terraform apply skipped
(demo)`, `promote backend / deploy (demo)`, `promote web / deploy
(demo)`) reached `success` — i.e. the required-reviewer approval in step 4
above was actually exercised and passed, three times.

### Live AC-2 check, run today against the current `demo` and `dev`

Fetched directly (no Azure credential — `config.json` is a public static
asset; `web/public/staticwebapp.config.json` excludes it from the SPA's
`index.html` rewrite):

```
$ python scripts/check_demo_swa_config.py \
    --host mango-desert-084c2231e.6.azurestaticapps.net --environment demo
[PASS] https://mango-desert-084c2231e.6.azurestaticapps.net/config.json config.json is a real,
non-localhost demo config (apiBaseUrl='https://ca-contigo-demo-api.lemonsea-be9510a4.northeurope.azurecontainerapps.io',
oidcClientId='85065229-1707-40b0-98ad-2b3d21db58cf')

$ python scripts/check_demo_swa_config.py \
    --host mango-pond-061bc6d1e.6.azurestaticapps.net --environment dev
[PASS] https://mango-pond-061bc6d1e.6.azurestaticapps.net/config.json config.json is a real,
non-localhost dev config (apiBaseUrl='https://ca-contigo-dev-api.politetree-8bd9702e.northeurope.azurecontainerapps.io',
oidcClientId='da08e279-f6f4-4713-bee1-9dc70406e030')
```

Raw payloads (fetched live, 2026-09-07/08):

| Field | `demo` | `dev` |
|---|---|---|
| `apiBaseUrl` | `https://ca-contigo-demo-api.lemonsea-be9510a4.northeurope.azurecontainerapps.io` | `https://ca-contigo-dev-api.politetree-8bd9702e.northeurope.azurecontainerapps.io` |
| `oidcClientId` | `85065229-1707-40b0-98ad-2b3d21db58cf` | `da08e279-f6f4-4713-bee1-9dc70406e030` |
| `oidcRedirectUri` | `https://mango-desert-084c2231e.6.azurestaticapps.net/` | `https://mango-pond-061bc6d1e.6.azurestaticapps.net/` |

This directly satisfies the parent story's AC-2 ("`demo` SWA `/config.json`
has the demo API URL and Entra public client — not localhost, not `dev`")
and the task's Definition of Done ("a recorded check shows demo SWA
`config.json` is not localhost and not the `dev` API URL"): two visibly
different hosts, two visibly different OIDC client ids, neither containing
`localhost`, each `apiBaseUrl` carrying its own environment's
`ca-contigo-<env>-api` segment and nothing else's.

**Negative-control proof** — the same script correctly *fails* when a
config is checked against the wrong environment (demo's real payload,
checked as if it were meant to be `dev`), confirming the check does not
just rubber-stamp "field is present":

```
$ python scripts/check_demo_swa_config.py \
    --host mango-desert-084c2231e.6.azurestaticapps.net --environment dev
[FAIL] https://mango-desert-084c2231e.6.azurestaticapps.net/config.json (dev):
  - apiBaseUrl='https://ca-contigo-demo-api.lemonsea-be9510a4.northeurope.azurecontainerapps.io' points at demo's API ('ca-contigo-demo-api'), not dev's
  - oidcApiScopes ['api://contigo-demo-api/Contigo.Read', 'api://contigo-demo-api/Contigo.Write'] reference 'contigo-demo-api' (demo), not dev
exit=1
```

Unit tests (23, no network — `tests/test_check_demo_swa_config.py`, run
via `python tests/test_check_demo_swa_config.py -v`): **OK**.

## No blocker recorded

Per this task's Definition of Done, the only alternative to a passing
recorded check is documenting the exact blocker if promote has not been
approved yet. That branch does not apply here: promotion has already been
approved and run to success (`demo-v1`, `demo-v3`), and the live check
above passes today. Nothing is entered on
`reports/audit/demo-readiness-gaps.md` for this story.

## Rollback

ADR-016: rollback for `demo` is re-tagging a known-good `main` SHA (e.g.
`git tag demo-v<N+1> <last-good-sha> && git push origin demo-v<N+1>`) —
there is no "undo", only a new, higher promotion that points at an older
commit. Promotion never touches data (ADR-016, ADR-001): `dev` and `demo`
keep separate Postgres/Storage/Service Bus for their entire lifetime, so a
rollback tag cannot revert or lose `demo` data.

## Troubleshooting (named errors already built into the reused jobs)

`web.yml`'s "Write per-environment config.json" step fails the run (not a
silent no-op) with a specific `::error::` line for each of these — every
one names the HCP Terraform run to check:

- `<swa-name> was not found in <rg>` → the HCP VCS apply for
  `contigo-<env>` has not created the Static Web App yet; check
  `https://app.terraform.io/app/contigo-platform/workspaces/contigo-<env>/runs`.
- `could not resolve defaultHostname` / `could not resolve ingress FQDN` →
  apply is not `CURRENT` yet, or the resource exists but has no
  hostname/ingress configured.
- `<workload-identity> is missing tag oidcPublicClientId` →
  `modules/identity` has not tagged the workload identity yet (same apply
  dependency).
- `GitHub Environment variable AZURE_TENANT_ID is empty` → the `demo`
  Environment's `vars.*` are not set (see `infra/README.md` "Identities —
  do not mix").

ADR-022 note: a missing/placeholder API JWT auth path on the API host is
*not* a Day-1 blocker for this runbook — the first `demo-v*` may keep
`X-Tenant-Id` tenancy on the API while the SPA still does real Entra PKCE
sign-in; only an operator HITL override on
`reports/audit/demo-readiness-gaps.md` would change that, and none was
needed for `demo-v1`/`demo-v3`.

## Related files

- `.github/workflows/demo-promote.yml` — the promotion trigger/gate (not
  modified by this task).
- `.github/workflows/web.yml` — `config.json` injection (not modified by
  this task).
- `scripts/check_demo_swa_config.py` + `tests/test_check_demo_swa_config.py`
  — this task's new verification check.
- `.github/workflows/demo-config-check.yml` — this task's new optional,
  on-demand CI wrapper around the check above.
- `infra/README.md` "Promotion to `demo` (ADR-016) — runbook" — short
  operator-facing pointer to this file, added by this task.
