# HITL — Ask savings copilot (e12)

Operator: review `inputs/ask-copilot-brief.md`, `reports/audit/ask-copilot-gaps.md`,
`reports/architecture/ADR-023-ask-savings-copilot.md`, and the amendment
footers on ADR-001 / 004 / 011 / 018 / 020.

Confirm:

- Ask is a **savings copilot**, not generic Q&A.
- Market comparison uses `IBenchmarkService` (fixture now), never another tenant's PDFs.
- No legal advice. Off-domain / greetings redirect into this portfolio.
- Foundry stays behind `IAiGateway` in `Contigo.AiGateway` only.

Stamp only after that review:

```
reports/plan/gates/ask-copilot.hitl-ok
```

Then, with Studio idle and **not** in parallel with e1011 (need
`reports/plan/gates/e1011.hitl-ok`):

```
cd .helix
python scripts/check_slice_prereqs.py --slice e12
./run.ps1 -Max -Slice e12 -o execution-fanout
```

`e12.previous` is `e1011`. Do not copy `e12.yaml` onto `slice.current.yaml`
while the e1011 fan-out is still running.
