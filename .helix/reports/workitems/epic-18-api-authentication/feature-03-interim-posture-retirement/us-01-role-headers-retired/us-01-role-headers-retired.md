---
id: us-01
type: user-story
parent: feature-03
wave: w15
status: active
---

# us-01-role-headers-retired — The dual role headers are retired

## Story

As a **security reviewer**, I want no request header to change any response, so
that the published contract stops advertising a spoofable parameter and the
retirement ADR-022 promised actually happens.

## Acceptance criteria

- [ ] AC-1 `GET /api/capabilities` no longer reads `X-Role`. Today `CapabilitiesEndpointExtensions.cs:54` defines `RoleHeaderName = "X-Role"` and `:77-80`'s `CallerIsAdmin` is used at `:64-67` to include or hide `CapabilityRoleGate.Admin` rows, so `-H "X-Role: Admin"` still flips catalog visibility.
- [ ] AC-2 The contract stops publishing the header: `raffa-api.v1.json:5131` declares `X-Role` as an optional header **parameter** — the only such parameter in the document — and `:5128` describes it.
- [ ] AC-3 The **stale** description at `raffa-api.v1.json:1142` is corrected: it still says `DELETE /api/documents/{id}` "carries the role as `X-Workspace-Role`", when that endpoint's gate is membership-based.
- [ ] AC-4 `reprocess-tenant-documents.yml` stops sending both headers (`:42-43`, `:205-206`, `:256-257`, `:274`), and its repair under the token regime is designed in the same change — it is **out of service from w15**.
- [ ] AC-5 The acceptance docs that still describe the headers are updated: `docs/ask-v2-acceptance.md:71`, `:357`, `:600`.
- [ ] AC-6 **No role header changes any response** — the wave-level assertion folded into A15-8.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-api-validates-the-token (`E18/F01/US01/T01`, w15) | the capabilities reader is the last live consumer; it can only go once role comes from a verified membership |

## Architecture decisions in force

- **ADR-022** (w14 footer) — already demoted these headers to UI shaping and gave each interim mechanism a named retirement item. **This is the retirement, not a new decision** — which is why the w15 requirements record `seats: none` for NW-31.
- **ADR-026** — the published contract must not carry a spoofable parameter or a stale description.
- **ADR-016** (w15 clause 21) — w15 leaves `reprocess-tenant-documents.yml` out of service deliberately, neither repaired nor edited, so that **one wave opens it once**. That wave is this one.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Delete the capabilities reader, the contract parameter and the workflow headers | M | **queued — W16** |

## Council decisions carried into this story

Not at the w15 table — **queued**, with `seats: none` recorded because ADR-022's
w14 footer already governs. The w15 table did decide one thing that binds this
story: **`reprocess-tenant-documents.yml` is NW-31's file in W16**, and w15
neither repairs nor edits it, because a deploy-time credential able to call the
API under the token regime is an identity-plane change rather than a side effect
of a wave that wanted a working script (ADR-016 clause 10, OQ-w15-dm-04).

## Open questions

- How the reprocess job authenticates after NW-05. **Assumption in force**: W16 designs it — either a token-bearing operator identity (security-architect owns the credential) or an operator job that enqueues on the worker side, now that NW-27 makes reprocess asynchronous. The second is likely cheaper and adds no credential.
