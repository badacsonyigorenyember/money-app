using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Money.Infrastructure.Import;
using Money.TestSupport;

namespace Money.Application.Tests.Import;

/// <summary>
/// The Enable Banking wire format, pinned against canned responses. No network: what is under
/// test is the JWT this code signs and the mapping from their JSON onto the ledger's own
/// sign convention.
/// </summary>
public sealed class EnableBankingClientTests
{
    private static readonly DateOnly From = new(2026, 7, 29);
    private static readonly DateOnly To = new(2026, 9, 1);

    private readonly RSA _key = RSA.Create(2048);
    private readonly FakeClock _clock = FakeClock.At(2026, 9, 1, 9, 0);

    private (EnableBankingClient Client, StubHandler Handler) Build(params string[] responses)
    {
        var handler = new StubHandler(responses);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.enablebanking.com") };
        var options = new EnableBankingOptions(
            ApplicationId: "app-123",
            PrivateKeyPem: _key.ExportPkcs8PrivateKeyPem(),
            SessionId: "sess-1",
            AccountUid: "acct-uid-1");

        return (new EnableBankingClient(http, options, _clock), handler);
    }

    private static string Body(string transactions, string? continuationKey = null) =>
        $$"""
          {
            "transactions": [{{transactions}}],
            "continuation_key": {{(continuationKey is null ? "null" : $"\"{continuationKey}\"")}}
          }
          """;

    private const string Spar = """
        {
          "entry_reference": "OTP-778899",
          "booking_date": "2026-08-30",
          "value_date": "2026-08-30",
          "status": "BOOK",
          "credit_debit_indicator": "DBIT",
          "transaction_amount": { "amount": "8475.50", "currency": "HUF" },
          "creditor": { "name": "SPAR MAGYARORSZAG KFT" },
          "remittance_information": ["CARD PURCHASE", "SPAR 1234 BUDAPEST"]
        }
        """;

    private const string Salary = """
        {
          "entry_reference": "OTP-778900",
          "booking_date": "2026-08-31",
          "status": "BOOK",
          "credit_debit_indicator": "CRDT",
          "transaction_amount": { "amount": "450000.00", "currency": "HUF" },
          "debtor": { "name": "MUNKAADO ZRT" },
          "remittance_information": ["MUNKABER 2026/08"]
        }
        """;

    [Fact]
    public async Task A_debit_becomes_a_negative_amount_and_a_credit_a_positive_one()
    {
        var (client, _) = Build(Body($"{Spar},{Salary}"));

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Money out of the account is a credit to it: negative, in the ledger's convention.
        var spar = result.Value.Single(t => t.ExternalRef == "eb:OTP-778899");
        spar.AmountMinor.Should().Be(-847_550);

        var salary = result.Value.Single(t => t.ExternalRef == "eb:OTP-778900");
        salary.AmountMinor.Should().Be(45_000_000);
    }

    [Fact]
    public async Task The_counterparty_is_read_from_whichever_side_is_not_the_user()
    {
        var (client, _) = Build(Body($"{Spar},{Salary}"));

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Value.Single(t => t.AmountMinor < 0).Payee.Should().Be("SPAR MAGYARORSZAG KFT");
        result.Value.Single(t => t.AmountMinor > 0).Payee.Should().Be("MUNKAADO ZRT");
    }

    [Fact]
    public async Task Remittance_lines_become_the_description_and_the_booking_date_the_ledger_date()
    {
        var (client, _) = Build(Body(Spar));

        var line = (await client.FetchBookedAsync(From, To, CancellationToken.None)).Value.Single();

        line.Description.Should().Be("CARD PURCHASE SPAR 1234 BUDAPEST");
        line.BookedOn.Should().Be(new DateOnly(2026, 8, 30));
        line.CurrencyCode.Should().Be("HUF");
    }

    [Fact]
    public async Task A_pending_entry_is_dropped_even_if_the_bank_ignores_the_status_filter()
    {
        var pending = Spar.Replace("\"BOOK\"", "\"PDNG\"", StringComparison.Ordinal)
                          .Replace("OTP-778899", "OTP-pending", StringComparison.Ordinal);

        var (client, _) = Build(Body($"{pending},{Salary}"));

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Value.Should().ContainSingle().Which.ExternalRef.Should().Be("eb:OTP-778900");
    }

    [Fact]
    public async Task A_line_with_no_entry_reference_gets_a_stable_hashed_one()
    {
        var noRef = Spar.Replace("\"entry_reference\": \"OTP-778899\",", "", StringComparison.Ordinal);

        var first = (await Build(Body(noRef)).Client
            .FetchBookedAsync(From, To, CancellationToken.None)).Value.Single();
        var second = (await Build(Body(noRef)).Client
            .FetchBookedAsync(From, To, CancellationToken.None)).Value.Single();

        first.ExternalRef.Should().StartWith("eb:h:");
        second.ExternalRef.Should().Be(first.ExternalRef);
    }

    [Fact]
    public async Task Pagination_follows_the_continuation_key_until_it_runs_out()
    {
        var (client, handler) = Build(
            Body(Spar, continuationKey: "page-2"),
            Body(Salary));

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Value.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Should().Contain("date_from=2026-07-29").And.Contain("date_to=2026-09-01");
        handler.Requests[1].Should().Contain("continuation_key=page-2");
    }

    [Fact]
    public async Task The_request_carries_a_bearer_jwt_this_application_actually_signed()
    {
        var (client, handler) = Build(Body(""));

        await client.FetchBookedAsync(From, To, CancellationToken.None);

        var jwt = handler.Authorization!.Replace("Bearer ", "", StringComparison.Ordinal);
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);

        var header = JsonDocument.Parse(Decode(parts[0])).RootElement;
        header.GetProperty("alg").GetString().Should().Be("RS256");
        header.GetProperty("kid").GetString().Should().Be("app-123");

        var body = JsonDocument.Parse(Decode(parts[1])).RootElement;
        body.GetProperty("iss").GetString().Should().Be("enablebanking.com");
        body.GetProperty("aud").GetString().Should().Be("api.enablebanking.com");
        body.GetProperty("iat").GetInt64().Should().Be(_clock.UtcNow.ToUnixTimeSeconds());
        body.GetProperty("exp").GetInt64().Should().Be(_clock.UtcNow.ToUnixTimeSeconds() + 3600);

        var signed = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        _key.VerifyData(signed, DecodeBytes(parts[2]),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .Should().BeTrue();
    }

    [Fact]
    public async Task An_expired_authorisation_says_so_rather_than_failing_opaquely()
    {
        var handler = new StubHandler([]) { Status = HttpStatusCode.Unauthorized };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.enablebanking.com") };
        var client = new EnableBankingClient(http, new EnableBankingOptions(
            "app-123", _key.ExportPkcs8PrivateKeyPem(), "sess-1", "acct-uid-1"), _clock);

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Error!.Code.Should().Be("bankfeed.session_expired");
    }

    [Fact]
    public async Task Exhausting_the_daily_unattended_fetch_allowance_is_reported_as_such()
    {
        var handler = new StubHandler([]) { Status = HttpStatusCode.TooManyRequests };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.enablebanking.com") };
        var client = new EnableBankingClient(http, new EnableBankingOptions(
            "app-123", _key.ExportPkcs8PrivateKeyPem(), "sess-1", "acct-uid-1"), _clock);

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Error!.Code.Should().Be("bankfeed.rate_limited");
    }

    [Fact]
    public async Task A_network_failure_is_a_result_not_an_exception()
    {
        var handler = new StubHandler([]) { Throw = new HttpRequestException("no route to host") };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.enablebanking.com") };
        var client = new EnableBankingClient(http, new EnableBankingOptions(
            "app-123", _key.ExportPkcs8PrivateKeyPem(), "sess-1", "acct-uid-1"), _clock);

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Error!.Code.Should().Be("bankfeed.unavailable");
    }

    private static string Decode(string segment) => Encoding.UTF8.GetString(DecodeBytes(segment));

    private static byte[] DecodeBytes(string segment) =>
        System.Buffers.Text.Base64Url.DecodeFromChars(segment);

    private sealed class StubHandler(string[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string> Requests { get; } = [];
        public string? Authorization { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public Exception? Throw { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw is not null) return Task.FromException<HttpResponseMessage>(Throw);

            Requests.Add(request.RequestUri!.ToString());
            Authorization = request.Headers.Authorization?.ToString();

            var body = _next < responses.Length ? responses[_next++] : "{}";

            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task An_unconfigured_feed_says_so_instead_of_calling_anything()
    {
        var handler = new StubHandler([]);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.enablebanking.com") };
        var client = new EnableBankingClient(
            http, new EnableBankingOptions("", "", "", ""), _clock);

        var result = await client.FetchBookedAsync(From, To, CancellationToken.None);

        result.Error!.Code.Should().Be("bankfeed.not_configured");
        handler.Requests.Should().BeEmpty();
    }
}
