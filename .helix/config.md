# Raffa — model, tool, and MCP catalogue

Bindings live in `raffa-process.yaml`. This file is the rationale.

---

## 1. Models

Two families, two jobs.

| Family | Helix `provider` | Used for | Why |
|---|---|---|---|
| DeepSeek (OpenAI-compatible chat) | `openai` | intake, council, decomposer, gates | analysis + native file tools |
| Claude Code Max (`claude login`) | `claude-code` (lineage) | implementer, reviewer, conflict-fixer | application code + barrier merges |

`provider: claude-code` is **not** a chat client. YAML `model: sonnet` is a
slot-tier remapped by `ANTHROPIC_DEFAULT_SONNET_MODEL`.

Chat models set `api: chat_completions` (local/OpenAI-compat reject Responses
`previous_response_id` in multi-turn). `context_window_tokens` is set on
council models (long `group_chat`). No `temperature` / `top_p` / `max_tokens`.

Every DeepSeek chat model sends `extra_body.thinking.type: disabled`. V4
(`deepseek-v4-pro`, `deepseek-v4-flash`) and the `deepseek-chat` alias enable
thinking by default. After the first tool call DeepSeek requires
`reasoning_content` on later turns; Helix's OpenAI client drops that field
and the API returns HTTP 400. Switching the model id does not help — Helix
must disable thinking on the request. Requires a Helix backend that pops
`options.extra_body` onto the SDK transport (restart Studio after that
patch).

Every DeepSeek model also sets `normalize_turns: false`, `tool_turn_cue: false`,
and `tool_call_content: ensure`. Helix defaults those middlewares on for local
llama.cpp; on DeepSeek they orphan `role: tool` messages (HTTP 400). Do not
copy Moonshot's `tool_call_content: strip`. PROCESS.md D8 / D10.

`defaults.harness.governance.policy.model_allowlist` lists the three model ids
and is **fail-closed**.

| Agent | Model |
|---|---|
| six council producers, backlog-decomposer, decomposition-remediator, decomposition-checker | `deepseek-reasoning` |
| docs-ingester, council-gate | `deepseek-fast` |
| implementer, reviewer, conflict-fixer | `coding-primary` |

---

## 2. Tools

Native (`kind: function`): `read_file`, `read_files`, `list_dir`, `glob`,
`grep`, `write_file`, `bash`. Artifact-relative; `..` refused.

No MCP tools. Cost/briefing spokes (and Firecrawl) are not in this artifact.

Producers get `write-file`. Decomposer and remediator also get `bash` **only**
to run `python scripts/cut_nightly_slices.py`. Consumers get read tools.
Critics (`council-gate`, `decomposition-checker`) get neither `write-file` nor
`bash`.

---

## 3. MCP servers

None. Firecrawl was bound only by the removed cost spokes.

No ADO, no Confluence — those were bit-flow intake, deleted here.

---

## 4. Harness

`defaults.harness.governance.hooks.allow_external: true` — admits the
`command` hooks on `execution-fanout` (`on_orchestration_stop` →
`scripts/open_fanout_pr.py` then `scripts/close_wave_slice.py`). See
PROCESS.md D4. Studio green ≠ PR opened. Open points become a GitHub
issue labelled `hitl`.

Coding agents (implementer, reviewer, conflict-fixer): `kind: external-coding-agent`,
`provider: claude-code`, `transport: agent-sdk` (transport is ignored on the
local path; documented). Reviewer `allowed_tools`: `[Read, Grep, Glob, Bash]`
— never Write/Edit. Conflict-fixer has Write/Edit/Bash and runs only at a
phase-barrier conflict (cwd = integration). Decomposer and remediator are
chat agents (PROCESS.md D9), not a coding harness.

---

## 5. Skills on coding agents

| Skill | Who | Why |
|---|---|---|
| `kb-contract`, `marker-discipline` | most agents | path contract + last-line markers |
| `afi` | implementer, reviewer | call-graph before edit / blast radius at review |
| `readme-hygiene` | implementer, reviewer, conflict-fixer | keep `infra/` `backend/` `web/` `mobile/` and root `README.md` current with public surface; standing scope, not a per-task Files row (PROCESS.md §2.4) |

