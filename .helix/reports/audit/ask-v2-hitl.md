# HITL — Ask Contigo V2 (epic-13 / e13)

Operator: review, in this order,

1. `inputs/requirements.md` (§0 decisions D1–D8, §5 requirements, §10 acceptance);
2. `inputs/design/prototypes/contigo-v2/ia-v2.md` (V2 routes, nav, divergences
   from `Contigo V2 Prototype.html`) and `screens-v2.md`;
3. `reports/audit/ask-v2-gaps.md` (every OPEN row → an e13 task);
4. `reports/architecture/ADR-024-ask-contigo-v2.md`, the superseded footer
   on ADR-023, the epic-13 amendment footers on ADR-001/004/011/018/020;
5. `reports/workitems/epic-13-ask-v2/` (11 features, 20 tasks) and
   `reports/plan/slices/e13.yaml` (five phases);
6. `reports/open-questions.md` OQ-askv2-001…009 (assumptions in force).

Confirm:

- Uploads happen only in Documents; Ask never accepts attachments (D1).
- Non-contracts are refused **before** storage and never persisted (D3).
- The "knowledge base" is the market-intelligence feed (mock now, API later)
  with its own shared index; tenant contracts never leave the tenant.
- Conversations are server-side, per user and workspace (D5).
- Criticality is a deterministic composite score (D6).
- No legal advice, no web / tool grounding on any model role.
- Every web task cites the V2 prototype (bundled + unpacked anchor).
- Passata 1 agents run on Claude Code Opus (`ANTHROPIC_DEFAULT_OPUS_MODEL`).

Stamp only after that review — write `reports/plan/gates/ask-v2.hitl-ok`
by hand in the same format as `gates/readiness-gaps.hitl-ok`
(`--record-hitl` only accepts slice ids):

```powershell
@"
slice: ask-v2
stamped_at: $(Get-Date -AsUTC -Format s)Z
reviewed: inputs/requirements.md, reports/audit/ask-v2-gaps.md, ADR-024, epic-13, slices/e13.yaml
decision: accept e13 (Ask Contigo V2) as the next wave; e12 superseded
blockers: none
"@ | Set-Content -Encoding ascii reports/plan/gates/ask-v2.hitl-ok
```

Then launch the wave from **Helix Studio**: Studio idle, Claude Code Max
login active (`ANTHROPIC_API_KEY` unset), `reports/plan/slice.current.yaml`
= `slices/e13.yaml` (already copied), open `.helix/contigo-process.yaml`,
select `execution-fanout`, Run. Or:

```
cd .helix
python scripts/check_slice_prereqs.py --slice e13
./run.ps1 -Max -Slice e13 -o execution-fanout
```

`e13.previous` is `e1011` (stamped 2026-09-08). e12 is superseded and must
not be launched. Read `reports/execution/wave-close.md` after the run —
Studio green does not mean the PR exists.
