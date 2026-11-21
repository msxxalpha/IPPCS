using Xunit;

public sealed class NetMrpSuggestionExplainerTests
{
    [Fact]
    public void Shows_batch_lot_uplift_and_release_before_baseline()
    {
        var item=Guid.NewGuid();var baseline=new DateOnly(2026,10,27);
        var b=new PlanningBalance{ItemId=item,SnapshotDate=baseline,LeadDays=6};
        var r=new MrpRow(item,baseline.AddDays(4),17m,10m,7m,10m,baseline.AddDays(-2),3m);
        var x=Assert.Single(NetMrpSuggestionExplainer.Explain([r],[b]));
        Assert.Equal(3m,x.LotSizingSurplus);
        Assert.True(x.ReleaseBeforePlanningBaseline);
        Assert.NotNull(x.Warning);
    }
    [Fact]
    public void Rejects_inconsistent_shortage_and_projected_balance()
    {
        var item=Guid.NewGuid();var d=new DateOnly(2026,10,27);
        var b=new PlanningBalance{ItemId=item,SnapshotDate=d,LeadDays=1};
        var invalid=new MrpRow(item,d.AddDays(2),10,4,5,6,d.AddDays(1),0);
        Assert.Throws<InvalidOperationException>(()=>NetMrpSuggestionExplainer.Explain([invalid],[b]));
    }
    [Fact]
    public void No_shortage_means_no_release_or_surplus()
    {
        var item=Guid.NewGuid();var d=new DateOnly(2026,10,27);
        var b=new PlanningBalance{ItemId=item,SnapshotDate=d,LeadDays=2};
        var row=new MrpRow(item,d.AddDays(2),4,10,0,0,null,6);
        var x=Assert.Single(NetMrpSuggestionExplainer.Explain([row],[b]));
        Assert.Equal(0,x.LotSizingSurplus);
        Assert.Null(x.ProposedReleaseDate);
    }
}
