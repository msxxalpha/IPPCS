using Xunit;

public sealed class BomAlternativePlanningTests
{
    [Fact]
    public void Baseline_MRP_counts_only_the_default_option_of_an_alternative_group()
    {
        var today = new DateOnly(2026, 10, 29);
        var product = Guid.NewGuid();
        var preferred = Guid.NewGuid();
        var substitute = Guid.NewGuid();
        var bom = new ManufacturingBomRevision
        {
            ParentItemId = product, Revision = "ALT-A", State = ManufacturingRevisionState.Approved,
            ValidFrom = today,
            Lines =
            [
                new ManufacturingBomLine
                {
                    ComponentItemId = preferred, QuantityPer = 2, ScrapPercent = 0,
                    Sequence = 10, AlternativeGroupCode = "MATERIAL-A",
                    IsDefaultAlternative = true, AlternativePriority = 1
                },
                new ManufacturingBomLine
                {
                    ComponentItemId = substitute, QuantityPer = 2, ScrapPercent = 0,
                    Sequence = 10, AlternativeGroupCode = "MATERIAL-A",
                    IsDefaultAlternative = false, AlternativePriority = 2
                }
            ]
        };
        var demand = new MpsDemandLine
        {
            ItemId = product, NeededOn = today.AddDays(2), Quantity = 10
        };
        var balances = new[]
        {
            new PlanningBalance { ItemId = product, SnapshotDate = today, PhysicalQuantity = 0, LeadDays = 1 },
            new PlanningBalance { ItemId = preferred, SnapshotDate = today, PhysicalQuantity = 0 }
        };
        var types = new Dictionary<Guid, PlanningItemType>
        {
            [product] = PlanningItemType.Finished,
            [preferred] = PlanningItemType.RawMaterial,
            [substitute] = PlanningItemType.RawMaterial
        };

        var rows = PlanningCalculators.RunMrp([demand], [bom], balances, [], types);

        Assert.Contains(rows, x => x.ItemId == preferred && x.GrossRequired == 20);
        Assert.DoesNotContain(rows, x => x.ItemId == substitute);
    }

    [Fact]
    public void Bom_approval_requires_two_options_and_exactly_one_default_per_group()
    {
        var parent = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var revision = new ManufacturingBomRevision
        {
            ParentItemId = parent, Revision = "ALT-B", State = ManufacturingRevisionState.Draft,
            ValidFrom = new DateOnly(2026, 1, 1),
            Lines =
            [
                new ManufacturingBomLine
                {
                    ComponentItemId = first, QuantityPer = 1, Sequence = 10,
                    AlternativeGroupCode = "ALT-01", IsDefaultAlternative = true, AlternativePriority = 1
                },
                new ManufacturingBomLine
                {
                    ComponentItemId = second, QuantityPer = 1, Sequence = 10,
                    AlternativeGroupCode = "ALT-01", IsDefaultAlternative = true, AlternativePriority = 2
                }
            ]
        };

        var issues = FactoryRevisionGovernance.BomIssues(revision, []);

        Assert.Contains(issues, x => x.Code == "AlternativeDefault");
    }
}
