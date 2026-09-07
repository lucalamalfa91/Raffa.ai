variable "environment" {
  description = "Deployment environment. Must be \"dev\" or \"demo\"."
  type        = string

  validation {
    condition     = contains(["dev", "demo"], var.environment)
    error_message = "environment must be \"dev\" or \"demo\"."
  }
}

# Task E10/F02/US01/T01 (foundry-ocr-ca, ADR-011): this environment's OWN
# workload identity only -- the caller (infra/environments/{dev,demo}/main.tf)
# must pass its own module.identity.workload_principal_id, never a literal,
# never the other environment's -- same rule modules/keyvault and
# modules/acr already follow for their own role assignments.
variable "workload_principal_id" {
  description = "AAD object (principal) id of this environment's user-assigned workload identity (modules/identity's `workload_principal_id` output). Granted the Cognitive Services data-plane role on the shared ADR-008 AI services account so the AI Gateway can authenticate without a stored key (ADR-011)."
  type        = string
}

variable "ai_services_resource_id" {
  description = <<-EOT
    Full ARM resource id of the single, shared ADR-008 Azure AI Services
    account (`aisvc-contigo` -- scripts/bootstrap_hcp_org.py
    AI_SERVICES_ACCOUNT_NAME). Empty ("") until an operator completes the
    ADR-008 Azure Portal step (the hub, both projects, and this account are
    not Terraform-managed in V1 -- ADR-008: "Model deployment may be a
    one-time Azure-based or portal step... not part of the Terraform module
    surface initially") and records the resulting resource id as this
    environment's HCP Terraform workspace variable
    `foundry_ai_services_resource_id`. While empty, this module still
    computes and exports the endpoint/project/connection names (a pure
    string derivation, no live dependency) but skips the role assignment --
    a resource that does not exist yet cannot be a role-assignment scope.
  EOT
  type        = string
  default     = ""
}
