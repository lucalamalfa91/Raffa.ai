using Raffa.Documents.Contracts.Application.Facts;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// The Veeam case email used the uploaded file name in its subject and text. A real contract is named
/// by its supplier and document type; a stored row that still carries the file name as its name is
/// read with the readable name; fixture contracts keep their curated names.
/// </summary>
public sealed class ContractDisplayNameTests
{
    [Theory]
    [InlineData("Veeam", "OrderForm", "Veeam Order Form")]
    [InlineData("GitLab", "Msa", "GitLab MSA")]
    [InlineData(" Veeam ", "Other", "Veeam")]
    [InlineData("Veeam", null, "Veeam")]
    [InlineData("", "Sow", "SOW")]
    [InlineData(null, "Other", "Contract")]
    public void Names_a_contract_by_its_supplier_and_type(string? supplier, string? type, string expected) =>
        Assert.Equal(expected, ContractDisplayName.Of(supplier, type));

    [Fact]
    public void A_stored_real_contract_named_after_its_file_reads_with_the_readable_name()
    {
        var stored = new ContractFacts { Supplier = "Veeam", Type = "OrderForm", Name = "Veeam_Subscription_2024.pdf", File = "Veeam_Subscription_2024.pdf" };

        var read = ContractDisplayName.Normalize(stored);

        Assert.Equal("Veeam Order Form", read.Name);
        Assert.Equal("Veeam_Subscription_2024.pdf", read.File);
    }

    [Fact]
    public void A_fixture_contract_and_an_already_readable_name_are_kept()
    {
        var fixture = new ContractFacts { FixtureId = 1, Name = "Salesforce_MSA_2024.pdf", File = "Salesforce_MSA_2024.pdf", Supplier = "Salesforce", Type = "Msa" };
        var named = new ContractFacts { Name = "Sales Cloud Enterprise — MSA + Order Form", File = "Salesforce_MSA_2024.pdf", Supplier = "Salesforce", Type = "Msa" };

        Assert.Same(fixture, ContractDisplayName.Normalize(fixture));
        Assert.Same(named, ContractDisplayName.Normalize(named));
    }
}
