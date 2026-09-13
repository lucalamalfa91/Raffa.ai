# Raffa — next-waves input · W17 (collected during the W15 / W16 test rounds)

Status: **binding input** for the W17 requirements document — a running file:
items are appended as the stakeholder's `dev` walks surface them, in addition
to the W17 queue already recorded in `w14-requirements.md` §5 (NW-20, NW-22,
NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66) plus whatever W16
overflows. IDs continue the `NW-*` sequence (NW-70 was the last used).

## 0. Binding instructions

1. Execute the W17 queue in full (`agents/next-intake.md` §2 queue discipline;
   NEXT-PROCESS D-N12). Overflow → head of W18.
2. **NW-71 is `must` and joins the review-screen group (NW-63 / NW-64 / NW-65 /
   NW-66)** — it is the rule those four items render; decide it once, at the
   council, before any of them is decomposed.

---

## 1. Review: automatic acceptance of high-confidence fields

### NW-71 — A field extracted with confidence ≥ 90 % is accepted automatically (server rule + web)

- **Status:** OPEN — **must**. Stakeholder ruling 2026-09-13 (asked and
  answered): the rule is **server + web**, and **90 % applies to every field,
  the five critical ones included** (value, cancellation, termination,
  renewal, uplift) — no stricter band, no "always review" list. This
  supersedes `inputs/product-spec.md:335` (">95 % automatically accept unless
  configured as always-review") and the pilot's "<80 % HITL, critical
  stricter" (`inputs/percorso-pilota-v1.md:46`), and it is the same HITL
  decision of 2026-09-10 ("≥90 % is auto-accepted … supersedes the earlier
  §7.3 bands") that ADR-019's w14 footer §6 says the ADR body is stale
  against and defers to NW-65.
- **Today:**
  - Web (`origin/main`): `web/src/styles/semantics.ts:29-49` still implements
    the old spec bands `>95 % Accepted / 80–95 % Flagged / <80 % Review`;
    "Accepted" on the review screen is a **client-side, per-session**
    acknowledgement (`web/src/routes/contracts/review/reviewViewModel.ts:224-251`,
    `acceptedThisSession`) that becomes durable only when "Mark as validated"
    posts `acceptedFields` to `POST /api/documents/{id}/validate`
    (`DocumentValidationService`).
  - Backend: **no auto-accept exists**. `DocumentQueryService.cs:33,42` holds
    the review bars (`WeakFactThreshold = 0.6`, `CriticalWeakFactThreshold =
    0.8`) that only decide `needs_review` via `WeakFactCount`; there is no
    persisted per-field "accepted" state other than the validation's
    `acceptedFields` audit.
  - The stakeholder's own 2026-09-10 web edits for the ≥90 % rule (8 files:
    `semantics.ts`, `reviewViewModel.ts`, their tests, `web/README.md` —
    +101 / −67) were **never committed**; they survive in the operator's
    `git stash` as `stash@{1}` ("wip: review auto-accept and next-waves-todo")
    and `stash@{2}` ("web review WIP … 2026-09-11") on the main checkout.
    Seed the web half from there, do not re-derive it.
- **Must:**
  - **Server rule, applied at extraction time** (and on reprocess): every
    extracted field whose evidence confidence is ≥ 0.90 is **officialized
    automatically** — persisted as accepted (who: `system`, why:
    `auto-accept ≥ 0.90`, when), with one audit row per document naming the
    auto-accepted fields (never the values); a field below 0.90 stays pending.
  - `needs_review` is decided **only** by pending (sub-threshold) fields: a
    document whose every extracted field is ≥ 0.90 goes straight to
    **Completed** with no human step; `WeakFactCount` and the Documents badge
    count only pending fields. The 0.6 / 0.8 bars are retired (one threshold,
    one definition, every caller — same "one rule, two callers" discipline as
    ADR-026 §D2).
  - **Web**: `semantics.ts` bands become `≥ 90 % Accepted automatically /
    < 90 % Review`; the review screen shows auto-accepted fields as
    **"Accepted automatically (NN %)"**, still **correctable** (a correction
    re-officializes the field as human-assigned and is audited as today);
    "Mark as validated" sends only the fields the human touched; Details /
    Contract 360 show **only officialized facts** (auto-accepted or
    human-assigned — NW-65's "no 'facts you still need to decide' list").
  - Reprocess re-applies the rule but never overwrites a human-assigned value
    (ADR-003: corrections are versioned; the human wins).
  - OpenAPI: `GET /api/contracts/{id}/evidence` (or the review read) exposes
    the per-field decision (`pending` / `auto_accepted` / `human`) and the
    threshold in force, so the web renders the server's fact, never its own
    computation.
- **Evidence:** files above; `docs/ask-v2-acceptance.md` A1/A9 (review flow);
  ADR-019 w14 footer §6; `reports/context/waves/w14-requirements.md:76-81`
  (the uncommitted edits the w14 intake found); `web/tests/styles/semantics.test.ts`,
  `web/tests/routes/contracts/review/*` (tests to update).
- **Acceptance (dev):**
  - A17-1 — upload a clean born-digital MSA: every field the extractor
    reports ≥ 0.90 lands **Accepted automatically**; if none is below, the
    document is **Completed** with no review, and Ask can use it at once.
  - A17-2 — a scan with one field at 0.85: the document is **Needs review**
    with exactly that field pending; accepting or correcting it completes the
    document.
  - A17-3 — correct an auto-accepted field: the correction wins, is audited,
    and survives a reprocess.
  - A17-4 — `curl GET /api/contracts/{id}/evidence`: each field carries its
    decision and confidence; the badge count equals the number of `pending`
    fields.

---

## 9. Traceability

NW-71 ← stakeholder ruling 2026-09-13 (chat) · HITL decision 2026-09-10
(`semantics.ts` stash) · ADR-019 w14 footer §6 → NW-65 · `product-spec.md:335`
(superseded) · `percorso-pilota-v1.md:46` (superseded).
