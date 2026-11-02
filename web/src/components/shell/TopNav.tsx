import { Link, useLocation } from "react-router-dom";
import { useI18n } from "../../i18n/I18nProvider";
import { useGuideState } from "./guide";
import AccountMenu, { type AccountMenuProps } from "./AccountMenu";

/** The tabs (prototype: Oggi · Rinnovi · Contratti · Risultati · Chiedi, then Impostazioni) and what makes each active. */
const TABS: ReadonlyArray<{ id: string; label: string; to: string; active: (path: string) => boolean }> = [
  { id: "today", label: "Today", to: "/today", active: (p) => p === "/" || p.startsWith("/today") },
  { id: "renewals", label: "Renewals⟦nav⟧", to: "/renewals", active: (p) => p.startsWith("/renewals") },
  {
    id: "contracts",
    label: "Contracts⟦nav⟧",
    to: "/contracts",
    active: (p) => p.startsWith("/contracts") || p.startsWith("/documents"),
  },
  { id: "results", label: "Results⟦nav⟧", to: "/savings", active: (p) => p.startsWith("/savings") },
  { id: "ask", label: "Ask⟦nav⟧", to: "/ask", active: (p) => p.startsWith("/ask") || p.startsWith("/quotes") },
  { id: "settings", label: "Settings", to: "/settings", active: (p) => p.startsWith("/settings") },
];

/**
 * The AskRaffa 4.0 top nav (prototype lines 38-44): logo square + "Raffa.ai", five tabs (active =
 * 3px accent bottom border; badge on Oggi), and the Guida Raffa toggle. It is the page's
 * `<header>` (banner) landmark.
 */
export default function TopNav(account: AccountMenuProps) {
  const { t } = useI18n();
  const { pathname } = useLocation();
  const { open, toggle, badge } = useGuideState();

  return (
    <header className="v4-nav">
      <div className="v4-nav-logo">
        <span className="v4-mark" aria-hidden="true" />
        Raffa.ai
      </div>
      <nav className="v4-nav-tabs" aria-label={t("Primary")}>
        {TABS.map((tab) => {
          const active = tab.active(pathname);
          return (
            <Link
              key={tab.id}
              to={tab.to}
              className={active ? "v4-tab is-active" : "v4-tab"}
              aria-current={active ? "page" : undefined}
              data-tab={tab.id}
            >
              {t(tab.label)}
              <span className="v4-tab-badge">{tab.id === "today" && badge ? String(badge) : ""}</span>
            </Link>
          );
        })}
      </nav>
      <button
        type="button"
        className={open ? "v4-guide-toggle is-open" : "v4-guide-toggle"}
        aria-pressed={open}
        onClick={toggle}
      >
        <span className="v4-mark-sm" aria-hidden="true" />
        {t("Raffa guide")}
      </button>
      <AccountMenu {...account} />
    </header>
  );
}
