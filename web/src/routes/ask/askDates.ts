import type { Lang } from "../../i18n/translate";

const MONTHS: Record<Lang, readonly string[]> = {
  it: ["Gen", "Feb", "Mar", "Apr", "Mag", "Giu", "Lug", "Ago", "Set", "Ott", "Nov", "Dic"],
  en: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
};

/** "02 Set 2026" (IT) / "02 Sep 2026" (EN), the prototype's conversation date, from an ISO timestamp (UTC day). */
export function conversationDay(iso: string, lang: Lang): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const day = String(d.getUTCDate()).padStart(2, "0");
  return `${day} ${MONTHS[lang][d.getUTCMonth()]} ${d.getUTCFullYear()}`;
}

/** True when `iso` falls on `today` (YYYY-MM-DD, the scenario clock or the real date). */
export function isSameDay(iso: string, today: string): boolean {
  return iso.slice(0, 10) === today;
}

export function todayIso(now?: string | null): string {
  return now && /^\d{4}-\d{2}-\d{2}$/.test(now) ? now : new Date().toISOString().slice(0, 10);
}
