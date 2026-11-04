using System.Net;
using System.Text.Json;

namespace Raffa.IntegrationTests;

/// <summary>
/// The AskRaffa 4.0 path a real upload takes, over HTTP against a migrated Postgres with RLS:
/// upload → worker → Revisione one field at a time (<c>POST /api/documents/{id}/reading/fields/{key}</c>)
/// → "Contract validated." → "Open the contract" (<c>GET /api/contracts/{id}/v4</c>) and the
/// Contracts list (<c>GET /api/contracts?view=v4</c>). A contract the reading calls validated has to
/// open and has to be listed.
/// </summary>
public sealed class R1ContractsV4ReviewEndToEndTests : IClassFixture<R1IntegrationFixture>
{
    private readonly R1IntegrationFixture _fixture;

    public R1ContractsV4ReviewEndToEndTests(R1IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_contract_validated_in_the_reading_opens_and_is_listed()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var (documentId, contractId) = await R1EndToEndTests.UploadAndProcessAsync(
            _fixture, client, tenantId, R1ExtractionFixtures.BuildBornDigitalPdfBytes(),
            R1ExtractionFixtures.BornDigitalFileName, R1ExtractionFixtures.BornDigitalMimeType);

        var reading = await R1EndToEndTests.ParseAsync(
            await R1EndToEndTests.GetAsync(client, $"/api/documents/{documentId}/reading", tenantId));
        Assert.Equal("review", reading.GetProperty("status").GetString());
        var keys = reading.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("key").GetString()!).ToList();
        Assert.NotEmpty(keys);

        JsonElement last = default;
        foreach (var key in keys)
        {
            var decided = await R1EndToEndTests.PostAsync(
                client, $"/api/documents/{documentId}/reading/fields/{key}", tenantId, new { decision = "accept", value = (string?)null });
            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
            last = await R1EndToEndTests.ParseAsync(decided);
        }

        Assert.Equal("done", last.GetProperty("status").GetString());
        Assert.Equal(contractId.ToString(), last.GetProperty("contractId").GetString());

        var opened = await R1EndToEndTests.GetAsync(client, $"/api/contracts/{contractId}/v4", tenantId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        var listed = await R1EndToEndTests.ParseAsync(
            await R1EndToEndTests.GetAsync(client, "/api/contracts?view=v4", tenantId));
        Assert.Contains(listed.GetProperty("rows").EnumerateArray(), r => r.GetProperty("contractId").GetString() == contractId.ToString());
    }

    /// <summary>
    /// QA (Asterlane): the reading said "validated", then "Open the contract" answered "The contract could not
    /// be loaded" and Contracts / Results kept showing only the earlier contracts. That is the state a
    /// document reaches when it is <c>Completed</c> but its contract facts cannot be written. Here the
    /// database refuses that write: the reading must not claim the contract is validated, and a contract
    /// validated another way must still open and be listed.
    /// </summary>
    [Fact]
    public async Task A_contract_whose_facts_cannot_be_written_is_never_shown_validated_and_never_lost()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var (documentId, contractId) = await R1EndToEndTests.UploadAndProcessAsync(
            _fixture, client, tenantId, R1ExtractionFixtures.BuildBornDigitalPdfBytes(),
            R1ExtractionFixtures.BornDigitalFileName, R1ExtractionFixtures.BornDigitalMimeType);
        var trigger = "raffa_test_reject_facts_" + contractId.ToString("N");
        await _fixture.ExecuteAsSuperuserAsync($"""
            CREATE FUNCTION {trigger}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.contract_id = '{contractId}' THEN RAISE EXCEPTION 'contract facts rejected (test)'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER {trigger} BEFORE INSERT OR UPDATE ON contract_facts FOR EACH ROW EXECUTE FUNCTION {trigger}();
            """);
        try
        {
            // The reading: the last decision cannot validate without the facts, so nothing is committed.
            var reading = await R1EndToEndTests.ParseAsync(
                await R1EndToEndTests.GetAsync(client, $"/api/documents/{documentId}/reading", tenantId));
            var keys = reading.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("key").GetString()!).ToList();
            HttpStatusCode last = default;
            foreach (var key in keys)
            {
                last = (await R1EndToEndTests.PostAsync(
                    client, $"/api/documents/{documentId}/reading/fields/{key}", tenantId, new { decision = "accept", value = (string?)null })).StatusCode;
            }

            Assert.Equal(HttpStatusCode.InternalServerError, last);
            var after = await R1EndToEndTests.ParseAsync(
                await R1EndToEndTests.GetAsync(client, $"/api/documents/{documentId}/reading", tenantId));
            Assert.Equal("review", after.GetProperty("status").GetString());

            // Validated another way (the Documents review): the contract opens and is listed all the same.
            var validated = await R1EndToEndTests.PostAsync(
                client, $"/api/documents/{documentId}/validate", tenantId, new { acceptedFields = keys });
            Assert.Equal(HttpStatusCode.OK, validated.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await R1EndToEndTests.GetAsync(client, $"/api/contracts/{contractId}/v4", tenantId)).StatusCode);
            var listed = await R1EndToEndTests.ParseAsync(
                await R1EndToEndTests.GetAsync(client, "/api/contracts?view=v4", tenantId));
            Assert.Contains(listed.GetProperty("rows").EnumerateArray(), r => r.GetProperty("contractId").GetString() == contractId.ToString());
        }
        finally
        {
            await _fixture.ExecuteAsSuperuserAsync($"DROP TRIGGER {trigger} ON contract_facts; DROP FUNCTION {trigger}();");
        }
    }
}
