using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// The <see cref="Contract"/> fields each scalar stage may propose: allow-listed both in the store and
/// in the JSON Schema's <c>enum</c>. The order is the schema <c>enum</c> order (byte parity of the
/// request sent to the model). <see cref="Contract.Type"/> is deliberately absent: that is
/// Classification's field. <see cref="ExtractionFieldNames.Supplier"/> is in the metadata list even
/// though it maps to no <see cref="Contract"/> scalar.
/// </summary>
public static class ExtractionStageFields
{
    public static readonly IReadOnlyList<string> Metadata =
        [ExtractionFieldNames.Supplier, "currency", "governingLaw", "status"];

    public static readonly IReadOnlyList<string> CommercialTerms =
        ["annualSpend", "totalContractValue", "paymentTerms"];

    public static readonly IReadOnlyList<string> DatesAndRenewalTerms =
        ["startDate", "endDate", "effectiveDate", "cancellationDeadline", "autoRenewal", "renewalTermMonths", "noticePeriodDays"];

    /// <summary>The allow-list of a scalar stage; throws for the four list stages.</summary>
    public static IReadOnlyList<string> For(ExtractionStage stage) => stage switch
    {
        ExtractionStage.Metadata => Metadata,
        ExtractionStage.CommercialTerms => CommercialTerms,
        ExtractionStage.DatesAndRenewalTerms => DatesAndRenewalTerms,
        _ => throw new InvalidOperationException($"Stage {stage} has no scalar field allow-list."),
    };
}
