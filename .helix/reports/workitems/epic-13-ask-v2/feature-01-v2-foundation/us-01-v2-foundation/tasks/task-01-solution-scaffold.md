---
id: E13/F01/US01/T01
type: task
story: us-01-v2-foundation
wave: 13
status: live
target_repo: contigo-backend
---

# task-01-solution-scaffold — V2 solution shape (slnx, projects, allow-list)

## Coding objective

Create the projects the rest of e13 fills in, so no later task touches
`backend/Contigo.slnx` or the architecture allow-list again:
`backend/src/Contigo.Market` (class library; references `Contigo.SharedKernel`,
`Contigo.AiGateway`, `Contigo.Benchmark`), `backend/src/Contigo.Insights`
(references `Contigo.SharedKernel`, `Contigo.Benchmark`),
`backend/tests/Contigo.Market.Tests`, `backend/tests/Contigo.Insights.Tests`,
`backend/tests/Contigo.Suppliers.Products.Tests`, `backend/tests/Contigo.AiEval`
(xunit; references `Contigo.Chat`, `Contigo.AiGateway`, `Contigo.SharedKernel`).
Each project gets a `ServiceCollectionExtensions.AddMarketModule()` /
`AddInsightsModule()` stub that registers nothing yet, one placeholder
test per test project, and the same `Directory.Build.props` conventions as
the existing projects. Add every project to `backend/Contigo.slnx`. Extend
`backend/tests/Contigo.ArchitectureTests/DependencyDirectionTests.cs`:
`AllowedReferences["Contigo.Market"] = ["Contigo.SharedKernel", "Contigo.AiGateway", "Contigo.Benchmark"]`,
`AllowedReferences["Contigo.Insights"] = ["Contigo.SharedKernel", "Contigo.Benchmark"]`,
and add both to the module lists so the direction test covers them.
`dotnet build Contigo.slnx` and `dotnet test Contigo.slnx` must stay green.

## Parent story AC covered
- AC-4

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/Contigo.slnx` | add six projects |
| `backend/src/Contigo.Market/Contigo.Market.csproj`, `ServiceCollectionExtensions.cs` | new, empty module |
| `backend/src/Contigo.Insights/Contigo.Insights.csproj`, `ServiceCollectionExtensions.cs` | new, empty module |
| `backend/tests/Contigo.Market.Tests/*`, `backend/tests/Contigo.Insights.Tests/*`, `backend/tests/Contigo.Suppliers.Products.Tests/*`, `backend/tests/Contigo.AiEval/*` | new test projects with one placeholder test each |
| `backend/tests/Contigo.ArchitectureTests/DependencyDirectionTests.cs` | allow-list + module lists |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (module map delta), ADR-002 (one project per bounded context; direction test), ADR-004 (SDKs only in `Contigo.AiGateway` — the new projects must not reference Azure SDKs).
- Gap G-SCAFFOLD (`reports/audit/ask-v2-gaps.md`).
- **Do not touch**: any existing module's source, `Program.cs`, `Contigo.AiGateway` (owned by T02 in this phase), the Chat module (owned by F05/T01), Documents (F04/T01), `web/`.
- The solution file is `backend/Contigo.slnx` (XML solution); CI builds it with `dotnet restore/build/test Contigo.slnx` (`.github/workflows/backend.yml`).

## Definition of done
- [ ] `dotnet build backend/Contigo.slnx --configuration Release` exit 0
- [ ] `dotnet test backend/Contigo.slnx --configuration Release --no-build` exit 0 (placeholder tests + `DependencyDirectionTests` green for the new modules)
- [ ] `grep -c "Contigo.Market\|Contigo.Insights\|Contigo.AiEval\|Contigo.Suppliers.Products.Tests" backend/Contigo.slnx` ≥ 6

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | direction allow-list includes Market / Insights and their csproj references match | `Contigo.ArchitectureTests/DependencyDirectionTests.cs` |
| unit | each new test project compiles and runs | placeholder tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F01/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-01-v2-foundation/us-01-v2-foundation/tasks/task-01-solution-scaffold.md
  produces: [v2-scaffold]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
