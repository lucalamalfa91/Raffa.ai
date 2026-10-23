/**
 * Demo identity. Responsibilities belong to each case; which *person* the signed-in account is on a
 * case is something the backend will answer. Until then `?as=<name>` picks a demo person (and is
 * remembered for the tab, so it survives moving between Today, Renewals and the chat).
 */

const KEY = "raffa.demo.as";

export function rememberedViewer(search: string): string | null {
  const requested = new URLSearchParams(search).get("as");
  try {
    if (requested) window.sessionStorage.setItem(KEY, requested);
    return requested ?? window.sessionStorage.getItem(KEY);
  } catch {
    return requested;
  }
}

export function forgetDemoViewer(): void {
  try {
    window.sessionStorage.removeItem(KEY);
  } catch {
    // nothing remembered
  }
}
