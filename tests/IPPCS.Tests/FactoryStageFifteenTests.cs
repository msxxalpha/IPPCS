using Xunit;

public sealed class FactoryStageFifteenTests
{
    [Fact]
    public void Consumption_reversal_is_exact_once_append_only_and_lot_attribution_survives()
    {
        var order=Guid.NewGuid();var component=Guid.NewGuid();var lot=Guid.NewGuid();
        var original=new FactoryMaterialUsage{OrderId=order,ComponentItemId=component,
            QuantityConsumed=4,Reference=FactoryLineSideCorrections.LotReference(lot,"packing")};
        var history=FactoryLineSideCorrections.Ledger([original]);
        Assert.Single(history);
        Assert.Equal(4,history[0].NetQuantity);
        Assert.Equal(lot,history[0].ConfirmationId);
        var reverse=new FactoryMaterialUsage{OrderId=order,ComponentItemId=component,
            QuantityConsumed=-4,
            Reference=FactoryLineSideCorrections.ReverseReference(original.Id,"incorrect count")};
        history=FactoryLineSideCorrections.Ledger([original,reverse]);
        Assert.Equal(0,history[0].NetQuantity);
        Assert.Equal(reverse.Id,history[0].ReversedById);
        Assert.Equal(4,original.QuantityConsumed);
        Assert.Throws<InvalidOperationException>(()=>
            FactoryLineSideCorrections.Ledger([original,reverse,new FactoryMaterialUsage{OrderId=order,ComponentItemId=component,QuantityConsumed=-4,Reference=FactoryLineSideCorrections.ReverseReference(original.Id,"duplicate")}]));
    }

    [Fact]
    public void Consumption_reversal_refuses_orphan_partial_and_mismatched_adjustments()
    {
        var original=new FactoryMaterialUsage{OrderId=Guid.NewGuid(),
            ComponentItemId=Guid.NewGuid(),QuantityConsumed=5,Reference="legacy manual"};
        FactoryMaterialUsage Rev(decimal amount,Guid source,Guid? item=null)=>new(){
            OrderId=original.OrderId,ComponentItemId=item??original.ComponentItemId,
            QuantityConsumed=amount,
            Reference=FactoryLineSideCorrections.ReverseReference(source,"correction")};
        Assert.Throws<InvalidOperationException>(()=>
            FactoryLineSideCorrections.Ledger([original,Rev(-5,Guid.NewGuid())]));
        Assert.Throws<InvalidOperationException>(()=>
            FactoryLineSideCorrections.Ledger([original,Rev(-4,original.Id)]));
        Assert.Throws<InvalidOperationException>(()=>
            FactoryLineSideCorrections.Ledger([original,Rev(-5,original.Id,Guid.NewGuid())]));
        var old=Assert.Single(FactoryLineSideCorrections.Ledger([original]));
        Assert.Null(old.ConfirmationId);
        Assert.True(old.IsLegacyUnattributed);
    }

    [Fact]
    public void Frozen_bom_scales_traceable_material_need_to_reported_batch_only()
    {
        var confirmation=new ManufacturingConfirmation{GoodQuantity=2,ScrapQuantity=1,
            ReworkQuantity=0,LotCode="CI-LOT"};
        var part=Guid.NewGuid();
        var bom=new[]{new ManufacturingBomLine{ComponentItemId=part,
            QuantityPer=2,ScrapPercent=0}};
        var entry=new FactoryMaterialUsage{ComponentItemId=part,
            QuantityConsumed=4,Reference=FactoryLineSideCorrections.LotReference(
                confirmation.Id,"traceable")};
        var row=Assert.Single(FactoryLineSideCorrections.LotBalances(
            confirmation,bom,FactoryLineSideCorrections.Ledger([entry])));
        Assert.Equal(6,row.StandardForReportedLot);
        Assert.Equal(4,row.RecordedNetQuantity);
        Assert.Equal(-2,row.Variance);
        Assert.Equal(1,row.ActiveEntries);
        Assert.Throws<InvalidOperationException>(()=>
            FactoryLineSideCorrections.LotBalances(confirmation,[
                new ManufacturingBomLine{ComponentItemId=part,QuantityPer=1,ScrapPercent=100}
            ],[]));
    }

    [Fact]
    public void Action_revision_preserves_prior_root_action_responsible_due_and_reason()
    {
        var d=new ProductionDeviation{Status=ProductionDeviationStatus.ActionAssigned,
            RootCause=new string('R',1000),CorrectiveAction="adjust operating process",
            Responsible="local production owner",ActionDueOn=new DateOnly(2026,11,2)};
        var input=new FactoryDeviationReviseInput("new root","new action","shift lead",
            "1405/08/15","approved operational change");
        var events=FactoryDeviationGovernance.RevisionEvidence(d,input,"planner");
        Assert.True(events.Count>=6);
        Assert.All(events,x=>Assert.InRange(x.Details.Length,1,1000));
        Assert.Contains(events,x=>x.Details.StartsWith("REVISION:OldRoot:1:"));
        Assert.Contains(events,x=>x.Details.StartsWith("REVISION:OldRoot:2:"));
        Assert.Contains(events,x=>x.Details.Contains("REVISION:OldDue:1:2026-11-02"));
        Assert.Contains(events,x=>x.Details.Contains("REVISION:Reason:1:approved operational change"));
        Assert.Equal(new string('R',1000),d.RootCause);
    }

    [Fact]
    public void Escalation_follow_up_and_factory_dashboard_keep_only_live_deviations()
    {
        var today=new DateOnly(2026,10,27);
        var live=new ProductionDeviation{Status=ProductionDeviationStatus.ActionAssigned,
            Responsible="CI Planning",ActionDueOn=today.AddDays(-2)};
        live.Events.Add(new ProductionDeviationEvent{DeviationId=live.Id,
            Status=ProductionDeviationStatus.ActionAssigned,
            Details=FactoryDeviationGovernance.EscalationPrefix+"2026-10-28|night shift urgent"});
        var result=FactoryDeviationGovernance.FollowUp(live,today);
        Assert.True(result.IsOverdue);
        Assert.Equal(1,result.EscalationCount);
        Assert.Equal("night shift urgent",result.LastEscalationReason);
        Assert.Equal(today.AddDays(1),result.NextReviewOn);
        var alert=FactoryExecutionInsights.ProductionAlerts(today,7,[],[],[live]);
        Assert.Equal(1,alert.CriticalCount);
        Assert.Equal("DeviationAction",Assert.Single(alert.Alerts).Type);
        live.Status=ProductionDeviationStatus.Closed;
        Assert.Empty(FactoryExecutionInsights.ProductionAlerts(today,7,[],[],[live]).Alerts);
    }
}
