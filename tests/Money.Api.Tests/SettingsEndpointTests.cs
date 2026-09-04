using System.Net;
using System.Net.Http.Json;
using Money.Application.Contracts;

namespace Money.Api.Tests;

public sealed class SettingsEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SettingsEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Settings_can_be_read_and_written()
    {
        using var client = _factory.CreateApiClient();

        var updated = await client.PutAsJsonAsync("/api/v1/settings",
            new { baseCurrencyCode = "EUR", periodAnchor = "DayOfMonth", periodAnchorDay = 25,
                  timeZoneId = "Europe/Budapest", firstDayOfWeek = "Monday", backupRetentionCount = 7 },
            CancellationToken.None);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await client.GetFromJsonAsync<SettingsDto>(
            "/api/v1/settings", CancellationToken.None);

        read!.PeriodAnchorDay.Should().Be(25);
        read.BackupRetentionCount.Should().Be(7);
    }

    [Fact]
    public async Task An_anchor_day_above_twenty_eight_is_a_four_hundred_with_the_explanation()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PutAsJsonAsync("/api/v1/settings",
            new { baseCurrencyCode = "EUR", periodAnchor = "DayOfMonth", periodAnchorDay = 31,
                  timeZoneId = "Europe/Budapest", firstDayOfWeek = "Monday", backupRetentionCount = 10 },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("not exist in every month");
    }
}
