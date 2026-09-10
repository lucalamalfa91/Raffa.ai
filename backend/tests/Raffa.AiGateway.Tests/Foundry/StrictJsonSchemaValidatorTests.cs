using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Foundry.Prompts;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>Proves <see cref="StrictJsonSchemaValidator"/> against the documented strict-mode
/// rules, and that this gateway's own prompt schemas pass it.</summary>
public class StrictJsonSchemaValidatorTests
{
    [Fact]
    public void A_closed_fully_required_object_schema_passes()
    {
        const string schema =
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"value":{"type":["number","null"]},"kind":{"type":"string","enum":["a","b"]}},"required":["name","value","kind"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}""";

        var result = StrictJsonSchemaValidator.Validate(schema);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
    }

    [Fact]
    public void The_gateways_own_prompt_schemas_pass()
    {
        Assert.True(StrictJsonSchemaValidator.Validate(ClassifyPromptTemplate.Schema).IsSuccess);
        Assert.True(StrictJsonSchemaValidator.Validate(AnswerPersonaPrompt.Schema).IsSuccess);
    }

    [Fact]
    public void A_missing_additionalProperties_false_is_named_with_its_path()
    {
        const string schema = """{"type":"object","properties":{"a":{"type":"string"}},"required":["a"]}""";

        var result = StrictJsonSchemaValidator.Validate(schema);

        Assert.True(result.IsFailure);
        Assert.Contains("$ must set additionalProperties: false", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_missing_from_required_is_named()
    {
        const string schema = """{"type":"object","properties":{"a":{"type":"string"},"b":{"type":["string","null"]}},"required":["a"],"additionalProperties":false}""";

        var result = StrictJsonSchemaValidator.Validate(schema);

        Assert.True(result.IsFailure);
        Assert.Contains("missing: b", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Bound_keywords_and_null_enum_members_are_rejected()
    {
        const string schema =
            """{"type":"object","properties":{"confidence":{"type":"number","minimum":0,"maximum":1},"due":{"type":["string","null"],"format":"date"},"level":{"type":["string","null"],"enum":["Low",null]}},"required":["confidence","due","level"],"additionalProperties":false}""";

        var result = StrictJsonSchemaValidator.Validate(schema);

        Assert.True(result.IsFailure);
        Assert.Contains("$.confidence uses unsupported keyword 'minimum'", result.Error, StringComparison.Ordinal);
        Assert.Contains("$.due uses unsupported keyword 'format'", result.Error, StringComparison.Ordinal);
        Assert.Contains("$.level.enum contains null", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_array_items_are_checked_too()
    {
        const string schema =
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"x":{"type":"string"}},"required":[]}}},"required":["items"],"additionalProperties":false}""";

        var result = StrictJsonSchemaValidator.Validate(schema);

        Assert.True(result.IsFailure);
        Assert.Contains("$.items.items must set additionalProperties: false", result.Error, StringComparison.Ordinal);
        Assert.Contains("$.items.items must list every property in required (missing: x)", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_root_must_be_an_object_schema()
    {
        Assert.True(StrictJsonSchemaValidator.Validate("""{"type":"array","items":{"type":"string"}}""").IsFailure);
        Assert.True(StrictJsonSchemaValidator.Validate("[1,2]").IsFailure);
        Assert.True(StrictJsonSchemaValidator.Validate("{not json").IsFailure);
    }
}
