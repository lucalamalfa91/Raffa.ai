# Cloud Architect — Web Delta Lane: No Infrastructure Delta

- **Seat**: cloud-architect
- **Pass**: web delta (wave/epic 6+)
- **Scope**: confirm the SPA consumes existing Azure infra with zero new SKU, region,
  Terraform module, or environment — or name the one SWA/config gap that blocks it.

## Conclusion

**No infrastructure delta is required.** The web experience is a client-only SPA
consuming the existing HTTP contracts, and its hosting was already decided and
scoped in the locked ADRs. Every cloud concern the web pass touches is already
covered; there is no new Azure service, SKU, region, Terraform module, or
environment to provision for wave/epic 6+.

## Basis — evidence from on-disk locked decisions

| Web pass need | Already satisfied by | Status |
|---|---|---|
| SPA static hosting, TLS, CDN, scale-to-zero | **ADR-012** (Azure Static Web Apps, free tier) | Locked |
| SWA free tier availability in region | **ADR-012** assumption; region locked **ADR-006** `northeurope` | Locked |
| Backend services the SPA calls | **ADR-005** (Container Apps consumption + Postgres Burstable + Storage + Service Bus Standard + Key Vault + Entra ID Free) | Locked |
| Foundry AI services (Ask Raffa / benchmarks) | **ADR-008** (one hub, two projects, pay-as-you-go AI) | Locked |
| Two environments `dev` / `demo`, isolated | Locked decision (Azure, two envs) + **ADR-007** two env Terraform roots | Locked |
| IaC layout / env roots | **ADR-007** reusable modules + two env roots, remote state per env | Locked |
| CI/CD deploy + promotion path | **ADR-014/015/016** (trunk-based, OIDC federation, `demo-v*` promotion) | Locked |
| No secrets in client bundle | Locked decision; **ADR-012** PKCE (public client, no secret) | Locked |

## The one assumption my lane closes

ADR-012 recorded — correctly and honestly — an **open assumption at council-close
V1**: "Azure Static Web Apps free tier is available and sufficient in the chosen
region" and "to be confirmed with cloud-architect at council-close." The web
delta pass does not change that answer; it confirms it:

- SWA free tier is a globally served static host with no regional exclusion that
  affects the `northeurope` backend origin. The SPA bundle is region-agnostic for
  hosting; only its API base URL and OIDC authority are env-specific (runtime
  `config.json`, already real per ADR-012 "config-not-code").
- SWA free tier is sufficient for a static React/Vite bundle with OIDC PKCE: no
  server process, no always-on runtime, no paid Function proxy required. The SPA
  calls the API origin directly (CORS scoped to front-end origins), so SWA needs
  no custom BFF/API-proxy SKU.
- **No new Terraform module, SKU, region, or environment is needed** for web.
  `web.yml` (the SWA deployment job) is a path-filtered CI/CD lane inside the
  existing `infra/`/promotion structure, not a new cloud surface.

## On ADR-007 not enumerating a `staticwebapps` Terraform module

ADR-007's module list (network, identity, postgres, storage, servicebus,
containerapps, keyvault, acr, monitor) deliberately omits a `staticwebapps`
module. This is **not a gap and is not a correction to raise**: SWA is a
managed, portal-provisioned static host whose deployment surface is the CI/CD
job (`web.yml` / Azure Static Web Apps deploy task with
`staticwebapp.config.json` under `web/`), governed by ADR-012 (host choice),
ADR-014/015 (CI + OIDC federation), and ADR-016 (`dev` auto-deploy, `demo-v*`
gated promotion). It is provisioned once per environment via the deployment
lane, not re-applied per-plan through the HCP Terraform module library. Nothing
in the web delta changes that — the SPA remains a static build product with no
server-side Terraform-managed resource.

## Boundary I enforce (and do not reopen)

- Web waves (e06+) must not introduce a BFF, a server-side rendering/runtime host,
  a second storage account, or a custom domain/SKU beyond the locked SWA free tier.
  Any such need would be a defect against ADR-012/ADR-005, which the drafting seat
  would raise as an OBJECT — not a new SKU decision for this lane to quietly sign.
- The only "gap" ever acceptable on this lane is a **deployment/config gap**: a
  missing SWA `web.yml` job or a missing per-env `config.json` pointer. Both are
  client-architect/delivery-manager residuals already covered by ADR-012 and
  ADR-014/015/016; they are not cloud infra deltas.

## Deliverable (default)

Per council-protocol-web: default deliverable is `no-infra-delta.md`. No ADR is
authored by this lane for the web delta, because no new infrastructure decision
exists — the correct outcome is to **not** reopen any SKU. ADR-018+ numbering is
reserved for the IA / design-system / screen-inventory covers owned by
ux-ui-designer and product-owner.

LANE_DRAFTS_WRITTEN: cloud-architect
