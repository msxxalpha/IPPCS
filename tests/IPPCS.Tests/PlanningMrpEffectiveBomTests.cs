using Xunit;

public sealed class PlanningMrpEffectiveBomTests
{
    [Fact]
    public void Historical_Bom_component_without_demand_does_not_require_planning_balance()
    {
        var today = new DateOnly(2026, 10, 29);
        var product = Guid.NewGuid();
        var currentRaw = Guid.NewGuid();
        var historicalRaw = Guid.NewGuid();

        var historical = new ManufacturingBomRevision
        {
            ParentItemId = product,
            Revision = "OLD",
            State = ManufacturingRevisionState.Approved,
            ValidFrom = today.AddDays(-30),
            ValidUntil = today.AddDays(-1),
            Lines = [new ManufacturingBomLine
            {
                ComponentItemId = historicalRaw,
                QuantityPer = 2,
                ScrapPercent = 0,
                Sequence = 10
            }]
        };
        var current = new ManufacturingBomRevision
        {
            ParentItemId = product,
            Revision = "CURRENT",
            State = ManufacturingRevisionState.Approved,
            ValidFrom = today,
            Lines = [new ManufacturingBomLine
            {
                ComponentItemId = currentRaw,
                QuantityPer = 3,
                ScrapPercent = 0,
                Sequence = 10
            }]
        };

        var demand = new MpsDemandLine
        {
            Id = Guid.NewGuid(),
            ItemId = product,
            NeededOn = today.AddDays(2),
            Quantity = 5
        };
        var balances = new[]
        {
            new PlanningBalance { ItemId = product, SnapshotDate = today, PhysicalQuantity = 0 },
            new PlanningBalance { ItemId = currentRaw, SnapshotDate = today, PhysicalQuantity = 100 }
        };
        var types = new Dictionary<Guid, PlanningItemType>
        {
            [product] = PlanningItemType.Finished,
            [currentRaw] = PlanningItemType.RawMaterial,
            [historicalRaw] = PlanningItemType.RawMaterial
        };

        var rows = PlanningCalculators.RunMrp(
            [demand], [historical, current], balances, [],
            types);

        Assert.Contains(rows, x => x.ItemId == currentRaw && x.GrossRequired == 15);
        Assert.DoesNotContain(rows, x => x.ItemId == historicalRaw);
    }
}
