output "id" {
  description = "Azure resource ID of the Static Web App."
  value       = azurerm_static_web_app.this.id
}

output "name" {
  description = "Name of the Static Web App (swa-raffa-<env>); web.yml composes this."
  value       = azurerm_static_web_app.this.name
}

output "default_host_name" {
  description = "Default hostname (no scheme) used as the public-client SPA redirect origin."
  value       = azurerm_static_web_app.this.default_host_name
}
