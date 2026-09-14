---
id: E18/F02/US01/T01
type: task
story: us-01-conversation-user-is-the-subject
wave: w15
status: queued
target_repo: raffa-backend
---

# task-01-conversation-user-is-the-subject — Normalize on one seam; decide the fate of existing rows

> **Queued for W16.** This task has **no entry in `reports/plan/slices/w15.yaml`**
> and is not fanned out this wave. It is decomposed now so the W16 intake picks it
> up with its evidence intact.

## Coding objective

Key conversations to the validated token subject, on the same seam every other
route uses. `ConversationsEndpointExtensions.cs:372-399` today prefers
`NameIdentifier` / `sub` **only if** the principal is authenticated (`:377-387`,
never reachable before NW-05) and otherwise takes the raw header **untrimmed and
un-normalized** (`:389-393`), else 400 (`:396-398`). Replace that block with the
`ICallerIdentity` seam `E18/F01/US01/T01` establishes, and fix the normalization
mismatch it exposes: `CallerIdentity.cs:71` lower-cases the identity and
`TryResolveUserId` does not, so a case difference in the Entra UPN silently
splits one user into two.

Then decide, and record in the task's own Definition of done, what happens to
rows already written under the old key.

## Parent story AC covered

- AC-1 `Conversation.UserId` is set from the validated token subject.
- AC-2 Normalization happens on one seam.
- AC-3 Reads filter on the same normalized value.
- AC-4 The fate of rows written under the old key is decided and recorded.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs` | `TryResolveUserId` (`:372-399`) is replaced by the `ICallerIdentity` seam |
| `backend/src/Raffa.Chat/Domain/Conversations/Conversation.cs` | the doc comment at `:13-18` ("non-authoritative until the task that lands the API JWT") is retired in the same edit |
| `backend/src/Raffa.Chat/Application/Conversations/ConversationService.cs` | the create path (`:72`, `:76-78`, `:87`) and the three read filters (`:125`, `:151`, `:201`) use the normalized subject |
| `backend/tests/Raffa.Chat.Tests/`, `backend/tests/Raffa.Api.Tests/` | the tests below |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-07`.

This item was **not** at the w15 table — it is queued. Its evidence is
`reports/context/waves/w15-requirements.md` §2, NW-07 row; the decision that
unblocks it is `reports/architecture/waves/w15.md` **NW-05**.

- **Architecture decisions in force**: **ADR-010** (the token subject is the
  identity); **ADR-024** (this closes `OQ-askv2-005`, which names the swap);
  **ADR-009** (per-user isolation inside a tenant).
- **The normalization mismatch is a live bug independent of NW-05** and is the
  half a reviewer is most likely to miss, because nothing fails loudly: the user
  simply stops seeing their own history.
- **Do not touch**: the Ask answer path, the grounding guards or the retrieval
  contract — this is a keying change only.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Chat.Tests` exit 0 — a conversation created under `User@Example.com` is readable by the same subject presenting `user@example.com`, and by nobody else
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exit 0 — a forged `X-User-Id` reaches no thread (it is a 401 before it reaches this code)
- [ ] `grep -n "non-authoritative" backend/src/Raffa.Chat/Domain/Conversations/Conversation.cs` returns **no match**
- [ ] The chosen disposition of pre-existing rows is written into the PR description and into `reports/open-questions.md`

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | one normalization, applied on create and on all three reads | `backend/tests/Raffa.Chat.Tests/` |
| API | per-user isolation holds under the token regime | `backend/tests/Raffa.Api.Tests/` |

## Open questions blocking this task

- The disposition of conversation rows written under the old key. **Assumption in force**: left in place, unreachable, never blindly remapped. W16 decides.

## Wave-spec entry

**None — this task is `status: queued` and is deliberately absent from
`reports/plan/slices/w15.yaml`.** When W16 schedules it:

```yaml
- id: E18/F02/US01/T01
  prompt: reports/workitems/epic-18-api-authentication/feature-02-identity-residuals/us-01-conversation-user-is-the-subject/tasks/task-01-conversation-user-is-the-subject.md
  produces: [conversation-subject-keying]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
