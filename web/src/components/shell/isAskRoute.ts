/**
 * True on the Ask Raffa screen itself (`/ask`, `/ask/:conversationId`) — the one place the
 * global Ask bar must not render, because that route already has its own composer.
 *
 * Matches a path *segment* named `ask`, so a leading-slash-less descendant remainder (`ask`,
 * `ask/<id>`) that React Router 7 can surface under App.tsx's `path="/*"` still counts, and a
 * trailing slash does too. `/askew` does not: `ask` must be a whole segment.
 */
export function isAskRoute(pathname: string): boolean {
  const trimmed = pathname.trim();
  const withSlash = trimmed.startsWith("/") ? trimmed : `/${trimmed}`;
  const normalized = withSlash.replace(/\/+$/, "") || "/";
  return normalized === "/ask" || /^\/ask\/[^/]+$/.test(normalized);
}
