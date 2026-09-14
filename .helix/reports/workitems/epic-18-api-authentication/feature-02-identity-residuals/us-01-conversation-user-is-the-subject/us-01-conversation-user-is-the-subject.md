---
id: us-01
type: user-story
parent: feature-02
wave: w15
status: active
---

# us-01-conversation-user-is-the-subject — Conversation `user_id` is the token subject

## Story

As a **signed-in user**, I want my Ask conversations keyed to my verified
identity, so that nobody can read my threads by typing my address into a header
and so that a difference in letter case does not split me into two people.

## Acceptance criteria

- [ ] AC-1 `Conversation.UserId` is set from the **validated token subject**, never from a raw header.
- [ ] AC-2 Identity normalization happens on **one** seam. Today `HeaderCallerIdentity` lower-cases the identity while `TryResolveUserId` does not — a live bug independent of NW-05, by which the same browser keys its conversations under a different string than its workspace membership.
- [ ] AC-3 Reads filter on the same normalized value, so a forged header can no longer reach another person's threads.
- [ ] AC-4 The fate of rows written under the old key is **decided and recorded** — this is a data question, not only a code one.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-api-validates-the-token (`E18/F01/US01/T01`, w15) | the claims branch at `ConversationsEndpointExtensions.cs:377-387` exists but can never execute until a principal exists |

## Architecture decisions in force

- **ADR-010** — the token subject is the identity.
- **ADR-024** — closes `OQ-askv2-005`, which already names this exact swap.
- **ADR-009** — per-user isolation inside a tenant is the property at stake.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Normalize on one seam; decide the fate of existing rows | M | **queued — W16** |

## Council decisions carried into this story

Not at the w15 table — this item is **queued**. Its evidence is recorded in
`reports/context/waves/w15-requirements.md` §2 (NW-07) so the W16 intake picks it
up without re-auditing: `Conversation.cs:26` holds `UserId`, documented `:13-18`
as *"the `X-User-Id` header (MSAL account username), non-authoritative until the
task that lands the API JWT (ADR-010) replaces it with the token subject"*.

## Open questions

- The fate of conversation rows written under the old key. **Assumption in force**: they are left in place and become unreachable to the new key rather than migrated blindly — a rewrite that guesses the mapping would attribute one person's threads to another. W16 decides, and it is a product question as much as a technical one.
