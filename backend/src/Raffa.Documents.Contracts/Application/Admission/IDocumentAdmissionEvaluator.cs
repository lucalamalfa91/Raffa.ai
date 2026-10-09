using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Admission;

/// <summary>
/// The content gate in front of extraction ("only contracts get in", R-DOC-03): parse in memory,
/// require enough readable text, classify, and admit or refuse. The module's extraction handler
/// depends on this port rather than on the gate's implementation, so the implementation can live
/// outside the module. <see cref="DocumentAdmissionGate"/> is the implementation.
/// </summary>
public interface IDocumentAdmissionEvaluator
{
    /// <summary>Evaluates one upload. See <see cref="DocumentAdmissionGate.EvaluateAsync"/> for the
    /// full contract.</summary>
    Task<AdmissionDecision> EvaluateAsync(
        TenantId tenantId,
        string actor,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
