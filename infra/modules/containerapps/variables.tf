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

# Task E01/F02/US04/T02 (ADR-011): the caller (infra/environments/{dev,demo}
# /main.tf) must pass its OWN environment's modules/identity
# `workload_identity_id` output here -- never a literal, never the other
# environment's -- so the API/worker Container Apps can only ever present
# this environment's own workload identity, and therefore can only ever
# reach this environment's own Key Vault (modules/keyvault's role
# assignment is scoped to the matching `workload_principal_id`).
# DefaultAzureCredential cannot guess WHICH user-assigned identity to use when a
# container app has one attached: without AZURE_CLIENT_ID it asks IMDS for the
# system-assigned identity, which does not exist here, and every token request
# fails. Passed separately from workload_identity_id (an ARM resource id, which
# the credential does not accept) -- modules/identity exposes both.
variable "workload_identity_client_id" {
  description = "Client (application) id of this environment's user-assigned workload identity (modules/identity's `workload_identity_client_id` output). Published to both containers as AZURE_CLIENT_ID so DefaultAzureCredential authenticates as that identity."
  type        = string
}

variable "workload_identity_id" {
  description = "ARM resource ID of this environment's user-assigned managed identity (modules/identity's `workload_identity_id` output). Assigned to the API and worker Container Apps so they can authenticate to this environment's own Key Vault (and other Azure services) without a stored secret."
  type        = string
}

variable "acr_login_server" {
  description = "Login server of this environment's Container Registry (modules/acr login_server). Used in registry {} so API/worker pull with the workload identity, not an admin password."
  type        = string
}

variable "postgres_connection_secret_id" {
  description = "Versionless Key Vault secret ID for the Postgres connection string (modules/keyvault postgres_connection_secret_versionless_id). Container Apps resolve it via the workload identity."
  type        = string
}

variable "storage_connection_secret_id" {
  description = "Versionless Key Vault secret ID for the Storage connection string (modules/keyvault storage_connection_secret_versionless_id). Container Apps resolve it via the workload identity."
  type        = string
}

variable "spa_host_name" {
  description = "Default hostname of this environment's Static Web App (no scheme). Ingress CORS allows https://<this> origin only."
  type        = string
}

variable "container_image" {
  description = "Placeholder container image for the API and worker apps until the first real image is published to the acr module's registry (ADR-005: Consumption profile Container Apps)."
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "cpu" {
  description = "vCPU per replica (ADR-005 default: 0.25)."
  type        = number
  default     = 0.25
}

variable "memory" {
  description = "Memory per replica (ADR-005 default: 0.5 GiB)."
  type        = string
  default     = "0.5Gi"
}

# Task E10/F02/US01/T01 (foundry-ocr-ca, ADR-004/ADR-008/ADR-017): non-secret
# AI Gateway connection info from this environment's modules/foundry
# instance (an endpoint URL and two names -- no key, ADR-011). No live
# IAiGateway implementation binds these yet -- only Fixtures/FixtureAiGateway.cs
# is registered today (see Contigo.AiGateway.ServiceCollectionExtensions'
# own doc comment) -- adding the env vars ahead of that implementation is
# the same "infra lands before the consuming code" sequencing this
# module's own ConnectionStrings__Savings / __Quotes env vars already used
# (task E09/F02/US01/T01). This closes AC-3's *env var* half only: the
# API/worker are no longer blocked on FixtureAiGateway *solely* because
# these were absent; a live gateway implementation is a separate,
# not-yet-scheduled task.
variable "ai_gateway_endpoint" {
  description = "This environment's AI services account endpoint (modules/foundry ai_services_endpoint output)."
  type        = string
}

variable "ai_gateway_project_name" {
  description = "This environment's Foundry project name (modules/foundry foundry_project_name output)."
  type        = string
}

variable "ai_gateway_document_intelligence_connection" {
  description = "This environment's Document Intelligence connection name (modules/foundry document_intelligence_connection output)."
  type        = string
}

# ADR-004 amendment 2026-09-09: the per-role model ids/versions the backend
# binds as AiGateway:Models:<Role>:ModelId / :ModelVersion, produced by
# modules/foundry from its deployment resources (model_env output) and
# published as one env block per entry. An empty map emits no env block at
# all, so a fixture-gateway environment never carries a model id it does
# not call. Non-secret (deployment names and version strings).
variable "ai_gateway_model_env" {
  description = "AiGateway__Models__<Role>__ModelId / __ModelVersion (and extra AiGateway__* knobs) env vars for both Container Apps (modules/foundry model_env output). Empty map = no env block emitted."
  type        = map(string)
}
