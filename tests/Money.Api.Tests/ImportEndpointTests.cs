using System.Net;
using System.Net.Http.Json;
using Money.Application.Abstractions;
using Money.Application.Contracts;
using Money.Domain.Primitives;

namespace Money.Api.Tests;

/// <summary>
/// The import wired end to end - route, DI, handler, SQLite - with the feed itself stubbed.
/// </summary>
public sealed class ImportEndpointTests
{
    private static readonly BankTransaction Spar = new(
        "eb:OTP-1", new DateOnly(2026, 8, 30), -847_550, "HUF", "SPAR 1234", "SPAR");

    private static readonly BankTransaction Salary = new(
        "eb:OTP-2", new DateOnly(2026, 8, 31), 45_000_000, "HUF", "MUNKABER", "MUNKAADO ZRT");

    private static async Task<Guid> CreateHufAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/accounts", new CreateAccountRequest(
            "OTP current", "Asset", "Bank", null, "HUF", null, null), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AccountDto>(CancellationToken.None))!.Id;
    }

    [Fact]
    public async Task A_sync_files_bank_lines_under_unclassified_and_is_idempotent()
    {
        using var factory = new ApiFactory { BankFeed = new StubFeed(Spar, Salary) };
        using var client = factory.CreateApiClient();

        var accountId = await CreateHufAccountAsync(client);

        var first = await Import(client, accountId);
        first!.Imported.Should().Be(2);
        first.From.Should().Be(new DateOnly(2026, 7, 29));
        first.To.Should().Be(new DateOnly(2026, 9, 1));

        var second = await Import(client, accountId);
        second!.Imported.Should().Be(0);
        second.AlreadyPresent.Should().Be(2);

        var page = await client.GetFromJsonAsync<TransactionPageDto>(
            "/api/v1/transactions", CancellationToken.None);

        page!.Items.Should().HaveCount(2);

        // Double-entry vocabulary never reaches a caller: the counterparty reads as a category.
        // The list's Category column is populated from the Expense leg only, so a money-in line
        // leaves it blank here exactly as a manually entered income does - the Unclassified
        // filter still finds both, because that filters on entries rather than on the headline.
        page.Items.Single(item => item.Description == "SPAR 1234")
            .CategoryName.Should().Be("Unclassified");
        page.Items.Single(item => item.Description == "MUNKABER")
            .CategoryName.Should().BeNull();
    }

    [Fact]
    public async Task Imported_lines_are_findable_by_filtering_on_the_unclassified_category()
    {
        using var factory = new ApiFactory { BankFeed = new StubFeed(Spar, Salary) };
        using var client = factory.CreateApiClient();

        var accountId = await CreateHufAccountAsync(client);
        await Import(client, accountId);

        var categories = await client.GetFromJsonAsync<IReadOnlyList<AccountDto>>(
            "/api/v1/accounts?kind=Income&role=Category", CancellationToken.None);

        var unclassifiedIncome = categories!.Single(c => c.Path == "/income/unclassified");

        var page = await client.GetFromJsonAsync<TransactionPageDto>(
            $"/api/v1/transactions?categoryId={unclassifiedIncome.Id}", CancellationToken.None);

        page!.Items.Should().ContainSingle().Which.Description.Should().Be("MUNKABER");
    }

    [Fact]
    public async Task A_feed_failure_comes_back_as_problem_details()
    {
        using var factory = new ApiFactory
        {
            BankFeed = new StubFeed(new DomainError(
                "bankfeed.session_expired", "Re-link the account."))
        };
        using var client = factory.CreateApiClient();

        var accountId = await CreateHufAccountAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/import/bank",
            new ImportBankTransactionsRequest(accountId, null), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("bankfeed.session_expired");
    }

    private static async Task<ImportResultDto?> Import(HttpClient client, Guid accountId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/import/bank",
            new ImportBankTransactionsRequest(accountId, null), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<ImportResultDto>(CancellationToken.None);
    }

    private sealed class StubFeed : IBankFeed
    {
        private readonly IReadOnlyList<BankTransaction> _lines;
        private readonly DomainError? _error;

        public StubFeed(params BankTransaction[] lines) => _lines = lines;

        public StubFeed(DomainError error)
        {
            _lines = [];
            _error = error;
        }

        public Task<Result<IReadOnlyList<BankTransaction>>> FetchBookedAsync(
            DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken = default) =>
            Task.FromResult(_error is not null
                ? Result<IReadOnlyList<BankTransaction>>.Fail(_error)
                : Result<IReadOnlyList<BankTransaction>>.Ok(_lines));
    }
}
