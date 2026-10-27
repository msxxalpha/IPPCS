using Xunit;

public sealed class FactoryStageFourteenTests
{
    [Fact]
    public void Routing_approval_refuses_overlapping_effective_versions_and_duplicate_operations()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var valid=new RoutingRevision{ItemId=item,State=RoutingState.Approved,
            EffectiveFrom=new DateOnly(2026,10,1),EffectiveTo=new DateOnly(2026,11,1)};
        var candidate=new RoutingRevision{ItemId=item,State=RoutingState.Draft,
            EffectiveFrom=new DateOnly(2026,10,27),EffectiveTo=new DateOnly(2026,12,1),
            Operations=[new RoutingOperation{Sequence=10,Name="Body assembly",WorkCenterId=center,
                SetupMinutes=5,RunMinutesPerUnit=2,YieldPercent=100}]};
        Assert.Contains(FactoryRevisionGovernance.RoutingIssues(candidate,[valid]),
            x=>x.Code=="ApprovedOverlap");
        candidate.EffectiveFrom=new DateOnly(2026,11,2);
        Assert.Empty(FactoryRevisionGovernance.RoutingIssues(candidate,[valid]));
        candidate.Operations.Add(new RoutingOperation{Sequence=10,Name="Duplicate",
            WorkCenterId=center,RunMinutesPerUnit=2,YieldPercent=100});
        Assert.Contains(FactoryRevisionGovernance.RoutingIssues(candidate,[valid]),
            x=>x.Code=="DuplicateSequence");
    }
    [Fact]
    public void Bom_approval_checks_cycle_duplicated_components_and_version_overlap()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        var old=new ManufacturingBomRevision{ParentItemId=b,
            State=ManufacturingRevisionState.Approved,
            ValidFrom=new DateOnly(2026,10,1),
            Lines=[new ManufacturingBomLine{ComponentItemId=c,QuantityPer=1,Sequence=10}]};
        var candidate=new ManufacturingBomRevision{ParentItemId=c,
            State=ManufacturingRevisionState.Draft,ValidFrom=new DateOnly(2026,11,1),
            Lines=[new ManufacturingBomLine{ComponentItemId=b,QuantityPer=1,Sequence=10}]};
        Assert.Contains(FactoryRevisionGovernance.BomIssues(candidate,[old]),
            x=>x.Code=="BomCycle");
        candidate.Lines.Clear();
        candidate.Lines.Add(new ManufacturingBomLine{ComponentItemId=a,QuantityPer=1,Sequence=10});
        Assert.Empty(FactoryRevisionGovernance.BomIssues(candidate,[old]));
        candidate.Lines.Add(new ManufacturingBomLine{ComponentItemId=a,QuantityPer=2,Sequence=20});
        Assert.Contains(FactoryRevisionGovernance.BomIssues(candidate,[old]),
            x=>x.Code=="DuplicateComponent");
    }
    [Fact]
    public void Draft_release_requires_valid_frozen_routing_and_bom_and_valid_mps_link()
    {
        var item=Guid.NewGuid();var today=new DateOnly(2026,10,27);
        var route=new RoutingRevision{ItemId=item,State=RoutingState.Approved,
            EffectiveFrom=today,Operations=[new RoutingOperation{Sequence=10,RunMinutesPerUnit=1}]};
        var bom=new ManufacturingBomRevision{ParentItemId=item,
            State=ManufacturingRevisionState.Approved,ValidFrom=today,
            Lines=[new ManufacturingBomLine{ComponentItemId=Guid.NewGuid(),QuantityPer=1}]};
        var order=new ManufacturingOrder{ItemId=item,PlannedStart=today,
            DueOn=today.AddDays(2),PlannedQuantity=10,Status=ManufacturingOrderStatus.Draft};
        Assert.Empty(FactoryRevisionGovernance.ReleaseIssues(order,route,bom,[],[]));
        var line=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=item,
            NeededOn=today.AddDays(1),Quantity=10};
        var peg=new ManufacturingOrderPeg{MpsLineId=line.Id,LinkedQuantity=6};
        Assert.Contains(FactoryRevisionGovernance.ReleaseIssues(order,route,bom,[peg],[line]),
            x=>x.Code=="PegMismatch");
        order.DueOn=today;
        Assert.Empty(FactoryRevisionGovernance.ReleaseIssues(order,route,bom,[peg],[line]));
        order.PlannedQuantity=5;
        Assert.Contains(FactoryRevisionGovernance.ReleaseIssues(order,route,bom,[peg],[line]),
            x=>x.Code=="PegOverAllocation");
    }
    [Fact]
    public void Dispatch_board_only_shows_live_orders_with_gated_operations_and_missing_slots()
    {
        var today=new DateOnly(2026,10,27);
        var open=new ManufacturingOrder{Number="LIVE",Status=ManufacturingOrderStatus.Released,
            PlannedStart=today,DueOn=today.AddDays(1),PlannedQuantity=5,
            Operations=[new ManufacturingOrderOperation{Sequence=10,
                Name="Shock absorber compression",WorkCenterId=Guid.NewGuid()}]};
        open.Operations[0].OrderId=open.Id;
        var held=new ManufacturingOrder{Number="HELD",Status=ManufacturingOrderStatus.OnHold,
            PlannedStart=today,DueOn=today.AddDays(1),PlannedQuantity=4,
            Operations=[new ManufacturingOrderOperation{Sequence=10,Name="Inspect"}]};
        held.Operations[0].OrderId=held.Id;
        var draft=new ManufacturingOrder{Number="DRAFT",Status=ManufacturingOrderStatus.Draft,
            PlannedStart=today,DueOn=today,PlannedQuantity=5};
        var result=FactoryRevisionGovernance.DispatchBoard(today,7,
            [open,held,draft],[],[],[]);
        Assert.Equal(2,result.OrdersConsidered);
        Assert.Single(result.Rows,x=>x.OrderNumber=="LIVE"&&
            x.CanStartByRouteAndQuality&&!x.HasBookedMachineSlot);
        Assert.Equal(1,result.MissingReservationCount);
        Assert.False(result.Rows.Single(x=>x.OrderNumber=="HELD").CanStartByRouteAndQuality);
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            FactoryRevisionGovernance.DispatchBoard(today,31,[],[],[],[]));
    }
}
