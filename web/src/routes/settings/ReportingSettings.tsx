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

/** The body's budget rates, read defensively (the generated type cannot describe a map). */
function ratesOf(body: WorkspaceSettingsBody | null): Record<string, number> {
  const raw = body?.budgetRates;
  if (!raw || typeof raw !== "object") return {};
  return Object.fromEntries(Object.entries(raw as Record<string, unknown>).filter((e): e is [string, number] => typeof e[1] === "number" && e[1] > 0));
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
 * and the budget rates used to convert contracts in other currencies. All three are the workspace's
 * (`/api/workspaces/{id}/settings`); only an Admin changes them. Raffa never converts at a guessed
 * rate: a contract in a currency without a rate stays out of the totals and is listed.
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

  const invalidRate = rates.some((r) => !r.code || !(parseRate(r.value) > 0));
  const duplicate = new Set(rates.map((r) => r.code)).size !== rates.length;
  const nextRates = Object.fromEntries(rates.filter((r) => r.code).map((r) => [r.code, parseRate(r.value)]));
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
      setSaved(t("Saved. Spesa e risparmi uses these settings from now on."));
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
        {t("How Spesa e risparmi reports money: the currency of every total, when your fiscal year starts, and the budget rates for contracts in other currencies.")}
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

      <div className="st-rates" role="group" aria-labelledby="st-rates-h">
        <div id="st-rates-h" className="st-label">
          {t("Budget rates")}
        </div>
        {currencyChanged && Object.keys(ratesOf(loaded)).length > 0 ? (
          <div className="st-note" role="status">
            {t("The budget rates were relative to {old}: enter them again against {new}.", { old: loaded.reportingCurrency ?? "CHF", new: currency })}
          </div>
        ) : null}
        {rates.length === 0 ? <div className="st-hint">{t("No budget rates: contracts in other currencies stay out of the totals, listed apart.")}</div> : null}
        {rates.map((r) => (
          <div key={r.key} className="st-rate">
            <span className="st-rate-one">1</span>
            <select
              className="input st-rate-code"
              aria-label={t("Currency")}
              value={r.code}
              disabled={!canEdit}
              onChange={(e) => {
                setRates((list) => list.map((x) => (x.key === r.key ? { ...x, code: e.target.value } : x)));
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
              aria-invalid={!(parseRate(r.value) > 0)}
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
            {t("Add a budget rate")}
          </button>
        ) : null}
        <div className="st-hint">{t("Raffa never converts at a guessed rate: these are yours, used as you enter them.")}</div>
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
