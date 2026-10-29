using Xunit;

public sealed class FactoryStageTwentyOneYieldCapacityTests
{
    [Fact]
    public void Product_capacity_accounts_for_all_downstream_yield_losses()
    {
        var item = Guid.NewGuid();
        var center1 = Guid.NewGuid();
        var center2 = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 25); // Sunday
        var route = new RoutingRevision
        {
            ItemId = item,
            RevisionCode = "A",
            EffectiveFrom = day.AddDays(-30),
            State = RoutingState.Approved,
            Operations =
            [
                new RoutingOperation
                {
                    Sequence = 1, Name = "Forming", WorkCenterId = center1,
                    SetupMinutes = 5m, RunMinutesPerUnit = 2m, YieldPercent = 90m
                },
                new RoutingOperation
                {
                    Sequence = 2, Name = "Assembly", WorkCenterId = center2,
                    SetupMinutes = 10m, RunMinutesPerUnit = 3m, YieldPercent = 80m
                }
            ]
        };
        var line = new MpsDemandLine
        {
            Id = Guid.NewGuid(), ItemId = item, NeededOn = day, Quantity = 100m
        };
        var shifts = new[]
        {
            new WorkShift { WorkCenterId = center1, DayOfWeek = 0,
                StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0) },
            new WorkShift { WorkCenterId = center2, DayOfWeek = 0,
                StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0) }
        };

        var rows = PlanningCalculators.RunProductCapacityCrp(
            new[] { line }, new[] { route }, shifts).OrderBy(x => x.OperationSequence).ToArray();

        Assert.Equal(2, rows.Length);
        // Operation 1 must cover its own 90% yield and downstream assembly's 80% yield.
        Assert.Equal(100m / (0.9m * 0.8m) * 2m + 5m, rows[0].RequiredMinutes, 5);
        Assert.Equal(100m / 0.8m * 3m + 10m, rows[1].RequiredMinutes, 5);
        Assert.Equal(214m, rows[0].StandaloneCapacityUnits);
        Assert.Equal(157m, rows[1].StandaloneCapacityUnits);
    }

    [Fact]
    public void Rough_cut_capacity_uses_the_same_cumulative_yield_math()
    {
        var item = Guid.NewGuid();
        var center1 = Guid.NewGuid();
        var center2 = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 25);
        var route = new RoutingRevision
        {
            ItemId = item,
            EffectiveFrom = day.AddDays(-30),
            State = RoutingState.Approved,
            Operations =
            [
                new RoutingOperation { Sequence = 1, WorkCenterId = center1,
                    SetupMinutes = 5m, RunMinutesPerUnit = 2m, YieldPercent = 90m },
                new RoutingOperation { Sequence = 2, WorkCenterId = center2,
                    SetupMinutes = 10m, RunMinutesPerUnit = 3m, YieldPercent = 80m }
            ]
        };
        var line = new MpsDemandLine { ItemId = item, NeededOn = day, Quantity = 100m };
        var shifts = new[]
        {
            new WorkShift { WorkCenterId = center1, DayOfWeek = 0,
                StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0) },
            new WorkShift { WorkCenterId = center2, DayOfWeek = 0,
                StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0) }
        };

        var rows = PlanningCalculators.RunRoughCutCrp(new[] { line }, new[] { route }, shifts);

        Assert.Equal(2, rows.Count);
        Assert.Equal(100m / (0.9m * 0.8m) * 2m + 5m,
            rows.Single(x => x.WorkCenterId == center1).RequiredMinutes, 5);
        Assert.Equal(100m / 0.8m * 3m + 10m,
            rows.Single(x => x.WorkCenterId == center2).RequiredMinutes, 5);
    }
}
