variable "environment" {
  description = "dev | demo -- names the app raffa-<env>-mailbox."
  type        = string
}

variable "api_public_base_url" {
  description = "Public https base of the API; the redirect URI is <this>/api/mailbox/callback/microsoft."
  type        = string

  validation {
    condition     = startswith(var.api_public_base_url, "https://")
    error_message = "api_public_base_url must be an https URL."
  }
}
