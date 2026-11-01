using Xunit;

public sealed class FactoryReleaseMaterialGateTests
{
    private static (ManufacturingOrder Order, ManufacturingBomLine Preferred,
        ManufacturingBomLine Substitute) Fixture()
    {
        var order = new ManufacturingOrder { Id = Guid.NewGuid(), PlannedQuantity = 100m };
        var preferred = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(), QuantityPer = 1m,
            ScrapPercent = 0m, Sequence = 10, AlternativeGroupCode = "SEAL",
            IsDefaultAlternative = true, AlternativePriority = 1
        };
        var substitute = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(), QuantityPer = 1.2m,
            ScrapPercent = 0m, Sequence = 10, AlternativeGroupCode = "SEAL",
            IsDefaultAlternative = false, AlternativePriority = 2
        };
        return (order, preferred, substitute);
    }

    [Fact]
    public void Engineering_default_is_valid_when_no_explicit_mix_exists()
    {
        var (order, preferred, substitute) = Fixture();
        var issues = BomAlternativeExecution.ValidateReleaseAllocations(
            order, [preferred, substitute], []);
        Assert.Empty(issues);
    }

    [Fact]
    public void Valid_mixed_alternative_share_and_calculated_quantities_pass()
    {
        var (order, preferred, substitute) = Fixture();
        var allocations = new[]
        {
            new ManufacturingOrderMaterialAllocation
            {
                OrderId = order.Id, BomLineId = preferred.Id,
                SharePercent = 60m, PlannedQuantity = 60m
            },
            new ManufacturingOrderMaterialAllocation
            {
                OrderId = order.Id, BomLineId = substitute.Id,
                SharePercent = 40m, PlannedQuantity = 48m
            }
        };
        Assert.Empty(BomAlternativeExecution.ValidateReleaseAllocations(
            order, [preferred, substitute], allocations));
    }

    [Fact]
    public void Incorrect_saved_component_quantity_blocks_release()
    {
        var (order, preferred, substitute) = Fixture();
        var allocations = new[]
        {
            new ManufacturingOrderMaterialAllocation
            {
                OrderId = order.Id, BomLineId = preferred.Id,
                SharePercent = 60m, PlannedQuantity = 61m
            },
            new ManufacturingOrderMaterialAllocation
            {
                OrderId = order.Id, BomLineId = substitute.Id,
                SharePercent = 40m, PlannedQuantity = 48m
            }
        };
        var issues = BomAlternativeExecution.ValidateReleaseAllocations(
            order, [preferred, substitute], allocations);
        Assert.Contains(issues, x => x.Contains(preferred.Id.ToString()));
    }

    [Fact]
    public void Allocations_cannot_reference_mandatory_or_foreign_bom_lines()
    {
        var (order, preferred, substitute) = Fixture();
        var mandatory = new ManufacturingBomLine
        {
            Id = Guid.NewGuid(), ComponentItemId = Guid.NewGuid(),
            QuantityPer = 2m, ScrapPercent = 0m, Sequence = 20
        };
        var foreign = new ManufacturingOrderMaterialAllocation
        {
            OrderId = order.Id, BomLineId = Guid.NewGuid(),
            SharePercent = 100m, PlannedQuantity = 100m
        };
        var mandatoryAllocation = new ManufacturingOrderMaterialAllocation
        {
            OrderId = order.Id, BomLineId = mandatory.Id,
            SharePercent = 100m, PlannedQuantity = 200m
        };
        var issues = BomAlternativeExecution.ValidateReleaseAllocations(
            order, [preferred, substitute, mandatory], [foreign, mandatoryAllocation]);
        Assert.Contains(issues, x => x.Contains("نامعتبر یا تکراری"));
        Assert.Contains(issues, x => x.Contains("جزء اجباری"));
    }
}
