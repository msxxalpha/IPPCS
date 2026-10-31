using Xunit;

public sealed class FactoryStageTwentyOneDraftRebindTests
{
    private static readonly DateOnly Effective = new(2026, 10, 27);
    private static (ManufacturingOrder Order, RoutingRevision Route,
        ManufacturingBomRevision Bom) Fixture()
    {
        var item = Guid.NewGuid();
        var order = new ManufacturingOrder
        {
            ItemId = item, Status = ManufacturingOrderStatus.Draft,
            PlannedQuantity = 120, PlannedStart = Effective, DueOn = Effective.AddDays(7),
            RoutingRevisionId = Guid.NewGuid(), BomRevisionId = Guid.NewGuid()
        };
        var route = new RoutingRevision
        {
            ItemId = item, State = RoutingState.Approved,
            EffectiveFrom = Effective.AddDays(-2), EffectiveTo = null,
            Operations = [new RoutingOperation
            {
                Sequence = 10, WorkCenterId = Guid.NewGuid(), Name = "Assembly",
                RunMinutesPerUnit = 1, YieldPercent = 100
            }]
        };
        var bom = new ManufacturingBomRevision
        {
            ParentItemId = item, State = ManufacturingRevisionState.Approved,
            ValidFrom = Effective.AddDays(-2), ValidUntil = null,
            Lines = [new ManufacturingBomLine
            {
                Sequence = 10, ComponentItemId = Guid.NewGuid(),
                QuantityPer = 2
            }]
        };
        return (order, route, bom);
    }

    [Fact]
    public void Draft_order_can_rebind_routing_only_with_approved_route_and_approved_counterpart()
    {
        var (order, route, bom) = Fixture();
        bom.Id = order.BomRevisionId;
        Assert.Empty(FactoryEngineeringDraftRebind.Assess("Routing",
            order.RoutingRevisionId, route.Id, order, route, bom));
        bom.State = ManufacturingRevisionState.Draft;
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("Routing",
            order.RoutingRevisionId, route.Id, order, route, bom),
            x => x.Code == "BomUnavailable");
    }

    [Fact]
    public void Bom_rebind_respects_validity_and_preserves_existing_routing()
    {
        var (order, route, bom) = Fixture();
        route.Id = order.RoutingRevisionId;
        Assert.Empty(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom));
        bom.ValidFrom = Effective.AddDays(1);
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom),
            x => x.Code == "BomUnavailable");
        bom.ValidFrom = Effective.AddDays(-2);
        bom.ValidUntil = Effective.AddDays(-1);
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom),
            x => x.Code == "BomUnavailable");
    }

    [Theory]
    [InlineData(ManufacturingOrderStatus.Released)]
    [InlineData(ManufacturingOrderStatus.InProgress)]
    [InlineData(ManufacturingOrderStatus.OnHold)]
    [InlineData(ManufacturingOrderStatus.Completed)]
    [InlineData(ManufacturingOrderStatus.Cancelled)]
    public void Released_or_executed_orders_cannot_be_rebound(ManufacturingOrderStatus status)
    {
        var (order, route, bom) = Fixture();
        route.Id = order.RoutingRevisionId;
        order.Status = status;
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom),
            x => x.Code == "OrderNotDraft");
    }

    [Fact]
    public void Materialized_operations_and_wrong_baseline_are_blocked_even_when_order_is_draft()
    {
        var (order, route, bom) = Fixture();
        route.Id = order.RoutingRevisionId;
        order.Operations.Add(new ManufacturingOrderOperation { Sequence = 10 });
        var issues = FactoryEngineeringDraftRebind.Assess("BOM",
            Guid.NewGuid(), bom.Id, order, route, bom);
        Assert.Contains(issues, x => x.Code == "OrderNotDraft");
        Assert.Contains(issues, x => x.Code == "BaselineMismatch");
    }

    [Fact]
    public void Rebind_does_not_accept_draft_or_empty_candidate_revision()
    {
        var (order, route, bom) = Fixture();
        route.Id = order.RoutingRevisionId;
        bom.State = ManufacturingRevisionState.Draft;
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom),
            x => x.Code == "BomUnavailable");
        bom.State = ManufacturingRevisionState.Approved;
        bom.Lines.Clear();
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom),
            x => x.Code == "BomUnavailable");
    }

    [Fact]
    public void Wrong_part_or_invalid_pair_never_passes()
    {
        var (order, route, bom) = Fixture();
        route.Id = order.RoutingRevisionId;
        bom.ParentItemId = Guid.NewGuid();
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, bom.Id, order, route, bom),
            x => x.Code == "BomUnavailable");
        Assert.Contains(FactoryEngineeringDraftRebind.Assess("BOM",
            order.BomRevisionId, order.BomRevisionId, order, route, bom),
            x => x.Code == "InvalidRevisionPair");
    }
}
