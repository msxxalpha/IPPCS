using Xunit;

public sealed class FactoryRecallImpactTests
{
    private static (FactoryTraceNode[] Nodes,FactoryTraceEdge[] Edges,
        Guid Source,Guid FinalOrder,Guid FinalLot,Guid FinalOperation) Sample()
    {
        var root=Guid.NewGuid();var semiOrder=Guid.NewGuid();
        var final=Guid.NewGuid();var finish=Guid.NewGuid();
        var finOperation=Guid.NewGuid();var start=DateTime.UtcNow.AddHours(-3);
        var nodes=new[]{
            new FactoryTraceNode(root,semiOrder,"SEMI-01",Guid.NewGuid(),
                Guid.NewGuid(),10,"SEMI-A",4,start),
            new FactoryTraceNode(finish,final,"FINAL-01",Guid.NewGuid(),
                finOperation,20,"FINAL-B",2,start.AddHours(1))
        };
        var edges=new[]{new FactoryTraceEdge(Guid.NewGuid(),root,finish,
            4,final,start.AddHours(2))};
        return(nodes,edges,root,final,finish,finOperation);
    }

    [Fact]
    public void Documented_source_lot_reports_final_serial_exposure_without_shipping_claims()
    {
        var (nodes,edges,root,final,finish,op)=Sample();
        var trace=FactoryGenealogyExplorer.Explore(root,"descendants",16,nodes,edges);
        var order=new ManufacturingOrder{Id=final,Kind=ManufacturingOrderKind.FinalAssembly,
            Operations=[new ManufacturingOrderOperation{Id=op,Sequence=20}]};
        var serial=new FactorySerialEvidence("DAMPER-0001",final,finish,
            "FINAL-B","FINAL-01",nodes[1].ItemId,"qa",DateTime.UtcNow,Guid.NewGuid());
        var report=FactoryRecallImpact.Assess(trace,[serial],
            new Dictionary<Guid,ManufacturingOrder>{{final,order}});
        Assert.Equal(1,report.FinalAssemblyOrders);
        Assert.Equal(1,report.RegisteredSerialCount);
        Assert.Single(report.SerialNumbers);
        Assert.True(report.HasUnserializedFinalOutput);
        Assert.Equal(1m,Assert.Single(report.FinalLots).UnserializedGood);
        Assert.Contains("انبار",report.Disclaimer);
        report=FactoryRecallImpact.Assess(trace,[serial,serial with{
            SerialNumber="DAMPER-0002"}],new Dictionary<Guid,ManufacturingOrder>{{final,order}});
        Assert.False(report.HasUnserializedFinalOutput);
    }

    [Fact]
    public void Depth_limit_does_not_silently_clear_a_longer_trace()
    {
        var (nodes,edges,root,final,finish,op)=Sample();
        var extra=Guid.NewGuid();
        var extended=nodes.Append(new FactoryTraceNode(extra,Guid.NewGuid(),"OTHER",
            Guid.NewGuid(),Guid.NewGuid(),10,"LOT-C",1,DateTime.UtcNow)).ToArray();
        var chain=edges.Append(new FactoryTraceEdge(Guid.NewGuid(),finish,extra,
            1,final,DateTime.UtcNow)).ToArray();
        var partial=FactoryGenealogyExplorer.Explore(root,"descendants",1,extended,chain);
        Assert.True(partial.HasMoreBeyondDepth);
        Assert.Throws<InvalidOperationException>(()=>FactoryRecallImpact.Assess(
            partial,[],new Dictionary<Guid,ManufacturingOrder>()));
        var full=FactoryGenealogyExplorer.Explore(root,"descendants",16,extended,chain);
        Assert.False(full.HasMoreBeyondDepth);
    }

    [Fact]
    public void Cyclic_and_dangling_production_links_are_not_trusted()
    {
        var (nodes,edges,root,_,finish,_)=Sample();
        var cycle=edges.Append(new FactoryTraceEdge(Guid.NewGuid(),finish,root,
            1,Guid.NewGuid(),DateTime.UtcNow)).ToArray();
        Assert.Throws<InvalidOperationException>(()=>
            FactoryGenealogyExplorer.Explore(root,"descendants",16,nodes,cycle));
        var missing=edges.Append(new FactoryTraceEdge(Guid.NewGuid(),finish,Guid.NewGuid(),
            1,Guid.NewGuid(),DateTime.UtcNow)).ToArray();
        Assert.Throws<InvalidOperationException>(()=>
            FactoryGenealogyExplorer.Explore(root,"descendants",16,nodes,missing));
        var forged=edges.Select(x=>x with{Quantity=0}).ToArray();
        Assert.Throws<InvalidOperationException>(()=>
            FactoryGenealogyExplorer.Explore(root,"descendants",16,nodes,forged));
    }
}
