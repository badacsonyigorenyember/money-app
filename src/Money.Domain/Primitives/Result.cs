namespace Money.Domain.Primitives;

public readonly struct Result
{
    private readonly bool _isSuccess;

    private Result(bool isSuccess, DomainError? error)
    {
        _isSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;
    public DomainError? Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(DomainError error) => new(false, error);

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    public static Result<T> Fail<T>(DomainError error) => Result<T>.Fail(error);

    public static implicit operator Result(DomainError error) => Fail(error);
}

public readonly struct Result<T>
{
    private readonly bool _isSuccess;
    private readonly T? _value;

    private Result(bool isSuccess, T? value, DomainError? error)
    {
        _isSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;
    public DomainError? Error { get; }

    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Result is a failure ({Error?.Code}: {Error?.Message}); its value cannot be read.");

    // CA1000 (avoid static members on generic types) is intentionally suppressed here:
    // Result<T>.Ok / Result<T>.Fail are the exact factory shape this codebase's callers
    // depend on (see task brief for Result). Result.Ok<T> / Result.Fail<T> exist as the
    // non-generic-friendly alternative, but the generic-typed factories stay public API.
#pragma warning disable CA1000
    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(DomainError error) => new(false, default, error);
#pragma warning restore CA1000

    public static implicit operator Result<T>(DomainError error) => Fail(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        _isSuccess ? Result<TOut>.Ok(map(_value!)) : Result<TOut>.Fail(ErrorOrThrowIfDefault());

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<DomainError, TOut> onFailure) =>
        _isSuccess ? onSuccess(_value!) : onFailure(ErrorOrThrowIfDefault());

    // Error is only ever null in the failure branch when this Result<T> was reached via
    // default(Result<T>) rather than Ok/Fail/the implicit conversion: Fail(DomainError error)
    // takes a non-nullable parameter under Nullable=enable, so a legitimately-constructed
    // failure always carries a non-null Error. Map/Match must not hand that null onward to a
    // caller-supplied handler (an NRE inside the handler, or a silently-produced failure with
    // a null Error one step further downstream, are both surprising and hard to trace back to
    // the real cause). Fail loudly here instead, at the point the default state is observed.
    private DomainError ErrorOrThrowIfDefault() =>
        Error ?? throw new InvalidOperationException(
            "Result<T> was used in its default, uninitialised state. Results must be created " +
            "via Result<T>.Ok, Result<T>.Fail, or the implicit conversion from DomainError - " +
            "never via default(Result<T>).");
}
