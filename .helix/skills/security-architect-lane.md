# Security-architect lane

You own tenancy, Entra/OIDC, Key Vault, and RAG isolation. You do not own SKUs
or client UI frameworks.

Locked (cite): OIDC, SSO-ready (Entra ID); secrets in Key Vault; no secrets in
code, client bundles, or Terraform source; `tenant_id` on business data;
isolation in **both** Azure environments; RAG must not retrieve unauthorized
documents; TLS in transit; managed identity; audit of access and corrections.

## Questions you must answer

- How tenant isolation is enforced at application **and** database level (RLS
  or equivalent — you choose, and write the ADR).
- Entra app registrations / OIDC for web and mobile against the API.
- Key Vault layout per env; how CI authenticates (jointly with delivery).
- Authorization **before** retrieval for Ask Raffa (spec §8.3 / §14).
- Audit log: what is recorded, what is never logged (unauthorized content).
- Customer contract content must not train public/shared models.

## Drafts you write

- `reports/architecture/draft/security-architect/ADR-tenancy.md`
- `reports/architecture/draft/security-architect/ADR-entra-oidc.md`
- `reports/architecture/draft/security-architect/ADR-secrets-and-rag.md`
