import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useI18n } from "../../i18n/I18nProvider";
import { canManageMembers, WORKSPACE_ROLE_LABEL, type WorkspaceRole } from "./navItems";

export interface AccountMenuProps {
  workspaceName: string;
  role: WorkspaceRole;
  userLabel: string;
  onSignOut: () => void;
  /** Back to the workspace picker: another workspace, or a new one. Absent: no such entry. */
  onSwitchWorkspace?: () => void;
}

/** Two letters for the avatar mark: the first letters of the first two words of the user's name, or of the address. */
export function initials(userLabel: string): string {
  const base = userLabel.split("@")[0] ?? userLabel;
  const parts = base.split(/[\s._-]+/).filter(Boolean);
  const letters = parts.length >= 2 ? `${parts[0]![0]}${parts[1]![0]}` : base.slice(0, 2);
  return letters.toUpperCase() || "?";
}

/**
 * The language switch (IT | EN) and the account menu, at the right end of the top nav: who is signed in
 * and as what, the workspace, "Workspace & members" (the Admin's roster and invitations), Documents, and
 * Sign out. They were the retired rail's footer; the 4.0 top nav had dropped them (the language switch
 * lived only in the demo bar).
 */
export default function AccountMenu({ workspaceName, role, userLabel, onSignOut, onSwitchWorkspace }: AccountMenuProps) {
  const { t, lang, setLang } = useI18n();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return undefined;
    const onPointer = (e: MouseEvent) => {
      if (root.current && !root.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onPointer);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onPointer);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  return (
    <div className="v4-nav-extras" data-parity-hide>
      <div className="v4-lang" role="group" aria-label={t("Language")}>
        <button type="button" className={lang === "it" ? "is-on" : ""} aria-pressed={lang === "it"} onClick={() => setLang("it")}>
          IT
        </button>
        <button type="button" className={lang === "en" ? "is-on" : ""} aria-pressed={lang === "en"} onClick={() => setLang("en")}>
          EN
        </button>
      </div>
      <div className="v4-acct" ref={root}>
        <button
          type="button"
          className={open ? "v4-acct-btn is-open" : "v4-acct-btn"}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label={t("Account: {user}", { user: userLabel })}
          onClick={() => setOpen((v) => !v)}
        >
          <span className="v4-acct-mark" aria-hidden="true">
            {initials(userLabel)}
          </span>
        </button>
        {open ? (
          <div className="v4-acct-menu" role="menu" aria-label={t("Account")}>
            <div className="v4-acct-who">
              <div className="v4-acct-user">{userLabel}</div>
              <div className="v4-acct-meta">{WORKSPACE_ROLE_LABEL[role]}</div>
              <div className="v4-acct-meta">{workspaceName}</div>
            </div>
            {onSwitchWorkspace ? (
              <button type="button" role="menuitem" className="v4-acct-item" onClick={onSwitchWorkspace}>
                {t("Switch or create a workspace")}
              </button>
            ) : null}
            <Link role="menuitem" to="/workspace/members" className="v4-acct-item" onClick={() => setOpen(false)}>
              {canManageMembers(role) ? t("Workspace & members") : t("Workspace members")}
            </Link>
            <Link role="menuitem" to="/documents" className="v4-acct-item" onClick={() => setOpen(false)}>
              {t("Documents")}
            </Link>
            <button type="button" role="menuitem" className="v4-acct-item" onClick={onSignOut}>
              {t("Sign out")}
            </button>
          </div>
        ) : null}
      </div>
    </div>
  );
}
