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

# Task E16/F01/US01/T01 (w15 Terraform, ADR-005 w15 footer clause 9, ADR-027
# §C8): the one and only subscription on extraction-events. A topic with no
# subscription DISCARDS every message -- this is why a producer shipped
# against the pre-w15 Terraform reported 201 and silently lost every
# document. Name is "document-processing", never "extraction-worker" --
# ADR-027 §C8 repaired that stale constant; one name, or the Terraform and
# the consumer disagree about which subscription holds the messages.
#
# max_delivery_count = 8 (raised from an earlier 5, ADR-005 w15 clause 9):
# deliveries and attempts do not advance together. The app's own
# MaxAttempts = 3 plus two DeliveryCount-consuming abandons is 5, but scale-
# in eviction (min_replicas = 0 on the worker) and the 30-minute lock-
# renewal ceiling against lock_duration = PT5M can each spend a further
# delivery with no attempt recorded -- a realistic worst case of 7 against a
# ceiling of 5, which would let the broker dead-letter a message before the
# handler ever writes Failed. 8 keeps max_delivery_count strictly above
# MaxAttempts with headroom for both, so the DATABASE -- not the broker --
# owns the terminal state (ADR-027).
#
# lock_duration = "PT5M" is the Service Bus maximum, coupled to the
# backend's ServiceBus__MaxAutoLockRenewalMinutes = 30. default_message_ttl
# = "P1D" bounds a looping message by time, not by count, so the headroom
# above can never loop forever. requires_session = false and
# dead_lettering_on_message_expiration = true are both deliberate: sessions
# would silently cap the worker at one effective replica (rejected,
# ADR-005), and a message that outlives its TTL must land in the DLQ rather
# than vanish -- the DLQ is durable and swept by nothing, which is what
# makes "recoverable" true (ADR-027 §C6).
resource "azurerm_servicebus_subscription" "document_processing" {
  name                = "document-processing"
  topic_id            = azurerm_servicebus_topic.extraction_events.id
  max_delivery_count  = 8
  lock_duration       = "PT5M"
  default_message_ttl = "P1D"
  requires_session    = false

  dead_lettering_on_message_expiration = true
}

# ADR-011 (owns the secret-versus-identity question, w15 footer): identity +
# RBAC, never a connection string and never RootManageSharedAccessKey.
# Topic-scoped (not namespace-wide), so a leaked grant discloses no more
# than this one topic -- and the message itself carries ids only (ADR-009),
# so even that residual discloses no contract content. NW-27 adds NO Key
# Vault secret because of these two role assignments.
resource "azurerm_role_assignment" "workload_servicebus_sender" {
  scope                            = azurerm_servicebus_topic.extraction_events.id
  role_definition_name             = "Azure Service Bus Data Sender"
  principal_id                     = var.workload_principal_id
  skip_service_principal_aad_check = true

  # Matches modules/acr's own documented gotcha: ARM rejects in-place
  # updates to a role assignment ("doesn't support update"), so a later
  # apply that touches nothing meaningful here must not attempt one.
  lifecycle {
    ignore_changes = [
      skip_service_principal_aad_check,
      principal_type,
      name,
    ]
  }
}

resource "azurerm_role_assignment" "workload_servicebus_receiver" {
  scope                            = azurerm_servicebus_topic.extraction_events.id
  role_definition_name             = "Azure Service Bus Data Receiver"
  principal_id                     = var.workload_principal_id
  skip_service_principal_aad_check = true

  lifecycle {
    ignore_changes = [
      skip_service_principal_aad_check,
      principal_type,
      name,
    ]
  }
}
