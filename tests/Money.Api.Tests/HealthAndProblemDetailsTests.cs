using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Money.Api.Tests;

public sealed class HealthAndProblemDetailsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthAndProblemDetailsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_reports_ok()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_openapi_document_is_served_at_the_versioned_path()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        body.Should().Contain("/api/v1/transactions");
    }

    [Fact]
    public async Task A_missing_account_produces_an_rfc_9457_problem_document()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync(
            $"/api/v1/accounts/{Guid.NewGuid()}/balance", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        document.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        document.RootElement.GetProperty("code").GetString().Should().Be("account.not_found");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://moneyapp.local/problems/account.not_found");
    }

    [Fact]
    public async Task A_validation_failure_is_a_four_hundred()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/accounts",
            new { name = "Bad", kind = "Asset", role = "Category", currencyCode = "EUR" },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));
        document.RootElement.GetProperty("code").GetString().Should().Be("account.kind_role_mismatch");
    }

    [Fact]
    public async Task A_conflict_is_a_four_hundred_and_nine()
    {
        using var client = _factory.CreateApiClient();

        var create = new { name = "Cash", kind = "Asset", role = "Cash", currencyCode = "EUR" };
        await client.PostAsJsonAsync("/api/v1/accounts", create, CancellationToken.None);
        var second = await client.PostAsJsonAsync("/api/v1/accounts", create,
                                                  CancellationToken.None);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
