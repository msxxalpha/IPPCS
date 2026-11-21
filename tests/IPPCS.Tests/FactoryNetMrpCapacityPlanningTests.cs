using Xunit;

public sealed class FactoryNetMrpCapacityPlanningTests
{
    private static readonly DateOnly Baseline = new(2026, 11, 2);

    private static NetPegPlannedLot Lot(string key, Guid item, DateOnly release,
        DateOnly receipt, decimal quantity) =>
        new(key, item, receipt, release, quantity, quantity, 0m,
            "Manufacturing", Guid.NewGuid(), release < Baseline, 0m);

    private static RoutingRevision Route(Guid item, Guid center, decimal runMinutes = 1m,
        decimal setupMinutes = 5m, decimal yield = 100m) =>
        new()
        {
            ItemId = item,
            State = RoutingState.Approved,
            EffectiveFrom = Baseline.AddDays(-30),
            Operations =
            [
                new RoutingOperation
                {
                    Sequence = 10, WorkCenterId = center, Name = "Damper assembly",
                    SetupMinutes = setupMinutes, RunMinutesPerUnit = runMinutes,
                    YieldPercent = yield
                }
            ]
        };

    private static WorkShift Shift(Guid center, DateOnly date) =>
        new()
        {
            WorkCenterId = center, DayOfWeek = (int)date.DayOfWeek,
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            EffectiveCapacityPercent = 100
        };

    [Fact]
    public void Crp_uses_net_manufacturing_lot_quantity_not_gross_mps_demand()
    {
        var item = Guid.NewGuid();
        var center = Guid.NewGuid();
        var release = Baseline.AddDays(1);
        var receipt = release.AddDays(2);
        var lot = Lot("PLAN:NET-20", item, release, receipt, 20m);

        var rows = FactoryNetMrpCapacityPlanning.RunProductCapacityCrp(
            [lot], [Route(item, center)], [Shift(center, release)], Baseline);

        var row = Assert.Single(rows);
        Assert.Equal(20m, row.DemandQuantity);
        Assert.Equal(release, row.NeededOn);
        Assert.Equal(25m, row.RequiredMinutes); // 5 setup + 20 x 1 minute
    }

    [Fact]
    public void Fully_covered_gross_demand_produces_no_crp_load_without_a_manufacturing_lot()
    {
        var item = Guid.NewGuid();
        var center = Guid.NewGuid();
        var external = Lot("PLAN:EXTERNAL", item, Baseline, Baseline.AddDays(1), 10m)
            with { ProposalType = "ExternalPlanningOnly" };

        var rows = FactoryNetMrpCapacityPlanning.RunRoughCutCrp(
            [external], [Route(item, center)], [Shift(center, Baseline)], Baseline);

        Assert.Empty(rows);
    }

    [Fact]
    public void Crp_aggregates_multiple_net_lots_on_the_same_center_and_release_day()
    {
        var center = Guid.NewGuid();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();
        var first = Lot("PLAN:A", itemA, Baseline, Baseline.AddDays(1), 10m);
        var second = Lot("PLAN:B", itemB, Baseline, Baseline.AddDays(1), 15m);

        var rows = FactoryNetMrpCapacityPlanning.RunRoughCutCrp(
            [first, second], [Route(itemA, center), Route(itemB, center)],
            [Shift(center, Baseline)], Baseline);

        var row = Assert.Single(rows);
        Assert.Equal(35m, row.RequiredMinutes); // two setup cycles + 25 run minutes
        Assert.Equal(480m, row.CapacityMinutes);
    }

    [Fact]
    public void Past_due_net_release_is_bucketed_at_the_planning_baseline()
    {
        var item = Guid.NewGuid();
        var center = Guid.NewGuid();
        var lot = Lot("PLAN:PAST", item, Baseline.AddDays(-2),
            Baseline.AddDays(1), 8m);

        var rows = FactoryNetMrpCapacityPlanning.RunRoughCutCrp(
            [lot], [Route(item, center)], [Shift(center, Baseline)], Baseline);

        Assert.Equal(Baseline, Assert.Single(rows).NeededOn);
    }

    [Fact]
    public void Product_capacity_override_and_yield_are_applied_to_net_lot()
    {
        var item = Guid.NewGuid();
        var center = Guid.NewGuid();
        var route = Route(item, center, runMinutes: 2m, yield: 80m);
        var opId = route.Operations.Single().Id;
        var lot = Lot("PLAN:RATE", item, Baseline, Baseline.AddDays(1), 10m);

        var row = Assert.Single(FactoryNetMrpCapacityPlanning.RunProductCapacityCrp(
            [lot], [route], [Shift(center, Baseline)], Baseline,
            new Dictionary<Guid, decimal> { [opId] = 30m }));

        Assert.True(row.HasProductCapacityOverride);
        Assert.Equal(30m, row.ProductCapacityUnitsPerHour);
        Assert.Equal(25m, row.RequiredMinutes); // 5 setup + 10 x (2 / 0.8)
    }
}
