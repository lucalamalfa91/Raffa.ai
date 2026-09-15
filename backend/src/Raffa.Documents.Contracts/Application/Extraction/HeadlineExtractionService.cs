using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Fast-identity pass: one <see cref="IAiGateway.ExtractAsync"/> call that extracts only the
/// identity-row fields (supplier, type, status, endDate, cancellationDeadline, optional title).
/// Runs after the content gate has admitted the document and before the full 7-stage
/// <see cref="StagedExtractionService"/> run, so Portfolio and Renewals show meaningful rows
/// within seconds of upload.
///
/// <para>
/// Key differences from <see cref="StagedExtractionService"/>:
/// <list type="bullet">
/// <item>ONE model call instead of seven.</item>
/// <item><c>ProvisionalSupplierName</c> is always written regardless of confidence — the plan's
/// "always persist provisional_supplier_name even below 0.8 confidence".</item>
/// <item>Sets <see cref="Contract.IdentityState"/> to <see cref="ContractIdentityState.Provisional"/>
/// (the full enrich sets it to <see cref="ContractIdentityState.Official"/>).</item>
/// <item>Does not embed, does not create <see cref="ExtractionEvidence"/> rows — those belong to
/// the 7-stage pipeline.</item>
/// </list>
/// </para>
/// </summary>
public sealed class HeadlineExtractionService(
    DocumentsContractsDbContext dbContext,
    IAiGateway aiGateway)
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Runs the headline extraction against <paramref name="pages"/>, updates the contract
    /// linked to <paramref name="document"/> with the provisional identity fields, and persists.
    /// </summary>
    public async Task RunAsync(
        TenantId tenantId,
        Document document,
        IReadOnlyList<DocumentPageText> pages,
        ContractDocumentType classifiedType,
        CancellationToken cancellationToken = default)
    {
        if (document.ContractId is null)
        {
            return;
        }

        var contract = await dbContext.Contracts
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.Id == document.ContractId,
                cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return;
        }

        var documentText = BuildText(pages);
        var schema = BuildHeadlineSchema();

        var result = await aiGateway
            .ExtractAsync(new AiExtractionRequest("headline", documentText, schema), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            // Headline failure is non-fatal: the contract remains provisional with the filename
            // as display name; the enrich pass will fill in the full details later.
            return;
        }

        ApplyHeadline(contract, classifiedType, result.Value.PayloadJson);
        contract.IdentityState = ContractIdentityState.Provisional;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyHeadline(Contract contract, ContractDocumentType classifiedType, string payloadJson)
    {
        HeadlinePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<HeadlinePayload>(payloadJson, PayloadOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (payload is null)
        {
            return;
        }

        // Type: use classify verdict (from admission gate) as the source of truth; headline
        // type field is a secondary hint only used when the contract was not yet classified.
        contract.Type = classifiedType;

        // Title: if the model found a better title than the filename, overwrite DisplayName.
        // Human corrections (Version > 1) are left untouched.
        if (!string.IsNullOrWhiteSpace(payload.Title) && contract.Version == 1)
        {
            contract.DisplayName = payload.Title.Trim();
        }

        // Status: overwrite the bootstrap "processing" placeholder.
        if (!string.IsNullOrWhiteSpace(payload.Status))
        {
            contract.Status = payload.Status.Trim();
        }

        // End date.
        if (TryParseDate(payload.EndDate, out var endDate))
        {
            contract.EndDate = endDate;
        }

        // Cancellation deadline.
        if (TryParseDate(payload.CancellationDeadline, out var cancelDeadline))
        {
            contract.CancellationDeadline = cancelDeadline;
        }

        // Supplier — always write regardless of confidence (plan requirement).
        if (!string.IsNullOrWhiteSpace(payload.Supplier))
        {
            contract.ProvisionalSupplierName = payload.Supplier.Trim();
        }
    }

    private static string BuildText(IReadOnlyList<DocumentPageText> pages)
    {
        // First three pages is enough for a headline identification — legal name, type,
        // effective date are almost always on page 1-3.
        var sb = new System.Text.StringBuilder();
        foreach (var page in pages.Take(3))
        {
            sb.Append("[[PAGE ").Append(page.PageNumber).Append("]]\n");
            sb.Append(page.Text);
            sb.Append("\n\n");
        }

        return sb.ToString();
    }

    private static string BuildHeadlineSchema()
    {
        // Strict structured output schema for the headline pass. One object, six fields.
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "supplier", "title", "type", "status", "endDate", "cancellationDeadline" },
            properties = new
            {
                supplier = new
                {
                    type = new[] { "string", "null" },
                    description = "The supplier's (vendor's, fornitore's) legal name exactly as written in the document — the party that provides goods or services, never the customer. Null when not found.",
                },
                title = new
                {
                    type = new[] { "string", "null" },
                    description = "The contract's own title or heading as it appears in the document (e.g. 'Master Service Agreement', 'Software License Agreement'). Null when the document has no explicit title.",
                },
                type = new
                {
                    type = new[] { "string", "null" },
                    description = "Contract type as one of: MSA, OrderForm, Amendment, SOW, RenewalLetter, Other. Null when unclear.",
                },
                status = new
                {
                    type = new[] { "string", "null" },
                    description = "Contract status as one short English word: active, draft, expired, terminated. Null when unclear.",
                },
                endDate = new
                {
                    type = new[] { "string", "null" },
                    description = "The date the current term ends, YYYY-MM-DD format. Null when not stated.",
                },
                cancellationDeadline = new
                {
                    type = new[] { "string", "null" },
                    description = "The last date a termination or non-renewal notice can be sent, YYYY-MM-DD. Null when not stated.",
                },
            },
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = false });
    }

    private static bool TryParseDate(string? value, out DateOnly result) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    // Payload shape — nullable throughout (a real model may return malformed JSON).
    private sealed record HeadlinePayload(
        [property: JsonPropertyName("supplier")] string? Supplier,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("endDate")] string? EndDate,
        [property: JsonPropertyName("cancellationDeadline")] string? CancellationDeadline);
}
