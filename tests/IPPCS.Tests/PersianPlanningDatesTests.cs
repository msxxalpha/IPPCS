using Xunit;

public class PersianPlanningDatesTests
{
    [Theory]
    [InlineData("1405/08/04", 2026, 10, 26)]
    [InlineData("۱۴۰۵/۰۸/۰۴", 2026, 10, 26)]
    [InlineData("١٤٠٥/٠٨/٠٤", 2026, 10, 26)]
    public void Jalali_input_roundtrips_to_gregorian(string input, int year, int month, int day)
    {
        Assert.True(PersianPlanningDates.TryParse(input, out var parsed));
        Assert.Equal(new DateOnly(year, month, day), parsed);
        Assert.Equal("1405/08/04", PersianPlanningDates.Format(parsed));
    }

    [Theory]
    [InlineData("1405/12/30")]
    [InlineData("1405/13/01")]
    [InlineData("1405/01/32")]
    [InlineData("1405/00/02")]
    [InlineData("1405/08/00")]
    [InlineData("1405/08/04 xyz")]
    [InlineData("")]
    public void Invalid_jalali_dates_are_rejected(string input)
    {
        Assert.False(PersianPlanningDates.TryParse(input, out _));
    }

    [Fact]
    public void Leap_year_has_thirty_esfand_days()
    {
        Assert.True(PersianPlanningDates.TryParse("1399/12/30", out var date));
        Assert.Equal(new DateOnly(2021,3,20), date);
    }
}
