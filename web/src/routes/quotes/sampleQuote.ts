import type { UploadQuoteFields } from "../../api/client";
import { buildSamplePdf } from "../documents/sampleDocument";

/**
 * The Quote check landing's sample proposal (`contigo-v2/markup.html` "QUOTE CHECK" block: "or use
 * the sample: Databricks proposal Q-88213"). Built with the same minimal-but-real PDF writer the
 * Documents screen's own samples use (`../documents/sampleDocument.ts#buildSamplePdf`), so the
 * file carries extractable page text -- three priced line items -- and goes through the exact
 * `apiClient.uploadQuote()` call a dropped file would. Whatever the real `QuoteExtractionPipeline`
 * returns for it is the honest answer, never a scripted one: on an environment whose AI gateway is
 * the deterministic fixture, that may well be fewer lines than the text names.
 */
export const SAMPLE_QUOTE_LABEL = "Databricks proposal Q-88213";
export const SAMPLE_QUOTE_FILE_NAME = "Databricks_Proposal_Q-88213.pdf";

/** Upload-time metadata the benchmark match needs (supplier · currency · geography) -- the sample's own, not guessed from the file. */
export const SAMPLE_QUOTE_FIELDS: UploadQuoteFields = { supplier: "Databricks", currency: "CHF", geography: "CH" };

const SAMPLE_QUOTE_PAGES: readonly string[] = [
  [
    "DATABRICKS, INC.",
    "Commercial proposal Q-88213",
    "Prepared for: Helvetia Foods AG, Zurich (CH)",
    "Currency: CHF. Valid until 30 September 2026. Term: 12 months.",
    "",
    "Line items",
    "1. Premium DBU - committed | SKU DBX-PREM-DBU | Qty 600000 | Unit price 0.55 | Annual 330000",
    "2. SQL Serverless DBU | SKU DBX-SQL-SRVLESS | Qty 200000 | Unit price 0.70 | Annual 140000",
    "3. Enterprise Support | SKU DBX-ENT-SUPPORT | Qty 1 | Unit price 50000 | Annual 50000",
    "",
    "Total annual commitment: CHF 520,000",
    "Payment terms: annual in advance, net 30.",
    "Pricing is a supplier proposal, not a signed contract.",
  ].join("\n"),
];

export function createSampleQuoteFile(): File {
  return new File([buildSamplePdf(SAMPLE_QUOTE_PAGES)], SAMPLE_QUOTE_FILE_NAME, { type: "application/pdf" });
}
