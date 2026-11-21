using Xunit;

public sealed class FactoryStageFortyOneApsSlotsTests
{
    private static readonly DateOnly Day=new(2026,10,27);
    private static readonly Guid Center=Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static FactoryApsUnavailableSlot Slot(DateOnly from, string start,
        DateOnly to,string end) => new(Center,
            PersianPlanningDates.Format(from),start,
            PersianPlanningDates.Format(to),end,"تعویض ابزار","CHANGEOVER");

    [Fact]
    public void Valid_partial_shift_and_midnight_windows_keep_tehran_local_clock()
    {
        Assert.True(FactoryApsScenarioSlots.TryWindow(
            Slot(Day,"10:00",Day,"12:00"),Day,2,out var interval));
        Assert.Equal(new DateTime(2026,10,27,10,0,0),interval.StartLocal);
        Assert.Equal(new DateTime(2026,10,27,12,0,0),interval.EndLocal);
        Assert.Equal(DateTimeKind.Unspecified,interval.StartLocal.Kind);
        Assert.True(FactoryApsScenarioSlots.TryWindow(
            Slot(Day,"23:00",Day.AddDays(1),"01:00"),Day,2,out var night));
        Assert.Equal(Day.AddDays(1).ToDateTime(new TimeOnly(1,0)),night.EndLocal);
    }

    [Theory]
    [InlineData("11:00","10:00")]
    [InlineData("10:00","10:00")]
    [InlineData("garbage","11:00")]
    [InlineData("10:00","25:10")]
    public void Invalid_or_zero_duration_slots_do_not_become_machine_capacity(
        string begin,string finish)
    {
        Assert.False(FactoryApsScenarioSlots.TryWindow(
            Slot(Day,begin,Day,finish),Day,1,out _));
    }

    [Fact]
    public void Slots_outside_horizon_or_longer_than_two_days_are_rejected()
    {
        Assert.False(FactoryApsScenarioSlots.TryWindow(
            Slot(Day.AddDays(-1),"10:00",Day,"10:00"),Day,2,out _));
        Assert.False(FactoryApsScenarioSlots.TryWindow(
            Slot(Day.AddDays(1),"10:00",Day.AddDays(2),"10:00"),Day,2,out _));
        Assert.False(FactoryApsScenarioSlots.TryWindow(
            Slot(Day,"10:00",Day.AddDays(3),"12:00"),Day,7,out _));
    }

    [Fact]
    public void Same_center_intervals_cannot_overlap_but_adjacent_slots_are_permitted()
    {
        FactoryApsScenarioSlots.TryWindow(Slot(Day,"09:00",Day,"11:00"),Day,1,out var a);
        FactoryApsScenarioSlots.TryWindow(Slot(Day,"11:00",Day,"12:00"),Day,1,out var b);
        FactoryApsScenarioSlots.TryWindow(Slot(Day,"10:30",Day,"13:00"),Day,1,out var c);
        Assert.False(FactoryApsScenarioSlots.Overlaps([a,b]));
        Assert.True(FactoryApsScenarioSlots.Overlaps([a,c]));
        Assert.False(FactoryApsScenarioSlots.Overlaps([
            a,c with{WorkCenterId=Guid.NewGuid()}]));
    }

    [Fact]
    public void Three_hour_operation_moves_behind_tool_changeover_instead_of_overlapping_it()
    {
        var shift=new WorkShift{WorkCenterId=Center,DayOfWeek=(int)Day.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),
            EffectiveCapacityPercent=100};
        var task=new ApsTask(Guid.NewGuid(),Guid.NewGuid(),Day,2,
            Guid.NewGuid(),10,Center,180,"Shock absorber test");
        var baseline=FiniteFactoryScheduler.Schedule([task],[shift],Day,1);
        Assert.Equal(new TimeOnly(8,0),Assert.Single(baseline.Allocations).StartsAt);
        Assert.True(FactoryApsScenarioSlots.TryWindow(
            Slot(Day,"10:00",Day,"12:00"),Day,1,out var maintenance));
        var blocked=FiniteFactoryScheduler.Schedule([task],[shift],Day,1,
            occupiedIntervals:[maintenance]);
        Assert.Equal(new TimeOnly(12,0),Assert.Single(blocked.Allocations).StartsAt);
        Assert.Equal(new TimeOnly(15,0),blocked.Allocations[0].FinishesAt);
        Assert.Empty(blocked.Unscheduled);
    }
}
