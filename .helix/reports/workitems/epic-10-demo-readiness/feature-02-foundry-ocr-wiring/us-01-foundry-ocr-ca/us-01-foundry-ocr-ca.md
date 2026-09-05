---
id: us-01
type: user-story
parent: feature-02
wave: 10
status: active
---

# us-01-foundry-ocr-ca — Foundry + OCR on Container Apps

## Acceptance criteria

- [ ] AC-1 Inventory `dev` and `demo` CA env for AI Gateway / Foundry /
      Document Intelligence. If already present, document and skip apply.
- [ ] AC-2 If missing, Terraform + HCP (`contigo-dev` / `contigo-demo`)
      injects the required endpoints/identities. No laptop `terraform apply`.
- [ ] AC-3 API/worker can resolve ADR-004 roles without remaining on
      `FixtureAiGateway` solely because env vars were absent.
