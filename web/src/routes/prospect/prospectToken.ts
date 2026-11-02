/**
 * The prospect invitation secret survives exactly one sign-in redirect. A prospect who arrives signed
 * out leaves for Microsoft and comes back to the app root with no memory of the link, so the secret is
 * kept in `sessionStorage` (this tab only) and redeemed -- then deleted -- by the first signed-in
 * resolution. It is single-use, bound to the invited address and expires, so a leftover is worthless.
 */
const KEY = "raffa.prospect.invitation";

export function rememberProspectToken(token: string): void {
  try {
    window.sessionStorage.setItem(KEY, token);
  } catch {
    // Private mode: the visitor re-opens the link after signing in.
  }
}

/** Returns and forgets the stored secret. */
export function takeProspectToken(): string | null {
  try {
    const token = window.sessionStorage.getItem(KEY);
    window.sessionStorage.removeItem(KEY);
    return token && token.length > 0 ? token : null;
  } catch {
    return null;
  }
}
