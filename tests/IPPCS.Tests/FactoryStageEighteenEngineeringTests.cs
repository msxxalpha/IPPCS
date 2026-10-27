using Xunit;
public sealed class FactoryStageEighteenEngineeringTests
{
    [Fact]
    public void Bom_delta_reports_added_removed_changed_components_and_frozen_orders()
    {
        var parent=Guid.NewGuid();var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        var original=new ManufacturingBomRevision{ParentItemId=parent,
            State=ManufacturingRevisionState.Approved,ValidFrom=new DateOnly(2026,10,1),
            ValidUntil=new DateOnly(2026,10,30),
            Lines=[new ManufacturingBomLine{ComponentItemId=a,QuantityPer=2,
                ScrapPercent=0,Sequence=10},
                new ManufacturingBomLine{ComponentItemId=b,QuantityPer=1,
                ScrapPercent=0,Sequence=20}]};
        var draft=new ManufacturingBomRevision{ParentItemId=parent,
            State=ManufacturingRevisionState.Draft,ValidFrom=new DateOnly(2026,10,31),
            Lines=[new ManufacturingBomLine{ComponentItemId=a,QuantityPer=3,
                ScrapPercent=1,Sequence=10},
                new ManufacturingBomLine{ComponentItemId=c,QuantityPer=1,
                ScrapPercent=0,Sequence=30}]};
        var frozen=new ManufacturingOrder{ItemId=parent,BomRevisionId=original.Id,
            Status=ManufacturingOrderStatus.Released,
            PlannedStart=new DateOnly(2026,11,2)};
        var report=FactoryEngineeringChangeImpact.Bom(original,draft,[original],[frozen]);
        Assert.True(report.ReadyForRegularApproval);
        Assert.False(report.OverlappingEffectiveDates);
        Assert.Equal(3,report.Parts.Count);
        Assert.Contains(report.Parts,x=>x.ComponentId==a&&x.NewQuantity==3&&x.ChangeType=="Changed");
        Assert.Contains(report.Parts,x=>x.ComponentId==b&&x.ChangeType=="Removed");
        Assert.Contains(report.Parts,x=>x.ComponentId==c&&x.ChangeType=="Added");
        Assert.Equal(1,report.ActiveOrCompletedOrders);
        Assert.Equal(frozen.Id,Assert.Single(report.Orders).OrderId);
        draft.ValidFrom=new DateOnly(2026,10,27);
        report=FactoryEngineeringChangeImpact.Bom(original,draft,[original],[frozen]);
        Assert.False(report.ReadyForRegularApproval);
        Assert.True(report.OverlappingEffectiveDates);
        Assert.Contains(report.Issues,x=>x.Code=="ApprovedOverlap");
    }
    [Fact]
    public void Routing_delta_detects_center_and_cycle_time_change_without_overwriting_source()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();var other=Guid.NewGuid();
        var baseline=new RoutingRevision{ItemId=item,State=RoutingState.Approved,
            EffectiveFrom=new DateOnly(2026,10,1),
            EffectiveTo=new DateOnly(2026,10,30),Operations=[
                new RoutingOperation{Sequence=10,Name="first",WorkCenterId=center,
                    RunMinutesPerUnit=3,YieldPercent=100}]};
        var draft=new RoutingRevision{ItemId=item,State=RoutingState.Draft,
            EffectiveFrom=new DateOnly(2026,10,31),Operations=[
                new RoutingOperation{Sequence=10,Name="first",WorkCenterId=other,
                    RunMinutesPerUnit=2,YieldPercent=98},
                new RoutingOperation{Sequence=20,Name="second",WorkCenterId=center,
                    RunMinutesPerUnit=1,YieldPercent=100}]};
        var report=FactoryEngineeringChangeImpact.Routing(baseline,draft,[baseline],[]);
        Assert.True(report.ReadyForRegularApproval);
        Assert.Equal(2,report.Operations.Count);
        Assert.Contains(report.Operations,x=>x.Sequence==10&&x.OldCenter==center&&
            x.NewCenter==other&&x.NewMinutesPerUnit==2);
        Assert.Equal("Added",report.Operations.Single(x=>x.Sequence==20).ChangeType);
        Assert.Equal(3,baseline.Operations[0].RunMinutesPerUnit);
        Assert.Throws<InvalidOperationException>(()=>
            FactoryEngineeringChangeImpact.Routing(draft,baseline,[baseline],[]));
    }
}
