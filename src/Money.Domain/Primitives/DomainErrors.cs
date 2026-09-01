namespace Money.Domain.Primitives;

public static class DomainErrors
{
    public static class Currency
    {
        public static DomainError InvalidCode(string code) =>
            new("currency.invalid_code", $"'{code}' is not a three-letter ISO-4217 currency code.");

        public static DomainError Unknown(string code) =>
            new("currency.unknown", $"Currency '{code}' is not in the known-currency table.");

        public static DomainError InvalidMinorUnitExponent(int exponent) =>
            new("currency.invalid_minor_unit_exponent",
                $"Minor-unit exponent {exponent} is outside the supported range 0-4.");

        public static DomainError InvalidMinorUnitExponent(string code, int requestedExponent, int canonicalExponent) =>
            new("currency.invalid_minor_unit_exponent",
                $"'{code}' is a known currency with minor-unit exponent {canonicalExponent}, " +
                $"not {requestedExponent}. Use FromCode, or Create with the canonical exponent.");
    }

    public static class Period
    {
        public static DomainError AnchorDayOutOfRange(int day) =>
            new("period.anchor_day_out_of_range",
                $"The period anchor day must be between 1 and 28, but was {day}. " +
                "Days above 28 do not exist in every month, which would make period boundaries ambiguous.");

        public static DomainError UnknownTimeZone(string id) =>
            new("period.unknown_time_zone", $"'{id}' is not a time zone this machine recognises.");

        public static DomainError IndexOutOfRange(string type, int index) =>
            new("period.index_out_of_range", $"{index} is not a valid index for a {type} period.");

        public static DomainError UnparsableKey(string text) =>
            new("period.unparsable_key", $"'{text}' is not a valid period key such as '2026-M09'.");

        public static DomainError EndBeforeStart(DateOnly start, DateOnly endExclusive) =>
            new("period.end_before_start",
                $"The end date {endExclusive:O} must be after the start date {start:O}.");
    }

    public static class Account
    {
        public static DomainError NameRequired() =>
            new("account.name_required", "A name is required.");

        public static DomainError NameTooLong(int max) =>
            new("account.name_too_long", $"The name must be {max} characters or fewer.");

        public static DomainError KindRoleMismatch(string kind, string role) =>
            new("account.kind_role_mismatch", $"A '{role}' account cannot have kind '{kind}'.");

        public static DomainError ParentKindMismatch(string childKind, string parentKind) =>
            new("account.parent_kind_mismatch",
                $"A '{childKind}' account cannot sit under a '{parentKind}' parent.");

        public static DomainError ParentArchived(string parentName) =>
            new("account.parent_archived", $"'{parentName}' is archived and cannot take new children.");

        public static DomainError CurrencyMismatchWithParent() =>
            new("account.currency_mismatch_with_parent",
                "A child account must use the same currency as its parent.");

        public static DomainError DuplicateSiblingName(string name) =>
            new("account.duplicate_sibling_name", $"'{name}' already exists at this level.");

        public static DomainError CannotReparentUnderOwnDescendant() =>
            new("account.cannot_reparent_under_own_descendant",
                "An account cannot be moved underneath one of its own descendants.");

        public static DomainError AlreadyArchived(string name) =>
            new("account.already_archived", $"'{name}' is already archived.");

        public static DomainError NotFound(Guid id) =>
            new("account.not_found", $"Account {id} does not exist.");

        public static DomainError NotACategory(string name) =>
            new("account.not_a_category", $"'{name}' is not a category.");

        public static DomainError NameUnusable(string name) =>
            new("account.name_unusable",
                $"'{name}' contains no letters or digits, so it cannot form a path segment.");
    }

    public static class Transaction
    {
        public static DomainError TooFewPostings(int count) =>
            new("transaction.too_few_entries",
                $"A transaction needs at least two entries, but had {count}.");

        public static DomainError DoesNotBalance(string currencyCode, long residualMinor) =>
            new("transaction.does_not_balance",
                $"The {currencyCode} entries do not balance; they are off by {residualMinor} minor units.");

        public static DomainError ZeroAmount() =>
            new("transaction.zero_amount", "An entry cannot be for zero.");

        public static DomainError AccountArchived(string accountName) =>
            new("transaction.account_archived",
                $"'{accountName}' is archived and cannot be used in new entries.");

        public static DomainError AccountUnknown(Guid accountId) =>
            new("transaction.account_unknown", $"Account {accountId} does not exist.");

        public static DomainError CurrencyMismatchWithAccount(string accountName) =>
            new("transaction.currency_mismatch_with_account",
                $"The amount's currency does not match the currency of '{accountName}'.");

        public static DomainError DescriptionRequired() =>
            new("transaction.description_required", "A description is required.");

        public static DomainError DuplicateAccount(string accountName) =>
            new("transaction.duplicate_account",
                $"'{accountName}' appears more than once; combine the entries instead.");

        public static DomainError AlreadyVoided() =>
            new("transaction.already_voided", "This transaction has already been voided.");

        public static DomainError CannotEditVoided() =>
            new("transaction.cannot_edit_voided", "A voided transaction cannot be edited.");

        public static DomainError VoidReasonRequired() =>
            new("transaction.void_reason_required", "A reason is required when voiding.");

        public static DomainError NotFound(Guid id) =>
            new("transaction.not_found", $"Transaction {id} does not exist.");

        public static DomainError DateInFuture(DateOnly occurredOn, DateOnly today) =>
            new("transaction.date_too_far_in_future",
                $"{occurredOn:O} is more than a year after {today:O}; check the date.");
    }

    public static class Settings
    {
        public static DomainError AlreadyInitialised() =>
            new("settings.already_initialised", "First-run setup has already been completed.");

        public static DomainError NotInitialised() =>
            new("settings.not_initialised", "First-run setup has not been completed yet.");
    }

    public static class Admin
    {
        public static DomainError BackupFailed(string reason) =>
            new("admin.backup_failed", $"The backup could not be created: {reason}");

        public static DomainError UnsupportedExportFormat(string format) =>
            new("admin.unsupported_export_format", $"'{format}' is not a supported export format.");
    }
}
