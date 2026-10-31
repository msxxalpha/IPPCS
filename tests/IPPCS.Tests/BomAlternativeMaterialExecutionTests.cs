using Xunit;

public sealed class BomAlternativeMaterialExecutionTests
{
    [Fact]
    public void Lot_material_standards_follow_the_order_mix_and_each_option_usage_ratio()
    {
        var original = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 1m, ScrapPercent = 0, Sequence = 10,
            AlternativeGroupCode = "SEAL", IsDefaultAlternative = true, AlternativePriority = 1
        };
        var substitute = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 1.2m, ScrapPercent = 0, Sequence = 10,
            AlternativeGroupCode = "SEAL", IsDefaultAlternative = false, AlternativePriority = 2
        };
        var allocations = new[]
        {
            new ManufacturingOrderMaterialAllocation
            {
                OrderId = Guid.NewGuid(), BomLineId = original.Id,
                SharePercent = 60m, PlannedQuantity = 60m
            },
            new ManufacturingOrderMaterialAllocation
            {
                OrderId = Guid.NewGuid(), BomLineId = substitute.Id,
                SharePercent = 40m, PlannedQuantity = 48m
            }
        };
        var confirmation = new ManufacturingConfirmation
        {
            GoodQuantity = 100m, ScrapQuantity = 0m, ReworkQuantity = 0m
        };

        var rows = FactoryLineSideCorrections.LotBalances(
            confirmation, [original, substitute], [], allocations);

        Assert.Equal(60m, Assert.Single(rows, x => x.ComponentItemId == original.ComponentItemId)
            .StandardForReportedLot);
        Assert.Equal(48m, Assert.Single(rows, x => x.ComponentItemId == substitute.ComponentItemId)
            .StandardForReportedLot);
    }

    [Fact]
    public void Unallocated_alternative_group_defaults_only_to_the_engineering_default()
    {
        var original = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 2m, ScrapPercent = 0, Sequence = 10,
            AlternativeGroupCode = "VALVE", IsDefaultAlternative = true, AlternativePriority = 1
        };
        var substitute = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 2m, ScrapPercent = 0, Sequence = 10,
            AlternativeGroupCode = "VALVE", IsDefaultAlternative = false, AlternativePriority = 2
        };
        var confirmation = new ManufacturingConfirmation { GoodQuantity = 25m };

        var rows = FactoryLineSideCorrections.LotBalances(
            confirmation, [original, substitute], [], []);

        Assert.Equal(50m, Assert.Single(rows, x => x.ComponentItemId == original.ComponentItemId)
            .StandardForReportedLot);
        Assert.Equal(0m, Assert.Single(rows, x => x.ComponentItemId == substitute.ComponentItemId)
            .StandardForReportedLot);
    }

    [Fact]
    public void Material_variance_uses_selected_mix_instead_of_adding_every_substitute()
    {
        var original = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 1m, ScrapPercent = 0,
            AlternativeGroupCode = "SEAL", IsDefaultAlternative = true, AlternativePriority = 1
        };
        var substitute = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 1.2m, ScrapPercent = 0,
            AlternativeGroupCode = "SEAL", IsDefaultAlternative = false, AlternativePriority = 2
        };
        var allocations = new[]
        {
            new ManufacturingOrderMaterialAllocation
            {
                BomLineId = original.Id, SharePercent = 60m, PlannedQuantity = 60m
            },
            new ManufacturingOrderMaterialAllocation
            {
                BomLineId = substitute.Id, SharePercent = 40m, PlannedQuantity = 48m
            }
        };
        var usage = new[]
        {
            new FactoryMaterialUsage { ComponentItemId = original.ComponentItemId, QuantityConsumed = 59m },
            new FactoryMaterialUsage { ComponentItemId = substitute.ComponentItemId, QuantityConsumed = 50m }
        };

        var rows = FactoryOperationalMetrics.MaterialVariances(100m,
            [original, substitute], usage, allocations);

        Assert.Equal(60m, Assert.Single(rows, x => x.ComponentItemId == original.ComponentItemId)
            .PlannedRequired);
        Assert.Equal(48m, Assert.Single(rows, x => x.ComponentItemId == substitute.ComponentItemId)
            .PlannedRequired);
        Assert.Equal(-1m, Assert.Single(rows, x => x.ComponentItemId == original.ComponentItemId)
            .Variance);
        Assert.Equal(2m, Assert.Single(rows, x => x.ComponentItemId == substitute.ComponentItemId)
            .Variance);
    }
}
