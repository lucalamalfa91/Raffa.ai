using Raffa.AiGateway.Agents;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>
/// Plan A-01/T-06: a definition is valid only if its schema is accepted by the strict validator and
/// its prompt still hashes to the value registered beside its version. This class is the
/// demonstration for the definition the tests ship (<c>Agents/Prompts/test/echo-agent/v1.md</c>); each
/// real definition has the same two assertions in its own test.
/// </summary>
public sealed class AgentDefinitionTests
{
    /// <summary>The hash registered next to <c>echo-agent</c> v1. Editing the prompt file without
    /// registering the new hash (and bumping the version) fails <see cref="The_prompt_file_has_not_drifted_from_its_registered_hash"/>.</summary>
    private const string EchoPromptV1Hash = "70c3740c7c643dc9dd2117014145c3089f84d1f02771b64451be3e91a8870c80";

    private const string EchoPromptPath = "Prompts/test/echo-agent/v1.md";

    private static AgentDefinition EchoFromFile(string? registeredHash = EchoPromptV1Hash) =>
        AgentDefinition.ForOutput<EchoOutput>(
            "echo-agent",
            "echo-v1",
            AgentPromptResource.ReadEmbedded(typeof(AgentDefinitionTests).Assembly, EchoPromptPath),
            TimeSpan.FromSeconds(10),
            AgentFailurePolicy.Skip,
            registeredPromptHash: registeredHash,
            promptResource: EchoPromptPath);

    [Fact]
    public void The_definition_is_sound_schema_strict_and_prompt_registered()
    {
        Assert.Empty(EchoFromFile().Validate());
    }

    [Fact]
    public void The_prompt_file_has_not_drifted_from_its_registered_hash()
    {
        var definition = EchoFromFile();

        Assert.Equal(EchoPromptV1Hash, definition.PromptHash);
        Assert.Empty(definition.Validate());
    }

    [Fact]
    public void A_prompt_edited_without_registering_the_new_hash_is_reported_as_drift()
    {
        var edited = new AgentDefinition(
            "echo-agent",
            "echo-v1",
            AgentPromptResource.ReadEmbedded(typeof(AgentDefinitionTests).Assembly, EchoPromptPath) + "\n3. A new law.",
            AgentSchema.For<EchoOutput>(),
            TimeSpan.FromSeconds(1),
            registeredPromptHash: EchoPromptV1Hash,
            promptResource: EchoPromptPath);

        var problem = Assert.Single(edited.Validate());
        Assert.Contains("prompt drift", problem, StringComparison.Ordinal);
        Assert.Contains(EchoPromptPath, problem, StringComparison.Ordinal);
        Assert.Contains("echo-v1", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hash_ignores_line_endings_and_trailing_whitespace()
    {
        Assert.Equal(
            AgentDefinition.HashPrompt("a\nb"),
            AgentDefinition.HashPrompt("a\r\nb\r\n\r\n"));
        Assert.NotEqual(AgentDefinition.HashPrompt("a\nb"), AgentDefinition.HashPrompt("a\nc"));
    }

    [Fact]
    public void A_non_strict_schema_is_reported_by_name()
    {
        var loose = new AgentDefinition(
            "loose-agent", "v1", "p",
            """{ "type": "object", "properties": { "x": { "type": "string", "format": "uuid" } } }""",
            TimeSpan.FromSeconds(1));

        var problems = loose.Validate();

        Assert.NotEmpty(problems);
        Assert.All(problems, p => Assert.StartsWith("loose-agent:", p, StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("additionalProperties", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_registered_hash_is_reported()
    {
        var definition = EchoFromFile("not-a-hash");

        Assert.Contains(definition.Validate(), p => p.Contains("not a SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void The_answer_role_has_no_schema_to_validate()
    {
        var answer = new AgentDefinition("answer-agent", "answer-v1", "p", string.Empty, TimeSpan.FromSeconds(1), role: AgentRole.Answer);

        Assert.Empty(answer.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("new\nline")]
    public void The_name_is_restricted_to_what_is_safe_in_audit_lines_and_schema_names(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new AgentDefinition(name, "v1", "p", "{}", TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_deadline_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentDefinition("a", "v1", "p", "{}", TimeSpan.Zero));
    }

    [Fact]
    public void The_language_directive_is_part_of_the_system_prompt_and_of_nothing_else()
    {
        var plain = new AgentDefinition("a", "v1", "PROMPT", "{}", TimeSpan.FromSeconds(1));
        var directed = new AgentDefinition(
            "a", "v1", "PROMPT", "{}", TimeSpan.FromSeconds(1), languageDirective: "Answer in the language of the question.");

        Assert.Equal("PROMPT", plain.SystemPrompt);
        Assert.StartsWith("PROMPT", directed.SystemPrompt, StringComparison.Ordinal);
        Assert.EndsWith("Answer in the language of the question.", directed.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(plain.PromptHash, directed.PromptHash);
    }

    [Fact]
    public void A_missing_or_ambiguous_prompt_file_is_a_named_error()
    {
        var assembly = typeof(AgentDefinitionTests).Assembly;

        var missing = Assert.Throws<InvalidOperationException>(() =>
            AgentPromptResource.ReadEmbedded(assembly, "Prompts/test/nobody/v9.md"));
        Assert.Contains("v9.md", missing.Message, StringComparison.Ordinal);
    }
}
