import { describe, expect, it } from "vitest";
import type { SavingsGoalProgressBody, SavingsResultsBody } from "../../../src/api/client";
import { translator } from "../../../src/i18n/translate";
import { buildGoalView, buildResultsView, day, invoiceEvidence, k, kk } from "../../../src/routes/savings/resultsViewModel";

const it_ = translator("it");

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** The "Da zero" scenario after Salesforce is validated and its case is open (golden 13). */
function zeroReport(): SavingsResultsBody {
  return {
    period: "fy",
    today: "2026-09-08",
    fiscalYear: 2026,
    headline: { contractual: 0, realized: 0, volume: 0, count: 0, volumeCount: 0, currency: "CHF" },
    kpis: {
      signedCount: 0,
      averagePerContract: null,
      provenPercent: null,
      realized: 0,
      contractual: 0,
      analysedSpend: 640000,
      validatedCount: 1,
      openObjectives: 0,
      openCount: 1,
      withoutObjectiveCount: 1,
      nextNotice: { contractId: "c1", supplier: "Salesforce", noticeDeadline: "2026-10-18", days: 40 },
    },
    months: MONTHS.map((label, i) => ({ year: 2026, month: i + 1, label: i === 0 ? "Jan 26" : label, future: i > 8, amount: 0, count: 0, items: [] })),
    peak: 0,
    bySupplier: [{ label: "Salesforce", contractual: 0, realized: 0, objectives: 0, n: 1 }],
    maxBySupplier: 0,
    levers: [],
    proof: [],
    openObjectives: [
      {
        contractId: "c1",
        caseId: "case-1",
        supplier: "Salesforce",
        category: "CRM",
        owner: "Luca Lamalfa",
        days: 40,
        noticeDeadline: "2026-10-18",
        spend: 640000,
        objectiveKind: null,
        objectivePercent: 0,
        targetAmount: null,
        hasCase: true,
      },
    ],
    contractual: [],
    volume: [],
  };
}

describe("Risultati view model", () => {
  it("formats like SQ.k and kk", () => {
    expect(k(0)).toBe("CHF 0k");
    expect(k(128000)).toBe("CHF 128k");
    expect(k(2400000)).toBe("CHF 2.4M");
    expect(kk(42000)).toBe("42k");
    expect(day("2026-10-18")).toBe("18 Oct 2026");
  });

  it("renders the zero scenario as the golden (13-risultati-senza-obiettivo)", () => {
    const rs = buildResultsView(zeroReport(), it_);

    expect(rs.nums.map((n) => [n.k, n.v])).toEqual([
      ["Risultati contrattuali · AF 2026", "CHF 0k"],
      ["Realizzati · da fatture", "CHF 0k"],
      ["Riduzioni di quantità", "CHF 0k"],
    ]);
    expect(rs.nums[0]!.sub).toBe("prezzo firmato vs contratto precedente, stesse quantità · 0 contratti");
    expect(rs.kpis.map((n) => [n.k, n.v, n.sub])).toEqual([
      ["Risultati firmati", "0", "nel periodo, esclusi i tagli di quantità"],
      ["Media per contratto", "—", "CHF / anno"],
      ["Provato da fatture", "—", "CHF 0k su CHF 0k"],
      ["Spesa analizzata", "CHF 640k", "1 contratti validati"],
      ["Obiettivi aperti", "CHF 0k", "1 rinnovi · 1 senza obiettivo"],
      ["Prossima disdetta", "40 g", "Salesforce · 18 Oct 2026"],
    ]);
    expect(rs.months[0]!.label).toBe("Jan 26");
    expect(rs.months.every((m) => m.h === "0.0%" && m.txt === "")).toBe(true);
    expect(rs.by).toEqual([{ label: "Salesforce", wReal: "0.0%", wSig: "0.0%", wObj: "0.0%", txt: "" }]);
    expect(rs.open).toEqual([
      expect.objectContaining({
        supplier: "Salesforce",
        est: "Nessun obiettivo ancora — si imposta nella pratica",
        status: "In negoziazione",
        tag: "tag-accent",
        days: "40 g alla disdetta",
        fg: "var(--color-accent)",
      }),
    ]);
  });

  it("shows proven and signed results with their evidence (scenario Con dati)", () => {
    const report = zeroReport();
    report.headline = { contractual: 42000, realized: 17500, volume: 14000, count: 1, volumeCount: 1, currency: "CHF" };
    report.contractual = [
      {
        id: "h1",
        signedOn: "2026-03-14",
        supplier: "Snowflake",
        kind: "price",
        note: "CHF 2.60 → 2.34 per credit, same volume (160,000 credits)",
        baseline: "2024 contract price · CHF 2.60 / credit",
        period: "12 months from 01 Apr 2026",
        method: "(old − new unit price) × contracted volume",
        evidence: "Snowflake_Capacity_2026_signed.pdf",
        amount: 42000,
        realized: true,
        realizedAmount: 17500,
        proof: { firstInvoice: "2026-04-01", lastInvoice: "2026-08-01", invoiceCount: 5, periodMonths: 12 },
      },
    ];
    report.proof = [{ resultId: "h1", supplier: "Snowflake", amount: 42000, realized: 17500 }];
    report.levers = [{ kind: "price", count: 1, amount: 42000 }];

    const rs = buildResultsView(report, it_);

    expect(rs.recent[0]!.realized).toBe("Realizzato finora: CHF 18k · Invoices Apr–Aug 2026 · 5 of 12 months");
    expect(rs.recent[0]!.when).toBe("14 Mar 2026");
    expect(rs.recent[0]!.amount).toBe("CHF 42k / anno");
    expect(rs.proof[0]).toEqual({ label: "Snowflake", w: "41.7%", txt: "18k / 42k" });
    expect(rs.levers[0]).toEqual({ label: "Prezzo unitario", n: "1 contratto", w: "100.0%", txt: "CHF 42k" });
    expect(invoiceEvidence({ firstInvoice: "2025-11-01", lastInvoice: "2026-01-01", invoiceCount: 3, periodMonths: null })).toBe(
      "Invoices Nov 2025–Jan 2026 · 3 months",
    );
  });

  it("renders the active goal as the golden (15-risultati-obiettivo-attivo)", () => {
    const label = { id: "IT contracts (software and cloud)" };
    const periodLabel = { id: "FY {year}", vars: { year: "2027" } };
    const progress: SavingsGoalProgressBody = {
      goal: {
        id: "g",
        text: "Voglio risparmiare il 20% sui contratti IT l’anno prossimo",
        pct: 20,
        annual: 128000,
        scope: { supplier: null, categories: ["CRM", "Productivity", "Cloud infrastructure"], contractIds: ["c1"] },
        period: "FY 2027",
        periodLabel,
        label,
        title: {
          id: "Save {target} on {scope} · {period}.",
          vars: { target: { id: "{pct}% — {amount} a year", vars: { pct: "20", amount: "CHF 128k" } }, scope: label, period: periodLabel },
        },
        createdAt: "2026-09-08T00:00:00Z",
      },
      spend: 640000,
      contractCount: 1,
      pct: 20,
      target: 128000,
      signed: 0,
      objectives: 0,
      gap: 128000,
      signedWidth: 0,
      objectivesWidth: 0,
      verdict: "gap",
      ifAllHit: 128000,
      rows: [
        {
          contractId: "c1",
          caseId: "case-1",
          supplier: "Salesforce",
          spend: 640000,
          hasCase: true,
          objectiveKind: null,
          objectivePercent: 0,
          objectiveAmount: 0,
          uplift: "7%",
          autoRenewal: true,
          ifHit: 128000,
          days: 40,
          noticeDeadline: "2026-10-18",
          primary: true,
        },
      ],
    };

    const gl = buildGoalView(progress, it_);

    expect(it_(progress.goal.title.id, progress.goal.title.vars as never)).toBe(
      "Risparmiare 20% — CHF 128k l’anno su contratti IT (software e cloud) · AF 2027.",
    );
    expect(gl.title).toBe("Verso il tuo obiettivo: 20% · CHF 128k l’anno · AF 2027");
    expect(gl.scope).toBe("contratti IT (software e cloud) · CHF 640k di spesa annua su 1 contratti");
    expect(gl.kpis.map((n) => [n.k, n.v, n.sub])).toEqual([
      ["Obiettivo", "CHF 128k", "20% di CHF 640k · contratti IT (software e cloud)"],
      ["Firmato finora", "CHF 0k", "nessun contratto firmato in questo ambito"],
      ["Obiettivi nelle pratiche", "CHF 0k", "target che hai fissato nei rinnovi aperti — non stime"],
      ["Ancora da coprire", "CHF 128k", "con i rinnovi in arrivo qui sotto"],
    ]);
    expect(gl.gapTxt).toBe("mancano CHF 128k");
    expect(gl.verdict).toBe(
      "Per coprire CHF 128k servono target più alti nei rinnovi in arrivo: il 20% su ciascun contratto qui sotto vale CHF 128k l’anno. Il target di ogni pratica si fissa alla domanda 3 dell’intervista.",
    );
    expect(gl.rows[0]).toEqual(
      expect.objectContaining({
        line: "Pratica aperta · obiettivo da fissare · aumento 7% · rinnovo automatico",
        ifHit: "CHF 128k / anno",
        cta: "Apri la pratica",
        cls: "btn-primary",
      }),
    );
  });
});
