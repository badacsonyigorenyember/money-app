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
}
