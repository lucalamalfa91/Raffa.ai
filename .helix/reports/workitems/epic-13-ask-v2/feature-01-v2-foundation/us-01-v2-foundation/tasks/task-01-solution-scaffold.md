---
id: E13/F01/US01/T01
type: task
story: us-01-v2-foundation
wave: 13
status: live
target_repo: raffa-backend
---

# task-01-solution-scaffold — V2 solution shape (slnx, projects, allow-list)

## Coding objective

Create the projects the rest of e13 fills in, so no later task touches
`backend/Raffa.slnx` or the architecture allow-list again:
`backend/src/Raffa.Market` (class library; references `Raffa.SharedKernel`,
`Raffa.AiGateway`, `Raffa.Benchmark`), `backend/src/Raffa.Insights`
(references `Raffa.SharedKernel`, `Raffa.Benchmark`),
`backend/tests/Raffa.Market.Tests`, `backend/tests/Raffa.Insights.Tests`,
`backend/tests/Raffa.Suppliers.Products.Tests`, `backend/tests/Raffa.AiEval`
(xunit; references `Raffa.Chat`, `Raffa.AiGateway`, `Raffa.SharedKernel`).
Each project gets a `ServiceCollectionExtensions.AddMarketModule()` /
`AddInsightsModule()` stub that registers nothing yet, one placeholder
test per test project, and the same `Directory.Build.props` conventions as
the existing projects. Add every project to `backend/Raffa.slnx`. Extend
`backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs`:
`AllowedReferences["Raffa.Market"] = ["Raffa.SharedKernel", "Raffa.AiGateway", "Raffa.Benchmark"]`,
`AllowedReferences["Raffa.Insights"] = ["Raffa.SharedKernel", "Raffa.Benchmark"]`,
and add both to the module lists so the direction test covers them.
`dotnet build Raffa.slnx` and `dotnet test Raffa.slnx` must stay green.

## Parent story AC covered
- AC-4

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/Raffa.slnx` | add six projects |
| `backend/src/Raffa.Market/Raffa.Market.csproj`, `ServiceCollectionExtensions.cs` | new, empty module |
| `backend/src/Raffa.Insights/Raffa.Insights.csproj`, `ServiceCollectionExtensions.cs` | new, empty module |
| `backend/tests/Raffa.Market.Tests/*`, `backend/tests/Raffa.Insights.Tests/*`, `backend/tests/Raffa.Suppliers.Products.Tests/*`, `backend/tests/Raffa.AiEval/*` | new test projects with one placeholder test each |
| `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` | allow-list + module lists |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (module map delta), ADR-002 (one project per bounded context; direction test), ADR-004 (SDKs only in `Raffa.AiGateway` — the new projects must not reference Azure SDKs).
- Gap G-SCAFFOLD (`reports/audit/ask-v2-gaps.md`).
- **Do not touch**: any existing module's source, `Program.cs`, `Raffa.AiGateway` (owned by T02 in this phase), the Chat module (owned by F05/T01), Documents (F04/T01), `web/`.
- The solution file is `backend/Raffa.slnx` (XML solution); CI builds it with `dotnet restore/build/test Raffa.slnx` (`.github/workflows/backend.yml`).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exit 0
- [ ] `dotnet test backend/Raffa.slnx --configuration Release --no-build` exit 0 (placeholder tests + `DependencyDirectionTests` green for the new modules)
- [ ] `grep -c "Raffa.Market\|Raffa.Insights\|Raffa.AiEval\|Raffa.Suppliers.Products.Tests" backend/Raffa.slnx` ≥ 6

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | direction allow-list includes Market / Insights and their csproj references match | `Raffa.ArchitectureTests/DependencyDirectionTests.cs` |
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
