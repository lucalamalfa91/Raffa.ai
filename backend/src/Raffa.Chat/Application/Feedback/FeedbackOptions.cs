namespace Raffa.Chat.Application.Feedback;

/// <summary>Bound by the host from <c>Feedback:*</c>. The module needs only the environment
/// name; the publisher's own settings (token, repository) belong to the host adapter.</summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>The deployment name written into every issue ("dev", "demo", "local").</summary>
    public string Environment { get; set; } = "local";
}
