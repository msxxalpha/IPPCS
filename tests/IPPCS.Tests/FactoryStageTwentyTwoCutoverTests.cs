using Xunit;

public sealed class FactoryStageTwentyTwoCutoverTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static RoutingRevision Route(Guid item, DateOnly start,
        RoutingState state, DateOnly? end=null)
        => new()
        {
            ItemId=item,EffectiveFrom=start,EffectiveTo=end,State=state,
            Operations=[new RoutingOperation
            {
                Sequence=10,Name="Shock absorber mounting",WorkCenterId=Guid.NewGuid(),
                SetupMinutes=2,RunMinutesPerUnit=1,YieldPercent=100
            }]
        };
    private static ManufacturingBomRevision Bom(Guid item, Guid component,DateOnly start,
        ManufacturingRevisionState state,DateOnly? end=null)
        => new()
        {
            ParentItemId=item,ValidFrom=start,ValidUntil=end,State=state,
            Lines=[new ManufacturingBomLine
            {
                ComponentItemId=component,Sequence=10,QuantityPer=2,ScrapPercent=0
            }]
        };

    [Fact]
    public void Routing_cutover_closes_yesterday_and_approves_nonoverlapping_future()
    {
        var item=Guid.NewGuid();
        var old=Route(item,Today,RoutingState.Approved);
        var next=Route(item,Today.AddDays(1),RoutingState.Draft);
        Assert.Empty(FactoryEngineeringCutover.CheckRouting(old,next,Today.AddDays(1),[old]));
        // It is a dry-run calculator: the persisted inputs remain unchanged.
        Assert.Null(old.EffectiveTo);
        Assert.Equal(RoutingState.Draft,next.State);
        Assert.Empty(FactoryRevisionGovernance.RoutingIssues(next,[
            Route(item,Today,RoutingState.Approved,Today)
        ]));
    }

    [Fact]
    public void Routing_rejects_mismatched_product_and_cutover_date()
    {
        var old=Route(Guid.NewGuid(),Today,RoutingState.Approved);
        var other=Route(Guid.NewGuid(),Today.AddDays(1),RoutingState.Draft);
        Assert.Contains(FactoryEngineeringCutover.CheckRouting(old,other,Today.AddDays(1),[old]),
            x=>x.Code=="InvalidTransition");
        other.ItemId=old.ItemId;
        Assert.Contains(FactoryEngineeringCutover.CheckRouting(old,other,Today,[old]),
            x=>x.Code=="InvalidCutoverDate");
        Assert.Contains(FactoryEngineeringCutover.CheckRouting(old,other,Today.AddDays(2),[old]),
            x=>x.Code=="InvalidCutoverDate");
        old.EffectiveTo=Today;
        Assert.Contains(FactoryEngineeringCutover.CheckRouting(old,other,Today.AddDays(1),[old]),
            x=>x.Code=="InvalidCutoverDate");
    }

    [Fact]
    public void Routing_rejects_third_approved_revision_at_cutover()
    {
        var item=Guid.NewGuid();
        var old=Route(item,Today,RoutingState.Approved);
        var candidate=Route(item,Today.AddDays(1),RoutingState.Draft);
        var third=Route(item,Today.AddDays(1),RoutingState.Approved);
        var issues=FactoryEngineeringCutover.CheckRouting(old,candidate,Today.AddDays(1),[old,third]);
        Assert.Contains(issues,x=>x.Code=="ApprovedOverlap");
    }

    [Fact]
    public void Bom_cutover_obeys_effective_date_and_preserves_old_revision_instance()
    {
        var item=Guid.NewGuid();var component=Guid.NewGuid();
        var old=Bom(item,component,Today,ManufacturingRevisionState.Approved);
        var candidate=Bom(item,component,Today.AddDays(1),ManufacturingRevisionState.Draft);
        candidate.Lines[0].QuantityPer=3;
        Assert.Empty(FactoryEngineeringCutover.CheckBom(old,candidate,Today.AddDays(1),[old]));
        Assert.Null(old.ValidUntil);
        Assert.Equal(ManufacturingRevisionState.Draft,candidate.State);
        candidate.ValidFrom=Today.AddDays(2);
        Assert.Contains(FactoryEngineeringCutover.CheckBom(old,candidate,Today.AddDays(1),[old]),
            x=>x.Code=="InvalidCutoverDate");
    }

    [Fact]
    public void Bom_cutover_rejects_cycle_and_invalid_candidate()
    {
        var item=Guid.NewGuid();var component=Guid.NewGuid();
        var old=Bom(item,component,Today,ManufacturingRevisionState.Approved);
        var next=Bom(item,component,Today.AddDays(1),ManufacturingRevisionState.Draft);
        var reverse=Bom(component,item,Today,ManufacturingRevisionState.Approved);
        var issues=FactoryEngineeringCutover.CheckBom(old,next,Today.AddDays(1),[old,reverse]);
        Assert.Contains(issues,x=>x.Code=="BomCycle");
        next.Lines.Clear();
        Assert.Contains(FactoryEngineeringCutover.CheckBom(old,next,Today.AddDays(1),[old]),
            x=>x.Code=="EmptyBom");
    }

    [Fact]
    public void Cutover_does_not_accept_duplicate_old_revision_or_already_approved_candidate()
    {
        var item=Guid.NewGuid();
        var old=Route(item,Today,RoutingState.Approved);
        var next=Route(item,Today.AddDays(1),RoutingState.Approved);
        Assert.Contains(FactoryEngineeringCutover.CheckRouting(old,next,Today.AddDays(1),[old,next]),
            x=>x.Code=="InvalidTransition");
        Assert.Contains(FactoryEngineeringCutover.CheckRouting(old,old,Today.AddDays(1),[old]),
            x=>x.Code=="InvalidTransition");
    }
}
