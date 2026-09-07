variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

# Task E10/F02/US01/T01 (foundry-ocr-ca, ADR-008): set as this root's HCP
# Terraform workspace variable (contigo-demo) once an operator completes the
# ADR-008 Azure Portal step for the shared `aisvc-contigo` AI services
# account. Empty is a safe, non-blocking default -- see modules/foundry's
# own variables.tf.
variable "foundry_ai_services_resource_id" {
  description = "Full ARM resource id of the shared ADR-008 AI services account (aisvc-contigo), or \"\" if the ADR-008 Azure Portal step has not run yet."
  type        = string
  default     = ""
}
