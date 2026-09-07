import type { PortfolioContractType, PortfolioListItem } from "../../api/client";
import { getRiskTag, type RiskLevel, type SemanticTag } from "../../styles/semantics";

/**
 * Pure view-model helpers for PortfolioTable.tsx (AC-3: columns quoted verbatim from screens.md #4 --
 * "Attention · Supplier · Contract · Annual spend · Start · End · Renewal · Cancel by · Auto ·
 * Status"). Same one-concern-per-file split `documents/documentTable.ts` already established for this
 * repo.
 */

/**
 * Product-spec §6.1 contract-hierarchy labels. Mirrors `documents/documentTable.ts`'s own (unexported)
 * `DOCUMENT_TYPE_LABEL` map verbatim -- duplicated rather than imported because that map is private to
 * the Documents screen's own Type column and the two screens are independent, separately-evolving
 * features; a future shared-styles pass could hoist both into one module.
 */
const CONTRACT_TYPE_LABEL: Record<PortfolioContractType, string> = {
  Msa: "MSA",
  OrderForm: "Order Form",
  Amendment: "Amendment",
  Sow: "SOW",
  RenewalLetter: "Renewal Letter",
  Other: "Other",
};

/**
 * "Contract" column (screens.md #4): `Contract` (the domain entity) has no title/name field yet
 * (`PortfolioListItem.cs`'s own doc comment), so this is the closest identifying information
 * `GET /api/contracts` actually returns for that column -- the same documented proxy that file's own
 * comment names, not an invented one.
 */
export function getContractTypeLabel(type: PortfolioContractType): string {
  return CONTRACT_TYPE_LABEL[type];
}

/**
 * "Start"/"End"/"Renewal"/"Cancel by" columns. `dateOnly` is the wire's `yyyy-MM-dd` (a `DateOnly`);
 * rendered DD/MM/YYYY, the same unambiguous non-locale-month-name convention
 * `documents/documentTable.ts#formatUploadedAt`'s own comment already chose for this app (no
 * time-of-day component to lose to a timezone shift here, since a date-only value has none to begin
 * with).
 */
export function formatDateOnly(dateOnly: string | null): string {
  if (dateOnly === null) return "—";
  const [year, month, day] = dateOnly.split("-");
  return `${day}/${month}/${year}`;
}

/**
 * "Annual spend" column. `PortfolioListItem` (`GET /api/contracts`) carries no currency code at all --
 * `Contract.Currency` exists on the domain entity but `PortfolioEndpointExtensions.GetPortfolioAsync`'s
 * own response projection never selects it (checked: `contractId, supplierId, type, annualSpend,
 * startDate, endDate, renewalDate, cancellationDeadline, autoRenewal, status, risk` -- no `currency`)
 * -- so this renders a grouped plain number, never a fabricated currency symbol.
 */
export function formatAnnualSpend(annualSpend: number | null): string {
  if (annualSpend === null) return "—";
  return new Intl.NumberFormat("en-GB").format(annualSpend);
}

/** "Auto" column. Text, not colour-only (ADR-019 accessibility baseline), and not a bare "true"/"false". */
export function formatAutoRenewal(autoRenewal: boolean): string {
  return autoRenewal ? "Yes" : "No";
}

/**
 * "Supplier" column. `Suppliers/Products` has no name-resolution endpoint anywhere in this codebase
 * yet -- even this same `GET /api/contracts` response only ever returns the raw id (see
 * `documents/documentStore.ts`'s own doc comment, which names this exact gap). Rather than repeat that
 * file's "Not yet available" text on every single portfolio row (this column *does* carry a real,
 * stable identifier, unlike Document's supplier column, which carries none at all), this shows a
 * short, human-scannable fragment of the id with the full id as the element's `title` (a hover/
 * accessible-name tooltip) so the value stays honest, and cross-referenceable, without a name.
 */
export function formatSupplier(supplierId: string | null): { label: string; title: string | undefined } {
  if (supplierId === null) return { label: "—", title: undefined };
  return { label: `Supplier ${supplierId.slice(0, 8)}`, title: supplierId };
}

/**
 * "Status" column tag (AC-2's semantic-mapping intent applied to this endpoint's actual data shape).
 * `PortfolioListItem.status` is `Contract.Status`, a free-text field -- not yet the closed enum
 * ADR-019's locked "Status completed/Ready -> neutral; needs_review -> outline; failed -> accent"
 * table was written against (that table matches `Document.ProcessingStatus`, a real backend enum;
 * `Contract.Status`'s bootstrap default is the literal string `processing`, otherwise whatever the
 * metadata extraction stage last wrote -- see `PortfolioFilter.cs`'s own doc comment). This applies
 * the same three-way *intent* to whatever string actually arrives: an exact `failed` is accent, an
 * exact `processing` or anything containing `review` is outline (not yet a trustworthy final state),
 * and any other real extracted business status (e.g. `Active`, `Expired`) is neutral -- never
 * re-deriving `styles/semantics.ts#getStatusTag`'s own document-status mapping, which is a different,
 * already-closed enum this field is not.
 */
export function getPortfolioStatusTag(status: string): SemanticTag {
  const normalized = status.trim().toLowerCase();

  if (normalized === "failed") return { variant: "accent", label: "Failed" };
  if (normalized === "processing") return { variant: "outline", label: "Processing" };
  if (normalized.includes("review")) return { variant: "outline", label: "Needs review" };

  return { variant: "neutral", label: capitalizeStatus(status) };
}

function capitalizeStatus(status: string): string {
  const trimmed = status.trim();
  if (trimmed.length === 0) return "Unknown";
  return trimmed.charAt(0).toUpperCase() + trimmed.slice(1);
}

/**
 * "Risk" column tag. Reuses `styles/semantics.ts#getRiskTag` (never re-derived) for the three tiers
 * ADR-019's locked semantic mapping actually names (High -> accent; Medium/Low -> neutral). The
 * domain's fourth tier, `RiskSeverity.Critical`, has no ADR-019 treatment defined at all -- folded
 * into the same accent treatment as `High` (the conservative, more-severe-leaning direction; see
 * `portfolioAttention.ts`'s header comment for the equivalent fold on the attention-bucket side), not
 * silently downgraded to `Medium`/`Low` or left untagged. `null` (a contract with no recorded `Risk`
 * row at all) is its own explicit, honest neutral label -- never a fabricated "Low risk".
 */
export function getPortfolioRiskTag(risk: PortfolioListItem["risk"]): SemanticTag {
  if (risk === null) return { variant: "neutral", label: "No risk recorded" };
  if (risk === "Critical") return getRiskTag("high");

  const known: readonly string[] = ["Low", "Medium", "High"];
  if (known.includes(risk)) return getRiskTag(risk.toLowerCase() as RiskLevel);

  // Defends against a future backend value this table doesn't know about yet (e.g. a fifth
  // RiskSeverity member) -- fails safe to a plain, honest label rather than mis-tagging it.
  return { variant: "neutral", label: risk };
}
