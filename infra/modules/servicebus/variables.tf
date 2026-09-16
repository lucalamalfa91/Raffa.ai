variable "environment" {
  description = "Deployment environment. Must be \"dev\" or \"demo\"."
  type        = string

  validation {
    condition     = contains(["dev", "demo"], var.environment)
    error_message = "environment must be \"dev\" or \"demo\"."
  }
}

variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

variable "resource_group_name" {
  description = "Name of the per-environment resource group this module's resources are created in."
  type        = string
}

# Task E16/F01/US01/T01 (w15 Terraform): the caller (infra/environments/{dev,demo}
# /main.tf) must pass its OWN environment's modules/identity workload_principal_id
# output here -- never the other environment's -- so the two topic-scoped role
# assignments below never cross the dev/demo isolation boundary (same rule
# modules/keyvault's workload_principal_id already follows).
variable "workload_principal_id" {
  description = "Principal (object) ID of this environment's user-assigned workload identity (modules/identity's `workload_principal_id` output). Granted two topic-scoped roles -- Azure Service Bus Data Sender and Data Receiver -- on the extraction-events topic only (ADR-011: identity + RBAC, never a namespace-wide rule, never a shared access key, never a Key Vault secret)."
  type        = string
}

# Task E20/F02/US01/T01 (ADR-022 w17 clause 1): the CI deploy principal
# (raffa-sp-<env>, resolved by both roots from data.azuread_service_principal.ci_deploy)
# that needs topic-scoped Azure Service Bus Data Sender to dispatch the
# bulk-reprocess workflow. Named to match modules/keyvault/variables.tf:34
# so a single grep finds every grant made to that principal across all modules.
variable "ci_deploy_principal_id" {
  description = "Object ID of the per-environment CI deploy service principal (raffa-sp-<env>). Granted Azure Service Bus Data Sender on the extraction-events topic only (E20/F02/US01/T01, ADR-005 w17 §15, ADR-022 w17 clause 1). No default -- both env roots must pass it explicitly."
  type        = string
}
