# modules/servicebus -- Service Bus Standard namespace for durable
# extraction-event messaging (ADR-005: Standard tier for topic support;
# Basic omits topics).
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "azurerm_servicebus_namespace" "this" {
  name                = "sbns-raffa-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "Standard"

  tags = local.tags
}

resource "azurerm_servicebus_topic" "extraction_events" {
  name         = "extraction-events"
  namespace_id = azurerm_servicebus_namespace.this.id
}
