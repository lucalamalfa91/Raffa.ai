output "client_id" {
  description = "Client id of the company-mailbox app (CaseMail__Microsoft__ClientId)."
  value       = azuread_application.mailbox.client_id
}

output "client_secret" {
  description = "Client secret of the company-mailbox app, for Key Vault (casemail-ms-client-secret)."
  value       = azuread_application_password.mailbox.value
  sensitive   = true
}
