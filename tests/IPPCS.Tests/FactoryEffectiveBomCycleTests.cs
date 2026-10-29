using Xunit;

public sealed class FactoryEffectiveBomCycleTests
{
    [Fact]
    public void Disjoint_revision_intervals_do_not_create_a_false_cycle()
    {
        var itemA=Guid.NewGuid();
        var itemB=Guid.NewGuid();
        var aToB=new ManufacturingBomRevision
        {
            ParentItemId=itemA, Revision="A-OLD", State=ManufacturingRevisionState.Approved,
            ValidFrom=new DateOnly(2026,1,1), ValidUntil=new DateOnly(2026,6,30),
            Lines=[new ManufacturingBomLine { ComponentItemId=itemB, QuantityPer=1, Sequence=10 }]
        };
        var bToA=new ManufacturingBomRevision
        {
            ParentItemId=itemB, Revision="B-NEW", State=ManufacturingRevisionState.Approved,
            ValidFrom=new DateOnly(2026,7,1),
            Lines=[new ManufacturingBomLine { ComponentItemId=itemA, QuantityPer=1, Sequence=10 }]
        };

        Assert.False(FactoryRevisionGovernance.HasEffectiveBomCycle([aToB,bToA]));
    }

    [Fact]
    public void Cycle_is_rejected_when_all_edges_are_effective_on_the_same_date()
    {
        var itemA=Guid.NewGuid();
        var itemB=Guid.NewGuid();
        var aToB=new ManufacturingBomRevision
        {
            ParentItemId=itemA, Revision="A", State=ManufacturingRevisionState.Approved,
            ValidFrom=new DateOnly(2026,1,1),
            Lines=[new ManufacturingBomLine { ComponentItemId=itemB, QuantityPer=1, Sequence=10 }]
        };
        var bToA=new ManufacturingBomRevision
        {
            ParentItemId=itemB, Revision="B", State=ManufacturingRevisionState.Approved,
            ValidFrom=new DateOnly(2026,2,1), ValidUntil=new DateOnly(2026,11,30),
            Lines=[new ManufacturingBomLine { ComponentItemId=itemA, QuantityPer=1, Sequence=10 }]
        };

        Assert.True(FactoryRevisionGovernance.HasEffectiveBomCycle([aToB,bToA]));
    }
}
