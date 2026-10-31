using Xunit;

public sealed class FactoryApsCumulativeYieldTests
{
    [Fact]
    public void Aps_operation_quantity_and_duration_include_downstream_yield_losses()
    {
        var item = Guid.NewGuid();
        var formingCenter = Guid.NewGuid();
        var assemblyCenter = Guid.NewGuid();
        var due = new DateOnly(2026, 11, 10);
        var route = new RoutingRevision
        {
            ItemId = item,
            RevisionCode = "A",
            State = RoutingState.Approved,
            EffectiveFrom = due.AddDays(-10),
            Operations =
            [
                new RoutingOperation
                {
                    Sequence = 10, Name = "Forming", WorkCenterId = formingCenter,
                    SetupMinutes = 5m, RunMinutesPerUnit = 2m, YieldPercent = 90m
                },
                new RoutingOperation
                {
                    Sequence = 20, Name = "Final assembly", WorkCenterId = assemblyCenter,
                    SetupMinutes = 10m, RunMinutesPerUnit = 3m, YieldPercent = 80m
                }
            ]
        };
        var line = new MpsDemandLine
        {
            Id = Guid.NewGuid(), ItemId = item, NeededOn = due, Quantity = 100m
        };

        var tasks = FactoryApsTaskExpansion.Expand(line, route)
            .OrderBy(x => x.OperationSequence).ToArray();
        var formingQuantity = 100m / (0.90m * 0.80m);
        var assemblyQuantity = 100m / 0.80m;

        Assert.Equal(2, tasks.Length);
        Assert.Equal(10, tasks[0].OperationSequence);
        Assert.Equal(formingQuantity, tasks[0].Quantity, 6);
        Assert.Equal(5m + 2m * formingQuantity, tasks[0].DurationMinutes, 6);
        Assert.Equal(20, tasks[1].OperationSequence);
        Assert.Equal(assemblyQuantity, tasks[1].Quantity, 6);
        Assert.Equal(10m + 3m * assemblyQuantity, tasks[1].DurationMinutes, 6);
    }

    [Fact]
    public void Aps_rejects_route_with_invalid_yield_instead_of_understating_capacity()
    {
        var item = Guid.NewGuid();
        var route = new RoutingRevision
        {
            ItemId = item,
            State = RoutingState.Approved,
            EffectiveFrom = new DateOnly(2026, 11, 1),
            Operations =
            [
                new RoutingOperation
                {
                    Sequence = 10, Name = "Forming", WorkCenterId = Guid.NewGuid(),
                    SetupMinutes = 0m, RunMinutesPerUnit = 2m, YieldPercent = 101m
                }
            ]
        };
        var line = new MpsDemandLine
        {
            Id = Guid.NewGuid(), ItemId = item,
            NeededOn = new DateOnly(2026, 11, 10), Quantity = 10m
        };

        Assert.Throws<InvalidOperationException>(() => FactoryApsTaskExpansion.Expand(line, route));
    }

    [Fact]
    public void Aps_keeps_operation_quantities_unchanged_when_every_yield_is_one_hundred_percent()
    {
        var item = Guid.NewGuid();
        var due = new DateOnly(2026, 11, 10);
        var route = new RoutingRevision
        {
            ItemId = item,
            State = RoutingState.Approved,
            EffectiveFrom = due.AddDays(-1),
            Operations =
            [
                new RoutingOperation
                {
                    Sequence = 10, Name = "Welding", WorkCenterId = Guid.NewGuid(),
                    SetupMinutes = 4m, RunMinutesPerUnit = 1.5m, YieldPercent = 100m
                },
                new RoutingOperation
                {
                    Sequence = 20, Name = "Assembly", WorkCenterId = Guid.NewGuid(),
                    SetupMinutes = 3m, RunMinutesPerUnit = 2m, YieldPercent = 100m
                }
            ]
        };
        var line = new MpsDemandLine
        {
            Id = Guid.NewGuid(), ItemId = item, NeededOn = due, Quantity = 12m
        };

        var tasks = FactoryApsTaskExpansion.Expand(line, route).ToArray();

        Assert.All(tasks, task => Assert.Equal(12m, task.Quantity));
        Assert.Equal(22m, tasks[0].DurationMinutes);
        Assert.Equal(27m, tasks[1].DurationMinutes);
    }
}
