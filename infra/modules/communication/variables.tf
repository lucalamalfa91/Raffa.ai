variable "environment" {
  description = "Deployment environment. Must be \"dev\" or \"demo\"."
  type        = string

  validation {
    condition     = contains(["dev", "demo"], var.environment)
    error_message = "environment must be \"dev\" or \"demo\"."
  }
}

variable "resource_group_name" {
  description = "Name of the per-environment resource group this module's resources are created in."
  type        = string
}

# Deliberately no `location` variable: every resource in this module is a
# global type with no `location` argument (see main.tf's header comment;
# ADR-006 w14 footer). Adding one here would be dead configuration no
# resource ever reads.
