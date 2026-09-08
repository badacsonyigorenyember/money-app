using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Money.Application.Abstractions;
using Money.Application.Presentation;
using Money.Domain.Accounts;
using Money.Domain.Money;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Infrastructure.Import;

/// <summary>
/// Reads booked transactions for one linked account from Enable Banking's aggregation API.
///
/// Enable Banking holds the PSD2 licence and the eIDAS certificates; this app authenticates as
/// an application of theirs with a short-lived RS256 JWT. No JWT library is pulled in for that -
/// two base64url segments and one RSA signature is the whole scheme.
/// </summary>
public sealed class EnableBankingClient(
    HttpClient http, EnableBankingOptions options, IClock clock) : IBankFeed
{
    private const int TokenTtlSeconds = 3600;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<IReadOnlyList<BankTransaction>>> FetchBookedAsync(
        DateOnly fromInclusive, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
            return new DomainError("bankfeed.not_configured",
                "No bank feed is set up. Add the EnableBanking application id, private key and " +
                "account uid to configuration, then restart.");

        var collected = new List<BankTransaction>();
        string? continuationKey = null;

        try
        {
            var token = SignToken();

            do
            {
                var page = await GetPageAsync(
                    token, fromInclusive, toInclusive, continuationKey, cancellationToken);
                if (page.IsFailure) return page.Error!;

                foreach (var entry in page.Value.Transactions ?? [])
                {
                    // Belt and braces on top of transaction_status=BOOK in the query: not every
                    // ASPSP honours the filter, and a pending entry re-issued under a new
                    // reference once it books would be imported twice.
                    if (!string.Equals(entry.Status, "BOOK", StringComparison.Ordinal)) continue;

                    if (Map(entry) is { } mapped) collected.Add(mapped);
                }

                continuationKey = page.Value.ContinuationKey;
            }
            while (!string.IsNullOrEmpty(continuationKey));
        }
        catch (HttpRequestException ex)
        {
            return new DomainError("bankfeed.unavailable", $"The bank feed is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DomainError("bankfeed.unavailable", "The bank feed timed out.");
        }
        catch (JsonException ex)
        {
            return new DomainError("bankfeed.unreadable", $"The bank feed sent something unreadable: {ex.Message}");
        }
        catch (CryptographicException ex)
        {
            return new DomainError("bankfeed.bad_credentials",
                $"The Enable Banking private key could not be used to sign a request: {ex.Message}");
        }

        return Result<IReadOnlyList<BankTransaction>>.Ok(collected);
    }

    private async Task<Result<TransactionsResponse>> GetPageAsync(
        string token, DateOnly fromInclusive, DateOnly toInclusive,
        string? continuationKey, CancellationToken cancellationToken)
    {
        var query = new StringBuilder()
            .Append("/accounts/").Append(Uri.EscapeDataString(options.AccountUid))
            .Append("/transactions?transaction_status=BOOK")
            .Append("&date_from=").Append(fromInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append("&date_to=").Append(toInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(continuationKey))
            query.Append("&continuation_key=").Append(Uri.EscapeDataString(continuationKey));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(query.ToString(), UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) return await ErrorFor(response, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<TransactionsResponse>(body, Json);

        return parsed is null
            ? new DomainError("bankfeed.unreadable", "The bank feed sent an empty response.")
            : Result<TransactionsResponse>.Ok(parsed);
    }

    private static async Task<DomainError> ErrorFor(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new DomainError(
                "bankfeed.session_expired",
                "The bank no longer accepts this authorisation. Re-link the account in the " +
                "Enable Banking Control Panel; PSD2 requires this roughly every 180 days."),

            HttpStatusCode.TooManyRequests => new DomainError(
                "bankfeed.rate_limited",
                "The bank has refused further requests for now. Unattended fetches are capped at " +
                "about four a day; try again later."),

            _ => new DomainError(
                "bankfeed.rejected",
                $"The bank feed answered {(int)response.StatusCode}: {Truncate(detail, 300)}")
        };
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";

    /// <summary>
    /// Maps one wire entry, or returns null for one that cannot be placed in the ledger at all
    /// (no date, no amount, an unusable currency code).
    /// </summary>
    private static BankTransaction? Map(TransactionEntry entry)
    {
        var bookedOn = FirstDate(entry.BookingDate, entry.ValueDate, entry.TransactionDate);
        if (bookedOn is not { } date) return null;

        if (entry.TransactionAmount?.Amount is not { } rawAmount) return null;
        if (!decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return null;

        var code = entry.TransactionAmount.Currency;
        if (string.IsNullOrWhiteSpace(code)) return null;

        // A code outside the known table still round-trips: the import use case rejects it as
        // foreign currency anyway (an account's own currency is always a known one), and a rough
        // exponent here keeps that line counted as skipped rather than silently vanishing.
        var currency = Currency.FromCode(code);
        if (currency.IsFailure) currency = Currency.Create(code, 2);
        if (currency.IsFailure) return null;

        // Asset is not a display-negated kind, so ToStored is a pure scale-and-round here. It is
        // used rather than a second RoundToMinor call site by design (see RoundingTests).
        var minor = DisplayAmountMapper.ToStored(amount, AccountKind.Asset, currency.Value);
        if (minor == 0) return null;

        var isCredit = string.Equals(entry.CreditDebitIndicator, "CRDT", StringComparison.Ordinal);
        var signed = isCredit ? minor : -minor;

        // Whoever is on the other side of the money: we paid the creditor, or the debtor paid us.
        var payee = Clean(isCredit ? entry.Debtor?.Name : entry.Creditor?.Name);

        var remittance = entry.RemittanceInformation is { Count: > 0 }
            ? Clean(string.Join(' ', entry.RemittanceInformation.Where(s => !string.IsNullOrWhiteSpace(s))))
            : null;

        return new BankTransaction(
            ExternalRef: ReferenceFor(entry, date, signed, currency.Value.Code, payee, remittance),
            BookedOn: date,
            AmountMinor: signed,
            CurrencyCode: currency.Value.Code,
            Description: remittance ?? payee ?? "Bank transaction",
            Payee: payee);
    }

    /// <summary>
    /// The dedup key. An ASPSP-supplied entry_reference is authoritative; the hash is only for
    /// banks that supply none.
    ///
    /// ponytail: two genuinely identical lines on one day (same shop, same amount, no reference)
    /// hash alike, so the second is dropped as a duplicate. Add the entry's position within the
    /// day to the hash if that ever bites.
    /// </summary>
    private static string ReferenceFor(
        TransactionEntry entry, DateOnly date, long signed, string code, string? payee, string? remittance)
    {
        if (!string.IsNullOrWhiteSpace(entry.EntryReference)) return "eb:" + entry.EntryReference.Trim();

        var composite = string.Join(
            '|', date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            signed.ToString(CultureInfo.InvariantCulture), code, payee ?? "", remittance ?? "");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
        return "eb:h:" + Convert.ToHexString(hash)[..32];
    }

    private static DateOnly? FirstDate(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (DateOnly.TryParse(candidate, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }

        return null;
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// header.payload.signature, RS256, per Enable Banking's authentication scheme: kid is the
    /// application id, iss and aud are fixed, and the maximum accepted TTL is 24 hours.
    /// </summary>
    private string SignToken()
    {
        var issuedAt = clock.UtcNow.ToUnixTimeSeconds();

        var header = Segment($$"""{"typ":"JWT","alg":"RS256","kid":"{{options.ApplicationId}}"}""");
        var payload = Segment(
            $$"""
              {"iss":"enablebanking.com","aud":"api.enablebanking.com","iat":{{issuedAt}},"exp":{{issuedAt + TokenTtlSeconds}}}
              """);

        var signingInput = header + "." + payload;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(options.PrivateKeyPem);

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return signingInput + "." + Base64Url.EncodeToString(signature);

        static string Segment(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
    }

    private sealed record TransactionsResponse(
        IReadOnlyList<TransactionEntry>? Transactions,
        string? ContinuationKey);

    private sealed record TransactionEntry(
        string? EntryReference,
        string? BookingDate,
        string? ValueDate,
        string? TransactionDate,
        string? Status,
        string? CreditDebitIndicator,
        AmountEntry? TransactionAmount,
        PartyEntry? Creditor,
        PartyEntry? Debtor,
        [property: JsonPropertyName("remittance_information")]
        IReadOnlyList<string>? RemittanceInformation);

    private sealed record AmountEntry(string? Amount, string? Currency);

    private sealed record PartyEntry(string? Name);
}
