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

variable "sku_name" {
  description = "Flexible Server compute tier (ADR-005: cheapest Burstable tier)."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "storage_mb" {
  description = "Allocated storage in MB (32768 = 32 GiB, the Flexible Server minimum)."
  type        = number
  default     = 32768
}

variable "postgres_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "16"
}

variable "administrator_login" {
  description = "Administrator login name. The password is generated (random_password), never a literal (ADR-007: no secrets in Terraform source)."
  type        = string
  default     = "raffaadmin"
}
