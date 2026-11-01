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

    [Fact]
    public void Selected_alternative_quantities_follow_current_order_output_not_stale_saved_quantity()
    {
        var preferred = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 1m, ScrapPercent = 0,
            AlternativeGroupCode = "SEAL", IsDefaultAlternative = true
        };
        var substitute = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 1.25m, ScrapPercent = 0,
            AlternativeGroupCode = "SEAL", IsDefaultAlternative = false
        };
        var allocations = new[]
        {
            new ManufacturingOrderMaterialAllocation
            {
                BomLineId = preferred.Id, SharePercent = 60m, PlannedQuantity = 60m
            },
            new ManufacturingOrderMaterialAllocation
            {
                BomLineId = substitute.Id, SharePercent = 40m, PlannedQuantity = 50m
            }
        };

        Assert.Equal(120m, BomAlternativeExecution.CalculateSelectedComponentQuantity(
            200m, preferred, [preferred, substitute], allocations));
        Assert.Equal(100m, BomAlternativeExecution.CalculateSelectedComponentQuantity(
            200m, substitute, [preferred, substitute], allocations));
    }



    [Fact]
    public void Same_component_on_distinct_bom_lines_keeps_separate_lot_consumption()
    {
        var component = Guid.NewGuid();
        var firstLine = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = component,
            QuantityPer = 1m, ScrapPercent = 0, Sequence = 10
        };
        var secondLine = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = component,
            QuantityPer = 2m, ScrapPercent = 0, Sequence = 20
        };
        var confirmation = new ManufacturingConfirmation
        {
            Id = Guid.NewGuid(), GoodQuantity = 10m
        };
        var orderId = Guid.NewGuid();
        var usages = new[]
        {
            new FactoryMaterialUsage
            {
                OrderId = orderId, ComponentItemId = component, QuantityConsumed = 4m,
                Reference = FactoryLineSideCorrections.LotReference(
                    confirmation.Id, firstLine.Id, "first BOM line")
            },
            new FactoryMaterialUsage
            {
                OrderId = orderId, ComponentItemId = component, QuantityConsumed = 7m,
                Reference = FactoryLineSideCorrections.LotReference(
                    confirmation.Id, secondLine.Id, "second BOM line")
            }
        };

        var ledger = FactoryLineSideCorrections.Ledger(usages);
        var balances = FactoryLineSideCorrections.LotBalances(
            confirmation, [firstLine, secondLine], ledger, []);

        var first = Assert.Single(balances, x => x.BomLineId == firstLine.Id);
        var second = Assert.Single(balances, x => x.BomLineId == secondLine.Id);
        Assert.Equal(10m, first.StandardForReportedLot);
        Assert.Equal(4m, first.RecordedNetQuantity);
        Assert.Equal(20m, second.StandardForReportedLot);
        Assert.Equal(7m, second.RecordedNetQuantity);
        Assert.All(ledger, x => Assert.False(x.IsLegacyUnattributed));
    }

    [Fact]
    public void Legacy_lot_consumption_without_bom_line_is_not_silently_attributed()
    {
        var lotId = Guid.NewGuid();
        var component = Guid.NewGuid();
        var usage = new FactoryMaterialUsage
        {
            ComponentItemId = component, QuantityConsumed = 3m,
            Reference = FactoryLineSideCorrections.LotPrefix +
                lotId.ToString("N") + "|legacy note"
        };

        var row = Assert.Single(FactoryLineSideCorrections.Ledger([usage]));
        Assert.Equal(lotId, row.ConfirmationId);
        Assert.Null(row.BomLineId);
        Assert.True(row.IsLegacyUnattributed);
    }

    [Fact]
    public void Non_lot_consumption_keeps_exact_bom_line_identity()
    {
        var lineId = Guid.NewGuid();
        var reference = FactoryLineSideCorrections.BomLineReference(lineId, "line-side note");
        var usage = new FactoryMaterialUsage
        {
            ComponentItemId = Guid.NewGuid(), QuantityConsumed = 2m, Reference = reference
        };

        var row = Assert.Single(FactoryLineSideCorrections.Ledger([usage]));
        Assert.Equal(lineId, row.BomLineId);
        Assert.Null(row.ConfirmationId);
        Assert.True(row.IsLegacyUnattributed);
    }
}
