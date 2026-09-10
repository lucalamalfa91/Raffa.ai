using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>Outcome of a successful <see cref="ContractCorrectionService.CorrectAsync"/> call.</summary>
public sealed record ContractCorrectionResult(
    EntityId ContractId,
    int VersionNumber,
    IReadOnlyList<string> CorrectedFields,
    DateTimeOffset CorrectedAt);
