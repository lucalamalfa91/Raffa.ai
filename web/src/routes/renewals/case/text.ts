/**
 * Bilingual copy for the renewal case engine.
 *
 * The prototype wrote every string as a `B(en, it)` pair inline. Step 7 of the wizard rollout moves
 * these into the app's i18n catalogue; until then the engine keeps the pairs so it stays pure (no
 * hidden locale state) and every string exists in both languages.
 */

export type Lang = "en" | "it";

export interface Bilingual {
  en: string;
  it: string;
}

/** A string that is either already final (a user's own words, a file name) or an EN/IT pair. */
export type Text = string | Bilingual;

export function B(en: string, it: string): Bilingual {
  return { en, it };
}

export function tx(text: Text | null | undefined, lang: Lang): string {
  if (text == null) return "";
  return typeof text === "string" ? text : (text[lang] ?? text.en);
}

/** `pick(lang)(en, it)` -- the prototype's `S(en, it)` shorthand. */
export function pick(lang: Lang): <T>(en: T, it: T) => T {
  return (en, it) => (lang === "it" ? it : en);
}

export function num(n: number): string {
  return Math.round(n).toLocaleString("en-US");
}

export function chf(n: number): string {
  return `CHF ${num(n)}`;
}
