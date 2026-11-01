using System.Globalization;
using System.Text.Json;
using Raffa.Chat.Application.Ask4;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Chat.Tests.Ask4;

/// <summary>The fixture answers equal the prototype's <c>answer()</c> (AskRaffa V4.dc.html) over contracts.json.</summary>
public sealed class ContractFactsAnswererTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    private static IReadOnlyList<ContractFacts> Contracts([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "fixtures", "askraffa4", "contracts.json"));
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<ContractsFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return file.Contracts.Select(c => c with
        {
            ContractId = Guid.NewGuid(),
            NoticeDeadline = DateOnly.ParseExact(c.Cancel, "dd MMM yyyy", CultureInfo.InvariantCulture),
            Evidence = file.Evidence,
        }).ToList();
    }

    private sealed record ContractsFile(List<ContractFacts> Contracts, Dictionary<string, ContractEvidence> Evidence);

    private static ContractFacts By(string supplier) => Contracts().Single(c => c.Supplier == supplier);

    [Fact]
    public void Notice_question_cites_the_renewal_clause()
    {
        var a = ContractFactsAnswerer.Answer("Quando dobbiamo dare disdetta a Salesforce?", By("Salesforce"), Today, "it");
        Assert.Equal(
            "Disdetta scritta entro il 18 Oct 2026 — 90 giorni prima della fine del 15 Jan 2027. Mancano 40 giorni. Senza disdetta si rinnova per 12 mesi.",
            a.Text);
        Assert.Equal("p.12 §8.4", a.Source);
    }

    [Fact]
    public void Liability_question_quotes_the_evidence_with_page_and_section()
    {
        var a = ContractFactsAnswerer.Answer("Qual è il massimale di responsabilità con AWS?", By("AWS"), Today, "it");
        Assert.StartsWith("AWS: ", a.Text, StringComparison.Ordinal);
        Assert.Contains("«", a.Text, StringComparison.Ordinal);
        Assert.Equal("AWS_EDP_Amendment_3.pdf p.19 §11.2", a.Source);
    }

    [Fact]
    public void Market_question_is_refused_honestly_with_the_declared_limit()
    {
        var a = ContractFactsAnswerer.Answer("Il prezzo Microsoft è in linea con il mercato?", By("Microsoft"), Today, "it");
        Assert.StartsWith("Non ho un benchmark di mercato affidabile in questo rilascio e non lo invento.", a.Text, StringComparison.Ordinal);
        Assert.Equal("limite dichiarato", a.Source);
        Assert.Equal("compareQuote", a.Cta?.Kind);
    }

    [Fact]
    public void Unknown_question_says_it_cannot()
    {
        var a = ContractFactsAnswerer.Answer("Chi è il referente DocuSign?", By("DocuSign"), Today, "it");
        Assert.Equal("nessuna evidenza sopra soglia", a.Source);
    }

    [Fact]
    public void Summary_offers_the_contract()
    {
        var a = ContractFactsAnswerer.Answer("Riassumi il contratto AWS", By("AWS"), Today, "it");
        Assert.StartsWith("AWS — Enterprise Discount Program: CHF 2,400,000 l’anno", a.Text, StringComparison.Ordinal);
        Assert.Equal("campi validati", a.Source);
        Assert.Equal("openContract", a.Cta?.Kind);
    }

    [Fact]
    public void A_question_naming_no_contract_needs_the_picker_and_titles_follow_the_prototype()
    {
        Assert.Null(AskRules.NamedContract("Quando scade?", Contracts()));
        Assert.Equal("Salesforce · Quando scade?", AskRules.Title("Quando scade?", "Salesforce"));
        Assert.Equal(new string('x', 50) + "…", AskRules.Title(new string('x', 60), null));
    }
}
