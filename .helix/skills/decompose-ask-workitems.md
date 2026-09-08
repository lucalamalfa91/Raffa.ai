# Decomposition — Ask savings copilot (epic-12 / e12)

Ids start at **E12/F01/US01/T01**.

Oracle: `inputs/ask-copilot-brief.md` + ADR-023.

## Features (all OPEN rows from the gap report)

| Feature | Layer | What |
|---------|-------|------|
| F01 | backend | Foundry 5-role gateway + DI + `LoggingAiGateway` wrap |
| F02 | backend | Expand fixture market catalog (labelled representative) |
| F03 | backend | Domain gate + context pack + chat endpoint (deps F01, F02) |
| F04 | backend | `IDocumentStorage.LoadAsync` + re-OCR/re-embed (deps F01) |
| F05 | web | Rich Ask reply UI (deps F03) |

Single-writer per file per phase. F01 owns `Contigo.AiGateway`. F02 owns
`FixtureBenchmarkAdapter`. F03 owns Chat + `ChatEndpointExtensions`. F04 owns
storage + reprocess. F05 owns `web/src/routes/ask/`.

After `wave-spec.ask.yaml`:

```
python scripts/cut_ask_slices.py
```

## Checker fail

- missing `ask-copilot-gaps.md`, ADR-023, or `e12.yaml`
- a write to e01–e11 / e1011 / `slice.current.yaml`
- a task that mixes tenant pgvector with the market catalog
- a task that implements legal advice or a paid Tropic/Vendr adapter
