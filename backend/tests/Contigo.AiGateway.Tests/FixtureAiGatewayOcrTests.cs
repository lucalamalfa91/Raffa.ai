using System.Text;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Fixtures;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests;

/// <summary>
/// Proves task E02/F01/US02/T02's `ocr` role addition against <see cref="FixtureAiGateway"/>:
/// full-document (no 2-page cap), page-mapped output, configured model metadata, and the ADR-017
/// page-budget safety mechanism ("fail visibly... never silently truncate").
/// </summary>
public class FixtureAiGatewayOcrTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway CreateGateway(AiGatewayOcrOptions? ocrOptions = null) =>
        new(new AiGatewayModelOptions(), new FixedClock(Now), ocrOptions);

    [Fact]
    public async Task Ocr_splits_content_on_form_feed_into_one_page_per_section()
    {
        var gateway = CreateGateway();
        var content = Encoding.UTF8.GetBytes("page one text\fpage two text\fpage three text");

        var result = await gateway.OcrAsync(new AiOcrRequest("contract.pdf", "application/pdf", content));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Pages.Count);
        Assert.Equal(1, result.Value.Pages[0].PageNumber);
        Assert.Equal("page one text", result.Value.Pages[0].Text);
        Assert.Equal(2, result.Value.Pages[1].PageNumber);
        Assert.Equal(3, result.Value.Pages[2].PageNumber);
        Assert.Equal("page three text", result.Value.Pages[2].Text);
    }

    [Fact]
    public async Task Ocr_processes_the_full_document_with_no_page_cap()
    {
        var gateway = CreateGateway();

        // 50 form-feed separated pages — well past any "first two pages" style cap.
        var content = Encoding.UTF8.GetBytes(string.Join('\f', Enumerable.Range(1, 50).Select(i => $"page {i}")));

        var result = await gateway.OcrAsync(new AiOcrRequest("big-msa.pdf", "application/pdf", content));

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value.Pages.Count);
        Assert.Equal("page 50", result.Value.Pages[49].Text);
    }

    [Fact]
    public async Task Ocr_returns_configured_model_metadata()
    {
        var options = new AiGatewayModelOptions { Ocr = new AiModelSelection("test-ocr-model", "3") };
        var gateway = new FixtureAiGateway(options, new FixedClock(Now));

        var result = await gateway.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "some text"u8.ToArray()));

        Assert.True(result.IsSuccess);
        Assert.Equal("test-ocr-model", result.Value.Metadata.ModelId);
        Assert.Equal("3", result.Value.Metadata.ModelVersion);
        Assert.Equal(Now, result.Value.Metadata.RespondedAtUtc);
    }

    [Fact]
    public async Task Ocr_fails_without_content()
    {
        var gateway = CreateGateway();

        var result = await gateway.OcrAsync(
            new AiOcrRequest("empty.pdf", "application/pdf", ReadOnlyMemory<byte>.Empty));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Ocr_returns_an_honest_placeholder_for_undecodable_binary_content()
    {
        var gateway = CreateGateway();

        // Not valid UTF-8 under any interpretation (0xFF is not a legal leading byte).
        byte[] binaryContent = [0xFF, 0xFE, 0xFD, 0xFC];

        var result = await gateway.OcrAsync(new AiOcrRequest("scanned.pdf", "application/pdf", binaryContent));

        Assert.True(result.IsSuccess);
        var page = Assert.Single(result.Value.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Contains("fixture-ocr", page.Text, StringComparison.Ordinal);
        Assert.Contains("4 bytes", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_reads_a_born_digital_pdf_through_the_fixture_scanner()
    {
        var gateway = CreateGateway();
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 80 >>\n" +
            "stream\n" +
            "BT (MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd.) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "3 0 obj << /Type /Page >> endobj\n" +
            "4 0 obj << /Length 40 >>\n" +
            "stream\n" +
            "BT (Governing law: Italy.) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        var result = await gateway.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", Encoding.Latin1.GetBytes(pdf)));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pages.Count);
        Assert.Equal("MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd.", result.Value.Pages[0].Text);
        Assert.Equal(2, result.Value.Pages[1].PageNumber);
        Assert.Equal("Governing law: Italy.", result.Value.Pages[1].Text);
    }

    [Fact]
    public async Task Ocr_returns_the_placeholder_for_a_pdf_the_scanner_cannot_pair()
    {
        var gateway = CreateGateway();
        // A "scanned" PDF: one page object, no text-showing operator anywhere.
        var pdf = "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n2 0 obj << /Length 8 >>\nstream\n/Im0 Do\nendstream\nendobj\n%%EOF\n";

        var result = await gateway.OcrAsync(
            new AiOcrRequest("scanned.pdf", "application/pdf", Encoding.Latin1.GetBytes(pdf)));

        Assert.True(result.IsSuccess);
        var page = Assert.Single(result.Value.Pages);
        Assert.Contains("fixture-ocr", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_visibly_when_the_page_budget_is_exceeded_instead_of_truncating()
    {
        var gateway = CreateGateway(new AiGatewayOcrOptions { MaxPagesPerDocument = 2 });
        var content = Encoding.UTF8.GetBytes("page one\fpage two\fpage three");

        var result = await gateway.OcrAsync(new AiOcrRequest("huge.pdf", "application/pdf", content));

        Assert.True(result.IsFailure);
        Assert.Contains("budget", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_within_the_configured_budget_succeeds()
    {
        var gateway = CreateGateway(new AiGatewayOcrOptions { MaxPagesPerDocument = 2 });
        var content = Encoding.UTF8.GetBytes("page one\fpage two");

        var result = await gateway.OcrAsync(new AiOcrRequest("ok.pdf", "application/pdf", content));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pages.Count);
    }
}
