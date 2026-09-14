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
