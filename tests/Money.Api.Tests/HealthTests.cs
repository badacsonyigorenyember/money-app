using System.Net;

namespace Money.Api.Tests;

public sealed class HealthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// The only route in the app that is not a page. CI starts the published exe headless and
    /// waits for this before calling the single-file publish good, so it has to keep answering.
    /// </summary>
    [Fact]
    public async Task Health_reports_ok()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
