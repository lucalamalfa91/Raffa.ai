import { buildPortfolioHighlightHref } from "../../contracts/portfolioViewModel";
import type { ReplyAction, ReplyCitation } from "./replyTypes";

/**
 * Pure grouping behind `EvidenceCard.tsx`: one reply's `citations[]` become **one** card, not one
 * card per citation. A procurement user reading "Where do I spend more?" wants the four contracts
 * the answer named, each under its supplier, with one place to open them -- not four look-alike
 * "OrderForm · OrderForm" boxes stacked down the page.
 *
 * Grouping rules (in order):
 * - `corpus: "market"` → the market section (opened in the market-record panel);
 * - a citation with a `contractId` → that contract's group (keyed by id, so two contracts of the
 *   same supplier never merge);
 * - any other tenant citation → a group keyed by the supplier half of its title;
 * - everything else (`raffa` feature cards, `calc` aggregates such as "Annual spend total") → the
 *   Raffa section.
 *
 * Titles come from the backend as "Supplier · Type" / "Supplier — lever" (`AskCopilotService`'s
 * `ContractTitle` and the savings/strategy pack builders). `splitCitationTitle` reads the supplier
 * off that shape; nothing here invents a name.
 */

export interface CitationTitleParts {
  head: string;
  tail: string | null;
}

const TITLE_SEPARATOR = / (?:·|—) /;

export function splitCitationTitle(title: string): CitationTitleParts {
  const trimmed = title.trim();
  const match = TITLE_SEPARATOR.exec(trimmed);
  if (match === null || match.index === 0) {
    return { head: trimmed, tail: null };
  }
  const head = trimmed.slice(0, match.index).trim();
  const tail = trimmed.slice(match.index + match[0].length).trim();
  return { head, tail: tail === "" ? null : tail };
}

export interface ContractEvidenceGroup {
  key: string;
  contractId: string | null;
  /** The supplier as the first citation's title names it -- never a guid. */
  title: string;
  /** `/contracts/{contractId}` when the group resolves to one contract, else null. */
  href: string | null;
  rows: readonly ReplyCitation[];
}

export interface EvidenceGroups {
  contracts: readonly ContractEvidenceGroup[];
  market: readonly ReplyCitation[];
  raffa: readonly ReplyCitation[];
  /** ADR-030: public web sources (the research role's citations) -- their own section, labelled
   * unverified, opened in a new tab; never an input to `buildEvidenceActions`. */
  web: readonly ReplyCitation[];
  total: number;
}

export function groupCitations(citations: readonly ReplyCitation[]): EvidenceGroups {
  const contractsByKey = new Map<string, { contractId: string | null; title: string; rows: ReplyCitation[] }>();
  const market: ReplyCitation[] = [];
  const raffa: ReplyCitation[] = [];
  const web: ReplyCitation[] = [];

  for (const citation of citations) {
    if (citation.corpus === "market") {
      market.push(citation);
      continue;
    }

    if (citation.corpus === "web") {
      web.push(citation);
      continue;
    }

    const contractId = citation.contractId?.trim() || null;
    const { head } = splitCitationTitle(citation.title);

    if (contractId === null && citation.corpus !== "tenant") {
      raffa.push(citation);
      continue;
    }

    const key = contractId !== null ? `id:${contractId}` : `title:${head.toLowerCase()}`;
    const existing = contractsByKey.get(key);
    if (existing) {
      existing.rows.push(citation);
    } else {
      contractsByKey.set(key, { contractId, title: head, rows: [citation] });
    }
  }

  const contracts: ContractEvidenceGroup[] = [];
  for (const [key, group] of contractsByKey) {
    contracts.push({
      key,
      contractId: group.contractId,
      title: group.title,
      href: group.contractId !== null ? `/contracts/${group.contractId}` : null,
      rows: group.rows,
    });
  }

  return { contracts, market, raffa, web, total: citations.length };
}

/** What a row adds beyond its group's supplier: "MSA", "criticality 72/100", "Liability clause".
 * `null` when the title is the supplier alone (nothing left to say). */
export function evidenceRowLabel(citation: ReplyCitation, groupTitle: string): string | null {
  const { head, tail } = splitCitationTitle(citation.title);
  if (head.toLowerCase() === groupTitle.toLowerCase()) {
    return tail;
  }
  const full = citation.title.trim();
  return full === "" ? null : full;
}

export interface EvidenceSummary {
  /** Supplier names, in citation order ("Salesforce, Microsoft"), else the section that has rows. */
  title: string;
  /** Counts per source, always non-empty for a non-empty reply. */
  subtitle: string;
}

const MAX_NAMED_SUPPLIERS = 3;

function plural(count: number, singular: string, pluralForm: string): string {
  return `${count} ${count === 1 ? singular : pluralForm}`;
}

export function describeEvidence(groups: EvidenceGroups): EvidenceSummary {
  const names = groups.contracts.map((group) => group.title);
  const shownNames = names.slice(0, MAX_NAMED_SUPPLIERS);
  const hiddenCount = names.length - shownNames.length;

  let title: string;
  if (names.length > 0) {
    title = hiddenCount > 0 ? `${shownNames.join(", ")} +${hiddenCount}` : shownNames.join(", ");
  } else if (groups.market.length > 0) {
    title = "Market";
  } else if (groups.web.length > 0) {
    title = "Public web";
  } else {
    title = "Raffa";
  }

  const parts: string[] = [];
  if (groups.contracts.length > 0) parts.push(plural(groups.contracts.length, "contract", "contracts"));
  if (groups.market.length > 0) parts.push(plural(groups.market.length, "market record", "market records"));
  if (groups.raffa.length > 0) parts.push(plural(groups.raffa.length, "Raffa item", "Raffa items"));
  if (groups.web.length > 0) parts.push(plural(groups.web.length, "web source", "web sources"));

  return { title, subtitle: parts.join(" · ") || plural(groups.total, "source", "sources") };
}

export const MAX_EVIDENCE_ACTIONS = 3;

/** The host of a web source, for the row's visible link text ("example.com ↗") -- never the
 * whole URL as prose. Falls back to the raw href when it does not parse. */
export function webSourceHost(href: string | null | undefined): string | null {
  if (!href) return null;
  try {
    return new URL(href).host;
  } catch {
    return href;
  }
}

const PORTFOLIO_ROUTE = "/contracts";

/**
 * The card's single action row, at most `MAX_EVIDENCE_ACTIONS` buttons: the contracts the answer
 * highlighted come first (Portfolio filtered to exactly those ids, or the one contract's own
 * Contract 360), then the backend's own actions (Renewals, Savings, ...) minus anything the first
 * ones already cover. The bare "Portfolio →" the backend sometimes adds is dropped whenever the
 * filtered Portfolio link supersedes it. Never invents an href: every link here is either the
 * catalog's own or a `/contracts` route built from citation ids the backend already resolved.
 */
export function buildEvidenceActions(groups: EvidenceGroups, replyActions: readonly ReplyAction[]): ReplyAction[] {
  const contractGroups = groups.contracts.filter(
    (group): group is ContractEvidenceGroup & { contractId: string; href: string } =>
      group.contractId !== null && group.href !== null,
  );
  const contractIds = Array.from(new Set(contractGroups.map((group) => group.contractId)));

  const result: ReplyAction[] = [];
  const seenHrefs = new Set<string>();
  const push = (label: string, href: string) => {
    if (seenHrefs.has(href) || result.length >= MAX_EVIDENCE_ACTIONS) return;
    seenHrefs.add(href);
    result.push({ label, href, kind: result.length === 0 ? "primary" : "secondary" });
  };

  if (contractIds.length >= 2) {
    push(`Portfolio · these ${contractIds.length} contracts →`, buildPortfolioHighlightHref(contractIds));
  } else if (contractIds.length === 1) {
    push(`${contractGroups[0].title} · Contract 360 →`, contractGroups[0].href);
  }

  for (const action of replyActions) {
    if (contractIds.length >= 2 && action.href === PORTFOLIO_ROUTE) continue;
    push(action.label, action.href);
  }

  return result;
}
