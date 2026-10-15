using System.Security.Cryptography;
using System.Text;
using Raffa.AiFlows.CapabilityGaps.Catalog;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Catalog;

/// <summary>INV-07: the catalogue is data, and the data is byte-equivalent to the code it replaced.</summary>
public sealed class GapCatalogueDataTests
{
    // SHA-256 of "<include regex>\n<exclude regex or empty>", captured from the code-defined
    // catalog before it moved to gap-catalogue.json. A change here is a lexicon change: bump the
    // file's version and its changelog together with this table.
    private static readonly Dictionary<string, string> PatternHashes = new()
    {
        ["send-supplier"] = "DB48312DCAB538531814F465E3819D911A4E3DB3860017CC6E63CE49EC470B20",
        ["email-draft"] = "E736F34F1C57F344936E529D7D08A684303FF270C2A17471224C6497FBD9C02E",
        ["reminder"] = "ADCB10EC9954830071F2066C6E08E135B8DDBBB8085459EFD82E09BF5BC799FD",
        ["export-file"] = "D5966715F79768E53380FA6A85754BA61A23D59DD54A1232425989D0C149CE69",
        ["purchase-order"] = "CB8346D01A33064CE8774AAE3444A044CD83450ADBDECD310178F8DD188310A6",
    };

    [Fact]
    public void The_file_loads_with_a_version_and_the_five_entries_in_priority_order()
    {
        Assert.False(string.IsNullOrWhiteSpace(CapabilityGapCatalog.Version));
        Assert.Equal(
            [
                CapabilityGapCatalog.SendSupplierKey, CapabilityGapCatalog.EmailDraftKey, CapabilityGapCatalog.ReminderKey,
                CapabilityGapCatalog.ExportFileKey, CapabilityGapCatalog.PurchaseOrderKey,
            ],
            CapabilityGapCatalog.All.Select(g => g.Key));
    }

    [Fact]
    public void Every_lexicon_is_byte_equivalent_to_the_one_the_code_defined()
    {
        foreach (var gap in CapabilityGapCatalog.All)
        {
            var text = gap.Pattern!.ToString() + "\n" + gap.Exclude?.ToString();
            Assert.Equal(PatternHashes[gap.Key], Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        }

        Assert.Null(CapabilityGapCatalog.Find(CapabilityGapCatalog.EmailDraftKey)!.Exclude);
        Assert.NotNull(CapabilityGapCatalog.Find(CapabilityGapCatalog.SendSupplierKey)!.Exclude);
    }

    [Fact]
    public void Texts_and_alternatives_are_the_documented_ones()
    {
        var send = CapabilityGapCatalog.Find(CapabilityGapCatalog.SendSupplierKey)!;
        Assert.Equal("send an email to the supplier", send.OperationEn);
        Assert.Equal("inviare un'email al fornitore", send.OperationIt);
        Assert.Equal(GapAlternative.DraftEmail, send.Alternative);
        Assert.Equal(GapAlternative.Renewals, CapabilityGapCatalog.Find(CapabilityGapCatalog.ReminderKey)!.Alternative);
        Assert.Equal(GapAlternative.Portfolio, CapabilityGapCatalog.Find(CapabilityGapCatalog.ExportFileKey)!.Alternative);
        Assert.Equal(GapAlternative.ContractDetail, CapabilityGapCatalog.Find(CapabilityGapCatalog.PurchaseOrderKey)!.Alternative);
    }

    [Fact]
    public void A_malformed_catalogue_fails_loudly()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => GapCatalogueData.Parse("{"));
        Assert.Throws<InvalidDataException>(() => GapCatalogueData.Parse("""{"version":"x","entries":[]}"""));
    }
}
