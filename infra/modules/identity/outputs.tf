# Task E01/F02/US04/T01 (entra-keyvault-provision). Non-secret values a
# later task wires into: the API/web/mobile OIDC configuration (ADR-010 --
# public_client_id, api_client_id/api_identifier_uri as audience, and
# issuer), and modules/keyvault's role assignment (ADR-011 --
# workload_principal_id, so this environment's managed identity can be
# granted access to this environment's own vault only).
output "public_client_id" {
  description = "Application (client) ID of the public-client Entra registration web and mobile authenticate with (Authorization Code + PKCE, no secret)."
  value       = azuread_application.public_client.client_id
}

output "api_client_id" {
  description = "Application (client) ID of the API Entra registration -- the default `aud` claim on v2 access tokens issued for this environment's API."
  value       = azuread_application.api.client_id
}

output "api_identifier_uri" {
  description = "App ID URI of the API Entra registration -- the alternate audience/resource identifier a client may request a token for."
  # identifier_uris is a set(string) -- sets have no index, but this
  # module only ever assigns exactly one, so `one(...)` is the correct
  # (and only valid) way to pull that single element back out.
  value = one(azuread_application.api.identifier_uris)
}

output "issuer" {
  description = "OIDC v2 issuer URL for this Entra tenant -- the API validates a token's `iss` claim against this value (ADR-010)."
  value       = "https://login.microsoftonline.com/${data.azuread_client_config.current.tenant_id}/v2.0"
}

# The client (application) id of the same identity. DefaultAzureCredential needs
# this one -- an ARM resource id is not something it can authenticate with -- and
# a container app with a user-assigned identity must publish it as AZURE_CLIENT_ID
# or every managed-identity token request fails (see modules/containerapps).
output "workload_identity_client_id" {
  description = "Client (application) id of the user-assigned workload identity. Published to the container apps as AZURE_CLIENT_ID."
  value       = azurerm_user_assigned_identity.workload.client_id
}

output "workload_principal_id" {
  description = "Principal (object) ID of this environment's user-assigned workload identity -- input to modules/keyvault's role assignment so the API/worker can read this environment's own vault only (ADR-011)."
  value       = azurerm_user_assigned_identity.workload.principal_id
}

# Task E01/F02/US04/T02 (keyvault-scope-grants) and task E01/F02/US05/T01
# (foundry-account-provision) both need the ARM resource id of the SAME
# workload identity `workload_principal_id` (above) identifies by
# principal/object id. `azurerm_role_assignment.principal_id` (modules/
# keyvault) and `azurerm_container_app.identity[0].identity_ids` (modules/
# containerapps) take two different shapes of identifier for one
# underlying identity -- a role assignment's principal_id is the AAD
# object id, while attaching a user-assigned identity to a resource, or
# recording the ADR-008 Foundry/Document Intelligence connection's RBAC
# grant (ADR-011), needs its full ARM resource id. Task E01/F02/US05/T02
# (foundry-connection-verify) records this same value against each
# per-project Foundry connection id (scripts/foundry_connection_verify.py)
# -- one workload identity per environment, never a literal, in any of
# its three consumers.
#
# NOTE (E01/F02/US05/T02): this output previously existed as two
# textually-duplicated `output "workload_identity_id" { ... }` blocks
# (one nested, malformed, inside the other) -- a phase-barrier merge
# collision between task E01/F02/US04/T02 and task E01/F02/US05/T01,
# which independently added the same-named output on sibling branches cut
# from the same parent commit. That state failed `terraform fmt
# -recursive -check` (Unclosed configuration block) and `terraform
# validate` (Missing required argument "value"; Unsupported block type)
# for both environments. Collapsed back to the single block Terraform
# requires; the value and consumers are unchanged.
output "workload_identity_id" {
  description = "Full ARM resource ID of this environment's user-assigned workload identity -- input to modules/containerapps' `identity { identity_ids = [...] }` block (ADR-011, task E01/F02/US04/T02) and recorded against this environment's Foundry project connection id (ADR-008, ADR-017, tasks E01/F02/US05/T01-T02), alongside workload_principal_id above."
  value       = azurerm_user_assigned_identity.workload.id
}
