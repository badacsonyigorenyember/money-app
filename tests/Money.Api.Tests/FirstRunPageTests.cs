using System.Net;
using System.Text.RegularExpressions;

namespace Money.Api.Tests;

public sealed class FirstRunPageTests
{
    [Fact]
    public async Task A_fresh_database_redirects_the_home_page_to_the_wizard()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("FirstRun");
    }

    [Fact]
    public async Task The_wizard_offers_a_currency_a_period_anchor_and_a_first_account()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var html = await client.GetStringAsync("/FirstRun", CancellationToken.None);

        html.Should().Contain("Currency");
        html.Should().Contain("EUR");
        html.Should().Contain("starter categories");
        html.Should().Contain("kept in your user folder",
            "the wizard explains where the data lives (spec section 14)");
    }

    [Fact]
    public async Task The_as_of_date_defaults_to_today_in_the_configured_time_zone()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var html = await client.GetStringAsync("/FirstRun", CancellationToken.None);

        // ApiFactory.Clock is fixed at 2026-09-01 09:00 UTC, which is also 2026-09-01 in the
        // wizard's default Europe/Budapest zone - the same fixed date other tests in this suite
        // (e.g. TransactionsPageTests) assert against.
        var openedOnValue = Regex.Match(html, "id=\"OpenedOn\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

        openedOnValue.Should().Be("2026-09-01",
            "the wizard is the first screen a user ever sees; a 0001-01-01 default would silently " +
            "misdate their opening balance unless they notice and correct it");
    }

    [Fact]
    public async Task Completing_the_wizard_seeds_the_app_and_stops_redirecting()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        // A real browser would GET the wizard before submitting its <form>, which is what sets
        // the antiforgery cookie and stamps the header token onto <body> (Shared/_Layout.cshtml)
        // that this request replays - exactly like TransactionsPageTests' antiforgery tests do.
        var wizardHtml = await client.GetStringAsync("/FirstRun", CancellationToken.None);
        var token = Regex.Match(wizardHtml, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        token.Should().NotBeNullOrEmpty("the layout should always stamp a token onto <body>");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["BaseCurrencyCode"] = "EUR",
            ["PeriodAnchor"] = "CalendarMonth",
            ["PeriodAnchorDay"] = "1",
            ["TimeZoneId"] = "Europe/Budapest",
            ["FirstDayOfWeek"] = "Monday",
            ["FirstAccountName"] = "Current account",
            ["FirstAccountRole"] = "Bank",
            ["OpeningBalance"] = "1500.00",
            ["OpenedOn"] = "2026-01-01",
            ["SeedStarterCategories"] = "true"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/FirstRun") { Content = form };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request, CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var home = await client.GetAsync("/", CancellationToken.None);
        home.StatusCode.Should().Be(HttpStatusCode.Redirect);
        home.Headers.Location!.ToString().Should().ContainEquivalentOf("transactions");

        var categories = await client.GetStringAsync("/categories", CancellationToken.None);
        categories.Should().Contain("Groceries").And.Contain("Alcohol").And.Contain("Gaming");
    }

    [Fact]
    public async Task The_settings_page_renders_and_can_run_an_integrity_check()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var html = await client.GetStringAsync("/settings", CancellationToken.None);
        html.Should().Contain("Backup").And.Contain("Export").And.Contain("Integrity");

        var token = Regex.Match(html, "RequestVerificationToken\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        token.Should().NotBeNullOrEmpty("the layout should always stamp a token onto <body>");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/settings?handler=IntegrityCheck");
        request.Headers.Add("RequestVerificationToken", token);

        var checkResponse = await client.SendAsync(request, CancellationToken.None);
        checkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
