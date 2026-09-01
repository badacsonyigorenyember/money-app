using Money.Domain.Periods;

namespace Money.Domain.Tests.Periods;

public sealed class PeriodKeyTests
{
    [Theory]
    [InlineData(PeriodType.Monthly, 2026, 9, "2026-M09")]
    [InlineData(PeriodType.Weekly, 2026, 36, "2026-W36")]
    [InlineData(PeriodType.Quarterly, 2026, 3, "2026-Q3")]
    [InlineData(PeriodType.Yearly, 2026, 1, "2026-Y")]
    public void A_key_renders_in_its_documented_string_form(
        PeriodType type, int year, int index, string expected)
    {
        PeriodKey.Create(type, year, index).Value.ToKeyString().Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-M09", PeriodType.Monthly, 2026, 9)]
    [InlineData("2026-W36", PeriodType.Weekly, 2026, 36)]
    [InlineData("2026-Q3", PeriodType.Quarterly, 2026, 3)]
    [InlineData("2026-Y", PeriodType.Yearly, 2026, 1)]
    public void Parsing_is_the_inverse_of_rendering(string text, PeriodType type, int year, int index)
    {
        PeriodKey.Parse(text).Value.Should().Be(PeriodKey.Create(type, year, index).Value);
    }

    [Theory]
    [InlineData(PeriodType.Monthly, 0)]
    [InlineData(PeriodType.Monthly, 13)]
    [InlineData(PeriodType.Quarterly, 5)]
    [InlineData(PeriodType.Weekly, 54)]
    [InlineData(PeriodType.Yearly, 2)]
    public void An_index_outside_its_type_s_range_is_rejected(PeriodType type, int index)
    {
        PeriodKey.Create(type, 2026, index).Error!.Code.Should().Be("period.index_out_of_range");
    }

    [Theory]
    [InlineData("2026-X09")]
    [InlineData("nonsense")]
    [InlineData("2026-M99")]
    [InlineData("2026-Y1")]
    [InlineData("")]
    public void An_unparsable_key_is_rejected(string text)
    {
        PeriodKey.Parse(text).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public void A_year_at_the_boundary_of_the_supported_range_is_accepted(int year)
    {
        PeriodKey.Create(PeriodType.Yearly, year, 1).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void A_year_outside_the_supported_range_is_rejected(int year)
    {
        PeriodKey.Create(PeriodType.Yearly, year, 1).Error!.Code.Should().Be("period.index_out_of_range");
    }

    [Fact]
    public void Parsing_a_null_key_is_rejected_rather_than_throwing()
    {
        var act = () => PeriodKey.Parse(null!);

        act.Should().NotThrow();
        PeriodKey.Parse(null!).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_whitespace_only_key_is_reported_verbatim_in_the_error()
    {
        PeriodKey.Parse("   ").Error!.Message.Should()
            .Be("'   ' is not a valid period key such as '2026-M09'.");
    }

    [Fact]
    public void A_key_missing_the_separator_dash_is_rejected_even_when_the_rest_would_parse()
    {
        // Position 4 must be '-'; without it the string must not be salvaged by the later
        // year/type/index parsing even though "2026" and "M09" would each parse in isolation.
        PeriodKey.Parse("2026XM09").IsFailure.Should().BeTrue();
    }
}
