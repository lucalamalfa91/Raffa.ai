/**
 * The app's translation core.
 *
 * Source strings are English and double as the message id (gettext style): `t("Notice by")`. English
 * is the default language and needs no catalogue; Italian lives in `it/` as `id → translation`.
 * Placeholders are `{name}`, filled from `vars`, so word order can differ per language:
 * `t("{n} items", { n: 3 })` → "3 elementi".
 *
 * A `Msg` is a message that has not been turned into text yet. The renewal engine stores these in a
 * case (log entries, Raffa's lines) so the same case reads in whichever language is on screen.
 */

import { IT } from "./it";

export type Lang = "en" | "it";
export const LANGS: readonly Lang[] = ["en", "it"];
export const DEFAULT_LANG: Lang = "en";

export type Vars = Readonly<Record<string, string | number>>;

/** A message by id (its English text) and its values, not yet in any language. */
export interface Msg {
  id: string;
  vars?: Vars;
}

export function isLang(value: unknown): value is Lang {
  return value === "en" || value === "it";
}

export function format(template: string, vars?: Vars): string {
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (whole, name: string) => (name in vars ? String(vars[name]) : whole));
}

/** The Italian text for `id`, or `undefined` when the catalogue has none (shown in English). */
export function italianFor(id: string): string | undefined {
  return IT[id];
}

let onMissingItalian: ((id: string) => void) | null = null;

/** Test hook: called with the id of every message shown in Italian that the catalogue lacks. */
export function trackMissingItalian(handler: ((id: string) => void) | null): void {
  onMissingItalian = handler;
}

export function translate(lang: Lang, id: string, vars?: Vars): string {
  if (lang !== "it") return format(id, vars);
  const text = IT[id];
  if (text === undefined) onMissingItalian?.(id);
  return format(text ?? id, vars);
}

export function msg(id: string, vars?: Vars): Msg {
  return vars ? { id, vars } : { id };
}

/** A message, or a string that is already final (a person's own words, a file name). */
export type Text = string | Msg;

export function resolveText(text: Text | null | undefined, lang: Lang): string {
  if (text == null) return "";
  return typeof text === "string" ? text : translate(lang, text.id, text.vars);
}

/** `translator(lang)("Notice by")` -- a `t` bound to one language. */
export type Translate = (id: string, vars?: Vars) => string;

export function translator(lang: Lang): Translate {
  return (id, vars) => translate(lang, id, vars);
}
