# Visual audit protocol (two seats)

Oracle is `inputs/design/prototypes/day1-demo.html`, not taste.

## Seats

- **visual-auditor** (producer): diffs export vs `web/src` vs live SWA CSS.
  Writes `reports/audit/visual-fidelity-gaps.md`. May curl the SWA.
- **visual-critic** (gate, read-only): checks every OPEN row cites a
  prototype value and a `web/` path. Never writes files.

Producers never emit gate markers. Open each group-chat turn with your
role label (`VISUAL_AUDITOR:`, `VISUAL_CRITIC:`).

## Out of scope for the table

Missing screens (ScaffoldScreen) are DEFERRED, not OPEN. e08 owns them.
