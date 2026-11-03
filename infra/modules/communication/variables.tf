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

# Customer-managed sender domain (e.g. raffa.ai). Two phases, because Azure only links a domain to the
# communication service once it is VERIFIED, and verification is a call Terraform cannot make:
#   phase 1  custom_domain (+ the DNS zone) set: creates the domain, the sender username and the DKIM
#            CNAMEs; the SPF / domain-verification TXT records at the zone apex are NOT written (they would
#            replace the apex TXT set, which usually already holds other records) and are shown in the
#            `manual_dns_records` output instead. Mail still goes from the Azure-managed address.
#   then     add those records, wait for DNS, run the four `az communication email domain initiate-verification`
#            calls (Domain, SPF, DKIM, DKIM2) until each is Verified.
#   phase 2  use_custom_sender = true: links the verified domain and switches sender_address to
#            <sender_username>@<custom_domain>.
variable "custom_domain" {
  description = "Domain to send from (must equal dns_zone_name). null = keep sending from the Azure-managed domain."
  type        = string
  default     = null
}

variable "dns_zone_name" {
  description = "Azure DNS zone that hosts custom_domain. null = Terraform writes no DNS records at all."
  type        = string
  default     = null
}

variable "dns_zone_resource_group_name" {
  description = "Resource group of that Azure DNS zone."
  type        = string
  default     = null
}

variable "sender_username" {
  description = "Local part of the sender address (noreply -> noreply@<custom_domain>)."
  type        = string
  default     = "noreply"
}

variable "sender_display_name" {
  description = "Name shown next to the sender address."
  type        = string
  default     = "Raffa.ai"
}

variable "use_custom_sender" {
  description = "Phase 2: link the VERIFIED custom domain and send from it. Leave false until all four verifications show Verified, or invitation mail stops."
  type        = bool
  default     = false
}
