import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useSearchParams } from "react-router-dom";
import { setActiveLang } from "./activeLang";
import { DEFAULT_LANG, isLang, translate, type Lang, type Translate } from "./translate";
import { useFixtureSession } from "../fixtures/FixtureSessionContext";

/** Shared with the AskRaffa 4.0 prototype (`localStorage['raffa4.lang']`). */
export const LANG_STORAGE_KEY = "raffa4.lang";

export interface I18nValue {
  lang: Lang;
  t: Translate;
  setLang: (lang: Lang) => void;
}

const defaultValue: I18nValue = {
  lang: DEFAULT_LANG,
  t: (id, vars) => translate(DEFAULT_LANG, id, vars),
  setLang: () => undefined,
};

const I18nContext = createContext<I18nValue>(defaultValue);

function readStored(): Lang | null {
  try {
    const stored = window.localStorage.getItem(LANG_STORAGE_KEY);
    return isLang(stored) ? stored : null;
  } catch {
    return null;
  }
}

function writeStored(lang: Lang): void {
  try {
    window.localStorage.setItem(LANG_STORAGE_KEY, lang);
  } catch {
    // Private mode: the choice lasts for this page only.
  }
}

/**
 * The language of the interface: `?lang=` (and remembered), else what was remembered, else Italian
 * in fixture mode (as the prototype) and English otherwise. Screens outside this provider read
 * English, so a bare component test needs no setup.
 */
export function I18nProvider({ children }: { children: ReactNode }) {
  const [search] = useSearchParams();
  const requested = search.get("lang");
  const fallback: Lang = useFixtureSession() ? "it" : DEFAULT_LANG;
  const [lang, setLangState] = useState<Lang>(() => (isLang(requested) ? requested : (readStored() ?? fallback)));

  useEffect(() => {
    if (isLang(requested)) {
      setLangState(requested);
      writeStored(requested);
    }
  }, [requested]);

  // The API client reads the language on screen from here: every request tells the backend which language to write in.
  setActiveLang(lang);
  useEffect(() => {
    setActiveLang(lang);
  }, [lang]);

  const value = useMemo<I18nValue>(
    () => ({
      lang,
      t: (id, vars) => translate(lang, id, vars),
      setLang: (next) => {
        setLangState(next);
        writeStored(next);
      },
    }),
    [lang],
  );
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nValue {
  return useContext(I18nContext);
}
