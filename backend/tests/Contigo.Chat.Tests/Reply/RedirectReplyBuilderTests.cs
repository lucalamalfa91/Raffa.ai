using Contigo.Chat.Application.Capabilities;
using Contigo.Chat.Application.Reply;
using Contigo.SharedKernel;

namespace Contigo.Chat.Tests.Reply;

/// <summary>
/// Proves task E13/F06/US01/T01's deterministic, zero-retrieval reply builders (ask-engine coding
/// objective point 6; R-ASK-02): every <c>Gate.DomainGate</c> label that never reaches the planner
/// produces a real <see cref="CopilotReply"/> with <see cref="ReplyProvenance.NoModelCall"/> — no
/// <c>Contigo.AiGateway</c> call ever happens for greeting/off-domain/needs-document/legal/capability.
/// </summary>
public sealed class RedirectReplyBuilderTests
{
    private static readonly CopilotAction[] NoActions = [];

    [Fact]
    public void Greeting_or_off_domain_with_a_hook_sentence_includes_it_and_uses_redirect_kind()
    {
        var reply = RedirectReplyBuilder.GreetingOrOffDomain(
            "your Salesforce renewal closes in 40 days", NoActions);

        Assert.Equal(ReplyKind.Redirect, reply.Kind);
        Assert.Contains("your Salesforce renewal closes in 40 days", reply.AnswerMarkdown);
        Assert.Empty(reply.Citations);
        Assert.Empty(reply.FollowUps);
        Assert.True(reply.Provenance.Sources.Count == 0);
        Assert.Null(reply.Provenance.ModelId);
    }

    [Fact]
    public void Greeting_or_off_domain_without_a_hook_sentence_uses_the_upload_invite_copy()
    {
        var reply = RedirectReplyBuilder.GreetingOrOffDomain(null, NoActions);

        Assert.Equal(ReplyKind.Redirect, reply.Kind);
        Assert.Contains("upload a contract in Documents", reply.AnswerMarkdown);
    }

    [Fact]
    public void GreetingOrOffDomain_rejects_null_actions()
    {
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.GreetingOrOffDomain("hook", null!));
    }

    [Fact]
    public void Needs_document_names_the_supplier_and_uses_redirect_kind()
    {
        var reply = RedirectReplyBuilder.NeedsDocument("Databricks", NoActions);

        Assert.Equal(ReplyKind.Redirect, reply.Kind);
        Assert.Contains("Databricks", reply.AnswerMarkdown);
        Assert.Null(reply.Provenance.ModelId);
    }

    [Fact]
    public void NeedsDocument_rejects_a_null_supplier()
    {
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.NeedsDocument(null!, NoActions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NeedsDocument_rejects_a_blank_supplier(string supplier)
    {
        Assert.Throws<ArgumentException>(() => RedirectReplyBuilder.NeedsDocument(supplier, NoActions));
    }

    [Fact]
    public void NeedsDocument_rejects_null_actions()
    {
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.NeedsDocument("Databricks", null!));
    }

    [Fact]
    public void Legal_uses_refusal_kind_and_never_gives_a_legal_reading()
    {
        var reply = RedirectReplyBuilder.Legal(NoActions);

        Assert.Equal(ReplyKind.Refusal, reply.Kind);
        Assert.Contains("can't give legal advice", reply.AnswerMarkdown);
        Assert.Null(reply.Provenance.ModelId);
    }

    [Fact]
    public void Legal_rejects_null_actions()
    {
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.Legal(null!));
    }

    [Fact]
    public void Capability_uses_answer_kind_not_a_decline()
    {
        var citations = new[]
        {
            new ReplyCitation(
                N: 1,
                Corpus: Contigo.Chat.Application.Pack.PackCorpus.Contigo,
                Title: "Ask Contigo",
                Subtitle: null,
                Snippet: "Ask about dates, spend, notice periods and clauses in plain language.",
                DocumentId: null,
                ContractId: null,
                Page: null,
                Section: null,
                PreviewUrl: null,
                Href: "/ask",
                RecordId: null),
        };

        var reply = RedirectReplyBuilder.Capability("I can help you with...", citations, NoActions);

        Assert.Equal(ReplyKind.Answer, reply.Kind);
        Assert.Same(citations, reply.Citations);
        Assert.Contains(Contigo.Chat.Application.Pack.PackCorpus.Contigo, reply.Provenance.Sources);
        Assert.Null(reply.Provenance.ModelId);
    }

    [Fact]
    public void Capability_rejects_null_markdown()
    {
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.Capability(null!, [], NoActions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Capability_rejects_blank_markdown(string markdown)
    {
        Assert.Throws<ArgumentException>(() => RedirectReplyBuilder.Capability(markdown, [], NoActions));
    }

    [Fact]
    public void Capability_rejects_null_citations_or_actions()
    {
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.Capability("markdown", null!, NoActions));
        Assert.Throws<ArgumentNullException>(() => RedirectReplyBuilder.Capability("markdown", [], null!));
    }

    [Fact]
    public void Every_deterministic_reply_carries_actions_that_never_contain_an_unresolved_placeholder()
    {
        // This task's own Definition of Done: "actions never contain `{`" — proven here against
        // the real CapabilityRouting.ResolveActions path (the only real actions[] producer) for
        // the deterministic (never-reaches-the-model) reply builders.
        var routing = new CapabilityRouting();
        var context = new RoutingContext(
            ValidatedContractCount: 0, Role: CapabilityCallerRole.Standard);
        var uploadAction = routing.ResolveActions([CapabilityIntent.UnknownSupplier], context);

        var reply = RedirectReplyBuilder.NeedsDocument("Databricks", uploadAction);

        Assert.NotEmpty(reply.Actions);
        Assert.All(reply.Actions, action => Assert.DoesNotContain('{', action.Href));
    }
}
