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

# Task E01/F02/US04/T01 (ADR-011): the caller (infra/environments/{dev,demo}
# /main.tf) must pass its OWN environment's modules/identity output here --
# never the other environment's -- so the role assignment in main.tf never
# crosses the dev/demo isolation boundary.
variable "workload_principal_id" {
  description = "Principal (object) ID of this environment's user-assigned managed identity (modules/identity's `workload_principal_id` output). Granted read access to secrets in this environment's own vault only."
  type        = string
}

# ADR-021: backend.yml reads postgres-connection as contigo-sp-<env>
# (GitHub OIDC). Contributor on the RG is not a Key Vault data-plane
# role once rbac_authorization_enabled=true.
variable "ci_deploy_principal_id" {
  description = "Principal (object) ID of this environment's GitHub Actions deploy SP (contigo-sp-<env>). Granted Key Vault Secrets User on this vault only so CI can fetch postgres-connection."
  type        = string
}

# ADR-011: connection strings land here from sibling modules -- never as
# literals in .tf source. Both are required so each env root must wire
# its own postgres/storage outputs (never the other env's).
variable "postgres_connection_string" {
  description = "Npgsql connection string for this environment's application database (modules/postgres connection_string). Written to this vault as postgres-connection."
  type        = string
  sensitive   = true
}

variable "storage_connection_string" {
  description = "Primary storage connection string for this environment (modules/storage primary_connection_string). Written to this vault as storage-connection."
  type        = string
  sensitive   = true
}
