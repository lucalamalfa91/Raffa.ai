import type { ContractListRowV4Body } from "../../api/client";

/** The kinds of digital content Raffa builds in the side panel from the validated contracts. */
export type ArtifactKind = "dashboard" | "kpi" | "document";

export interface Tile {
  label: string;
  value: string;
  hint?: string;
  tone?: "urgent" | "good";
}
export interface BarChart {
  title: string;
  /** Plain bars (value + label); the unit is part of `format`. */
  items: Array<{ label: string; value: number; display: string }>;
}
export interface DataTable {
  title: string;
  columns: string[];
  rows: string[][];
}
export interface Artifact {
  id: string;
  kind: ArtifactKind;
  title: string;
  subtitle: string;
  tiles: Tile[];
  charts: BarChart[];
  tables: DataTable[];
  /** Document body (markdown subset: headings, lists, paragraphs, bold). */
  markdown: string;
}

type T = (id: string, vars?: Record<string, string | number>) => string;

const chfK = (v: number) => `CHF ${Math.round(v / 1000).toLocaleString("en-US")}k`;
const WINDOWS: Array<{ id: string; max: number }> = [
  { id: "Up to 30 days", max: 30 },
  { id: "31–90 days", max: 90 },
  { id: "91–180 days", max: 180 },
  { id: "181–365 days", max: 365 },
  { id: "Later", max: Number.POSITIVE_INFINITY },
];

/** Rows that have a notice deadline ahead (dates not read, or passed, are counted apart). */
function aheadRows(rows: readonly ContractListRowV4Body[]) {
  return rows.filter((r) => r.noticeDays >= 0 && r.noticeDays < 100000).sort((a, b) => a.noticeDays - b.noticeDays);
}

/**
 * Everything is computed from the validated contracts' own figures — nothing is estimated and no model
 * writes a number. A contract whose dates were not read, or whose notice deadline passed, is counted in
 * its own KPI instead of being guessed at.
 */
export function buildArtifact(kind: ArtifactKind, rows: readonly ContractListRowV4Body[], t: T, today: string): Artifact {
  const ahead = aheadRows(rows);
  const total = rows.reduce((s, r) => s + (r.annualValue || 0), 0);
  const within90 = ahead.filter((r) => r.noticeDays <= 90);
  const valueWithin180 = ahead.filter((r) => r.noticeDays <= 180).reduce((s, r) => s + (r.annualValue || 0), 0);
  const passed = rows.filter((r) => r.noticeDays < 0).length;
  const autoRenewed = rows.filter((r) => r.termStatus === "rolledOver").length;
  const next = ahead[0];

  const tiles: Tile[] = [
    { label: t("Validated contracts"), value: String(rows.length) },
    { label: t("Annual value"), value: chfK(total), hint: t("sum of the contracts' yearly values") },
    {
      label: t("Notice due in 90 days"),
      value: String(within90.length),
      hint: within90.length ? within90.map((r) => r.supplier).slice(0, 3).join(", ") : undefined,
      tone: within90.length ? "urgent" : "good",
    },
    {
      label: t("Value up for notice in 180 days"),
      value: chfK(valueWithin180),
      hint: t("yearly value of contracts whose notice deadline is within 180 days"),
    },
    {
      label: t("Next notice deadline"),
      value: next ? `${next.noticeDays} ${t("d⟦days⟧")}` : "—",
      hint: next ? `${next.supplier} · ${next.noticeDeadline}` : undefined,
      tone: next && next.noticeDays <= 30 ? "urgent" : undefined,
    },
    { label: t("Renewed by themselves"), value: String(autoRenewed), hint: passed ? t("{n} notice deadlines already passed", { n: passed }) : undefined },
  ];

  const bySupplier = new Map<string, number>();
  for (const r of rows) bySupplier.set(r.supplier, (bySupplier.get(r.supplier) ?? 0) + (r.annualValue || 0));
  const top = [...bySupplier.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8);
  const buckets = WINDOWS.map((w, i) => {
    const min = i === 0 ? 0 : WINDOWS[i - 1]!.max + 1;
    const count = ahead.filter((r) => r.noticeDays >= min && r.noticeDays <= w.max).length;
    return { label: t(w.id), value: count, display: String(count) };
  });

  const charts: BarChart[] = [
    { title: t("Annual value by supplier"), items: top.map(([label, value]) => ({ label, value, display: chfK(value) })) },
    { title: t("Notice deadlines by window"), items: buckets },
  ];
  const tables: DataTable[] = [
    {
      title: t("Next notice deadlines"),
      columns: [t("Supplier"), t("Contract"), t("Notice by"), t("Days"), t("Annual value")],
      rows: ahead.slice(0, 10).map((r) => [r.supplier, r.name.split(" — ")[0] ?? r.name, r.noticeDeadline, String(r.noticeDays), chfK(r.annualValue || 0)]),
    },
  ];

  const lines: string[] = [];
  lines.push(`**${t("Where we stand")}**`);
  lines.push(
    t("{n} validated contracts worth {value} a year. {urgent} have a notice deadline within 90 days.", {
      n: rows.length,
      value: chfK(total),
      urgent: within90.length,
    }),
  );
  if (next) lines.push(t("The next notice deadline is {supplier} on {date}, in {n} days.", { supplier: next.supplier, date: next.noticeDeadline, n: next.noticeDays }));
  lines.push("");
  lines.push(`**${t("To decide soon")}**`);
  for (const r of within90.slice(0, 6)) lines.push(`- ${r.supplier} · ${r.noticeDeadline} · ${r.noticeDays} ${t("d⟦days⟧")} · ${chfK(r.annualValue || 0)}`);
  if (!within90.length) lines.push(`- ${t("No notice deadline within 90 days.")}`);
  lines.push("");
  lines.push(`**${t("Biggest contracts")}**`);
  for (const [label, value] of top.slice(0, 5)) lines.push(`- ${label} · ${chfK(value)}`);
  if (autoRenewed || passed) {
    lines.push("");
    lines.push(`**${t("Already renewed or past notice")}**`);
    if (autoRenewed) lines.push(`- ${t("{n} contracts renewed by themselves", { n: autoRenewed })}`);
    if (passed) lines.push(`- ${t("{n} notice deadlines already passed", { n: passed })}`);
  }

  const title = kind === "kpi" ? t("Contract KPIs") : kind === "dashboard" ? t("Contracts dashboard") : t("Portfolio brief");
  return {
    id: `${kind}-${Date.now()}`,
    kind,
    title,
    subtitle: t("From the validated contracts · {date}", { date: today }),
    tiles: kind === "document" ? [] : tiles,
    charts: kind === "dashboard" ? charts : [],
    tables: kind === "dashboard" ? tables : [],
    markdown: kind === "document" ? lines.join("\n") : "",
  };
}

/** The artifact as plain markdown: what Copy and Download hand to the user. */
export function artifactToMarkdown(a: Artifact): string {
  const out: string[] = [`# ${a.title}`, a.subtitle, ""];
  if (a.tiles.length) {
    for (const x of a.tiles) out.push(`- **${x.label}**: ${x.value}${x.hint ? ` (${x.hint})` : ""}`);
    out.push("");
  }
  for (const c of a.charts) {
    out.push(`## ${c.title}`);
    for (const i of c.items) out.push(`- ${i.label}: ${i.display}`);
    out.push("");
  }
  for (const tb of a.tables) {
    out.push(`## ${tb.title}`, `| ${tb.columns.join(" | ")} |`, `| ${tb.columns.map(() => "---").join(" | ")} |`);
    for (const r of tb.rows) out.push(`| ${r.join(" | ")} |`);
    out.push("");
  }
  if (a.markdown) out.push(a.markdown);
  return out.join("\n").trim() + "\n";
}

/** The first table (or the chart data) as CSV, for a spreadsheet. */
export function artifactToCsv(a: Artifact): string | null {
  const esc = (v: string) => (/[",\n;]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
  const table = a.tables[0];
  if (table) return [table.columns, ...table.rows].map((r) => r.map(esc).join(",")).join("\n") + "\n";
  const chart = a.charts[0];
  if (chart) return [["label", "value"], ...chart.items.map((i) => [i.label, String(i.value)])].map((r) => r.map(esc).join(",")).join("\n") + "\n";
  return null;
}

/** Which artifact a typed request asks for ("create a dashboard", "fammi i KPI", "scrivi un brief"), or null. */
export function artifactRequest(text: string): ArtifactKind | null {
  const s = text.toLowerCase();
  const verb = /\b(crea|creami|create|build|make|genera|generate|fammi|prepara|costruisci|write|scrivi|show me|mostrami)\b/.test(s);
  if (!verb) return null;
  if (/\b(dashboard|cruscotto)\b/.test(s)) return "dashboard";
  if (/\b(kpi|indicatori)\b/.test(s)) return "kpi";
  if (/\b(brief|report|documento|document|riepilogo)\b/.test(s)) return "document";
  return null;
}
