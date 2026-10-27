using Xunit;

public sealed class FactoryStageTwelveTests
{
    [Fact]
    public void Trace_walks_multiple_orders_and_intermediate_operations_in_both_directions()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();var third=Guid.NewGuid();
        var oa=Guid.NewGuid();var ob=Guid.NewGuid();
        var at=new DateTime(2026,10,27,6,0,0,DateTimeKind.Utc);
        FactoryTraceNode Node(Guid id,Guid order,string lot)=>new(id,order,lot,
            Guid.NewGuid(),Guid.NewGuid(),10,lot,5,at);
        var nodes=new[]{Node(first,oa,"RAW-TRACE"),Node(second,oa,"SEMI-TRACE"),
            Node(third,ob,"ASSEMBLY-TRACE")};
        var edges=new[]{
            new FactoryTraceEdge(Guid.NewGuid(),first,second,5,oa,at),
            new FactoryTraceEdge(Guid.NewGuid(),second,third,4,ob,at.AddMinutes(1))
        };
        var ancestors=FactoryGenealogyExplorer.Explore(third,"ancestors",8,nodes,edges);
        Assert.Equal(3,ancestors.Nodes.Count);
        Assert.Equal(2,ancestors.Edges.Count);
        Assert.Equal(2,ancestors.DepthReached);
        Assert.Equal(2,ancestors.OrderCount);
        Assert.Equal(3,ancestors.BatchCount);
        var descendants=FactoryGenealogyExplorer.Explore(first,"descendants",8,nodes,edges);
        Assert.Equal(3,descendants.Nodes.Count);
        Assert.Equal(2,descendants.Edges.Count);
        var limited=FactoryGenealogyExplorer.Explore(first,"descendants",1,nodes,edges);
        Assert.Equal(2,limited.Nodes.Count);
        Assert.Single(limited.Edges);
        Assert.True(limited.HasMoreBeyondDepth);
        Assert.False(ancestors.HasMoreBeyondDepth);
    }
    [Fact]
    public void Trace_missing_root_dangling_edge_and_bad_quantities_fail_closed()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var nodes=new[]{new FactoryTraceNode(a,Guid.NewGuid(),"WO-A",
            Guid.NewGuid(),Guid.NewGuid(),10,"A",2,DateTime.UtcNow)};
        var edges=new[]{new FactoryTraceEdge(Guid.NewGuid(),a,b,1,Guid.NewGuid(),DateTime.UtcNow)};
        Assert.Throws<InvalidOperationException>(()=>
            FactoryGenealogyExplorer.Explore(b,"ancestors",8,nodes,edges));
        Assert.Throws<InvalidOperationException>(()=>
            FactoryGenealogyExplorer.Explore(a,"descendants",8,nodes,edges));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            FactoryGenealogyExplorer.Explore(a,"both",8,nodes,[]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            FactoryGenealogyExplorer.Explore(a,"ancestors",17,nodes,[]));
        Assert.Throws<InvalidOperationException>(()=>
            FactoryGenealogyExplorer.Explore(a,"descendants",8,[..nodes,
                new FactoryTraceNode(b,Guid.NewGuid(),"WO-B",Guid.NewGuid(),
                    Guid.NewGuid(),20,"B",2,DateTime.UtcNow)],
                [edges[0] with{Quantity=0}]));
    }
    [Fact]
    public void Material_usage_for_partially_reported_output_is_not_treated_as_stock()
    {
        var comp=Guid.NewGuid();
        var order=new ManufacturingOrder{PlannedQuantity=10,
            Operations=[new ManufacturingOrderOperation{Sequence=10,
                GoodQuantity=7,ScrapQuantity=1}]};
        var bom=new ManufacturingBomLine{ComponentItemId=comp,QuantityPer=2,
            ScrapPercent=0};
        var usage=new FactoryMaterialUsage{ComponentItemId=comp,QuantityConsumed=15};
        var row=Assert.Single(FactoryExecutionInsights.MaterialReconciliation(
            order,[bom],[usage]));
        Assert.Equal(20,row.StandardForPlannedQuantity);
        Assert.Equal(16,row.StandardForReportedOutput);
        Assert.Equal(15,row.ActualLoggedConsumption);
        Assert.Equal(-1m,row.OutputBasedVariance);
        Assert.Equal(-6.25m,row.OutputBasedVariancePercent);
        Assert.Equal("RecordedInternalEvidence",row.Confidence);
    }
    [Fact]
    public void Missing_material_usage_and_missing_final_output_are_not_zero_variance()
    {
        var part=Guid.NewGuid();
        var bom=new ManufacturingBomLine{ComponentItemId=part,QuantityPer=1};
        var withoutOutput=new ManufacturingOrder{PlannedQuantity=10,
            Operations=[new ManufacturingOrderOperation{Sequence=10}]};
        var row=Assert.Single(FactoryExecutionInsights.MaterialReconciliation(
            withoutOutput,[bom],[]));
        Assert.Null(row.ActualLoggedConsumption);
        Assert.Null(row.StandardForReportedOutput);
        Assert.Null(row.OutputBasedVariance);
        Assert.Equal("NoFinalOutput",row.Confidence);
        withoutOutput.Operations[0].GoodQuantity=8;
        row=Assert.Single(FactoryExecutionInsights.MaterialReconciliation(
            withoutOutput,[bom],[]));
        Assert.Equal(8m,row.StandardForReportedOutput);
        Assert.Null(row.OutputBasedVariance);
        Assert.Equal("NoConsumptionLog",row.Confidence);
    }
    [Fact]
    public void Spc_uses_individual_moving_ranges_without_claiming_approved_limits()
    {
        var req=new ManufacturingInspectionRequirement{Id=Guid.NewGuid(),
            Code="DAMP-FORCE",Minimum=8,Maximum=13};
        var values=new decimal[]{10,11,9,10,50};
        var samples=values.Select((value,i)=>new ManufacturingInspectionResult{
            RequirementId=req.Id,Value=value,Passed=value>=8&&value<=13,
            RecordedUtc=new DateTime(2026,10,27,10,0,0,DateTimeKind.Utc).AddMinutes(i)
        }).ToArray();
        var result=FactoryExecutionInsights.IndividualsChart(Guid.NewGuid(),req,samples);
        Assert.Equal(5,result.SampleCount);
        Assert.Equal(18m,result.Mean);
        Assert.Equal(11m,result.MovingRangeMean);
        Assert.Equal(1,result.SpecificationFailures);
        Assert.Equal(1,result.ControlLimitViolations);
        Assert.True(result.HasReferenceLimits);
        Assert.True(result.UpperControlLimit<50);
    }
    [Fact]
    public void Sparse_or_constant_spc_never_reports_imaginary_control_limits()
    {
        var req=new ManufacturingInspectionRequirement{Id=Guid.NewGuid(),Code="X"};
        var clock=new DateTime(2026,10,27,10,0,0,DateTimeKind.Utc);
        var shortSample=FactoryExecutionInsights.IndividualsChart(Guid.NewGuid(),req,[
            new ManufacturingInspectionResult{RecordedUtc=clock,Value=2}
        ]);
        Assert.Null(shortSample.UpperControlLimit);
        Assert.Contains("پنج",shortSample.Warning);
        var constant=FactoryExecutionInsights.IndividualsChart(Guid.NewGuid(),req,
            Enumerable.Range(0,5).Select(i=>new ManufacturingInspectionResult{
                RecordedUtc=clock.AddMinutes(i),Value=2}).ToArray());
        Assert.Equal(0m,constant.MovingRangeMean);
        Assert.Null(constant.UpperControlLimit);
    }
    [Fact]
    public void Alerts_distinguish_open_quality_from_overdue_orders_and_corrective_actions()
    {
        var today=new DateOnly(2026,10,27);
        var orders=new[]{
            new ManufacturingOrder{Number="LATE",Status=ManufacturingOrderStatus.InProgress,
                DueOn=today.AddDays(-2)},
            new ManufacturingOrder{Number="SOON",Status=ManufacturingOrderStatus.Released,
                DueOn=today.AddDays(2)},
            new ManufacturingOrder{Number="DONE",Status=ManufacturingOrderStatus.Completed,
                DueOn=today.AddDays(-5)}
        };
        var hold=new ManufacturingQualityHold{OrderId=orders[0].Id,
            Status=ManufacturingQualityStatus.Open,Quantity=2,Reason="QC"};
        var deviation=new ProductionDeviation{Status=ProductionDeviationStatus.ActionAssigned,
            ActionDueOn=today.AddDays(-1),Responsible="Quality lead"};
        var alert=FactoryExecutionInsights.ProductionAlerts(today,3,orders,[hold],[deviation]);
        Assert.Equal(1,alert.CriticalCount);
        Assert.Equal(2,alert.HighCount);
        Assert.Equal(1,alert.MediumCount);
        Assert.DoesNotContain(alert.Alerts,x=>x.Identifier=="DONE");
        Assert.Equal("QualityHold",alert.Alerts[0].Type);
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            FactoryExecutionInsights.ProductionAlerts(today,31,[],[],[]));
    }
}
