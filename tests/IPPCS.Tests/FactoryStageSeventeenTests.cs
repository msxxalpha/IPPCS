using Xunit;

public sealed class FactoryStageSeventeenTests
{
    [Fact]
    public void Mps_controlled_revision_compares_same_day_item_quantity_without_double_counting()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var day=new DateOnly(2026,10,27);
        var old=new MpsVersion{State=PlanningApproval.Approved,Lines=[
            new MpsDemandLine{ItemId=a,NeededOn=day,Quantity=12},
            new MpsDemandLine{ItemId=a,NeededOn=day,Quantity=3},
            new MpsDemandLine{ItemId=b,NeededOn=day.AddDays(1),Quantity=4}
        ]};
        var revised=new MpsVersion{State=PlanningApproval.Draft,Lines=[
            new MpsDemandLine{ItemId=a,NeededOn=day,Quantity=13},
            new MpsDemandLine{ItemId=a,NeededOn=day,Quantity=3},
            new MpsDemandLine{ItemId=b,NeededOn=day.AddDays(2),Quantity=4}
        ]};
        var changes=FactoryMpsChangeControl.CompareDemand(old.Lines,revised.Lines);
        Assert.Equal(3,changes.Count);
        Assert.Equal(1,changes.Single(x=>x.ItemId==a).Difference);
        Assert.Equal(-4,changes.Single(x=>x.ItemId==b&&x.NeededOn==day.AddDays(1)).Difference);
        Assert.Equal(4,changes.Single(x=>x.ItemId==b&&x.NeededOn==day.AddDays(2)).Difference);
        var crpBefore=new[]{
            new CrpRow(a,day,80,60,20,133.33m),
            new CrpRow(b,day,30,60,0,50)};
        var crpAfter=new[]{
            new CrpRow(a,day,110,60,50,183.33m),
            new CrpRow(b,day,20,60,0,33.33m)};
        var mrpBefore=new[]{new MrpRow(a,day,15,0,15,15,day,0)};
        var mrpAfter=new[]{new MrpRow(a,day,16,0,16,16,day,0)};
        var impact=FactoryMpsChangeControl.Compare(old,revised,crpBefore,crpAfter,
            mrpBefore,mrpAfter);
        Assert.Equal(19,impact.BeforeQuantity);
        Assert.Equal(20,impact.AfterQuantity);
        Assert.Equal(1,impact.DeltaQuantity);
        Assert.Equal(30,impact.Capacity.Single(x=>x.WorkCenterId==a).DeltaMinutes);
        Assert.Equal(50,impact.Capacity.Single(x=>x.WorkCenterId==a).AfterOverloadMinutes);
        Assert.Equal(1,Assert.Single(impact.Materials).SuggestedDifference);
        Assert.Equal(PlanningApproval.Approved,impact.SourceState);
        Assert.Equal(PlanningApproval.Draft,impact.CandidateState);
    }

    [Fact]
    public void Unchanged_version_produces_zero_planning_impact()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var day=new DateOnly(2026,10,27);
        var baseline=new MpsVersion{State=PlanningApproval.Approved,Lines=[
            new MpsDemandLine{ItemId=item,NeededOn=day,Quantity=10}]};
        var clone=new MpsVersion{State=PlanningApproval.Draft,Lines=[
            new MpsDemandLine{ItemId=item,NeededOn=day,Quantity=10}]};
        var rows=new[]{new CrpRow(center,day,100,120,0,83.33m)};
        var material=new[]{new MrpRow(item,day,10,0,10,10,day,0)};
        var impact=FactoryMpsChangeControl.Compare(baseline,clone,rows,rows,material,material);
        Assert.Empty(impact.Demand);
        Assert.Empty(impact.Capacity);
        Assert.Empty(impact.Materials);
        Assert.Equal(0,impact.DeltaQuantity);
    }
}
