import { describe, expect, it } from "vitest";
import {
  buildEvidenceActions,
  describeEvidence,
  evidenceRowLabel,
  groupCitations,
  MAX_EVIDENCE_ACTIONS,
  splitCitationTitle,
} from "../../../../src/routes/ask/reply/evidenceGrouping";
import type { ReplyAction, ReplyCitation } from "../../../../src/routes/ask/reply/replyTypes";

const SALESFORCE = "11111111-1111-1111-1111-111111111111";
const MICROSOFT = "22222222-2222-2222-2222-222222222222";
const GOOGLE = "33333333-3333-3333-3333-333333333333";

function citation(overrides: Partial<ReplyCitation> & Pick<ReplyCitation, "n" | "title">): ReplyCitation {
  return {
    corpus: "tenant",
    subtitle: "",
    snippet: `${overrides.title} ends on 2027-01-09, auto-renews unless notice is given.`,
    href: null,
    contractId: null,
    ...overrides,
  };
}

/** The "Where do I spend more?" reply from the screenshots: four contract facts, each its own card
 * before, plus the aggregate the planner adds on top. */
function spendReply(): ReplyCitation[] {
  return [
    citation({ n: 1, title: "Google Cloud · OrderForm", contractId: GOOGLE, href: `/contracts/${GOOGLE}` }),
    citation({ n: 2, title: "Microsoft · OrderForm", contractId: MICROSOFT, href: `/contracts/${MICROSOFT}` }),
    citation({ n: 3, title: "Salesforce · MSA", contractId: SALESFORCE, href: `/contracts/${SALESFORCE}` }),
    citation({ n: 4, title: "Salesforce — criticality 72/100", subtitle: "driven by renewal urgency", contractId: SALESFORCE, href: `/contracts/${SALESFORCE}` }),
    citation({ n: 5, title: "Annual spend total", corpus: "calc", snippet: "Total annual spend across 4 validated contracts." }),
  ];
}

describe("splitCitationTitle", () => {
  it("reads the supplier off 'Supplier · Type' and 'Supplier — lever'", () => {
    expect(splitCitationTitle("Salesforce · MSA")).toEqual({ head: "Salesforce", tail: "MSA" });
    expect(splitCitationTitle("Salesforce — criticality 72/100")).toEqual({ head: "Salesforce", tail: "criticality 72/100" });
  });

  it("keeps a title with no separator whole", () => {
    expect(splitCitationTitle("Annual spend total")).toEqual({ head: "Annual spend total", tail: null });
  });

  it("does not split on a plain hyphen inside a name", () => {
    expect(splitCitationTitle("Hewlett-Packard · SOW")).toEqual({ head: "Hewlett-Packard", tail: "SOW" });
  });
});

describe("groupCitations", () => {
  it("files every contract citation under its supplier, keyed by contract id", () => {
    const groups = groupCitations(spendReply());

    expect(groups.total).toBe(5);
    expect(groups.contracts.map((group) => group.title)).toEqual(["Google Cloud", "Microsoft", "Salesforce"]);
    expect(groups.contracts[2].rows.map((row) => row.n)).toEqual([3, 4]);
    expect(groups.contracts[2].href).toBe(`/contracts/${SALESFORCE}`);
  });

  it("never merges two contracts of the same supplier", () => {
    const groups = groupCitations([
      citation({ n: 1, title: "Salesforce · MSA", contractId: SALESFORCE, href: `/contracts/${SALESFORCE}` }),
      citation({ n: 2, title: "Salesforce · OrderForm", contractId: MICROSOFT, href: `/contracts/${MICROSOFT}` }),
    ]);

    expect(groups.contracts).toHaveLength(2);
    expect(groups.contracts.map((group) => group.contractId)).toEqual([SALESFORCE, MICROSOFT]);
  });

  it("sends market citations to the market section and id-less calc/raffa citations to Raffa", () => {
    const groups = groupCitations([
      ...spendReply(),
      citation({ n: 6, title: "ServiceNow · ITSM · CH", corpus: "market", snippet: "P50 EUR 120 per user." }),
      citation({ n: 7, title: "Renewals", corpus: "raffa", href: "/renewals", snippet: "Deadlines and the action for each." }),
    ]);

    expect(groups.market.map((row) => row.n)).toEqual([6]);
    expect(groups.raffa.map((row) => row.n)).toEqual([5, 7]);
  });

  it("groups an id-less tenant citation by the supplier half of its title, without a 360 link", () => {
    const groups = groupCitations([
      citation({ n: 1, title: "Allianz · Liability clause", subtitle: "p.12 §8.4" }),
      citation({ n: 2, title: "Allianz · Notice clause", subtitle: "p.3 §2" }),
    ]);

    expect(groups.contracts).toHaveLength(1);
    expect(groups.contracts[0]).toMatchObject({ title: "Allianz", contractId: null, href: null });
    expect(groups.contracts[0].rows).toHaveLength(2);
  });
});

describe("evidenceRowLabel", () => {
  it("keeps only what the row adds beyond the supplier", () => {
    expect(evidenceRowLabel(citation({ n: 1, title: "Salesforce · MSA" }), "Salesforce")).toBe("MSA");
    expect(evidenceRowLabel(citation({ n: 1, title: "Salesforce — criticality 72/100" }), "Salesforce")).toBe("criticality 72/100");
    expect(evidenceRowLabel(citation({ n: 1, title: "Salesforce" }), "Salesforce")).toBeNull();
  });

  it("keeps the whole title when it does not start with the group's supplier", () => {
    expect(evidenceRowLabel(citation({ n: 1, title: "Similar type · Microsoft · OrderForm" }), "Salesforce")).toBe(
      "Similar type · Microsoft · OrderForm",
    );
  });
});

describe("describeEvidence", () => {
  it("names the suppliers and counts every source", () => {
    const summary = describeEvidence(
      groupCitations([...spendReply(), citation({ n: 6, title: "ServiceNow · ITSM", corpus: "market" })]),
    );

    expect(summary.title).toBe("Google Cloud, Microsoft, Salesforce");
    expect(summary.subtitle).toBe("3 contracts · 1 market record · 1 Raffa item");
  });

  it("caps the named suppliers and says how many more there are", () => {
    const groups = groupCitations(
      ["A", "B", "C", "D", "E"].map((name, index) =>
        citation({ n: index + 1, title: `${name} · MSA`, contractId: `id-${name}`, href: `/contracts/id-${name}` }),
      ),
    );

    expect(describeEvidence(groups).title).toBe("A, B, C +2");
  });

  it("falls back to the section that has rows", () => {
    expect(describeEvidence(groupCitations([citation({ n: 1, title: "ServiceNow · ITSM", corpus: "market" })])).title).toBe("Market");
    expect(describeEvidence(groupCitations([citation({ n: 1, title: "Renewals", corpus: "raffa" })])).title).toBe("Raffa");
  });
});

describe("buildEvidenceActions", () => {
  const backend: ReplyAction[] = [
    { label: "Portfolio →", href: "/contracts", kind: "primary" },
    { label: "Renewals →", href: "/renewals", kind: "secondary" },
    { label: "Savings →", href: "/savings", kind: "secondary" },
    { label: "Quote check →", href: "/quotes", kind: "secondary" },
  ];

  it("leads with Portfolio filtered to the highlighted contracts, drops the bare Portfolio link and caps at three", () => {
    const actions = buildEvidenceActions(groupCitations(spendReply()), backend);

    expect(actions).toHaveLength(MAX_EVIDENCE_ACTIONS);
    expect(actions[0]).toEqual({
      label: "Portfolio · these 3 contracts →",
      href: `/contracts?ids=${GOOGLE},${MICROSOFT},${SALESFORCE}`,
      kind: "primary",
    });
    expect(actions.map((action) => action.href)).toEqual([`/contracts?ids=${GOOGLE},${MICROSOFT},${SALESFORCE}`, "/renewals", "/savings"]);
    expect(actions.slice(1).every((action) => action.kind === "secondary")).toBe(true);
  });

  it("leads with the one contract's own Contract 360 when a single contract is cited", () => {
    const actions = buildEvidenceActions(
      groupCitations([citation({ n: 1, title: "Salesforce · MSA", contractId: SALESFORCE, href: `/contracts/${SALESFORCE}` })]),
      [{ label: "Open Contract 360", href: `/contracts/${SALESFORCE}`, kind: "primary" }, ...backend],
    );

    expect(actions[0]).toEqual({ label: "Salesforce · Contract 360 →", href: `/contracts/${SALESFORCE}`, kind: "primary" });
    // The backend's identical Contract 360 link is folded, the bare Portfolio link is kept: it is
    // not superseded when only one contract is on the card.
    expect(actions.map((action) => action.href)).toEqual([`/contracts/${SALESFORCE}`, "/contracts", "/renewals"]);
  });

  it("passes the backend's actions through, first one primary, when nothing resolves to a contract", () => {
    const actions = buildEvidenceActions(groupCitations([citation({ n: 1, title: "Renewals", corpus: "raffa", href: "/renewals" })]), backend);

    expect(actions.map((action) => action.href)).toEqual(["/contracts", "/renewals", "/savings"]);
    expect(actions[0].kind).toBe("primary");
  });

  it("returns nothing when there is neither a contract nor a backend action", () => {
    expect(buildEvidenceActions(groupCitations([citation({ n: 1, title: "Annual spend total", corpus: "calc" })]), [])).toEqual([]);
  });
});
