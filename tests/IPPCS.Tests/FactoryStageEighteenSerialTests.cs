using Xunit;
public sealed class FactoryStageEighteenSerialTests
{
    [Fact]
    public void Normalized_serial_is_unique_case_insensitive_and_persian_digits_are_latinized()
    {
        Assert.Equal("SAIPA-1405-001",FactorySerialGenealogy.Normalize("  saipa-۱۴۰۵-۰۰۱ "));
        Assert.True(FactorySerialGenealogy.Valid("SAIPA-1405-001"));
        Assert.False(FactorySerialGenealogy.Valid("serial;unsafe"));
        Assert.False(FactorySerialGenealogy.Valid("نامعتبر"));
        Assert.False(FactorySerialGenealogy.Valid("AB"));
    }
    private static (ManufacturingOrder,ManufacturingConfirmation) Finished(string code,int count)
    {
        var order=new ManufacturingOrder{Status=ManufacturingOrderStatus.Completed,
            Number=code,Kind=ManufacturingOrderKind.FinalAssembly};
        var op=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=10,
            GoodQuantity=count,IsComplete=true};
        order.Operations.Add(op);
        return (order,new ManufacturingConfirmation{OperationId=op.Id,GoodQuantity=count,
            LotCode="LOT-"+code});
    }
    [Fact]
    public void Journal_round_trip_validates_global_order_lot_source_and_detects_duplicates()
    {
        var (o,l)=Finished("ONE",2);
        var a=new ManufacturingOrderAudit{OrderId=o.Id,
            Action=FactorySerialGenealogy.AssignmentAction,
            Details=FactorySerialGenealogy.Encode("SHOCK-0001",l.Id,"qa-operator")};
        var b=new ManufacturingOrderAudit{OrderId=o.Id,
            Action=FactorySerialGenealogy.AssignmentAction,
            Details=FactorySerialGenealogy.Encode("SHOCK-0002",l.Id,"qa-operator")};
        var rows=FactorySerialGenealogy.BuildLedger([a,b],[l],
            new Dictionary<Guid,ManufacturingOrder>{{o.Id,o}});
        Assert.Equal(2,rows.Count);
        Assert.All(rows,x=>Assert.Equal(l.Id,x.ConfirmationId));
        Assert.Equal("qa-operator",rows[0].RecordedBy);
        Assert.Throws<InvalidOperationException>(()=>FactorySerialGenealogy.BuildLedger(
            [a,a],[l],new Dictionary<Guid,ManufacturingOrder>{{o.Id,o}}));
        var excessive=new ManufacturingOrderAudit{OrderId=o.Id,
            Action=FactorySerialGenealogy.AssignmentAction,
            Details=FactorySerialGenealogy.Encode("SHOCK-0003",l.Id,"qa")};
        Assert.Throws<InvalidOperationException>(()=>FactorySerialGenealogy.BuildLedger(
            [a,b,excessive],[l],new Dictionary<Guid,ManufacturingOrder>{{o.Id,o}}));
        var (otherOrder,otherLot)=Finished("TWO",1);
        var forged=new ManufacturingOrderAudit{OrderId=otherOrder.Id,Action=a.Action,
            Details=a.Details};
        Assert.Throws<InvalidOperationException>(()=>FactorySerialGenealogy.BuildLedger(
            [a,forged],[l,otherLot],
            new Dictionary<Guid,ManufacturingOrder>{{o.Id,o},{otherOrder.Id,otherOrder}}));
    }
    [Fact]
    public void Journal_refuses_legacy_malformed_links_and_nonintegral_finished_good()
    {
        var (o,l)=Finished("ONE",2);
        var a=new ManufacturingOrderAudit{OrderId=o.Id,
            Action=FactorySerialGenealogy.AssignmentAction,
            Details=FactorySerialGenealogy.Encode("SHOCK-0100",l.Id,"qa")};
        l.GoodQuantity=2.5m;
        Assert.Throws<InvalidOperationException>(()=>FactorySerialGenealogy.BuildLedger(
            [a],[l],new Dictionary<Guid,ManufacturingOrder>{{o.Id,o}}));
        l.GoodQuantity=2;
        a.Details="{\"serial\":\"INVALID;INJECTION\",\"confirmationId\":\""+
            l.Id+"\",\"recordedBy\":\"qa\"}";
        Assert.Throws<InvalidOperationException>(()=>FactorySerialGenealogy.BuildLedger(
            [a],[l],new Dictionary<Guid,ManufacturingOrder>{{o.Id,o}}));
    }
}
