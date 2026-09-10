# Parse / reference check (Helix Realization Engineer)

Command (from this machine, `${VAR}` stubbed — unset is a hard bind error):

```text
python raffa-flow/.helix/scripts/validate-artifact.py \
  raffa-flow/.helix/raffa-process.yaml \
  --helix-backend C:/Users/luca.la-malfa/source/repos/helix/src/backend \
  --stub-env
```

Verbatim output (2026-09-01, after cost-hub / ceo-briefing / raffa-cost-briefing
were removed; passata 1 is terminal on `DECOMPOSITION_OK:`):

```text
OK ['docs-intake', 'lane-product-owner', 'lane-software-architect', 'lane-cloud-architect', 'lane-security-architect', 'lane-client-architect', 'lane-delivery-manager', 'architecture-lanes', 'council-close', 'architecture-council', 'decomposition', 'decomposition-check', 'decomposition-remediation', 'raffa-design', 'raffa-plan-r0-r4', 'raffa-plan-close', 'execution-loop', 'raffa-execution', 'execution-fanout']
advisory ADR-0103: SKIPPED (missing module agent_framework: non-blocking advisories)
prompt files: all present
```

Exit code 0. UserWarnings on `coding-primary` omitting `context_window_tokens` are pre-existing (Pattern E Claude Code, not a chat-client window).

Earlier first-pass failures (fixed before APPROVE, now obsolete):

1. `duplicate id: cost-research` — skill and orchestration shared an id. Both removed with the cost hub.
2. Cyclic `decomposition-check` loop required `limits.max_iterations` (ADR-0120, `le=25`), not only `max_steps`.
3. `duplicate id: ceo-briefing` — skill renamed `ceo-briefing-protocol`. Both removed with the briefing phase.
