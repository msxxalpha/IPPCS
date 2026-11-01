using Xunit;

public sealed class FactoryStageThirtyEightManagementReportsTests
{
    [Fact]
    public void Persian_calendar_range_is_bounded_and_has_exclusive_tehran_end()
    {
        Assert.True(FactoryManagementReports.TryWindow(
            "۱۴۰۵/۰۸/۰۱", "۱۴۰۵/۰۸/۰۲",
            out var from, out var through, out var start, out var end));
        Assert.Equal(new DateOnly(2026,10,23),from);
        Assert.Equal(from.AddDays(1),through);
        Assert.Equal(48,(end-start).TotalHours);
        var tz=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        Assert.Equal(from.ToDateTime(TimeOnly.MinValue),
            TimeZoneInfo.ConvertTimeFromUtc(start,tz));
        Assert.Equal(through.AddDays(1).ToDateTime(TimeOnly.MinValue),
            TimeZoneInfo.ConvertTimeFromUtc(end,tz));
    }

    [Theory]
    [InlineData("۱۴۰۵/۰۸/۱۰","۱۴۰۵/۰۸/۰۱")]
    [InlineData("۱۴۰۵/۱۳/۰۱","۱۴۰۵/۰۸/۰۱")]
    [InlineData("۱۴۰۴/۰۱/۰۱","۱۴۰۵/۰۸/۰۱")]
    [InlineData("۱۴۰۵/۰۸/۱","")]
    public void Invalid_or_unbounded_period_does_not_publish_partial_reports(
        string from, string through)
    {
        Assert.False(FactoryManagementReports.TryWindow(from,through,
            out _,out _,out _,out _));
    }

    [Fact]
    public void Missing_denominator_is_null_never_a_fictitious_percentage()
    {
        Assert.Null(FactoryManagementReports.Percent(0,0));
        Assert.Null(FactoryManagementReports.Percent(20,-1));
        Assert.Equal(75m,FactoryManagementReports.Percent(3,4));
        Assert.Equal(33.33m,FactoryManagementReports.Percent(1,3));
    }

    [Fact]
    public void Utc_rollover_is_at_tehran_midnight_not_utc_midnight()
    {
        var tz=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var date=new DateOnly(2026,10,27);
        var utc=TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue),tz);
        Assert.Equal(date,FactoryManagementReports.LocalDate(utc));
        Assert.Equal(date.AddDays(-1),FactoryManagementReports.LocalDate(
            utc.AddSeconds(-1)));
    }
}
