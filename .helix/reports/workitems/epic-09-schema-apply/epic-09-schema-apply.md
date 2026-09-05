---
id: epic-09
type: epic
wave: 9
status: active
---

# epic-09-schema-apply — Land EF schema on Azure Postgres

Closes the R0 gap: migrations exist and are tested; they were never applied
to `psql-contigo-dev` / `contigo_dev`. ADR-021.

## Features

| ID | Title |
|----|-------|
| F01 | Idempotent SQL scripts for every DbContext |
| F02 | Apply-on-deploy + Savings/Quotes connection strings |
