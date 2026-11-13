import { useCallback, useEffect, useMemo, useState } from "react";
import type { ApiClient, UpdateWorkspaceSettingsRequest, WorkspaceSettingsBody } from "../../api/client";
import { useI18n } from "../../i18n/I18nProvider";

export interface ReportingSettingsProps {
  apiClient: ApiClient;
  tenantId: string;
}

interface RateRow {
  key: number;
  code: string;
  value: string;
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

/** The body's budget rates: the Admin's manual overrides. */
function ratesOf(body: WorkspaceSettingsBody | null): Record<string, number> {
  return mapOf(body?.budgetRates);
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

/** A row the Admin added and left untouched: ignored rather than blocking the save. */
function isBlank(row: RateRow): boolean {
  return !row.code && row.value.trim() === "";
}

/** A stable text of a set of rates, to tell whether it changed. */
function rateKey(rates: Record<string, number>): string {
  return Object.keys(rates)
    .sort()
    .map((k) => `${k}:${rates[k]}`)
    .join(",");
}

function rowsOf(rates: Record<string, number>): RateRow[] {
  return Object.entries(rates)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([code, value], i) => ({ key: i, code, value: String(value) }));
}

/** A rate the user typed ("0.95" or "0,95"), or NaN. */
function parseRate(text: string): number {
  const n = Number(text.trim().replace(",", "."));
  return text.trim() === "" ? Number.NaN : n;
}

/**
 * Impostazioni, Spesa e risparmi: the currency every total is in, the month the fiscal year starts,
 * and the exchange rates used to convert contracts in other currencies. The rates are the ECB reference
 * rates Raffa downloads every morning; the Admin may override any of them (a budget rate fixed by
 * finance, or a currency the ECB does not publish). All of it is the workspace's
 * (`/api/workspaces/{id}/settings`); only an Admin changes it. A contract in a currency with neither
 * a reference rate nor an override stays out of the totals and is listed.
 */
export default function ReportingSettings({ apiClient, tenantId }: ReportingSettingsProps) {
  const { t, lang } = useI18n();
  const [loaded, setLoaded] = useState<WorkspaceSettingsBody | null>(null);
  const [currency, setCurrency] = useState("");
  const [month, setMonth] = useState("");
  const [rates, setRates] = useState<RateRow[]>([]);
  const [nextKey, setNextKey] = useState(100);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  const apply = useCallback((body: WorkspaceSettingsBody) => {
    setLoaded(body);
    setCurrency(body.reportingCurrency ?? "CHF");
    setMonth(body.fiscalYearStartMonth ? String(body.fiscalYearStartMonth) : "");
    setRates(rowsOf(ratesOf(body)));
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

  const filled = rates.filter((r) => !isBlank(r));
  const invalidRate = filled.some((r) => !r.code || !(parseRate(r.value) > 0));
  const duplicate = new Set(filled.map((r) => r.code)).size !== filled.length;
  const nextRates = Object.fromEntries(filled.filter((r) => r.code).map((r) => [r.code, parseRate(r.value)]));
  const market = useMemo(() => marketInto(loaded, currency), [loaded, currency]);
  const marketCodes = supported.filter((c) => c !== currency && market[c] > 0);
  const overridden = new Set(filled.map((r) => r.code).filter(Boolean));
  const uncovered = supported.filter((c) => c !== currency && !(market[c] > 0) && !overridden.has(c));
  const marketDate = loaded?.marketRatesDate ?? null;
  const ratesChanged = loaded !== null && rateKey(nextRates) !== rateKey(ratesOf(loaded));
  const monthChanged = loaded !== null && month !== "" && Number(month) !== (loaded.fiscalYearStartMonth ?? 0);
  const dirty = currencyChanged || monthChanged || ratesChanged;

  const pickCurrency = (code: string) => {
    setCurrency(code);
    setSaved(null);
    // Rates are relative to the reporting currency: a new currency needs new rates.
    if (loaded && code !== (loaded.reportingCurrency ?? "CHF")) setRates([]);
    else if (loaded) setRates(rowsOf(ratesOf(loaded)));
  };

  const save = async () => {
    if (!dirty || invalidRate || duplicate) return;
    const request: UpdateWorkspaceSettingsRequest = {};
    if (currencyChanged) request.reportingCurrency = currency;
    if (monthChanged) request.fiscalYearStartMonth = Number(month);
    if (ratesChanged || currencyChanged) request.budgetRates = nextRates;
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

  const candidates = (current: string) => supported.filter((c) => c !== currency && (c === current || !rates.some((r) => r.code === c)));

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
              <li key={c} className={overridden.has(c) ? "st-market-rate is-overridden" : "st-market-rate"}>
                <span>1 {c}</span>
                <span className="st-rate-eq">=</span>
                <span className="st-market-value">
                  {formatRate(market[c], lang)} {currency}
                </span>
                {overridden.has(c) ? <span className="st-market-tag">{t("overridden")}</span> : null}
              </li>
            ))}
          </ul>
        ) : null}
        {uncovered.length > 0 ? (
          <div className="st-hint">
            {t("No reference rate for {currencies}: add an override to include them in the totals.", { currencies: uncovered.join(", ") })}
          </div>
        ) : null}
      </div>

      <div className="st-rates" role="group" aria-labelledby="st-rates-h">
        <div id="st-rates-h" className="st-label">
          {t("Manual overrides (optional)")}
        </div>
        {currencyChanged && Object.keys(ratesOf(loaded)).length > 0 ? (
          <div className="st-note" role="status">
            {t("The overrides were relative to {old}: enter them again against {new}.", { old: loaded.reportingCurrency ?? "CHF", new: currency })}
          </div>
        ) : null}
        {rates.length === 0 ? <div className="st-hint">{t("No overrides: every conversion uses the reference rates above.")}</div> : null}
        {rates.map((r) => (
          <div key={r.key} className="st-rate">
            <span className="st-rate-one">1</span>
            <select
              className="input st-rate-code"
              aria-label={t("Currency")}
              value={r.code}
              disabled={!canEdit}
              onChange={(e) => {
                const code = e.target.value;
                // Start from the reference rate, so an override is a tweak rather than a lookup.
                const start = market[code] > 0 ? formatRate(market[code], "en") : "";
                setRates((list) => list.map((x) => (x.key === r.key ? { ...x, code, value: x.value.trim() === "" ? start : x.value } : x)));
                setSaved(null);
              }}
            >
              <option value="">—</option>
              {candidates(r.code).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <span className="st-rate-eq">=</span>
            <input
              className="input st-rate-value"
              inputMode="decimal"
              aria-label={t("Rate in {currency}", { currency })}
              aria-invalid={!isBlank(r) && !(parseRate(r.value) > 0)}
              value={r.value}
              disabled={!canEdit}
              onChange={(e) => {
                setRates((list) => list.map((x) => (x.key === r.key ? { ...x, value: e.target.value } : x)));
                setSaved(null);
              }}
            />
            <span className="st-rate-cur">{currency}</span>
            {canEdit ? (
              <button
                type="button"
                className="btn btn-ghost"
                aria-label={t("Remove the {currency} rate", { currency: r.code || "—" })}
                onClick={() => setRates((list) => list.filter((x) => x.key !== r.key))}
              >
                {t("Remove")}
              </button>
            ) : null}
          </div>
        ))}
        {canEdit && candidates("").length > 0 ? (
          <button
            type="button"
            className="btn btn-secondary st-rate-add"
            onClick={() => {
              setRates((list) => [...list, { key: nextKey, code: "", value: "" }]);
              setNextKey((k) => k + 1);
              setSaved(null);
            }}
          >
            {t("Add an override")}
          </button>
        ) : null}
        <div className="st-hint">{t("An override replaces the reference rate for that currency, e.g. a budget rate fixed by your finance team.")}</div>
      </div>

      {canEdit ? (
        <div className="st-actions">
          <button type="button" className="btn btn-primary" disabled={!dirty || invalidRate || duplicate || saving} onClick={() => void save()}>
            {saving ? t("Saving…") : t("Save the settings")}
          </button>
          <button type="button" className="btn btn-ghost" disabled={!dirty || saving} onClick={() => apply(loaded)}>
            {t("Discard changes")}
          </button>
          {invalidRate ? <span className="st-hint">{t("Each rate needs a currency and a number greater than 0.")}</span> : null}
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
