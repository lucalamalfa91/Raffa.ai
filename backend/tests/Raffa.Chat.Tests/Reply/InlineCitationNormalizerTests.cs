using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Tests.Reply;

/// <summary>
/// A citation key the model leaves in its prose (<c>[fact:{contractId}:renewal]</c>) never
/// reaches the reader: it becomes the reply's own <c>[n]</c> marker when it names a cited or
/// citable pack item, and disappears otherwise (R-ASK-08 -- no id is ever rendered).
/// </summary>
public sealed class InlineCitationNormalizerTests
{
    private const string ContractId = "11111111-1111-1111-1111-111111111111";
    private const string DocumentId = "22222222-2222-2222-2222-222222222222";

    private static PackItem Item(string key, string title) => new(
        key,
        PackCorpus.Tenant,
        title,
        "p.12 §8.4",
        Page: 12,
        Section: "8.4",
        Snippet: "Renews unless notice is given.",
        Href: null,
        PreviewUrl: null,
        RecordId: null,
        Provenance: "validated contract",
        Values: [],
        ContractId: ContractId,
        DocumentId: DocumentId);

    private static readonly string RenewalKey = $"fact:{ContractId}:renewal";
    private static readonly string ChunkKey = $"fact:{DocumentId}:chunk[3]";
    private static readonly PackItem RenewalItem = Item(RenewalKey, "Atlassian · MSA");
    private static readonly PackItem ChunkItem = Item(ChunkKey, "Atlassian · MSA");

    [Fact]
    public void A_bracketed_key_the_model_also_listed_becomes_that_entrys_own_marker()
    {
        var (markdown, keys) = InlineCitationNormalizer.Normalize(
            $"It expires on 2029-01-06 and does not renew automatically.[{RenewalKey}]",
            [RenewalKey],
            [RenewalItem, ChunkItem]);

        Assert.Equal("It expires on 2029-01-06 and does not renew automatically. [1]", markdown);
        Assert.Equal([RenewalKey], keys);
    }

    [Fact]
    public void A_pack_key_the_model_forgot_to_list_is_appended_so_its_card_exists()
    {
        var (markdown, keys) = InlineCitationNormalizer.Normalize(
            $"Notice is 90 days [{RenewalKey}] and the clause says so [{ChunkKey}].",
            [RenewalKey],
            [RenewalItem, ChunkItem]);

        Assert.Equal("Notice is 90 days [1] and the clause says so [2].", markdown);
        Assert.Equal([RenewalKey, ChunkKey], keys);
    }

    [Fact]
    public void A_key_that_resolves_to_nothing_is_removed_and_the_sentence_tidied()
    {
        var (markdown, keys) = InlineCitationNormalizer.Normalize(
            "The cap is 12 months [fact:33333333-3333-3333-3333-333333333333:clause] . Saving [fact:x:saving[0]] of CHF 80k",
            [RenewalKey],
            [RenewalItem]);

        Assert.Equal("The cap is 12 months. Saving of CHF 80k", markdown);
        Assert.Equal([RenewalKey], keys);
    }

    [Fact]
    public void A_bare_key_with_a_dotted_tail_is_handled_and_a_repeated_marker_collapses()
    {
        var (markdown, _) = InlineCitationNormalizer.Normalize(
            $"Give notice by 18 Oct 2026 fact:{ContractId}:priced-line[Sales-Cloud].unitPrice. [1] [{RenewalKey}]",
            [RenewalKey],
            [RenewalItem]);

        Assert.Equal("Give notice by 18 Oct 2026. [1]", markdown);
        Assert.DoesNotContain("fact:", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ContractId, markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ordinary_prose_with_a_colon_after_a_prefix_word_is_left_alone()
    {
        var (markdown, keys) = InlineCitationNormalizer.Normalize(
            "On the market: the P50 is CHF 132 [1].",
            [RenewalKey],
            [RenewalItem]);

        Assert.Equal("On the market: the P50 is CHF 132 [1].", markdown);
        Assert.Equal([RenewalKey], keys);
    }

    [Fact]
    public void Null_or_empty_markdown_passes_through()
    {
        var (markdown, keys) = InlineCitationNormalizer.Normalize(null, [RenewalKey], [RenewalItem]);
        Assert.Equal(string.Empty, markdown);
        Assert.Equal([RenewalKey], keys);
    }
}
