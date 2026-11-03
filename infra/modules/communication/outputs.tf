# Task E16/F01/US01/T01 (w15 Terraform). Both outputs feed
# modules/keyvault (the connection string, written as secret `acs-connection`,
# handle `acs-cs`) and modules/containerapps (the sender address, published
# as `Invitations__Mail__SenderAddress` on the API app only) via the
# environment root -- never a literal typed a second time anywhere else.
output "primary_connection_string" {
  description = "Primary connection string of this environment's Azure Communication Service resource. Sensitive -- written to Key Vault as acs-connection; never a literal in Terraform source, and not re-exported from any environment root output."
  value       = azurerm_communication_service.this.primary_connection_string
  sensitive   = true
}

output "sender_address" {
  description = "The address invitation mail is sent from: <sender_username>@<custom_domain> once use_custom_sender is true (phase 2, domain verified); otherwise the Azure Managed Domain's default DoNotReply@<mail_from_sender_domain>. Callers must consume this output rather than hand-composing an address."
  value = (
    local.custom_domain_enabled && var.use_custom_sender
    ? "${var.sender_username}@${var.custom_domain}"
    : "DoNotReply@${azurerm_email_communication_service_domain.this.mail_from_sender_domain}"
  )
}

output "manual_dns_records" {
  description = "Records for the custom domain that Terraform deliberately does NOT write (apex TXT sets, and everything when no DNS zone is given), to add or merge by hand: Domain verification, SPF (merge into an existing SPF, there can be only one), plus a suggested DMARC. Empty when no custom_domain is set."
  value = local.custom_domain_enabled ? concat(
    [for key, record in local.txt_candidates : "${upper(key)} (${record.type}) name=${record.name} ttl=${record.ttl} value=${record.value}" if !contains(keys(local.txt_records), key)],
    ["DMARC (TXT) name=_dmarc value=v=DMARC1; p=none; rua=mailto:dmarc@${var.custom_domain} [suggested, optional; skip if a _dmarc record already exists]"],
  ) : []
}
