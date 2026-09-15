---
id: E18/F02/US01/T01
type: task
story: us-01-conversation-user-is-the-subject
wave: w15
status: live
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

---

## Wave w16 addendum (2026-09-15) — promoted to `live`, phase 3

**Appended by `next-decomposer`; the body above is unchanged (raw §0.4).** Where
this section and the body disagree, **this section wins.** The body was written
against the pre-w15 tree and **its premise is out of date**.

**Scheduled**: `reports/plan/slices/w16.yaml`, **phase 3**. Artifact
`conversation-subject-keying`.

**Decision row**: `reports/architecture/waves/w16.md`, **NW-07**
(product-owner, software-architect, security-architect).

### ⚠ DoD box `:69` MUST NOT BE IMPLEMENTED

> The body's Definition-of-done line `:69` requires *"a conversation created under
> `User@Example.com` is readable by the same subject presenting
> `user@example.com`"*. **Satisfying it re-introduces the exact normalization
> ADR-010 rejected.** The conversation key is `oid`, an opaque, case-sensitive
> Entra object id, and `backend/src/Raffa.Api/Infrastructure/CallerIdentity.cs:60-68`
> records that **not** lower-casing it is deliberate. **A task reaching for
> `ToLowerInvariant()` here is doing the wrong thing.** Superseded by ADR-001 w16
> clause 1 and ADR-010 w16 clause 2a. Use the reworded acceptance below.

Likewise **the body's whole "Coding objective" is void**: `TryResolveUserId`
exists **nowhere** under `backend/src` — it was deleted in w15. Its only trace is
a dangling `<see cref="TryResolveUserId"/>` in the stale doc paragraph this task
removes. All five conversation/chat handlers already resolve through
`ICallerContext`; absent identity is **401**, never 400. The body's rows for
`Conversation.cs` and `ConversationService.cs` are **not** load-bearing this wave
— touch them only if the stale-comment sweep reaches them.

### What this task actually does

1. **Delete the stale doc paragraph** at
   `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs:23-37`. It still
   asserts an `X-User-Id` fallback, a 400 that the code beneath it does not have,
   and a cref to a deleted method. This is the doc-comment class that makes a
   grep-based audit return a **false verdict**.
2. **Delete the email leg from the two authorization comparators** (S16-1,
   ADR-010 w16 clause 1). Both
   `backend/src/Raffa.Api/Infrastructure/CallerContext.cs:143` (membership → 404)
   and `backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs:80` (role →
   403) match `user.Email == identity || user.ExternalSubjectId == identity`, so
   **membership and role can be granted by a match on `workspace_user.Email`** —
   a column an Admin writes at invite time. Not exploitable on this tree (an
   email never equals a GUID) — but it is **one `workspace_user` row carrying a
   GUID-shaped `Email` away from being a grant**, creatable through the ordinary
   invite path. Email keeps only its two existing jobs.
3. **Fix the normalization at the source, not per site** (S16-2, ADR-010 w16
   clause 2). The same predicate exists four times and they disagree:
   `CallerContext.cs:143` and `WorkspaceRoleResolver.cs:80` are ordinal;
   `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceDirectoryService.cs:100`
   lower-cases the identity and `:104`/`:144` then compare the **`oid`** leg
   against that lower-cased value; the shipped RLS `identity_self` policy is
   `lower(email) = lower(guc)` **or** an ordinal subject match. Benign only while
   every subject is a canonical lowercase GUID — and nothing enforces GUID-ness
   (the column is `varchar(200)`). **The identity stops being lower-cased and
   each leg normalizes itself**, which is what the shipped policy already does.
   > **The shipped policy text is NOT rewritten** (it is already correct and
   > already applied), and ADR-009 w15 5b's **bound parameter** on
   > `set_config('app.identity_subject', …)` stays.
   The failure this prevents is asymmetric and user-visible: *the workspace is
   listed and every route inside it 404s*.
4. **Record the pre-w15 rows as retired — never re-key them** (ADR-001 w16
   clause 1, S16-3). Conversations created before w15 are keyed by the MSAL
   username (an email); a returning caller is keyed by `oid`, so those rows are
   unreachable. **They stay in place: never remapped, never deleted.**
   `backend/src/Raffa.Chat/Migrations/` holds only `Initial` and the RLS policy,
   and **this task writes no migration.**
   > **One mechanism is forbidden outright**: a backfill re-pointing
   > `conversation.user_id` by matching `workspace_user.Email` would move
   > ownership **through the very leg step 2 deletes** — a cross-user data move by
   > the weakest key in the system. If history is ever wanted, the only
   > acceptable form is an explicit `(old key, oid)` pair supplied at HITL, one
   > tenant at a time, audited. **Orphaned history is a smaller problem than
   > mis-attributed history.**
   The disposition goes in the PR description **and** in
   `reports/open-questions.md` (the body's DoD box `:72` — keep it).
5. **Standing constraint, preserve do not build**: no surface may *count* a
   conversation it cannot open. The list filters on the same key, so an orphan
   never appears as a phantom row. **Do not regress that.**

### Reworded acceptance — A16-1 (ADR-001 w16 clause 1), on `dev`

1. User A creates a conversation, reloads → still readable by A.
2. **User B, a member of the same workspace, can neither list nor read it**
   (`inputs/requirements.md` R-CONV-01 AC-1). *Never narrowed.*
3. A pre-w15 conversation is not listed and not counted anywhere; opening it is a
   clean **404** — never a 500, never another user's thread.
4. The disposition is recorded in the PR description and in
   `reports/open-questions.md`.

### Named test this task carries — S-T24

- (a) a `workspace_user` whose `Email` is set to **another member's `oid`
  string** confers **no** membership (404) and **no** role (403);
- (b) a subject stored with non-lowercase characters resolves **identically**
  through `GET /api/workspaces`, `CallerContext` and `WorkspaceRoleResolver` —
  the workspace is either **listed-and-usable** or **absent-and-404**, never
  **listed-and-404**;
- (c) ADR-009 w15 5b's injection negative (`'`, `;`, `--` in the identity) stays
  green.

### ADR actions and single writer, phase 3

`OQ-askv2-005` **retires with this item** (ADR-024 w16 clause 1). This task owns
`backend/src/Raffa.Api/ConversationsEndpointExtensions.cs`,
`backend/src/Raffa.Api/Infrastructure/CallerContext.cs`,
`backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs` and
`backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceDirectoryService.cs`.
**Do not touch** `web/**` — phase 3's other task is the sole writer of the
contract file and `web/src/api/client.ts`.
