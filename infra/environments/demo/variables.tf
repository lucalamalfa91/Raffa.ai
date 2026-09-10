variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

# ADR-008 amendment 2026-09-09: demo never creates the shared account (the
# dev root owns it); it attaches by name once the dev root has applied.
# While false this root reads no data source and creates no Foundry
# project, deployment or role assignment, so `terraform plan` never fails
# on an account that does not exist yet.
variable "ai_account_attached" {
  description = "true once the dev root has applied the shared aisvc-contigo account in rg-contigo-ai: attach to it and create demo's own project, deployments and role assignments."
  type        = bool
  default     = true
}

# Two-phase Foundry wiring, same as the dev root: flipped with the demo
# promotion that carries the live-Foundry backend.
variable "ai_gateway_wired" {
  description = "Publish AiGateway__Endpoint and the AiGateway__Models__* env vars to this environment's Container Apps (Foundry path). false keeps the fixture gateway even though the account exists."
  type        = bool
  default     = false
}

# Entra object ids (not secrets) granted the two data-plane roles on the
# shared account. Must not repeat an id already listed in the dev root
# (RoleAssignmentExists on the second apply).
variable "ai_operator_principal_ids" {
  description = "Entra object ids of human operators granted the two data-plane roles on the shared AI services account. Not secrets. Never repeat an id listed in the dev root."
  type        = list(string)
  default     = []
}

variable "ai_gateway_extra_env" {
  description = "Extra non-secret AiGateway__* env vars for both Container Apps, published only when ai_gateway_wired is true."
  type        = map(string)
  default     = {}
}
