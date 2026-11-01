using Xunit;

public sealed class FactoryStageFiftyThreeProductCapacityTests
{
    private static DateOnly Due => new(2026, 11, 10);

    private static (RoutingRevision route, Guid firstOperation, Guid secondOperation, Guid firstCenter, Guid secondCenter)
        Route()
    {
        var item = Guid.NewGuid();
        var firstCenter = Guid.NewGuid();
        var secondCenter = Guid.NewGuid();
        var firstOperation = Guid.NewGuid();
        var secondOperation = Guid.NewGuid();
        var route = new RoutingRevision
        {
            Id = Guid.NewGuid(),
            ItemId = item,
            RevisionCode = "A",
            State = RoutingState.Approved,
            EffectiveFrom = Due.AddDays(-10),
            Operations =
            [
                new RoutingOperation
                {
                    Id = firstOperation, Sequence = 10, Name = "Forming",
                    WorkCenterId = firstCenter, SetupMinutes = 5m,
                    RunMinutesPerUnit = 4m, YieldPercent = 90m
                },
                new RoutingOperation
                {
                    Id = secondOperation, Sequence = 20, Name = "Final assembly",
                    WorkCenterId = secondCenter, SetupMinutes = 10m,
                    RunMinutesPerUnit = 6m, YieldPercent = 80m
                }
            ]
        };
        return (route, firstOperation, secondOperation, firstCenter, secondCenter);
    }

    private static WorkShift[] Shifts(Guid first, Guid second) =>
    [
        new WorkShift { WorkCenterId = first, DayOfWeek = (int)Due.DayOfWeek,
            StartTime = new TimeOnly(8,0), EndTime = new TimeOnly(16,0),
            EffectiveCapacityPercent = 80m },
        new WorkShift { WorkCenterId = second, DayOfWeek = (int)Due.DayOfWeek,
            StartTime = new TimeOnly(8,0), EndTime = new TimeOnly(16,0),
            EffectiveCapacityPercent = 80m }
    ];

    [Fact]
    public void Product_capacity_override_changes_CRPs_operation_rate_and_exposes_the_source()
    {
        var (route, firstId, secondId, firstCenter, secondCenter) = Route();
        var line = new MpsDemandLine { ItemId = route.ItemId, NeededOn = Due, Quantity = 100m };
        var shifts = Shifts(firstCenter, secondCenter);
        var rates = new Dictionary<Guid, decimal> { [firstId] = 30m, [secondId] = 20m };

        var rows = PlanningCalculators.RunProductCapacityCrp([line], [route], shifts, rates)
            .OrderBy(x => x.OperationSequence).ToArray();

        Assert.Equal(2, rows.Length);
        Assert.True(rows[0].HasProductCapacityOverride);
        Assert.Equal(30m, rows[0].ProductCapacityUnitsPerHour);
        Assert.Equal(2m, rows[0].RunMinutesPerUnit);
        Assert.Equal(5m + 100m * 2m / (0.90m * 0.80m), rows[0].RequiredMinutes, 5);
        Assert.Equal(20m, rows[1].ProductCapacityUnitsPerHour);
        Assert.Equal(3m, rows[1].RunMinutesPerUnit);
        Assert.Equal(10m + 100m * 3m / 0.80m, rows[1].RequiredMinutes, 5);
    }

    [Fact]
    public void Missing_or_inactive_product_capacity_falls_back_to_the_approved_routing_standard()
    {
        var (route, firstId, _, firstCenter, secondCenter) = Route();
        var line = new MpsDemandLine { ItemId = route.ItemId, NeededOn = Due, Quantity = 100m };
        var rows = PlanningCalculators.RunProductCapacityCrp([line], [route],
            Shifts(firstCenter, secondCenter),
            new Dictionary<Guid, decimal> { [firstId] = 30m })
            .OrderBy(x => x.OperationSequence).ToArray();

        Assert.True(rows[0].HasProductCapacityOverride);
        Assert.False(rows[1].HasProductCapacityOverride);
        Assert.Null(rows[1].ProductCapacityUnitsPerHour);
        Assert.Equal(6m, rows[1].RunMinutesPerUnit);
        Assert.Equal(10m + 100m * 6m / 0.80m, rows[1].RequiredMinutes, 5);
    }

    [Fact]
    public void Shared_rough_cut_CRp_uses_the_same_product_specific_rate_override()
    {
        var (route, firstId, secondId, firstCenter, secondCenter) = Route();
        var line = new MpsDemandLine { ItemId = route.ItemId, NeededOn = Due, Quantity = 100m };
        var rates = new Dictionary<Guid, decimal> { [firstId] = 30m, [secondId] = 20m };

        var rows = PlanningCalculators.RunRoughCutCrp([line], [route],
            Shifts(firstCenter, secondCenter), rates);

        Assert.Equal(2, rows.Count);
        Assert.Equal(5m + 100m * 2m / (0.90m * 0.80m),
            rows.Single(x => x.WorkCenterId == firstCenter).RequiredMinutes, 5);
        Assert.Equal(10m + 100m * 3m / 0.80m,
            rows.Single(x => x.WorkCenterId == secondCenter).RequiredMinutes, 5);
    }

    [Fact]
    public void Applying_rates_changes_only_the_in_memory_routing_snapshot_not_the_saved_standard()
    {
        var (route, firstId, secondId, _, _) = Route();
        var originalFirst = route.Operations.Single(x => x.Id == firstId).RunMinutesPerUnit;
        var originalSecond = route.Operations.Single(x => x.Id == secondId).RunMinutesPerUnit;

        WorkCenterProductCapacityRules.ApplyToRoutes([route],
            new Dictionary<Guid, decimal> { [firstId] = 30m, [secondId] = 20m });

        Assert.Equal(2m, route.Operations.Single(x => x.Id == firstId).RunMinutesPerUnit);
        Assert.Equal(3m, route.Operations.Single(x => x.Id == secondId).RunMinutesPerUnit);
        Assert.Equal(4m, originalFirst);
        Assert.Equal(6m, originalSecond);
    }

    [Fact]
    public void Capacity_evidence_keeps_routing_standard_separate_from_effective_product_rate()
    {
        var (route, firstId, secondId, _, _) = Route();
        var evidence = WorkCenterProductCapacityRules.BuildEvidence([route],
            new Dictionary<Guid, decimal> { [firstId] = 30m });

        var overridden = evidence.Single(x => x.RoutingOperationId == firstId);
        Assert.Equal(route.Id, overridden.RoutingRevisionId);
        Assert.Equal(route.ItemId, overridden.ItemId);
        Assert.Equal(4m, overridden.RoutingRunMinutesPerUnit);
        Assert.Equal(2m, overridden.EffectiveRunMinutesPerUnit);
        Assert.Equal(30m, overridden.CapacityUnitsPerHour);
        Assert.True(overridden.HasProductCapacityOverride);

        var standard = evidence.Single(x => x.RoutingOperationId == secondId);
        Assert.Equal(6m, standard.RoutingRunMinutesPerUnit);
        Assert.Equal(6m, standard.EffectiveRunMinutesPerUnit);
        Assert.Null(standard.CapacityUnitsPerHour);
        Assert.False(standard.HasProductCapacityOverride);

        // Capturing evidence must not mutate approved Routing inputs.
        Assert.Equal(4m, route.Operations.Single(x => x.Id == firstId).RunMinutesPerUnit);
        Assert.Equal(6m, route.Operations.Single(x => x.Id == secondId).RunMinutesPerUnit);
    }

    [Fact]
    public void Invalid_zero_throughput_never_produces_an_infinite_or_zero_duration()
    {
        var (route, firstId, _, _, _) = Route();
        Assert.Throws<InvalidOperationException>(() =>
            WorkCenterProductCapacityRules.ApplyToRoutes([route],
                new Dictionary<Guid, decimal> { [firstId] = 0m }));
    }
}
