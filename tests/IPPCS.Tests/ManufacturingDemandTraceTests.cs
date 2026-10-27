using Xunit;

public sealed class ManufacturingDemandTraceTests
{
    [Fact]
    public void Shared_component_keeps_two_independent_MPS_sources()
    {
        var parent=Guid.NewGuid();var assembly=Guid.NewGuid();var raw=Guid.NewGuid();
        var date=new DateOnly(2026,10,30);
        var a=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=parent,Quantity=10,NeededOn=date};
        var b=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=parent,Quantity=5,NeededOn=date};
        var top=new ManufacturingBomRevision{ParentItemId=parent,State=ManufacturingRevisionState.Approved,
            ValidFrom=date.AddDays(-1),Lines=[new ManufacturingBomLine{ComponentItemId=assembly,QuantityPer=2}]};
        var inner=new ManufacturingBomRevision{ParentItemId=assembly,State=ManufacturingRevisionState.Approved,
            ValidFrom=date.AddDays(-1),Lines=[new ManufacturingBomLine{ComponentItemId=raw,QuantityPer=3}]};
        var traces=ManufacturingDemandTrace.Trace([a,b],[top,inner]);
        Assert.Equal(4,traces.Count);
        Assert.Equal(60m,Assert.Single(traces.Where(x=>x.MpsLineId==a.Id&&x.ComponentItemId==raw)).GrossQuantity);
        Assert.Equal(30m,Assert.Single(traces.Where(x=>x.MpsLineId==b.Id&&x.ComponentItemId==raw)).GrossQuantity);
        Assert.All(traces.Where(x=>x.ComponentItemId==raw),x=>Assert.Equal(2,x.Level));
    }
    [Fact]
    public void Invalid_scrap_and_bom_cycles_are_rejected()
    {
        var date=new DateOnly(2026,10,30);var root=Guid.NewGuid();var raw=Guid.NewGuid();
        var line=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=root,Quantity=2,NeededOn=date};
        var rev=new ManufacturingBomRevision{ParentItemId=root,State=ManufacturingRevisionState.Approved,
            ValidFrom=date,Lines=[new ManufacturingBomLine{ComponentItemId=raw,QuantityPer=1,ScrapPercent=100}]};
        Assert.Throws<InvalidOperationException>(()=>ManufacturingDemandTrace.Trace([line],[rev]));
        rev.Lines[0].ScrapPercent=0;rev.Lines[0].ComponentItemId=root;
        Assert.Throws<InvalidOperationException>(()=>ManufacturingDemandTrace.Trace([line],[rev]));
    }
}
