---
id: epic-17
type: epic
wave: w15
status: active
extends: [epic-15, epic-06]
---

# epic-17-invitation-delivery-and-identity — An invited colleague receives a mail, has an identity, and lands inside the workspace, with no Azure portal

## Business capability

A workspace Admin types a colleague's address and presses Invite. Raffa.ai
creates that person's guest identity in the company directory, sends them a
Raffa-branded email with a working link, and when they click it and sign in with
a one-time code from Microsoft they are inside the workspace as Procurement —
with no second "Join" click and no administrator opening the Azure portal. When
any of that cannot happen, the pane says which part failed, in words the Admin
can act on, and no invitation claims to have been sent.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w15-todo.md` §2 | NW-67 — Raffa provisions the invitee's Entra B2B guest at invite time (Graph) |
| `inputs/next/w15-todo.md` §2 | NW-68 — Raffa sends the invitation email (ACS Email) |
| `inputs/next/w15-todo.md` §2 | NW-69 — invite pane: honest delivery and identity state |
| `inputs/next/w15-todo.md` §2 | NW-58r — invitation e2e (N3b) runs because the flow creates the second account |
| spec §20 Day 1 (`:863`) | "Create a workspace and invite Procurement users" |
| spec §13.4 / §16.1 (`:619`, `:626`, `:756`) | email / notification delivery is **P1 / V1**, not a non-goal |
| `inputs/design/prototypes/raffa-v2/screens-v2.md` §10 (`:147-154`) | Members table and invite form |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | Guest identity and mail transport (the invite request does both) | w15 |
| feature-02 | The invite pane and the accept screen tell the truth | w15 |
| feature-03 | The invitation e2e, and the test seam that must never ship | w15 |

## Extends

- **epic-15 F01** (invitation lifecycle) — its two *deferred* `must` clauses land here. The delivered lifecycle stands unchanged and takes **no banner**: §6 of the intake and product-owner's work-item instructions agree on this.
- **epic-15 F02** (invitation web) — the pane grows from one boolean to one server outcome string.
- **epic-06 F04** (members and roles) — the roster is unchanged; a delivery outcome is a *pane* fact about the last action, not a property of a row.

## Success looks like

An address the tenant has never seen is invited from `dev`; a Raffa.ai email
arrives within a minute; the link leads to a one-time-code sign-in that ends
inside the workspace as Procurement, with no "Join" click and no Azure portal;
the roster shows Active. Inviting an address that already exists behaves
identically and creates no duplicate guest. With the directory permission
missing, the pane names the failure, an audit row records it, and **no
invitation claiming "sent" exists**.

## Architecture decisions in force

- **ADR-025** (new **§J**, §J.1–§J.10) — the Graph permission and its blast radius, guest-before-row ordering, directory presence is not a grant, the `oid` bind, the failure contract, removal, audit, the test-seam refusal and **S-T23**.
- **ADR-026** (w15 footers §1–§9) — the 201's shape: `identityProvisioned`, `deliveryOutcome`, the declared 502 with its closed `reason` set, replace-on-live-invitation.
- **ADR-010** (w15 §2.3–§2.4) — the `#EXT#` UPN is never parsed for an authorization decision; the `email` optional claim is requested in Terraform and the design does not depend on it.
- **ADR-011** (w15 §1, §4) — `acs-connection` is the wave's **one** new Key Vault entry; NW-67 adds **none**; the log/audit split.
- **ADR-005 / ADR-007** (w15 footers) — the ACS module, the managed domain, the four `Invitations__*` keys, the Graph role assignment. **$0.00 fixed-cost delta.**
- **ADR-015** (w15 clauses 1–9) — the runtime identity's directory permission, and the **apply** identity's rights, granted out of band by default.
- **ADR-016** (w15 clauses 13–16, 19–20, 22) — the first per-environment API key, the flag table, the promotion steps, N3b as a numbered acceptance step.
- **ADR-020** (w15 §3, §4) — screen 10's three outcomes, the non-shipping "Try sending again" affordance, the 502 reason copy, and **surface 12, the invitation email**.
- **ADR-012** (w15 §7, §8, §13.1–§13.3) — the pane renders one server string; accept auto-continues; the typed `failureReason` off a **declared** 502.

## Out of scope

- **Deleting or blocking an Entra guest, ever** (OQ-w15-007). The directory object is the customer's, the same person may belong to other workspaces, and Rule D.5b already makes membership removal immediate. No directory-deletion or directory-block path is built in this wave or any later one without a new ADR.
- **Any directory read beyond provisioning.** The invite form must not become a directory-enumeration oracle: a fresh provision and a no-op are indistinguishable in shape.
- **The Graph `inviteRedeemUrl`** in any response, mail, store, log or audit row — it redeems a guest with no Raffa row, no token and no membership write.
- **A test-only passcode seam, on any environment, in any wave** (ADR-025 §J.8).
- **"Try sending again" on the invite pane** — the server cannot re-send the original link (the token is stored only as a SHA-256 hash), so any retry is a re-issue that kills the link the Admin is looking at. The link block is the remedy; the roster row's "Send a new invitation" is the only re-issue path.
- A notification framework. Exactly one mail type ships: the invitation.
