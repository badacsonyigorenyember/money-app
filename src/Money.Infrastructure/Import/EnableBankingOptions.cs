namespace Money.Infrastructure.Import;

/// <summary>
/// What the Enable Banking Control Panel gives you for one linked account.
///
/// <paramref name="PrivateKeyPem"/> is the PKCS#8 key downloaded when the application was
/// created; it is the credential that signs every request, so it belongs in user secrets or an
/// environment variable, never in a committed appsettings file.
///
/// <paramref name="SessionId"/> comes from the authorisation redirect and expires - up to 180
/// days under the amended PSD2 RTS, less if OTP says so. When it lapses the feed returns
/// bankfeed.session_expired and the account has to be re-linked in the Control Panel.
/// </summary>
public sealed record EnableBankingOptions(
    string ApplicationId,
    string PrivateKeyPem,
    string SessionId,
    string AccountUid)
{
    public const string SectionName = "EnableBanking";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApplicationId)
        && !string.IsNullOrWhiteSpace(PrivateKeyPem)
        && !string.IsNullOrWhiteSpace(AccountUid);
}
