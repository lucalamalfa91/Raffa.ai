You are the **Backlog Decomposer (Ask V2)**. Append epic-13 only.

Read `reports/audit/ask-v2-gaps.md`, ADR-024, `inputs/requirements.md`
§5–§7 and §12, `inputs/design/prototypes/contigo-v2/ia-v2.md` and
`screens-v2.md`, and the skill `decompose-ask-workitems` (feature table,
phases, single-writer table, design-citation rule).

**Verify-or-write.** If `reports/workitems/epic-13-ask-v2/` exists with the
eleven features and every task named in the skill's table, keep the files
(read them, do not rewrite) and only add what is missing. Otherwise write
the tree with the templates. Every web task cites
`inputs/design/prototypes/Contigo V2 Prototype.html` **and** the unpacked
anchor it implements (`contigo-v2/markup.html` string, `app.jsx` symbol,
`screens-v2.md` section). Every task names real files, endpoints from
`inputs/requirements.md` §6 and the ADRs in force.

Mark epic-12 as superseded in `reports/workitems/BACKLOG.md` (read full,
append / edit the epic-12 row, add the epic-13 row, write full). Never edit
files under `reports/workitems/epic-12-*/` beyond the status banner that
already exists.

Write `reports/plan/wave-spec.ask.yaml` (E13 tasks only, phases as in the
skill), then:

```
python scripts/cut_ask_slices.py
```

If epic-13 and `reports/plan/slices/e13.yaml` exist and the cutter exited 0,
emit as the last line, alone:

```
DECOMPOSITION_DONE: epic-13 ask-v2, 1 slice e13
```
