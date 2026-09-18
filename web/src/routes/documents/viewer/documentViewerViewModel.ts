import type { Contract360ClauseBody } from "../../../api/client";

/** First page of a document. Named so this folder never silently rewrites the asked page. */
export const FIRST_PAGE = 1;

export const PAGE_NAV_TOTAL_UNKNOWN = "Page 1 · total unknown";

export const EMPTY_PAGE_COPY = "No page image yet";

export const BEYOND_COUNT_HEADING = "Page not in this document";

export const REAPED_PAGE_HEADING = "Page no longer in this document";

export const REAPED_PAGE_COPY =
  "This page is no longer part of this document — it was re-processed after this link was made.";

export const CITATION_UNRESOLVABLE_COPY = "The citation could not be restored.";

export const DOCUMENT_MISSING_COPY = "This document is not in this workspace.";

export const PREVIEW_SERVICE_NAME = "Raffa.ai's document preview service";

export type CitationResolution =
  | { kind: "none" }
  | { kind: "resolved"; clause: Contract360ClauseBody; sourcePage: number }
  | { kind: "unresolvable" };

export type NotFoundCause = "beyond-count" | "reaped";

export type ViewerSurface =
  | { kind: "beyond-count"; pageCount: number; askedPage: number }
  | { kind: "page"; page: number; fetchPage: number; citation: CitationResolution };

/**
 * A 1-based positive integer, or `null` when the query is absent or not a
 * positive integer (zero, negative, empty, or non-numeric).
 */
export function parsePositivePage(pageParam: string | null): number | null {
  if (pageParam === null || pageParam === "") return null;
  if (!/^[1-9]\d*$/.test(pageParam)) return null;
  return Number(pageParam);
}

export function formatBeyondCountCopy(pageCount: number): string {
  return `This document has ${pageCount} pages.`;
}

export function formatPageNav(page: number, pageCount: number | null): string {
  if (pageCount === null) return PAGE_NAV_TOTAL_UNKNOWN;
  return `Page ${page} of ${pageCount}`;
}

export function formatHighlightAffordance(page: number): string {
  return `Page ${page} — the wording is highlighted below`;
}

/**
 * `?clause=` must name a clause that belongs to this document and carries a
 * resolvable `sourcePage`. A missing or non-positive `?page=`, an unknown
 * clause, a clause with no `sourcePage`, a clause from another document, and
 * the arrived-cold (no query) case are all unresolvable — the route still
 * opens the document on {@link FIRST_PAGE}.
 *
 * Paging with a valid `?page=` and no `?clause=` is not a citation (`none`).
 */
export function resolveCitation(input: {
  documentId: string;
  pageParam: string | null;
  clauseParam: string | null;
  clauses: readonly Contract360ClauseBody[] | null;
}): CitationResolution {
  const { documentId, pageParam, clauseParam, clauses } = input;
  const parsedPage = parsePositivePage(pageParam);

  if (pageParam !== null && parsedPage === null) {
    return { kind: "unresolvable" };
  }

  if (clauseParam === null) {
    if (pageParam === null) return { kind: "unresolvable" };
    return { kind: "none" };
  }

  if (clauses === null) return { kind: "unresolvable" };

  const clause = clauses.find((row) => row.clauseId === clauseParam);
  if (clause === undefined) return { kind: "unresolvable" };
  if (clause.sourceDocumentId !== documentId) return { kind: "unresolvable" };
  if (clause.sourcePage === null || clause.sourcePage < FIRST_PAGE) return { kind: "unresolvable" };

  return { kind: "resolved", clause, sourcePage: clause.sourcePage };
}

/**
 * Page bounds and citation together. `page > pageCount` is the route's own
 * not-found (no fetch). An unresolvable citation is an ordinary page on
 * {@link FIRST_PAGE}, never a silent stand-in for the cited page.
 *
 * A null `pageCount` shows {@link FIRST_PAGE} and {@link PAGE_NAV_TOTAL_UNKNOWN}.
 */
export function resolveViewerSurface(input: {
  documentId: string;
  pageParam: string | null;
  clauseParam: string | null;
  pageCount: number | null;
  clauses: readonly Contract360ClauseBody[] | null;
}): ViewerSurface {
  const citation = resolveCitation(input);
  const parsedPage = parsePositivePage(input.pageParam);

  if (parsedPage !== null && input.pageCount !== null && parsedPage > input.pageCount) {
    return { kind: "beyond-count", pageCount: input.pageCount, askedPage: parsedPage };
  }

  if (citation.kind === "unresolvable" || input.pageCount === null) {
    return { kind: "page", page: FIRST_PAGE, fetchPage: FIRST_PAGE, citation };
  }

  const page = parsedPage ?? (citation.kind === "resolved" ? citation.sourcePage : FIRST_PAGE);
  return { kind: "page", page, fetchPage: page, citation };
}

export function previewNotFoundCause(pageCount: number | null): NotFoundCause | "empty" {
  return pageCount === null ? "empty" : "reaped";
}

/** In-app destination of the viewer route (`/documents/:documentId/viewer?page=&clause=`). */
export interface DocumentViewerTarget {
  documentId: string;
  page: string | null;
  clause: string | null;
}

/**
 * Parse a viewer deep-link. Returns `null` for any other in-app href (360, Documents list,
 * Renewals). Query values stay strings so the viewer can keep treating absent/`page=` the same
 * way the route already does.
 */
export function parseDocumentViewerHref(href: string): DocumentViewerTarget | null {
  try {
    const url = new URL(href, "https://raffa.local");
    const match = url.pathname.match(/^\/documents\/([^/]+)\/viewer$/);
    if (match === null) return null;
    return {
      documentId: match[1],
      page: url.searchParams.get("page"),
      clause: url.searchParams.get("clause"),
    };
  } catch {
    return null;
  }
}

export function isDocumentViewerHref(href: string): boolean {
  return parseDocumentViewerHref(href) !== null;
}

export function buildDocumentViewerHref(
  documentId: string,
  page?: number | string | null,
  clause?: string | null,
): string {
  const params = new URLSearchParams();
  if (page !== null && page !== undefined && page !== "") params.set("page", String(page));
  if (clause) params.set("clause", clause);
  const query = params.toString();
  return `/documents/${documentId}/viewer${query === "" ? "" : `?${query}`}`;
}
