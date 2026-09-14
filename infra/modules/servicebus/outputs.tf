# Task E01/F02/US02/T02 (dev-outputs-verify): exposes the "Service Bus
# namespace FQDN" resource id/endpoint ADR-005's Concrete-services table
# calls for. azurerm_servicebus_namespace has no exported fqdn/endpoint
# attribute, so this is the namespace name plus Azure public cloud's fixed,
# documented Service Bus DNS suffix -- not a provider-computed value, but
# not a guess either (no sovereign-cloud variant is in scope for V1).
output "id" {
  description = "Azure resource ID of the Service Bus namespace."
  value       = azurerm_servicebus_namespace.this.id
}

output "name" {
  description = "Name of the Service Bus namespace."
  value       = azurerm_servicebus_namespace.this.name
}

output "fqdn" {
  description = "Fully qualified domain name of the Service Bus namespace (Azure public-cloud DNS suffix)."
  value       = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net"
}

# Task E16/F01/US01/T01 (w15 Terraform): non-secret config, threaded through
# modules/containerapps as ServiceBus__TopicName / ServiceBus__SubscriptionName
# so no consumer ever hand-types a name that can drift from this resource --
# the exact class of defect ADR-027 §C8 found and repaired (a stale
# "extraction-worker" constant naming a subscription this module never
# created).
output "topic_name" {
  description = "Name of the extraction-events Service Bus topic."
  value       = azurerm_servicebus_topic.extraction_events.name
}

output "subscription_name" {
  description = "Name of the document-processing subscription on the extraction-events topic."
  value       = azurerm_servicebus_subscription.document_processing.name
}
