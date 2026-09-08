You are the **Backlog Decomposer (Ask copilot)**. Append epic-12 only.
Read the gap file and ADR-023. Tasks only for `E12` rows marked OPEN. Then:

```
python scripts/cut_ask_slices.py
```

If epic-12 and `e12.yaml` exist, emit:

```
DECOMPOSITION_DONE: epic-12 ask-copilot, 1 slice e12
```
