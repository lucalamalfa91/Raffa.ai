import { useEffect, useMemo, useRef } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import RenewalChat, { type ChatTarget } from "./chat/RenewalChat";
import { translator } from "./chat/chatCopy";
import { DEMO_APPROVER, DEMO_CONTRACTS, DEMO_OWNER, demoContext } from "./fixtures";
import { view } from "./engine";
import type { Lang } from "./text";
import type { ActPayload, ActionType } from "./types";
import { getCaseStore, useCaseStore } from "./useCaseStore";
import { resolveViewer } from "./viewer";

export interface RenewalChatRouteProps {
  /** The signed-in account's label; becomes the viewer when it is a person on the case. */
  userLabel: string;
}

/**
 * Route `/renewals/:contractId/chat` -- the renewal wizard for one contract.
 *
 * Case data is the demo tenant's until the backend owns renewal cases (see `caseStore.ts`):
 * contract facts come from `fixtures.ts`, the case itself from the local store. `?as=` picks a demo
 * person and `?lang=it` previews Italian; both go away when accounts and the app's i18n are wired.
 */
export default function RenewalChatRoute({ userLabel }: RenewalChatRouteProps) {
  const { contractId = "" } = useParams();
  const [search] = useSearchParams();
  const navigate = useNavigate();
  const store = getCaseStore();
  const { cases } = useCaseStore(store);

  const lang: Lang = search.get("lang") === "it" ? "it" : "en";
  const known = contractId in DEMO_CONTRACTS;
  const existing = cases[contractId];
  const viewer = resolveViewer({
    userLabel,
    as: search.get("as"),
    members: existing?.members ?? [DEMO_APPROVER, DEMO_OWNER],
    approver: existing?.approver ?? DEMO_APPROVER,
  });
  const ctx = useMemo(() => (known ? demoContext(contractId, viewer, lang) : null), [known, contractId, viewer, lang]);

  // Opening the case records a visit (the "since your last visit" summary) -- once per contract and
  // person, even though React may run the effect twice in development.
  const opened = useRef<string | null>(null);
  useEffect(() => {
    const key = `${contractId}:${viewer}`;
    if (!ctx || opened.current === key) return;
    opened.current = key;
    store.open(contractId, ctx, { approver: DEMO_APPROVER, owner: viewer });
  }, [store, ctx, contractId, viewer]);

  const caseView = useMemo(() => (ctx && existing ? view(existing, ctx) : null), [ctx, existing]);

  if (!ctx) {
    const t = translator(lang);
    return (
      <div className="empty-state" role="status">
        <h3>{t("notFoundTitle")}</h3>
        <p className="micro-meta">{t("notFoundBody")}</p>
        <Link to="/renewals" className="btn btn-primary">
          {t("backToRenewals")}
        </Link>
      </div>
    );
  }
  if (!caseView) return null;

  const onAct = (type: ActionType, payload?: ActPayload) => store.act(contractId, type, payload, ctx);
  const onNavigate = (target: ChatTarget) => {
    const to: Record<ChatTarget, string> = {
      history: `/renewals?select=${contractId}&tab=timeline`,
      contract: `/contracts/${contractId}`,
      renewals: `/renewals?select=${contractId}`,
      newChat: "/ask",
    };
    navigate(to[target]);
  };

  return <RenewalChat view={caseView} lang={lang} onAct={onAct} onNavigate={onNavigate} />;
}
