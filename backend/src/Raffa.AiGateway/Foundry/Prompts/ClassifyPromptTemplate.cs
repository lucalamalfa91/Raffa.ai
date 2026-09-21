using System.Text.Json;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiGateway.Foundry.Prompts;

/// <summary>
/// Versioned prompt + JSON schema for the `classify` role (ADR-004: fixed label set;
/// ADR-024: "classify is reused for the document admission gate and for the Ask domain gate with
/// fixed label sets"). The label list is generated from <see cref="AiDocumentType"/> itself, never
/// duplicated by hand, so the fixed set the model is constrained to always matches the enum this
/// gateway's own contract exposes. Documents arrive in any language (Italian supplier contracts are
/// the primary case), so each label carries its common Italian/German names.
/// </summary>
public static class ClassifyPromptTemplate
{
    /// <summary>Bump when the prompt or schema text below changes — recorded in every
    /// <see cref="AiCallMetadata.PromptVersion"/> this role produces.</summary>
    public const string Version = "foundry-classify-v3";

    private static readonly string[] Labels = Enum.GetNames<AiDocumentType>();

    private static readonly IReadOnlyDictionary<AiDocumentType, string> Glosses = new Dictionary<AiDocumentType, string>
    {
        [AiDocumentType.Msa] = "master, framework or umbrella agreement that governs a supplier relationship: master services agreement, master subscription agreement, enterprise or software license agreement, cloud services or cloud commitment agreement, SaaS terms, general terms and conditions (contratto quadro, accordo quadro, contratto di licenza, condizioni generali, Rahmenvertrag, Lizenzvertrag)",
        [AiDocumentType.OrderForm] = "order form, order schedule, purchase order or subscription order that buys specific products, quantities or seats, usually under a master agreement (ordine, modulo d'ordine, ordine di acquisto, Bestellung, Bestellschein)",
        [AiDocumentType.Sow] = "statement of work, work order or service description that defines deliverables and activities (capitolato, descrizione dei servizi, ordine di lavoro, Leistungsbeschreibung)",
        [AiDocumentType.Amendment] = "amendment or addendum to an existing contract (atto integrativo, addendum, Nachtrag)",
        [AiDocumentType.Quote] = "quotation or commercial proposal (offerta, preventivo, Angebot)",
        [AiDocumentType.Invoice] = "invoice (fattura, Rechnung)",
        [AiDocumentType.PriceList] = "price list or rate card (listino prezzi, Preisliste)",
        [AiDocumentType.Nda] = "non-disclosure or confidentiality agreement (accordo di riservatezza, Geheimhaltungsvereinbarung)",
        [AiDocumentType.Dpa] = "data processing agreement (accordo sul trattamento dei dati, nomina a responsabile, Auftragsverarbeitungsvertrag)",
        [AiDocumentType.Other] = "none of the above (for example a recipe, an article, a CV, a personal letter, a manual)",
    };

    public static readonly string SystemPrompt =
        "You classify one business document into exactly one of these types: " +
        string.Join(", ", Labels) + ". Meaning of each type, with common non-English names: " +
        string.Join("; ", Enum.GetValues<AiDocumentType>().Select(t => $"{t} = {Glosses[t]}")) + ". " +
        "The document may be written in any language; classify it on its meaning, never on its " +
        "language, and never on a file name. Decide by what the document does, not by its title: a " +
        "framework agreement that also lists prices is still an Msa; a signed order under a framework " +
        "is an OrderForm; a document that is both a master agreement and its first order schedule is " +
        "the type of the part that governs the relationship (Msa). Use Other only when the document " +
        "is clearly none of the listed types. Report confidence as a number between 0 and 1: your " +
        "probability that the chosen type is correct. Respond with strict JSON matching the given " +
        "schema only: no prose, no markdown fences.";

    /// <summary>
    /// <c>{ documentType: enum(AiDocumentType names), confidence: number }</c>, strict-mode
    /// compliant (every property required, <c>additionalProperties: false</c>, no numeric bounds —
    /// the 0..1 range lives in the description). Parsed once at type-initialization time and never
    /// disposed — the underlying <see cref="JsonDocument"/> stays reachable for the process lifetime
    /// because <see cref="Schema"/> (a <see cref="JsonElement"/>) keeps its own internal reference to
    /// it (see <see cref="AnswerPersonaPrompt.Schema"/> for the same convention).
    /// </summary>
    public static readonly JsonElement Schema = JsonDocument.Parse(
        $$"""
        {
          "type": "object",
          "properties": {
            "documentType": {
              "type": "string",
              "enum": {{JsonSerializer.Serialize(Labels)}},
              "description": "The single type that best describes the whole document."
            },
            "confidence": {
              "type": "number",
              "description": "Probability between 0 and 1 that documentType is correct."
            }
          },
          "required": ["documentType", "confidence"],
          "additionalProperties": false
        }
        """).RootElement;
}
