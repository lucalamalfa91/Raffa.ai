# Marker discipline — the control plane

Helix routes this process on **text markers**. An edge condition or a termination
reads your turn, looks for a marker at the **start of a line**, and decides what
runs next.

## The four hard rules

1. **A marker is the LAST line of your turn.** Nothing after it.
2. **A marker starts its line.** No indentation, no bullet, no bold, no backticks,
   no quotes. The matcher sees `^MARKER:`.
3. **A marker means what it says.** `DECOMPOSITION_OK:` followed by a list of
   remaining gaps is a lie the engine will act on.
4. **Emit exactly ONE control marker per turn, and never write the sibling token
   anywhere else** — not in a heading, not in a quoted example, not in an
   explanation of what you would emit.

Exception: `council-gate` emits a **files-written** marker
immediately above the **approved** marker. Those two lines are the last two
lines of the turn, in that order. No third marker.

## Who may emit what

| Marker | Only this role |
|---|---|
| `CONTEXT_READY:` | docs-ingester |
| `LANE_DRAFTS_WRITTEN:` | a council producer, at the end of its independent lane |
| `COUNCIL_FILES_WRITTEN:` | council-gate |
| `COUNCIL_APPROVED:` | council-gate |
| `DECOMPOSITION_DONE:` | backlog-decomposer |
| `DECOMPOSITION_OK:` / `DECOMPOSITION_GAPS:` | decomposition-checker |
| `REMEDIATION_DONE:` | decomposition-remediator |
| `IMPLEMENTATION_APPROVED:` | reviewer |
| `IMPLEMENTATION_GAPS:` | reviewer |
| `HALTED:` | any agent whose precondition failed |
| `COMMITTED:` | any agent that ran `git commit` |

A producer never certifies its own work. If a marker is not in your row, do not
write that token at all.

## `HALTED:` — the honest abort

Emit `HALTED: <what is missing and who must supply it>` when a required input
file does not exist. Do not invent the missing input and continue.

On `execution-loop`, `HALTED:` **stops the workflow immediately**. The
implementer → reviewer edge does not fire; the reviewer → implementer
back-edge does not fire. Do not emit `IMPLEMENTATION_GAPS:` after a halt
(that would start another lap). If the previous turn halted, echo the same
`HALTED:` line and stop — sticky, do not recode.

## Speak only for yourself

In a group chat, open every turn with your own role label on its own line
(`PRODUCT_OWNER:`, `COUNCIL_GATE:`, `REVIEWER:`, …) and write **only** your own
turn. Never write another participant's turn. Ghost-writing corrupts the
transcript the gates read.

## Never claim an unverified result

Every factual claim must be backed by a file you read, a command you ran with
its exit code, or a path you listed. "The file was written" without a `list_dir`
or `ls` in the same phase is not delivery.

## Next-wave process (`contigo-next-process.yaml`) — additional rows

| Marker | Only this role |
|---|---|
| `CONTEXT_READY:` | next-intake (the same token the docs-ingester uses) |
| `LANE_SKIPPED:` | a council seat whose lane is not involved in the wave |
| `VOTE: PASS` | an uninvolved seat at the table (a vote line, not a control marker) |
| `COUNCIL_FILES_WRITTEN:` + `COUNCIL_APPROVED:` | next-council-gate (last two lines, that order) |
| `DECOMPOSITION_DONE:` | next-decomposer |
| `DECOMPOSITION_OK:` / `DECOMPOSITION_GAPS:` | next-checker |
| `REMEDIATION_DONE:` | next-remediator |

Uninvolved seats must still speak (round robin gives them the turn): one
short turn that names the wave, the table round and a seat-specific reason,
then `VOTE: PASS`. Never copy another seat's PASS line verbatim — the engine's
stuck-loop detector compares consecutive turns across participants.
