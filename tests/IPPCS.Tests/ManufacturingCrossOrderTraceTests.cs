using Xunit;

public sealed class ManufacturingCrossOrderTraceTests
{
    static (ManufacturingOrder Semi,ManufacturingOrder Final,
        ManufacturingConfirmation Source,ManufacturingConfirmation Target,
        ManufacturingBomLine Bom) Setup()
    {
        var semi=new ManufacturingOrder{Id=Guid.NewGuid(),Kind=ManufacturingOrderKind.SemiFinished,
            ItemId=Guid.NewGuid(),Status=ManufacturingOrderStatus.Completed,
            Operations=[new ManufacturingOrderOperation{Sequence=10,GoodQuantity=12,IsComplete=true}]};
        var dest=new ManufacturingOrder{Id=Guid.NewGuid(),Kind=ManufacturingOrderKind.FinalAssembly,
            Status=ManufacturingOrderStatus.InProgress,BomRevisionId=Guid.NewGuid(),
            Operations=[new ManufacturingOrderOperation{Sequence=10,GoodQuantity=8,IsComplete=true}]};
        var source=new ManufacturingConfirmation{Id=Guid.NewGuid(),
            OperationId=semi.Operations[0].Id,LotCode="SEM-01",GoodQuantity=12,
            OccurredUtc=new DateTime(2026,10,26,8,0,0,DateTimeKind.Utc)};
        var target=new ManufacturingConfirmation{Id=Guid.NewGuid(),
            OperationId=dest.Operations[0].Id,LotCode="FIN-01",GoodQuantity=8,
            OccurredUtc=source.OccurredUtc.AddHours(2)};
        var bom=new ManufacturingBomLine{RevisionId=dest.BomRevisionId,
            ComponentItemId=semi.ItemId,QuantityPer=1.5m,ScrapPercent=0};
        return(semi,dest,source,target,bom);
    }

    [Fact]
    public void Approved_semi_finished_good_can_be_attributed_to_a_final_assembly_lot()
    {
        var (semi,final,source,target,bom)=Setup();
        Assert.True(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            9,0,0,8,out var reason),reason);
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            4,9,0,8,out reason));
        Assert.Contains("نیمه‌ساخته",reason);
    }

    [Fact]
    public void Duplicate_or_excess_component_links_never_overrun_bom_quantity()
    {
        var (semi,final,source,target,bom)=Setup();
        Assert.True(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            12,0,0,8,out var reason),reason);
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            1,0,12,8,out reason));
        Assert.Contains("BOM",reason);
    }

    [Fact]
    public void Wrong_bom_revision_and_future_source_are_rejected()
    {
        var (semi,final,source,target,bom)=Setup();
        bom.RevisionId=Guid.NewGuid();
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            1,0,0,8,out _));
        bom.RevisionId=final.BomRevisionId;
        source.OccurredUtc=target.OccurredUtc.AddHours(1);
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            1,0,0,8,out _));
    }

    [Fact]
    public void Source_must_be_completed_and_quality_released_and_destination_quality_gated()
    {
        var (semi,final,source,target,bom)=Setup();
        semi.Status=ManufacturingOrderStatus.InProgress;
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            1,0,0,8,out _));
        semi.Status=ManufacturingOrderStatus.Completed;
        semi.Operations[0].QualityRejectedQuantity=5;
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            9,0,0,8,out _));
        semi.Operations[0].QualityRejectedQuantity=0;
        final.Operations[0].InspectionRequirements.Add(new ManufacturingInspectionRequirement{
            Mandatory=true,LastPassed=false});
        Assert.False(ManufacturingCrossOrderTrace.TryLink(semi,final,source,target,bom,
            1,0,0,8,out _));
    }

    [Fact]
    public void Exact_bom_coverage_counts_missing_and_excess_without_claiming_stock()
    {
        var (semi,final,source,target,bom)=Setup();
        var computed=Assert.Single(ManufacturingCrossOrderTrace.Coverage(
            [bom],[target],[(semi.ItemId,5m)]));
        Assert.Equal(12m,computed.RequiredForReportedOutput);
        Assert.Equal(5m,computed.LinkedProductionQuantity);
        Assert.Equal(7m,computed.MissingProductionQuantity);
        Assert.Equal(0m,computed.OverlinkedProductionQuantity);
    }

    [Fact]
    public void Operation_readiness_respects_quality_gates_and_wip_without_inventory()
    {
        var order=new ManufacturingOrder{Status=ManufacturingOrderStatus.InProgress,
            Operations=[
                new ManufacturingOrderOperation{Sequence=10,GoodQuantity=9,
                    QualityPendingQuantity=2,IsComplete=true,
                    InspectionRequirements=[
                        new ManufacturingInspectionRequirement{Mandatory=true,LastPassed=false}
                    ]},
                new ManufacturingOrderOperation{Sequence=20}
            ]};
        var ready=ManufacturingCrossOrderTrace.Readiness(order);
        Assert.Equal(7m,ready[0].ForwardAvailable);
        Assert.Equal(1,ready[0].FailedMandatory);
        Assert.False(ready[1].CanProceed);
        order.Operations[0].InspectionRequirements[0].LastPassed=true;
        order.Operations[0].QualityPendingQuantity=0;
        ready=ManufacturingCrossOrderTrace.Readiness(order);
        Assert.True(ready[1].CanProceed);
    }
}
