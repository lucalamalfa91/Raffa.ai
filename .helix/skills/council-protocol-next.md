# Council protocol — next-wave table (dynamic participation)

The council amends the architecture for **one wave**. Existing ADRs stay
accepted; the council **adds** ADRs, **appends** amendment footers, or
records "no change". It never re-litigates a locked decision
(`reports/context/locked-decisions.md`) and never rewrites an ADR body.

Oracle of the table: `reports/context/waves/<w>-requirements.md` (the
normalized requirements, with the seat roster) and its companion
`reports/architecture/waves/<w>.md` (the decision record skeleton).

## Two steps: independent lanes, then the table

**Lane** (`next-lanes`, concurrent — you have not read the other seats):

1. Read the normalized requirements. Find your seat key in §3 (roster).
2. **Not listed** → write nothing. Last line, alone:
   `LANE_SKIPPED: <seat> — not involved in <w>`
3. **Listed** → for each item that names you, read the item block, the ADRs it
   touches, and the code paths it cites. Write
   `reports/architecture/draft/next/<seat>/<w>.md`: one section per item —
   the decision you propose, the ADR action (`new ADR-NNN <slug>` |
   `amend ADR-NNN` | `supersede ADR-NNN by ADR-NNN` | `none — <why>`), the
   consequences for the decomposition (files, endpoints, modules, tokens),
   open questions with an assumption in force. Use
   `templates/adr-template.md` only for a **new** ADR. Last line:
   `LANE_DRAFTS_WRITTEN: <seat>`
4. **Size cap (binding since w15).** The draft is an intermediate artefact —
   the table reads it once and the decision record + ADR footers are the real
   output. Keep it **≤ 15 KB (≈ 250 lines)**: only the items that name you;
   per item, the decision, the ADR action, the consequences and the open
   questions as bullets; **no** restating of the requirement, the ADR body or
   the code you read (cite `path:line` instead). Measured on w15: seven drafts
   of 47–81 KB (420 KB) took ~100 min of serial writing and were the bulk of
   the council's time; the table used a fraction of it.

**Table** (`next-council-close`, group_chat, round robin, 8 participants):

- Open every turn with your label on its own line (`SOFTWARE_ARCHITECT:`).
- **Not involved** (no item names you): do not read drafts, do not analyse.
  One short turn — your label, one line that names the wave, the table round
  and why you are out (your seat-specific reason, e.g. "no Azure resource,
  Terraform module, environment key or SKU is touched by W14-01…W14-12"),
  then `VOTE: PASS`. Vary the wording between rounds (say the round number).
- **Involved**: on your first table turn promote your decisions to disk:
  - a new ADR → `reports/architecture/ADR-NNN-<slug>.md` (next free number,
    `Status: accepted`, deciders = the involved seats, cites the wave, the
    item ids and the design refs); append its row to
    `reports/architecture/INDEX.md` (read full, append, write full);
  - an amendment → append `## Amendment (<YYYY-MM-DD>, wave <w>)` at the end
    of that ADR (item ids, what changes, what stays); never touch the body;
  - a supersede → status line `superseded by ADR-NNN` + footer on the old
    ADR, new ADR as above;
  - fill your rows in `reports/architecture/waves/<w>.md` (Decision, ADR
    action, File) — read the whole file, edit your rows, write it back;
  - reconcile with the other involved seats' drafts
    (`reports/architecture/draft/next/*/<w>.md`): one decision per item, no
    contradictions between ADRs. Where two seats disagree, the seat that
    **owns** the ADR (see `skills/next-seats.md`) writes; the other objects
    or approves.
  - end with `VOTE: APPROVE` only when your files are on disk and you have
    read them back. Otherwise `VOTE: OBJECT — <file or decision missing>`,
    `VOTE: PROPOSE — <change you need>`, or `VOTE: ABSTAIN — <who must rule>`.
- **Gate** (`COUNCIL_GATE:`): checks, does not deliberate. See below.

## Amendment rules

- Keep `Status: accepted` on every existing ADR unless superseded.
- Never weaken ADR-009 (RLS), ADR-011 (authz before retrieval), ADR-001 §1.2
  non-goals; never pull a paid market API into `demo`; never put Foundry SDKs
  outside `Raffa.AiGateway`.
- A design decision cites the design oracle path (`inputs/design/…`) and the
  anchor (a markup string, a `.jsx` symbol, a section of an `ia-*.md` /
  `screens-*.md`).
- Every ADR action names the item ids it serves (`W14-03`) so the decomposer
  can trace tasks to decisions.
- "No ADR change" is a valid, recorded decision: the row says
  `none — handled by task; ADR-NNN already covers it`.

## What the gate checks (read-only, sole emitter of the close markers)

1. Roster: for every seat in §3 marked **involved**, the seat spoke at this
   table and its latest `VOTE:` is `APPROVE`. Every uninvolved seat's latest
   vote is `PASS`. A seat that has not spoken has no vote.
2. Decision record: `reports/architecture/waves/<w>.md` has no `pending`
   row for an item with an involved seat; every `ADR action` names a file
   that exists on disk and carries the footer / the new ADR.
3. INDEX: every new ADR has a row; no existing row was removed.
4. Bodies: for each amended ADR the original `## Decision outcome` (or the
   original decision text) is still present above the new footer.
5. Nothing under `reports/architecture/draft/<seat>/` (the initial council)
   or an earlier wave's files was rewritten.

Only when all five hold, the gate ends its turn with these two lines, in this
order, nothing after:

```
COUNCIL_FILES_WRITTEN: waves/<w>.md + <n> ADR files
COUNCIL_APPROVED: <w> — <n> decisions, <n> ADRs amended, <n> new, <n> no-change
```

An empty wave (every seat `PASS`, zero decisions) is still closed by those two
lines once the decision record says so.

## What you must not do

- Do not deliberate items that name no seat (they are tasks, not decisions).
- Do not add locked rules the brief did not lock.
- Do not write application code, Terraform, CI YAML, or anything under `..`.
- Producers never emit `COUNCIL_APPROVED:` or `COUNCIL_FILES_WRITTEN:`.
