import { isLang, type Lang } from "./translate";

/** The same key the I18nProvider remembers the language under (`localStorage['raffa4.lang']`). */
const STORAGE_KEY = "raffa4.lang";

let active: Lang | null = null;

/** The provider tells the API client which language is on screen, so Raffa answers in it. */
export function setActiveLang(lang: Lang): void {
  active = lang;
}

/** The language on screen (what the user chose), or null before anything was chosen. */
export function getActiveLang(): Lang | null {
  if (active) return active;
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return isLang(stored) ? stored : null;
  } catch {
    return null;
  }
}

/** Header carried by every API request: the backend writes every reply (answers, chats, texts) in it. */
export const LANG_HEADER = "X-Raffa-Lang";
