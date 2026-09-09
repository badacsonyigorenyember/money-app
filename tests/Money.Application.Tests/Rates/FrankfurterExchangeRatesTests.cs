using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Money.Infrastructure.Rates;
using Money.TestSupport;

namespace Money.Application.Tests.Rates;

/// <summary>
/// Frankfurter's wire format and, more importantly, what happens when it is not there. A local
/// app is expected to be run with the network off; that has to cost one failed request, not one
/// per account per page load, and it has to end in "no rate" rather than an exception or an
/// invented number.
/// </summary>
public sealed class FrankfurterExchangeRatesTests
{
    private const string Latest =
        """{"amount":1.0,"base":"EUR","date":"2026-09-08","rates":{"HUF":363.95,"USD":1.1614}}""";

    private readonly FakeClock _clock = FakeClock.At(2026, 9, 8, 9, 0);

    private (FrankfurterExchangeRates Rates, StubHandler Handler) Build(
        string body = Latest, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(body) { Status = status };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") };

        return (new FrankfurterExchangeRates(http, _clock, NullLogger<FrankfurterExchangeRates>.Instance), handler);
    }

    [Fact]
    public async Task A_quoted_pair_comes_back_as_the_rate_the_source_published()
    {
        var (rates, handler) = Build();

        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m);

        handler.Requests.Should().ContainSingle()
            .Which.Should().EndWith("/v1/latest?base=EUR");
    }

    [Fact]
    public async Task A_currency_against_itself_needs_no_source_at_all()
    {
        var (rates, handler) = Build();

        (await rates.RateAsync("eur", "EUR")).Should().Be(1m);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task One_fetch_answers_every_pair_that_shares_a_base()
    {
        // Six accounts on one page must not be six requests.
        var (rates, handler) = Build();

        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m);
        (await rates.RateAsync("EUR", "USD")).Should().Be(1.1614m);
        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m);

        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_currency_the_source_does_not_quote_is_unknown_rather_than_wrong()
    {
        var (rates, _) = Build();

        (await rates.RateAsync("EUR", "GBP")).Should().BeNull();
        (await rates.RateAsync("EUR", "nonsense")).Should().BeNull();
    }

    [Fact]
    public async Task With_no_network_there_is_no_rate_and_no_exception()
    {
        var handler = new StubHandler(Latest) { Throw = new HttpRequestException("offline") };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") };
        var rates = new FrankfurterExchangeRates(http, _clock, NullLogger<FrankfurterExchangeRates>.Instance);

        (await rates.RateAsync("EUR", "HUF")).Should().BeNull();
    }

    [Fact]
    public async Task A_failed_fetch_is_retried_later_rather_than_on_the_very_next_ask()
    {
        var handler = new StubHandler(Latest) { Status = HttpStatusCode.ServiceUnavailable };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") };
        var rates = new FrankfurterExchangeRates(http, _clock, NullLogger<FrankfurterExchangeRates>.Instance);

        (await rates.RateAsync("EUR", "HUF")).Should().BeNull();
        (await rates.RateAsync("EUR", "USD")).Should().BeNull();
        handler.Requests.Should().HaveCount(1, "an offline page must not fire one request per account");

        _clock.UtcNow = _clock.UtcNow.AddMinutes(6);
        handler.Status = HttpStatusCode.OK;

        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m, "the failure was not cached for the day");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_request_the_browser_dropped_is_not_remembered_as_a_missing_rate()
    {
        // The page's own cancellation token cancels on a reload or an htmx swap. Recording that
        // as "the source has no rate" left an account off the chart for every reload in the next
        // five minutes, which is indistinguishable from an account that can never be drawn.
        var handler = new StubHandler(Latest);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") };
        var rates = new FrankfurterExchangeRates(http, _clock, NullLogger<FrankfurterExchangeRates>.Instance);

        using var abandoned = new CancellationTokenSource();
        await abandoned.CancelAsync();

        (await rates.RateAsync("EUR", "HUF", abandoned.Token)).Should().BeNull();

        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m, "the very next ask asks again");
    }

    [Fact]
    public async Task A_rate_that_once_worked_survives_a_fetch_that_later_fails()
    {
        // Offline for a moment is not the same as unknown. Yesterday's rate draws a better chart
        // than no chart, so a failed refresh moves the retry timer and nothing else.
        var handler = new StubHandler(Latest);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") };
        var rates = new FrankfurterExchangeRates(http, _clock, NullLogger<FrankfurterExchangeRates>.Instance);

        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m);

        _clock.UtcNow = _clock.UtcNow.AddHours(13);
        handler.Status = HttpStatusCode.ServiceUnavailable;

        (await rates.RateAsync("EUR", "HUF")).Should().Be(363.95m, "the last good table is still the best answer");
        handler.Requests.Should().HaveCount(2, "it did try for a fresher one");
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Throw { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!.ToString());

            if (Throw is not null) return Task.FromException<HttpResponseMessage>(Throw);

            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
