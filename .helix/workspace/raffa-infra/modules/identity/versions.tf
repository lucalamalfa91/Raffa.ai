# ------------------------------------------------------------------
# Required provider versions for the identity module.
#
# Prevents silent provider version drift in downstream roots that
# consume this module.
# ------------------------------------------------------------------

terraform {
  required_version = ">= 1.5"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = ">= 2.47, < 3.0"
    }
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.85, < 4.0"
    }
  }
}
