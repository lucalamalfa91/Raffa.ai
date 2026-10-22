using System.Linq.Expressions;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Docker-free proof that the latency trims in <see cref="DocumentQueryService.ListAsync"/> and
/// <see cref="PortfolioQueryService.GetPortfolioAsync"/> change the number of queries and rows, never
/// the answer. Two kinds of assertion: (1) every result is compared with a plain in-memory
/// computation over the seeded rows (the semantics the service had before the trims, written out
/// independently), across tenants, statuses, filters and pages; (2) the number of queries a call
/// issues is pinned, counted as the distinct query shapes EF compiles for it on a fresh internal
/// service provider (so nothing is cached from an earlier call) -- each shape runs once per call.
/// The Postgres-backed behaviour of the same methods stays with
/// <see cref="PortfolioQueryServiceTests"/> and <see cref="DocumentQueryServiceTests"/> (Docker).
/// </summary>
public sealed class QueryTrimmingTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    // --- DocumentQueryService.ListAsync ------------------------------------------------------

    [Fact]
    public async Task List_total_count_matches_the_rows_of_the_requested_status_for_every_status_and_page()
    {
        var env = new Env();
        var tenant = TenantId.New();
        var other = TenantId.New();
        var statuses = new[]
        {
            DocumentProcessingStatus.Uploaded, DocumentProcessingStatus.Uploaded,
            DocumentProcessingStatus.Processing,
            DocumentProcessingStatus.NeedsReview, DocumentProcessingStatus.NeedsReview,
            DocumentProcessingStatus.Completed, DocumentProcessingStatus.Completed, DocumentProcessingStatus.Completed,
            DocumentProcessingStatus.Failed,
            DocumentProcessingStatus.Rejected, DocumentProcessingStatus.Rejected,
        };

        var seeded = statuses.Select((status, i) => NewDocument(tenant, status, Epoch.AddMinutes(i))).ToList();
        // Another tenant's rows must not move any number.
        seeded.AddRange(statuses.Select((status, i) => NewDocument(other, status, Epoch.AddMinutes(i))));
        await env.SeedAsync(db => db.Documents.AddRange(seeded));

        foreach (var pageSize in new[] { 1, 4, 100 })
        {
            // No filter: every row of the tenant, Rejected included (the old COUNT(*) did).
            var unfiltered = await env.ListAsync(tenant, status: null, page: 1, pageSize);
            Assert.Equal(statuses.Length, unfiltered.TotalCount);

            foreach (var status in Enum.GetValues<DocumentProcessingStatus>())
            {
                var expected = statuses.Count(s => s == status);
                var result = await env.ListAsync(tenant, status, page: 1, pageSize);

                Assert.Equal(expected, result.TotalCount);
                Assert.Equal(Math.Min(expected, pageSize), result.Items.Count);
                Assert.All(result.Items, item => Assert.Equal(status, item.ProcessingStatus));
            }

            // A page past the end keeps the total and answers no items (counts still present).
            var past = await env.ListAsync(tenant, DocumentProcessingStatus.Completed, page: 99, pageSize);
            Assert.Equal(3, past.TotalCount);
            Assert.Empty(past.Items);
            Assert.NotNull(past.Counts);
        }
    }

    [Fact]
    public async Task List_counts_are_tenant_wide_and_the_total_follows_the_filter()
    {
        var env = new Env();
        var tenant = TenantId.New();
        await env.SeedAsync(db => db.Documents.AddRange(
            NewDocument(tenant, DocumentProcessingStatus.Uploaded, Epoch),
            NewDocument(tenant, DocumentProcessingStatus.Processing, Epoch.AddMinutes(1)),
            NewDocument(tenant, DocumentProcessingStatus.NeedsReview, Epoch.AddMinutes(2)),
            NewDocument(tenant, DocumentProcessingStatus.Completed, Epoch.AddMinutes(3)),
            NewDocument(tenant, DocumentProcessingStatus.Failed, Epoch.AddMinutes(4)),
            NewDocument(tenant, DocumentProcessingStatus.Rejected, Epoch.AddMinutes(5))));

        var page = await env.ListAsync(tenant, DocumentProcessingStatus.Completed, page: 1, pageSize: 1);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(new DocumentCounts(All: 5, NeedsAttention: 4, NeedsReview: 1, Processing: 2, Rejected: 1), page.Counts);
    }

    [Fact]
    public async Task List_for_a_tenant_without_documents_or_an_undefined_status_is_empty_not_an_error()
    {
        var env = new Env();
        var tenant = TenantId.New();
        await env.SeedAsync(db => db.Documents.Add(NewDocument(TenantId.New(), DocumentProcessingStatus.Completed, Epoch)));

        var empty = await env.ListAsync(tenant, status: null, page: 1, pageSize: 25);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        Assert.Equal(DocumentCounts.Empty, empty.Counts);

        // The endpoint's Enum.TryParse accepts a bare number, so an undefined status can reach here.
        var undefined = await env.ListAsync(tenant, (DocumentProcessingStatus)99, page: 1, pageSize: 25);
        Assert.Equal(0, undefined.TotalCount);
    }

    [Fact]
    public async Task The_shell_counts_probe_is_two_queries_the_grouped_counts_and_the_page()
    {
        var env = new Env();
        var tenant = TenantId.New();
        await env.SeedAsync(db => db.Documents.AddRange(
            NewDocument(tenant, DocumentProcessingStatus.Completed, Epoch),
            NewDocument(tenant, DocumentProcessingStatus.Completed, Epoch.AddMinutes(1)),
            NewDocument(tenant, DocumentProcessingStatus.Rejected, Epoch.AddMinutes(2))));

        env.Counter.Reset();
        var probe = await env.ListAsync(tenant, status: null, page: 1, pageSize: 1);

        Assert.Equal(3, probe.TotalCount);
        Assert.Single(probe.Items);
        // Was three: a COUNT(*) of its own, the grouped counts, the page.
        Assert.Equal(2, env.Counter.Compiled);
    }

    // --- PortfolioQueryService.GetPortfolioAsync ---------------------------------------------

    [Theory]
    [InlineData(null, 3)]
    [InlineData(null, 1)]
    [InlineData(null, 50)]
    [InlineData(RiskSeverity.High, 2)]
    [InlineData(RiskSeverity.Low, 100)]
    [InlineData(RiskSeverity.Critical, 1)]
    public async Task Every_portfolio_page_equals_the_plain_in_memory_computation(RiskSeverity? riskFilter, int pageSize)
    {
        var env = new Env();
        var tenant = TenantId.New();
        var other = TenantId.New();
        var data = BuildPortfolio(tenant, other, contractCount: 8);
        await env.SeedAsync(db =>
        {
            db.Contracts.AddRange(data.Contracts);
            db.Risks.AddRange(data.Risks);
            db.Documents.AddRange(data.Documents);
        });

        var filter = new PortfolioFilter(Risk: riskFilter);
        var expected = data.Expected(tenant, riskFilter);
        var pages = (int)Math.Ceiling(Math.Max(expected.Count, 1) / (double)pageSize) + 1; // one past the end

        var collected = new List<PortfolioListItem>();
        for (var page = 1; page <= pages; page++)
        {
            var result = await env.Portfolio.GetPortfolioAsync(tenant, filter, new PortfolioPageRequest(page, pageSize));

            Assert.Equal(expected.Count, result.TotalCount);
            Assert.Equal(page, result.Page);
            Assert.Equal(pageSize, result.PageSize);
            Assert.Equal(expected.Skip((page - 1) * pageSize).Take(pageSize).ToList(), result.Items.ToList());
            collected.AddRange(result.Items);
        }

        Assert.Equal(expected, collected);
    }

    [Fact]
    public async Task A_contract_with_several_risks_and_documents_shows_its_worst_risk_and_newest_document_on_any_page()
    {
        var env = new Env();
        var tenant = TenantId.New();
        var contracts = Enumerable.Range(0, 5)
            .Select(i => NewContract(tenant, Epoch.AddDays(-i)))
            .ToList();
        var risks = contracts.SelectMany(c => new[]
        {
            NewRisk(tenant, c.Id, RiskSeverity.Low),
            NewRisk(tenant, c.Id, RiskSeverity.Critical),
            NewRisk(tenant, c.Id, RiskSeverity.Medium),
        }).ToList();
        var documents = contracts.SelectMany(c => new[]
        {
            NewDocument(tenant, DocumentProcessingStatus.Completed, Epoch.AddHours(-2), c.Id, fileName: $"old-{c.Id.Value:N}.pdf"),
            NewDocument(tenant, DocumentProcessingStatus.Processing, Epoch.AddHours(-1), c.Id, fileName: $"new-{c.Id.Value:N}.pdf"),
        }).ToList();
        await env.SeedAsync(db =>
        {
            db.Contracts.AddRange(contracts);
            db.Risks.AddRange(risks);
            db.Documents.AddRange(documents);
        });

        // Page 2 of 2-per-page: not the first rows the service sees.
        var page = await env.Portfolio.GetPortfolioAsync(tenant, PortfolioFilter.None, new PortfolioPageRequest(2, 2));

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item =>
        {
            Assert.Equal(RiskSeverity.Critical, item.Risk);
            Assert.Equal($"new-{item.ContractId:N}.pdf", item.FileName);
            Assert.Equal(DocumentProcessingStatus.Processing, item.DocumentProcessingStatus);
        });
    }

    [Fact]
    public async Task An_empty_portfolio_is_one_query_not_three()
    {
        var env = new Env();
        var tenant = TenantId.New();
        await env.SeedAsync(db => db.Contracts.Add(NewContract(TenantId.New(), Epoch)));

        env.Counter.Reset();
        var result = await env.Portfolio.GetPortfolioAsync(tenant, PortfolioFilter.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, env.Counter.Compiled);
    }

    [Fact]
    public async Task A_page_past_the_end_skips_the_risk_and_document_reads_unless_a_risk_filter_needs_them()
    {
        var env = new Env();
        var tenant = TenantId.New();
        var data = BuildPortfolio(tenant, TenantId.New(), contractCount: 4);
        await env.SeedAsync(db =>
        {
            db.Contracts.AddRange(data.Contracts);
            db.Risks.AddRange(data.Risks);
            db.Documents.AddRange(data.Documents);
        });

        env.Counter.Reset();
        var past = await env.Portfolio.GetPortfolioAsync(tenant, PortfolioFilter.None, new PortfolioPageRequest(9, 25));
        Assert.Empty(past.Items);
        Assert.Equal(data.Expected(tenant, null).Count, past.TotalCount);
        Assert.Equal(1, env.Counter.Compiled);

        // A Risk filter is a function of every matched contract's risk rows, so it still reads them
        // all (and its total is the filtered count, as PortfolioQueryServiceTests pins on Postgres).
        env.Counter.Reset();
        var filtered = await env.Portfolio.GetPortfolioAsync(
            tenant, new PortfolioFilter(Risk: RiskSeverity.Critical), new PortfolioPageRequest(9, 25));
        Assert.Empty(filtered.Items);
        Assert.Equal(data.Expected(tenant, RiskSeverity.Critical).Count, filtered.TotalCount);
        Assert.Equal(3, env.Counter.Compiled);
    }

    [Fact]
    public async Task A_page_in_range_is_still_three_queries()
    {
        var env = new Env();
        var tenant = TenantId.New();
        var data = BuildPortfolio(tenant, TenantId.New(), contractCount: 4);
        await env.SeedAsync(db =>
        {
            db.Contracts.AddRange(data.Contracts);
            db.Risks.AddRange(data.Risks);
            db.Documents.AddRange(data.Documents);
        });

        env.Counter.Reset();
        var page = await env.Portfolio.GetPortfolioAsync(tenant, PortfolioFilter.None, new PortfolioPageRequest(1, 2));

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, env.Counter.Compiled);
    }

    // --- fixtures ----------------------------------------------------------------------------

    /// <summary>A portfolio with a varied mix: contracts with and without risks, with one or two
    /// documents, some without any (those are excluded from the list), plus another tenant's rows.</summary>
    private static PortfolioData BuildPortfolio(TenantId tenant, TenantId other, int contractCount)
    {
        var contracts = new List<Contract>();
        var risks = new List<Risk>();
        var documents = new List<Document>();
        var severities = new[] { RiskSeverity.Low, RiskSeverity.Medium, RiskSeverity.High, RiskSeverity.Critical };

        for (var i = 0; i < contractCount; i++)
        {
            var contract = NewContract(tenant, Epoch.AddHours(-i));
            contracts.Add(contract);

            // contract 3 is an orphan shell (no document): it must not appear.
            if (i != 3)
            {
                documents.Add(NewDocument(tenant, DocumentProcessingStatus.Completed, Epoch.AddDays(-2).AddMinutes(i), contract.Id, $"first-{i}.pdf"));
                if (i % 2 == 0)
                {
                    documents.Add(NewDocument(tenant, DocumentProcessingStatus.NeedsReview, Epoch.AddDays(-1).AddMinutes(i), contract.Id, $"second-{i}.pdf"));
                }
            }

            // i = 0: no risk; otherwise 1..3 risks of cycling severity.
            for (var r = 0; r < i % 4; r++)
            {
                risks.Add(NewRisk(tenant, contract.Id, severities[(i + r) % severities.Length]));
            }
        }

        // Another tenant: same shapes, must never leak into the tenant's result.
        var foreign = NewContract(other, Epoch.AddHours(1));
        contracts.Add(foreign);
        documents.Add(NewDocument(other, DocumentProcessingStatus.Completed, Epoch, foreign.Id, "foreign.pdf"));
        risks.Add(NewRisk(other, foreign.Id, RiskSeverity.Critical));

        return new PortfolioData(contracts, risks, documents);
    }

    private sealed record PortfolioData(List<Contract> Contracts, List<Risk> Risks, List<Document> Documents)
    {
        /// <summary>The list the service had always produced, written out directly: listed contracts
        /// (those with at least one document), newest first, risk = highest recorded severity,
        /// document = newest by creation time, optionally narrowed to one risk.</summary>
        public List<PortfolioListItem> Expected(TenantId tenant, RiskSeverity? riskFilter)
        {
            var items = Contracts
                .Where(c => c.TenantId == tenant && Documents.Any(d => d.TenantId == tenant && d.ContractId == c.Id))
                .OrderByDescending(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Select(c =>
                {
                    var worst = Risks.Where(r => r.TenantId == tenant && r.ContractId == c.Id)
                        .Select(r => (RiskSeverity?)r.Severity)
                        .DefaultIfEmpty(null)
                        .Max();
                    var newest = Documents.Where(d => d.TenantId == tenant && d.ContractId == c.Id)
                        .OrderByDescending(d => d.CreatedAt)
                        .First();
                    return new PortfolioListItem(
                        c.Id.Value,
                        c.SupplierId?.Value,
                        c.Type,
                        c.AnnualSpend,
                        c.Currency,
                        c.StartDate,
                        c.EndDate,
                        c.AutoRenewal ? c.EndDate : null,
                        c.CancellationDeadline,
                        c.AutoRenewal,
                        c.Status,
                        worst,
                        newest.FileName,
                        newest.ProcessingStatus,
                        c.NoticePeriodDays);
                });

            return (riskFilter is { } risk ? items.Where(i => i.Risk == risk) : items).ToList();
        }
    }

    private static Contract NewContract(TenantId tenant, DateTimeOffset createdAt) => new()
    {
        TenantId = tenant,
        Type = ContractDocumentType.Msa,
        Status = "Active",
        Currency = "EUR",
        AnnualSpend = 1000m,
        EndDate = new DateOnly(2027, 1, 1),
        AutoRenewal = createdAt.Hour % 2 == 0,
        CreatedAt = createdAt,
    };

    private static Risk NewRisk(TenantId tenant, EntityId contractId, RiskSeverity severity) => new()
    {
        TenantId = tenant,
        ContractId = contractId,
        RiskType = "test-risk",
        Severity = severity,
        Description = "seeded for test",
        IdentifiedAt = Epoch,
    };

    private static Document NewDocument(
        TenantId tenant,
        DocumentProcessingStatus status,
        DateTimeOffset createdAt,
        EntityId? contractId = null,
        string fileName = "doc.pdf") => new()
    {
        TenantId = tenant,
        ContractId = contractId,
        FileName = fileName,
        MimeType = "application/pdf",
        StoragePath = $"{tenant.Value}/{Guid.NewGuid():N}.pdf",
        Checksum = $"checksum-{Guid.NewGuid():N}",
        ProcessingStatus = status,
        CreatedAt = createdAt,
    };

    /// <summary>One InMemory database shared by a seeding context and a service context, with a query
    /// counter on the service side.</summary>
    private sealed class Env
    {
        private readonly InMemoryDatabaseRoot _root = new();
        private readonly string _name = $"query-trimming-{Guid.NewGuid()}";
        private readonly TenantContext _tenantContext = new();

        public QueryCounter Counter { get; } = new();

        public PortfolioQueryService Portfolio => new(Open(Counter), _tenantContext, new PortfolioAnalysisCalculator());

        public async Task SeedAsync(Action<DocumentsContractsDbContext> seed)
        {
            await using var db = Open(counter: null);
            seed(db);
            await db.SaveChangesAsync();
        }

        public Task<DocumentListPage> ListAsync(TenantId tenant, DocumentProcessingStatus? status, int page, int pageSize) =>
            new DocumentQueryService(Open(Counter), _tenantContext).ListAsync(tenant, status, page, pageSize);

        private DocumentsContractsDbContext Open(QueryCounter? counter)
        {
            var builder = new DbContextOptionsBuilder<DocumentsContractsDbContext>()
                .UseInMemoryDatabase(_name, _root)
                // A fresh internal service provider per context: no query compiled by an earlier call
                // is cached, so every distinct query shape of a call is compiled (and counted) again.
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .ReplaceService<IModelCustomizer, IgnoreVectorModelCustomizer>();
            if (counter is not null)
            {
                builder.AddInterceptors(counter);
            }

            return new DocumentsContractsDbContext(builder.Options);
        }
    }

    private sealed class QueryCounter : IQueryExpressionInterceptor
    {
        private int _compiled;

        public int Compiled => _compiled;

        public void Reset() => Interlocked.Exchange(ref _compiled, 0);

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Interlocked.Increment(ref _compiled);
            return queryExpression;
        }
    }

    // The pgvector column has no InMemory mapping; same single-property carve-out InMemoryDocumentsDb makes.
    private sealed class IgnoreVectorModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Embedding>().Ignore(e => e.Vector);
        }
    }
}
