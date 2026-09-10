# Software-architect module map (draft)

Locked topology (spec §5.1, brief §7, §10) names these boundaries. This map fixes the boundary of each module, its primary entities/aggregates, which Appendix A endpoints it owns, and — critically — its **dependency direction** (who may reference whom).

## Rule of direction (non-negotiable)

- **Domain modules** (Identity/Workspace, Documents/Contracts, Suppliers/Products, Renewals, Savings, Quotes, Chat) may reference **only**: the Shared Kernel, and the **interfaces** of AI Gateway and Benchmark Service. Never a provider SDK, never another domain module's internals.
- **AI Gateway** and **Benchmark Service** own their provider adapters and are referenced by domain modules through interfaces only.
- **Audit** is a cross-cutting capability that every module writes to (via a shared audit abstraction), never a domain it is subordinate to.
- The API host and Worker host are thin composition roots above all modules; they do not contain business logic.

## Module boundaries

| Module (bounded context) | Owns (entities / aggregates) | Appendix A endpoints | Notes |
| --- | --- | --- | --- |
| Identity / Workspace | Workspace, User, Role, Membership, `tenant_id` ownership | `/api/workspaces`, `/api/users` | Multi-tenancy origin; owns tenant scoping (`tenant_id` on business data). |
| Documents / Contracts | Document, DocumentVersion, ExtractionJob, Contract, ContractVersion, Clause, Obligation, Risk, CorrectionHistory | `/api/documents`, `/api/contracts` | Staged, schema-constrained extraction; human correction history; evidence + confidence metadata. |
| Suppliers / Products | Supplier, Product, SKU, catalog mapping | `/api/suppliers`, `/api/products` | Normalized supplier/product references for benchmark matching. |
| Renewals | Renewal (dates, cancellation deadline), RenewalAction | `/api/renewals` | Deterministic date/deadline computation in code (Appendix C rule 6). |
| Savings | SavingsOpportunity, RealizedSavings | `/api/savings` | Consumes Benchmark Service; deterministic money math in code. |
| Quotes | Quote, QuoteLine, Assessment, NegotiationOutcome | `/api/quotes`, `/api/negotiations/outcomes` | Line-level assessment; records negotiated outcome as proprietary learning data. |
| Chat (Ask Raffa) | Query, QueryRoute, Answer, Citation | `/api/chat/query` | Routes structured → deterministic query, semantic/legal → RAG with citations; auth-before-retrieval. |
| Benchmark Service | BenchmarkQuery, BenchmarkResult, ProviderAdapter (interface) | `/api/benchmarks` | Interface + replaceable adapter; fixture adapter is enough for first `demo` (brief §3). |
| AI Gateway | OcrRequest, ClassifyRequest, ExtractRequest, EmbedRequest, RAG/AnsweredRequest, usage log records | (internal; no public endpoints) | Only place that touches Foundry / Document Intelligence; logs model/version/prompt-version/timestamp/input-hash (brief §8) and OCR page count (ADR-017). |
| Audit | AuditEvent (access, correction, negotiation, AI usage) | `/api/audit` | Append-only; captures access and corrections from day one (Appendix C rule 9). |
| Shared Kernel | `TenantId`, `EntityId`, `Result<T>`, `IClock`, audit abstraction, domain-event envelope | — | No business logic; foundation types only. |

## Dependency graph (allowed references)

```
Shared Kernel ◄── all domain modules, AI Gateway, Benchmark Service, Audit
AI Gateway (interface) ◄── Documents/Contracts, Chat
Benchmark Service (interface) ◄── Renewals, Savings, Quotes
Audit abstraction ◄── all modules (write-only)
AI Gateway (impl) ──references──► Foundry SDK (isolated)
Benchmark Service (impl) ──references──► provider adapters (isolated)
API Host ──► all modules (composition root)
Worker Host ──► Documents/Contracts, Renewals, Savings, Quotes, AI Gateway, Benchmark Service (composition root)
```

## Worker responsibilities (single Worker host, multiple queue types)

- Document extraction (Documents/Contracts): hybrid parse (native text and/or OCR via AI Gateway, ADR-017) → classify → extract → schema-constrain → persist → mark job complete. Full document; no 2-page cap.
- Renewal recomputation (Renewals): recompute dates/deadlines when a contract/term is corrected.
- Benchmark refresh (Benchmark Service): enqueue/refresh benchmark lookups; recompute savings opportunities on `benchmark.updated`.
- Quote assessment (Quotes): run line-level benchmark matching and assessment.

## Cross-cutting concerns

- **Tenancy** (`tenant_id`): applied at the data layer via row-level security/query filters (see security-architect ADR-data-store tenancy); domain code passes tenant context, does not implement RLS itself.
- **Deterministic math**: dates, cancellation deadlines, money, and savings arithmetic stay in domain code (Renewals, Savings, Quotes), never in the LLM (Appendix C rule 6).
- **Evidence + confidence**: all extracted/consequential facts carry source span + confidence; corrections are versioned, never destructively overwritten (Appendix C rules 2, 5).
