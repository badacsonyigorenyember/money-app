using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class AdminEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AdminEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_integrity_check_reports_a_healthy_ledger()
    {
        using var client = _factory.CreateApiClient();

        var report = await (await client.PostAsync("/api/v1/admin/integrity-check", null,
                                                   CancellationToken.None))
            .Content.ReadFromJsonAsync<IntegrityReportDto>(CancellationToken.None);

        report!.IsHealthy.Should().BeTrue();
        report.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task The_json_export_downloads_as_a_file()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/admin/export?format=json",
                                             CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("money-export.json");
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("schemaVersion");
    }

    [Fact]
    public async Task The_csv_export_downloads_with_the_flat_entry_header()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/admin/export?format=csv",
                                             CancellationToken.None);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().StartWith("TransactionId,OccurredOn,Description");
    }

    [Fact]
    public async Task An_unsupported_export_format_is_a_four_hundred()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/admin/export?format=xml",
                                             CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_export_never_contains_the_word_posting()
    {
        using var client = _factory.CreateApiClient();
        await SeedATransactionAsync(client);

        var json = await client.GetStringAsync("/api/v1/admin/export?format=json",
                                               CancellationToken.None);

        json.Should().NotContain("posting", "the export is a user-facing document");
        json.Should().Contain("entries");
    }

    // A fresh ApiFactory ledger has no transactions, and the export's per-transaction "entries"
    // property only serializes on transaction objects that exist - an empty ledger contains the
    // word "entries" nowhere, regardless of naming. Seed one transaction so the assertion checks
    // what it says it checks, instead of passing vacuously.
    private static async Task SeedATransactionAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var bank = await (await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bank " + suffix, kind = "Asset", role = "Bank", currencyCode = "EUR",
                  openingBalance = 1000m, openedOn = "2026-01-01" },
            CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        var category = await (await client.PostAsJsonAsync("/api/v1/categories",
            new { name = "Food " + suffix, kind = "Expense" }, CancellationToken.None))
            .Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None);

        await client.PostAsJsonAsync("/api/v1/transactions/quick-entry",
            new { amount = 12.50m, categoryId = category!.Id, accountId = bank!.Id,
                  occurredOn = "2026-09-01", description = "Lunch" },
            CancellationToken.None);
    }
}
