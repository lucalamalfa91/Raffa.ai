#!/usr/bin/env python3
"""Generate the master wave-spec.execution.yaml (inline-format the cutter parses)."""
from pathlib import Path

EP = "epic-0%d-platform"

# (phase, id, prompt_suffix, produces, depends_on, effort, layer)
# prompt_suffix is the part after reports/.../tasks/
def P(slug):
    return f"reports/workitems/{slug}"

ROWS = [
 # --- R0 epic-01 (existing 13 tasks + expansions + new features) ---
 (1, "E01/F01/US01/T01", P("epic-01-platform/feature-01-platform-bootstrap/us-01-github-org-repo-protection/tasks/task-01-github-org-repo-protection.md"), ["github-org-repos","github-branch-protection"], [], "M", "backend"),
 (2, "E01/F01/US01/T02", P("epic-01-platform/feature-01-platform-bootstrap/us-01-github-org-repo-protection/tasks/task-02-monorepo-secret-scan.md"), ["repo-secret-scan"], ["github-org-repos"], "S", "backend"),
 (2, "E01/F01/US02/T01", P("epic-01-platform/feature-01-platform-bootstrap/us-02-hcp-terraform-workspaces/tasks/task-01-hcp-workspaces.md"), ["hcp-terraform-workspaces"], ["github-org-repos"], "S", "backend"),
 (3, "E01/F01/US02/T02", P("epic-01-platform/feature-01-platform-bootstrap/us-02-hcp-terraform-workspaces/tasks/task-02-hcp-vcs-wiring.md"), ["hcp-vcs-wiring"], ["hcp-terraform-workspaces"], "S", "backend"),
 (3, "E01/F02/US01/T01", P("epic-01-platform/feature-02-azure-infrastructure/us-01-terraform-module-library/tasks/task-01-terraform-module-library.md"), ["terraform-module-library"], ["github-org-repos","hcp-terraform-workspaces"], "L", "backend"),
 (4, "E01/F02/US01/T02", P("epic-01-platform/feature-02-azure-infrastructure/us-01-terraform-module-library/tasks/task-02-terraform-env-roots.md"), ["terraform-env-roots"], ["terraform-module-library"], "M", "backend"),
 (5, "E01/F02/US02/T01", P("epic-01-platform/feature-02-azure-infrastructure/us-02-dev-environment/tasks/task-01-dev-environment-provision.md"), ["azure-dev-environment"], ["terraform-env-roots"], "L", "backend"),
 (5, "E01/F02/US03/T01", P("epic-01-platform/feature-02-azure-infrastructure/us-03-demo-environment/tasks/task-01-demo-environment-provision.md"), ["azure-demo-environment"], ["terraform-env-roots"], "L", "backend"),
 (6, "E01/F02/US02/T02", P("epic-01-platform/feature-02-azure-infrastructure/us-02-dev-environment/tasks/task-02-dev-outputs-verify.md"), ["dev-outputs-verified"], ["azure-dev-environment"], "S", "backend"),
 (6, "E01/F02/US03/T02", P("epic-01-platform/feature-02-azure-infrastructure/us-03-demo-environment/tasks/task-02-demo-isolation-check.md"), ["demo-isolation-verified"], ["azure-demo-environment"], "S", "backend"),
 (7, "E01/F02/US04/T01", P("epic-01-platform/feature-02-azure-infrastructure/us-04-entra-keyvault/tasks/task-01-entra-keyvault-provision.md"), ["entra-registrations","keyvaults"], ["azure-dev-environment","azure-demo-environment"], "L", "backend"),
 (8, "E01/F02/US04/T02", P("epic-01-platform/feature-02-azure-infrastructure/us-04-entra-keyvault/tasks/task-02-keyvault-scope-grants.md"), ["keyvault-scope-grants"], ["entra-registrations","keyvaults"], "M", "backend"),
 (8, "E01/F02/US05/T01", P("epic-01-platform/feature-02-azure-infrastructure/us-05-foundry-account/tasks/task-01-foundry-account-provision.md"), ["foundry-account"], ["entra-registrations"], "M", "backend"),
 (9, "E01/F02/US05/T02", P("epic-01-platform/feature-02-azure-infrastructure/us-05-foundry-account/tasks/task-02-foundry-connection-verify.md"), ["foundry-connection"], ["foundry-account"], "S", "backend"),
 (9, "E01/F03/US01/T01", P("epic-01-platform/feature-03-ci-cd-delivery/us-01-ci-azure-oidc/tasks/task-01-ci-azure-oidc.md"), ["ci-azure-auth"], ["entra-registrations"], "L", "backend"),
 (10, "E01/F03/US01/T02", P("epic-01-platform/feature-03-ci-cd-delivery/us-01-ci-azure-oidc/tasks/task-02-workflow-auth-step.md"), ["ci-workflow-auth"], ["ci-azure-auth"], "S", "backend"),
 (10, "E01/F03/US02/T01", P("epic-01-platform/feature-03-ci-cd-delivery/us-03-promotion-dev-demo/../../us-02-per-folder-workflows/tasks/task-01-per-folder-workflows.md"), ["ci-cd-workflows"], ["ci-azure-auth"], "L", "backend"),
 (10, "E01/F04/US01/T01", P("epic-01-platform/feature-04-backend-foundation/us-01-dotnet-solution-shape/tasks/task-01-dotnet-solution.md"), ["dotnet-solution"], ["github-org-repos"], "L", "backend"),
 (11, "E01/F03/US02/T02", P("epic-01-platform/feature-03-ci-cd-delivery/us-02-per-folder-workflows/tasks/task-02-path-filter-verify.md"), ["ci-path-filters"], ["ci-cd-workflows"], "S", "backend"),
 (11, "E01/F03/US03/T01", P("epic-01-platform/feature-03-ci-cd-delivery/us-03-promotion-dev-demo/tasks/task-01-promotion-workflow.md"), ["demo-promotion"], ["ci-cd-workflows"], "M", "backend"),
 (11, "E01/F04/US01/T02", P("epic-01-platform/feature-04-backend-foundation/us-01-dotnet-solution-shape/tasks/task-02-architecture-test.md"), ["dotnet-architecture-test"], ["dotnet-solution"], "S", "backend"),
 (12, "E01/F03/US03/T02", P("epic-01-platform/feature-03-ci-cd-delivery/us-03-promotion-dev-demo/tasks/task-02-demo-environment-reviewers.md"), ["demo-reviewers"], ["demo-promotion"], "S", "backend"),
 (12, "E01/F04/US02/T01", P("epic-01-platform/feature-04-backend-foundation/us-02-relational-store/tasks/task-01-ef-core-pgvector.md"), ["postgres-schema"], ["dotnet-solution"], "L", "backend"),
 (13, "E01/F04/US02/T02", P("epic-01-platform/feature-04-backend-foundation/us-02-relational-store/tasks/task-02-migrations-apply.md"), ["postgres-migrations"], ["postgres-schema"], "S", "backend"),
 (14, "E01/F04/US03/T01", P("epic-01-platform/feature-04-backend-foundation/us-03-tenant-rls/tasks/task-01-tenant-rls.md"), ["tenant-rls"], ["postgres-schema"], "L", "backend"),
 (15, "E01/F04/US03/T02", P("epic-01-platform/feature-04-backend-foundation/us-03-tenant-rls/tasks/task-02-rls-migration-check.md"), ["rls-migration-check"], ["tenant-rls"], "S", "backend"),
 (15, "E01/F04/US04/T01", P("epic-01-platform/feature-04-backend-foundation/us-04-deployable-api/tasks/task-01-deployable-api.md"), ["deployable-api"], ["dotnet-solution","tenant-rls"], "M", "backend"),
 (16, "E01/F04/US04/T02", P("epic-01-platform/feature-04-backend-foundation/us-04-deployable-api/tasks/task-02-deployable-worker.md"), ["deployable-worker"], ["deployable-api"], "M", "backend"),
 (17, "E01/F05/US01/T01", P("epic-01-platform/feature-05-identity-workspace/us-01-workspace-roles/tasks/task-01-workspace-roles.md"), ["workspace-roles"], ["tenant-rls"], "M", "backend"),
 (18, "E01/F05/US01/T02", P("epic-01-platform/feature-05-identity-workspace/us-01-workspace-roles/tasks/task-02-membership-invite.md"), ["workspace-membership"], ["workspace-roles"], "M", "backend"),
 (19, "E01/F06/US01/T01", P("epic-01-platform/feature-06-document-ingestion/us-01-document-upload/tasks/task-01-document-upload.md"), ["document-upload"], ["workspace-roles","deployable-api"], "M", "backend"),
 (20, "E01/F06/US01/T02", P("epic-01-platform/feature-06-document-ingestion/us-01-document-upload/tasks/task-02-document-metadata.md"), ["document-metadata"], ["document-upload"], "S", "backend"),
 (21, "E01/F06/US02/T01", P("epic-01-platform/feature-06-document-ingestion/us-02-audit-baseline/tasks/task-01-audit-events.md"), ["audit-abstraction"], ["workspace-roles"], "M", "backend"),
 (22, "E01/F06/US02/T02", P("epic-01-platform/feature-06-document-ingestion/us-02-audit-baseline/tasks/task-02-audit-query.md"), ["audit-query"], ["audit-abstraction"], "S", "backend"),
 (23, "E01/F07/US01/T01", P("epic-01-platform/feature-07-web-client/us-01-web-oidc-shell/tasks/task-01-web-oidc-shell.md"), ["web-client"], ["deployable-api","workspace-roles"], "M", "frontend"),
 (24, "E01/F07/US01/T02", P("epic-01-platform/feature-07-web-client/us-01-web-oidc-shell/tasks/task-02-web-api-client.md"), ["web-api-client"], ["web-client"], "S", "frontend"),
 (24, "E01/F08/US01/T01", P("epic-01-platform/feature-08-mobile-scaffold/us-01-mobile-scaffold/tasks/task-01-mobile-scaffold.md"), ["mobile-scaffold"], ["deployable-api"], "M", "frontend"),
 (25, "E01/F08/US01/T02", P("epic-01-platform/feature-08-mobile-scaffold/us-01-mobile-scaffold/tasks/task-02-mobile-oidc.md"), ["mobile-oidc"], ["mobile-scaffold"], "S", "frontend"),
 (26, "E01/F09/US01/T01", P("epic-01-platform/feature-09-r0-integration/us-01-final-integration/tasks/task-01-r0-integration.md"), ["r0-integration"], ["document-upload","document-metadata","audit-query","web-client","web-api-client","mobile-scaffold","mobile-oidc","deployable-worker"], "L", "backend"),
 # --- R1 epic-02 ---
 (27, "E02/F01/US01/T01", P("epic-02-contract-intelligence/feature-01-extraction-pipeline/us-01-ai-gateway-classification/tasks/task-01-ai-gateway-roles.md"), ["ai-gateway-roles"], ["deployable-worker"], "M", "backend"),
 (28, "E02/F01/US01/T02", P("epic-02-contract-intelligence/feature-01-extraction-pipeline/us-01-ai-gateway-classification/tasks/task-02-ai-gateway-logging.md"), ["ai-gateway-logging"], ["ai-gateway-roles"], "S", "backend"),
 (28, "E02/F02/US01/T01", P("epic-02-contract-intelligence/feature-02-contract-schema/us-01-contract-clause-obligation/tasks/task-01-contract-schema.md"), ["contract-schema"], ["postgres-schema"], "L", "backend"),
 (29, "E02/F01/US02/T01", P("epic-02-contract-intelligence/feature-01-extraction-pipeline/us-02-staged-extraction/tasks/task-01-staged-extraction.md"), ["extraction-pipeline"], ["ai-gateway-roles","contract-schema"], "L", "backend"),
 (29, "E02/F02/US01/T02", P("epic-02-contract-intelligence/feature-02-contract-schema/us-01-contract-clause-obligation/tasks/task-02-schema-evidence.md"), ["contract-evidence-schema"], ["contract-schema"], "M", "backend"),
 (30, "E02/F01/US02/T02", P("epic-02-contract-intelligence/feature-01-extraction-pipeline/us-02-staged-extraction/tasks/task-02-hybrid-ocr.md"), ["hybrid-ocr"], ["extraction-pipeline"], "M", "backend"),
 (30, "E02/F02/US02/T01", P("epic-02-contract-intelligence/feature-02-contract-schema/us-02-embedding-search-index/tasks/task-01-embedding-entity.md"), ["embedding-entity"], ["contract-schema"], "M", "backend"),
 (31, "E02/F02/US02/T02", P("epic-02-contract-intelligence/feature-02-contract-schema/us-02-embedding-search-index/tasks/task-02-tenant-retrieval.md"), ["tenant-retrieval"], ["embedding-entity"], "M", "backend"),
 (32, "E02/F03/US01/T01", P("epic-02-contract-intelligence/feature-03-portfolio-contract-360/us-01-portfolio-list-filters/tasks/task-01-portfolio-list.md"), ["portfolio-list"], ["contract-schema"], "M", "backend"),
 (33, "E02/F03/US01/T02", P("epic-02-contract-intelligence/feature-03-portfolio-contract-360/us-01-portfolio-list-filters/tasks/task-02-portfolio-filters.md"), ["portfolio-filters"], ["portfolio-list"], "M", "backend"),
 (33, "E02/F03/US02/T01", P("epic-02-contract-intelligence/feature-03-portfolio-contract-360/us-02-contract-360-aggregate/tasks/task-01-contract-360.md"), ["contract-360"], ["portfolio-list"], "M", "backend"),
 (34, "E02/F04/US01/T01", P("epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-01-query-router/tasks/task-01-query-router.md"), ["query-router"], ["contract-schema"], "M", "backend"),
 (35, "E02/F04/US01/T02", P("epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-01-query-router/tasks/task-02-deterministic-queries.md"), ["deterministic-queries"], ["query-router"], "M", "backend"),
 (35, "E02/F04/US02/T01", P("epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-02-rag-citations/tasks/task-01-rag-citations.md"), ["rag-citations"], ["tenant-retrieval","ai-gateway-roles"], "L", "backend"),
 (36, "E02/F04/US02/T02", P("epic-02-contract-intelligence/feature-04-ask-raffa-citations/us-02-rag-citations/tasks/task-02-abstain-guard.md"), ["abstain-guard"], ["rag-citations"], "M", "backend"),
 (37, "E02/F05/US01/T01", P("epic-02-contract-intelligence/feature-05-validation-corrections/us-01-correction-history/tasks/task-01-correction-history.md"), ["correction-history"], ["contract-schema"], "M", "backend"),
 (38, "E02/F05/US01/T02", P("epic-02-contract-intelligence/feature-05-validation-corrections/us-01-correction-history/tasks/task-02-correction-audit.md"), ["correction-audit"], ["correction-history","audit-abstraction"], "S", "backend"),
 (39, "E02/F06/US01/T01", P("epic-02-contract-intelligence/feature-06-r1-integration/us-01-final-integration/tasks/task-01-r1-integration.md"), ["r1-integration"], ["hybrid-ocr","contract-evidence-schema","tenant-retrieval","portfolio-filters","contract-360","deterministic-queries","abstain-guard","correction-audit"], "L", "backend"),
 # --- R2 epic-03 ---
 (40, "E03/F01/US01/T01", P("epic-03-renewal-intelligence/feature-01-renewal-engine/us-01-deterministic-dates/tasks/task-01-deterministic-dates.md"), ["renewal-engine"], ["contract-schema"], "M", "backend"),
 (41, "E03/F01/US01/T02", P("epic-03-renewal-intelligence/feature-01-renewal-engine/us-01-deterministic-dates/tasks/task-02-renewal-opportunity.md"), ["renewal-opportunity"], ["renewal-engine"], "M", "backend"),
 (41, "E03/F01/US02/T01", P("epic-03-renewal-intelligence/feature-01-renewal-engine/us-02-priority-score/tasks/task-01-priority-score.md"), ["renewal-priority"], ["renewal-engine"], "M", "backend"),
 (42, "E03/F01/US02/T02", P("epic-03-renewal-intelligence/feature-01-renewal-engine/us-02-priority-score/tasks/task-02-priority-explainability.md"), ["renewal-priority-explain"], ["renewal-priority"], "S", "backend"),
 (42, "E03/F02/US01/T01", P("epic-03-renewal-intelligence/feature-02-cancellation-alerts/us-01-threshold-scheduler/tasks/task-01-threshold-scheduler.md"), ["threshold-scheduler"], ["renewal-engine"], "M", "backend"),
 (43, "E03/F02/US01/T02", P("epic-03-renewal-intelligence/feature-02-cancellation-alerts/us-01-threshold-scheduler/tasks/task-02-alert-recompute.md"), ["renewal-alerts"], ["threshold-scheduler","correction-history"], "M", "backend"),
 (43, "E03/F03/US01/T01", P("epic-03-renewal-intelligence/feature-03-renewal-dashboard/us-01-renewal-dashboard-api/tasks/task-01-renewal-dashboard.md"), ["renewal-dashboard"], ["renewal-engine"], "M", "backend"),
 (44, "E03/F03/US01/T02", P("epic-03-renewal-intelligence/feature-03-renewal-dashboard/us-01-renewal-dashboard-api/tasks/task-02-renewal-action.md"), ["renewal-action"], ["renewal-dashboard"], "M", "backend"),
 (45, "E03/F04/US01/T01", P("epic-03-renewal-intelligence/feature-04-r2-integration/us-01-final-integration/tasks/task-01-r2-integration.md"), ["r2-integration"], ["renewal-opportunity","renewal-priority-explain","renewal-alerts","renewal-action"], "L", "backend"),
 # --- R3 epic-04 ---
 (46, "E04/F01/US01/T01", P("epic-04-savings-intelligence/feature-01-benchmark-service/us-01-benchmark-interface/tasks/task-01-benchmark-interface.md"), ["benchmark-interface"], ["dotnet-solution"], "M", "backend"),
 (47, "E04/F01/US01/T02", P("epic-04-savings-intelligence/feature-01-benchmark-service/us-01-benchmark-interface/tasks/task-02-adapter-registry.md"), ["benchmark-registry"], ["benchmark-interface"], "M", "backend"),
 (47, "E04/F01/US02/T01", P("epic-04-savings-intelligence/feature-01-benchmark-service/us-02-fixture-adapter/tasks/task-01-fixture-adapter.md"), ["fixture-adapter"], ["benchmark-interface"], "M", "backend"),
 (48, "E04/F01/US02/T02", P("epic-04-savings-intelligence/feature-01-benchmark-service/us-02-fixture-adapter/tasks/task-02-fixture-confidence.md"), ["fixture-confidence"], ["fixture-adapter"], "M", "backend"),
 (48, "E04/F02/US01/T01", P("epic-04-savings-intelligence/feature-02-savings-engine/us-01-price-normalization/tasks/task-01-price-normalization.md"), ["savings-normalization"], ["benchmark-interface"], "L", "backend"),
 (49, "E04/F02/US01/T02", P("epic-04-savings-intelligence/feature-02-savings-engine/us-01-price-normalization/tasks/task-02-savings-provenance.md"), ["savings-provenance"], ["savings-normalization"], "S", "backend"),
 (49, "E04/F02/US02/T01", P("epic-04-savings-intelligence/feature-02-savings-engine/us-02-savings-opportunity/tasks/task-01-savings-opportunity.md"), ["savings-opportunity"], ["savings-normalization"], "M", "backend"),
 (50, "E04/F02/US02/T02", P("epic-04-savings-intelligence/feature-02-savings-engine/us-02-savings-opportunity/tasks/task-02-realized-savings.md"), ["realized-savings"], ["savings-opportunity","audit-abstraction"], "M", "backend"),
 (50, "E04/F03/US01/T01", P("epic-04-savings-intelligence/feature-03-savings-dashboard/us-01-savings-kpis/tasks/task-01-savings-kpis.md"), ["savings-kpis"], ["savings-opportunity"], "M", "backend"),
 (51, "E04/F03/US01/T02", P("epic-04-savings-intelligence/feature-03-savings-dashboard/us-01-savings-kpis/tasks/task-02-savings-list.md"), ["savings-list"], ["savings-kpis"], "M", "backend"),
 (52, "E04/F04/US01/T01", P("epic-04-savings-intelligence/feature-04-r3-integration/us-01-final-integration/tasks/task-01-r3-integration.md"), ["r3-integration"], ["benchmark-registry","fixture-confidence","savings-provenance","realized-savings","savings-list"], "L", "backend"),
 # --- R4 epic-05 ---
 (53, "E05/F01/US01/T01", P("epic-05-quote-check/feature-01-quote-extraction/us-01-quote-line-extraction/tasks/task-01-quote-extraction.md"), ["quote-extraction"], ["extraction-pipeline"], "L", "backend"),
 (54, "E05/F01/US01/T02", P("epic-05-quote-check/feature-01-quote-extraction/us-01-quote-line-extraction/tasks/task-02-quote-normalization.md"), ["quote-normalization"], ["quote-extraction"], "M", "backend"),
 (54, "E05/F01/US02/T01", P("epic-05-quote-check/feature-01-quote-extraction/us-02-sku-normalization/tasks/task-01-sku-normalization.md"), ["sku-normalization"], ["quote-extraction"], "M", "backend"),
 (55, "E05/F01/US02/T02", P("epic-05-quote-check/feature-01-quote-extraction/us-02-sku-normalization/tasks/task-02-sku-recalculate.md"), ["sku-recalculate"], ["sku-normalization"], "M", "backend"),
 (55, "E05/F02/US01/T01", P("epic-05-quote-check/feature-02-quote-assessment/us-01-market-assessment/tasks/task-01-market-assessment.md"), ["market-assessment"], ["sku-normalization","benchmark-interface"], "L", "backend"),
 (56, "E05/F02/US01/T02", P("epic-05-quote-check/feature-02-quote-assessment/us-01-market-assessment/tasks/task-02-target-saving.md"), ["target-saving"], ["market-assessment"], "M", "backend"),
 (57, "E05/F03/US01/T01", P("epic-05-quote-check/feature-03-negotiation-strategy/us-01-negotiation-strategy/tasks/task-01-negotiation-strategy.md"), ["negotiation-strategy"], ["target-saving"], "L", "backend"),
 (58, "E05/F03/US01/T02", P("epic-05-quote-check/feature-03-negotiation-strategy/us-01-negotiation-strategy/tasks/task-02-strategy-evidence.md"), ["strategy-evidence"], ["negotiation-strategy"], "M", "backend"),
 (58, "E05/F03/US02/T01", P("epic-05-quote-check/feature-03-negotiation-strategy/us-02-outcome-capture/tasks/task-01-outcome-capture.md"), ["negotiation-outcome"], ["negotiation-strategy"], "M", "backend"),
 (59, "E05/F03/US02/T02", P("epic-05-quote-check/feature-03-negotiation-strategy/us-02-outcome-capture/tasks/task-02-realized-propagation.md"), ["outcome-propagation"], ["negotiation-outcome","realized-savings"], "M", "backend"),
 (60, "E05/F04/US01/T01", P("epic-05-quote-check/feature-04-r4-integration/us-01-final-integration/tasks/task-01-r4-integration.md"), ["r4-integration"], ["quote-normalization","sku-recalculate","target-saving","strategy-evidence","outcome-propagation"], "L", "backend"),
]

# Fix one bad prompt path (the per-folder workflows task had a `../../` detour)
fixed = []
for r in ROWS:
    ph, tid, prompt, prod, dep, eff, layer = r
    if "us-03-promotion-dev-demo/../../us-02-per-folder-workflows" in prompt:
        prompt = prompt.replace("us-03-promotion-dev-demo/../../us-02-per-folder-workflows", "us-02-per-folder-workflows")
    fixed.append((ph, tid, prompt, prod, dep, eff, layer))
ROWS = fixed

lines = ["waveId: wave-v1-demo-r0-r4", "status: planned", "phases:"]
cur_phase = None
for ph, tid, prompt, prod, dep, eff, layer in ROWS:
    if ph != cur_phase:
        lines.append(f"  - id: {ph}")
        lines.append(f"    name: phase-{ph}")
        lines.append("    tasks:")
        cur_phase = ph
    prod_s = ", ".join(prod)
    dep_s = ", ".join(dep)
    lines.append(f"      - {{id: {tid}, prompt: {prompt}, produces: [{prod_s}], depends_on: [{dep_s}], effort: {eff}, layer: {layer}, status: live}}")
lines.append("forks: []")
lines.append("")

out = Path(chr(34)+chr(34)) if False else __import__("pathlib").Path("reports/plan/wave-spec.execution.yaml")

out.write_text(chr(10).join(lines), encoding="utf-8")
print("wrote", len(ROWS), "tasks")
