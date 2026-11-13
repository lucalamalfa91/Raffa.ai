import { useCallback, useEffect, useMemo, useState } from "react";
import type { ApiClient, UpdateWorkspaceSettingsRequest, WorkspaceSettingsBody } from "../../api/client";
import { useI18n } from "../../i18n/I18nProvider";

export interface ReportingSettingsProps {
  apiClient: ApiClient;
  tenantId: string;
}

const MONTHS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

/** "Gennaio" / "January": the month's name in the UI language, capitalised. */
function monthName(month: number, lang: string): string {
  const name = new Intl.DateTimeFormat(lang === "it" ? "it-CH" : "en-GB", { month: "long", timeZone: "UTC" }).format(Date.UTC(2026, month - 1, 1));
  return name.charAt(0).toUpperCase() + name.slice(1);
}

/** A map of positive rates, read defensively (the generated type cannot describe a map). */
function mapOf(raw: unknown): Record<string, number> {
  if (!raw || typeof raw !== "object") return {};
  return Object.fromEntries(Object.entries(raw as Record<string, unknown>).filter((e): e is [string, number] => typeof e[1] === "number" && e[1] > 0));
}

/**
 * The reference rates (ECB, downloaded every morning) as units of `currency` per unit of each other
 * currency. The server crosses them into the saved reporting currency; while the Admin is picking another
 * one they are crossed again here, through the saved one.
 */
function marketInto(body: WorkspaceSettingsBody | null, currency: string): Record<string, number> {
  const market = mapOf(body?.marketRates);
  const saved = body?.reportingCurrency ?? "CHF";
  if (currency === saved) return market;
  const pivot = market[currency];
  if (!pivot) return {};
  const crossed: Record<string, number> = { [saved]: 1 / pivot };
  for (const [code, rate] of Object.entries(market)) if (code !== currency) crossed[code] = rate / pivot;
  return crossed;
}

/** A rate for display and as a starting value: at most 6 significant digits, no exponent. */
function formatRate(rate: number, lang: string): string {
  return new Intl.NumberFormat(lang === "it" ? "it-CH" : "en-GB", { maximumSignificantDigits: 6, useGrouping: false }).format(rate);
}

/** "2026-11-12" → "12 novembre 2026" / "12 November 2026". */
function formatDay(day: string, lang: string): string {
  const [y, m, d] = day.split("-").map(Number);
  if (!y || !m || !d) return day;
  return new Intl.DateTimeFormat(lang === "it" ? "it-CH" : "en-GB", { day: "numeric", month: "long", year: "numeric", timeZone: "UTC" }).format(Date.UTC(y, m - 1, d));
}

/**
 * Impostazioni, Spesa e risparmi: the currency every total is in, the month the fiscal year starts,
 * and the exchange rates used to convert contracts in other currencies: the ECB reference rates Raffa
 * downloads every morning, shown read-only. The settings are the workspace's
 * (`/api/workspaces/{id}/settings`); only an Admin changes them. A contract in a currency without a
 * reference rate stays out of the totals and is listed.
 */
export default function ReportingSettings({ apiClient, tenantId }: ReportingSettingsProps) {
  const { t, lang } = useI18n();
  const [loaded, setLoaded] = useState<WorkspaceSettingsBody | null>(null);
  const [currency, setCurrency] = useState("");
  const [month, setMonth] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  const apply = useCallback((body: WorkspaceSettingsBody) => {
    setLoaded(body);
    setCurrency(body.reportingCurrency ?? "CHF");
    setMonth(body.fiscalYearStartMonth ? String(body.fiscalYearStartMonth) : "");
  }, []);

  useEffect(() => {
    // A client without the workspace settings route (fixtures, tests of other sections) shows nothing.
    if (typeof apiClient.getWorkspaceSettings !== "function") return;
    let alive = true;
    void apiClient.getWorkspaceSettings(tenantId).then((r) => {
      if (!alive) return;
      if (r.ok && r.settings) apply(r.settings);
      else setError(r.error);
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId, apply]);

  const canEdit = loaded?.canEdit ?? false;
  const supported = useMemo(() => [...(loaded?.supportedCurrencies ?? [])].sort(), [loaded]);
  const currencyChanged = loaded !== null && currency !== (loaded.reportingCurrency ?? "CHF");

  const market = useMemo(() => marketInto(loaded, currency), [loaded, currency]);
  const marketCodes = supported.filter((c) => c !== currency && market[c] > 0);
  const uncovered = supported.filter((c) => c !== currency && !(market[c] > 0));
  const marketDate = loaded?.marketRatesDate ?? null;
  const monthChanged = loaded !== null && month !== "" && Number(month) !== (loaded.fiscalYearStartMonth ?? 0);
  const dirty = currencyChanged || monthChanged;

  const pickCurrency = (code: string) => {
    setCurrency(code);
    setSaved(null);
  };

  const save = async () => {
    if (!dirty) return;
    const request: UpdateWorkspaceSettingsRequest = {};
    if (currencyChanged) request.reportingCurrency = currency;
    if (monthChanged) request.fiscalYearStartMonth = Number(month);
    setSaving(true);
    setError(null);
    const r = await apiClient.updateWorkspaceSettings(tenantId, request);
    setSaving(false);
    if (r.ok && r.settings) {
      apply(r.settings);
      setSaved(t("Saved. Spend and savings uses these settings from now on."));
    } else {
      setError(r.error);
    }
  };

  if (!loaded) {
    return error ? (
      <section className="st-card st-reporting" aria-labelledby="st-reporting-h">
        <h3 id="st-reporting-h" className="st-h">
          {t("Spend and savings")}
        </h3>
        <div className="st-error" role="alert">
          {error}
        </div>
      </section>
    ) : null;
  }

  return (
    <section className="st-card st-reporting" aria-labelledby="st-reporting-h">
      <h3 id="st-reporting-h" className="st-h">
        {t("Spend and savings")}
      </h3>
      <p className="st-p">
        {t("How Spend and savings reports money: the currency of every total, when your fiscal year starts, and the exchange rates for contracts in other currencies.")}
      </p>
      {!canEdit ? <div className="st-note">{t("Only a Workspace Admin can change these settings.")}</div> : null}

      <div className="st-reporting-fields">
        <div className="field">
          <label className="st-label" htmlFor="st-currency">
            {t("Reporting currency")}
          </label>
          <select id="st-currency" className="input" value={currency} disabled={!canEdit} onChange={(e) => pickCurrency(e.target.value)}>
            {supported.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
          {!loaded.reportingCurrencySet && !currencyChanged ? <div className="st-hint">{t("Your workspace's currency, until you choose one.")}</div> : null}
        </div>

        <div className="field">
          <label className="st-label" htmlFor="st-fy">
            {t("Fiscal year starts in")}
          </label>
          <select
            id="st-fy"
            className="input"
            value={month}
            disabled={!canEdit}
            onChange={(e) => {
              setMonth(e.target.value);
              setSaved(null);
            }}
          >
            {month === "" ? <option value="">{t("Not set (calendar year)")}</option> : null}
            {MONTHS.map((m) => (
              <option key={m} value={String(m)}>
                {monthName(m, lang)}
              </option>
            ))}
          </select>
          <div className="st-hint">{t("A fiscal year is named by the year it starts in: April 2026 – March 2027 is FY 2026/27.")}</div>
        </div>
      </div>

      <div className="st-rates" role="group" aria-labelledby="st-market-h">
        <div id="st-market-h" className="st-label">
          {t("Exchange rates")}
        </div>
        {marketDate ? (
          <div className="st-hint">
            {t("Updated automatically every morning from the European Central Bank reference rates (rates of {date}).", { date: formatDay(marketDate, lang) })}
          </div>
        ) : (
          <div className="st-hint">{t("The European Central Bank reference rates are downloaded every morning: they will show here after the first download.")}</div>
        )}
        {marketCodes.length > 0 ? (
          <ul className="st-market" aria-label={t("Reference rates in {currency}", { currency })}>
            {marketCodes.map((c) => (
              <li key={c} className="st-market-rate">
                <span>1 {c}</span>
                <span className="st-rate-eq">=</span>
                <span className="st-market-value">
                  {formatRate(market[c], lang)} {currency}
                </span>
              </li>
            ))}
          </ul>
        ) : null}
        {uncovered.length > 0 ? (
          <div className="st-hint">
            {t("No reference rate for {currencies}: contracts in these currencies stay out of the totals, listed apart.", { currencies: uncovered.join(", ") })}
          </div>
        ) : null}
      </div>

      {canEdit ? (
        <div className="st-actions">
          <button type="button" className="btn btn-primary" disabled={!dirty || saving} onClick={() => void save()}>
            {saving ? t("Saving…") : t("Save the settings")}
          </button>
          <button type="button" className="btn btn-ghost" disabled={!dirty || saving} onClick={() => apply(loaded)}>
            {t("Discard changes")}
          </button>
          {saved ? (
            <span className="st-saved" role="status">
              {saved}
            </span>
          ) : null}
        </div>
      ) : null}
      {error ? (
        <div className="st-error" role="alert">
          {error}
        </div>
      ) : null}
    </section>
  );
}
