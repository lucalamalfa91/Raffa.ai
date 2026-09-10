namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Input to the `classify` role. Classification runs on document text only (product spec §7.2:
/// classification is the first, cheapest extraction stage — it does not need the full staged
/// pipeline behind it), so this request intentionally carries no schema or evidence beyond the
/// text itself.
/// </summary>
/// <param name="DocumentText">Native or OCR'd text of the document (or a representative prefix of it).</param>
public sealed record AiClassificationRequest(string DocumentText);
