using System.Security.Cryptography;
using System.Text;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>
/// The identity of one extraction input: <c>SHA256("staged-v1\n" + pageMarkedText)</c>, stamped on every
/// <c>ExtractionJob.InputHash</c>. A resume reuses a finished stage checkpoint only when the hash is equal,
/// so the algorithm, the page-marker format (<c>PageMarkedText</c>) and <see cref="PipelineVersion"/> are a
/// contract with every row already stored.
/// </summary>
public static class ExtractionInputHash
{
    /// <summary>Mixed into the hash: bump it when the stage set or schemas change so that an old
    /// checkpoint is never reused.</summary>
    public const string PipelineVersion = "staged-v1";

    public static string Compute(string documentText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PipelineVersion + "\n" + documentText)));
}
