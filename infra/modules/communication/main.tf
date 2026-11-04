# modules/communication -- Azure Communication Services Email, the
# invitation mail transport (ADR-005 w14/w15 footers; ADR-011 w15: this
# module's connection string is the wave's ONE new Key Vault secret).
#
# Every resource here is a GLOBAL type -- none takes a `location` argument
# -- so `data_location = "Europe"` is the region control (ADR-006 w14
# footer `:69-83`: a global-type resource carrying `data_location` is not a
# second region; this is the same deviation already in force for
# modules/staticwebapp, applied here on schedule rather than by surprise).
# Four resources, never shared across environments: the Communication
# Service (the "send" resource callers get a connection string from), the
# Email Communication Service, an Azure Managed Domain on it, and the
# association that lets the Communication Service actually send through
# that domain. All four are SKU-less and metered per message with no idle
# charge, so no third fixed monthly cost line joins Service Bus Standard
# and ACR Basic (ADR-005: $0.00 fixed-cost delta).
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "azurerm_communication_service" "this" {
  name                = "acs-raffa-${var.environment}"
  resource_group_name = var.resource_group_name
  data_location       = "Europe"

  tags = local.tags
}

resource "azurerm_email_communication_service" "this" {
  name                = "acsemail-raffa-${var.environment}"
  resource_group_name = var.resource_group_name
  data_location       = "Europe"

  tags = local.tags
}

# "AzureManagedDomain" is the fixed sentinel name Azure requires for an
# Azure Managed Domain (as opposed to a customer-owned custom domain, which
# would instead need the domain's own name here plus TXT/DKIM verification
# this wave deliberately does not take on). The real, auto-generated
# `<guid>.azurecomm.net` hostname comes back as this resource's own
# `mail_from_sender_domain` attribute -- never hand-composed by a caller
# (outputs.tf `sender_address`, ADR-005: "the module output, never hand-
# composed").
resource "azurerm_email_communication_service_domain" "this" {
  name              = "AzureManagedDomain"
  email_service_id  = azurerm_email_communication_service.this.id
  domain_management = "AzureManaged"

  tags = local.tags
}

# Without this association the Communication Service has a connection
# string but no domain it is allowed to send FROM -- every SendEmail call
# fails, invisibly to `terraform validate`/`plan` (the association is what
# an operator or the acceptance walk would notice missing, not a plan diff).
resource "azurerm_communication_service_email_domain_association" "this" {
  communication_service_id = azurerm_communication_service.this.id
  email_service_domain_id  = azurerm_email_communication_service_domain.this.id
}

# ---------------------------------------------------------------------------------------------------------
# Customer-managed sender domain (optional, see the variables for the two-phase rollout).
# ---------------------------------------------------------------------------------------------------------
locals {
  custom_domain_enabled = var.custom_domain != null
  manage_dns            = local.custom_domain_enabled && var.dns_zone_name != null && var.dns_zone_resource_group_name != null

  verification = local.custom_domain_enabled ? azurerm_email_communication_service_domain.custom[0].verification_records[0] : null

  # DKIM selectors are CNAMEs: always safe to manage (written when a DNS zone is given, reported otherwise).
  dkim_candidates = local.custom_domain_enabled ? {
    dkim  = local.verification.dkim[0]
    dkim2 = local.verification.dkim2[0]
  } : {}
  dkim_records = local.manage_dns ? local.dkim_candidates : {}

  # Domain-verification and SPF are TXT records, and they are never written here, only reported
  # (`manual_dns_records`):
  # - custom_domain must equal the zone (precondition below), so both sit at the zone apex, where a TXT record
  #   set is one object: writing it would REPLACE any TXT already there (mail-provider verification, an
  #   existing SPF);
  # - their names are only known after the domain exists, and a for_each filtered on them would fail the
  #   first plan ("Invalid for_each argument": keys must be known at plan time).
  txt_candidates = local.custom_domain_enabled ? {
    domain = local.verification.domain[0]
    spf    = local.verification.spf[0]
  } : {}
}

resource "azurerm_email_communication_service_domain" "custom" {
  count = local.custom_domain_enabled ? 1 : 0

  name              = var.custom_domain
  email_service_id  = azurerm_email_communication_service.this.id
  domain_management = "CustomerManaged"

  tags = local.tags
}

resource "azurerm_email_communication_service_domain_sender_username" "custom" {
  count = local.custom_domain_enabled ? 1 : 0

  name                    = var.sender_username
  email_service_domain_id = azurerm_email_communication_service_domain.custom[0].id
  display_name            = var.sender_display_name
}

data "azurerm_dns_zone" "mail" {
  count = local.manage_dns ? 1 : 0

  name                = var.dns_zone_name
  resource_group_name = var.dns_zone_resource_group_name
}

resource "azurerm_dns_cname_record" "dkim" {
  for_each = local.dkim_records

  name                = each.value.name
  zone_name           = data.azurerm_dns_zone.mail[0].name
  resource_group_name = data.azurerm_dns_zone.mail[0].resource_group_name
  ttl                 = each.value.ttl
  record              = each.value.value

  lifecycle {
    precondition {
      condition     = var.custom_domain == var.dns_zone_name
      error_message = "custom_domain must equal dns_zone_name (a sender sub-domain of a parent zone is not supported here)."
    }
  }
}

# Phase 2 only: Azure links a domain to the communication service once it is verified.
resource "azurerm_communication_service_email_domain_association" "custom" {
  count = local.custom_domain_enabled && var.use_custom_sender ? 1 : 0

  communication_service_id = azurerm_communication_service.this.id
  email_service_domain_id  = azurerm_email_communication_service_domain.custom[0].id
}
