using Money.Domain.Primitives;

namespace Money.Domain.Tests.Primitives;

public sealed class ResultTests
{
    private static readonly DomainError SomeError = new("test.failed", "Something went wrong.");

    [Fact]
    public void A_successful_result_carries_its_value()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void A_failed_result_carries_its_error()
    {
        var result = Result<int>.Fail(SomeError);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_is_a_programmer_error()
    {
        var result = Result<int>.Fail(SomeError);

        var act = () => _ = result.Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*test.failed*");
    }

    [Fact]
    public void A_domain_error_converts_implicitly_to_a_failed_result()
    {
        Result<string> result = SomeError;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void A_default_constructed_result_is_a_failure_rather_than_a_silent_success()
    {
        default(Result<int>).IsSuccess.Should().BeFalse();
        default(Result).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Map_transforms_a_success_and_passes_a_failure_through()
    {
        Result<int>.Ok(21).Map(x => x * 2).Value.Should().Be(42);
        Result<int>.Fail(SomeError).Map(x => x * 2).Error.Should().Be(SomeError);

        // F2: the failure branch must not evaluate the projection at all. A `Map` that
        // eagerly called `map(_value!)` before checking success/failure would not be
        // caught by the assertions above (int defaults to 0, so `0 * 2` throws nothing) -
        // a throwing projection makes eager evaluation observable.
        var act = () => Result<int>.Fail(SomeError).Map<int>(x => throw new InvalidOperationException("must not be called"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Match_picks_the_branch_matching_the_outcome()
    {
        Result<int>.Ok(1).Match(v => $"ok:{v}", e => $"err:{e.Code}").Should().Be("ok:1");
        Result<int>.Fail(SomeError).Match(v => $"ok:{v}", e => $"err:{e.Code}").Should().Be("err:test.failed");
    }

    [Fact]
    public void Match_on_a_default_constructed_result_fails_loudly_instead_of_handing_a_null_error_to_the_failure_branch()
    {
        var act = () => default(Result<int>).Match(v => "ok", e => e.Code);

        act.Should().Throw<InvalidOperationException>().WithMessage("*default*");
    }

    [Fact]
    public void Map_on_a_default_constructed_result_fails_loudly_instead_of_producing_a_failure_with_a_null_error()
    {
        var act = () => default(Result<int>).Map(x => x * 2);

        act.Should().Throw<InvalidOperationException>().WithMessage("*default*");
    }
}
