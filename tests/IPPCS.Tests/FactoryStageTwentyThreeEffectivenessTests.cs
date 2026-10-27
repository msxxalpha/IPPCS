using Xunit;

public sealed class FactoryStageTwentyThreeEffectivenessTests
{
    private static ProductionDeviation ClosedCase()
    {
        var item=new ProductionDeviation
        {
            CreatedBy="planner-creator",Status=ProductionDeviationStatus.Closed,
            Responsible="shift-controller",ClosedUtc=DateTime.UtcNow
        };
        item.Events=[
            new ProductionDeviationEvent{DeviationId=item.Id,
                Username="planner-creator",Status=ProductionDeviationStatus.Open,
                Details="New deviation",AtUtc=new DateTime(2026,10,27,8,0,0,DateTimeKind.Utc)},
            new ProductionDeviationEvent{DeviationId=item.Id,
                Username="planner-action",Status=ProductionDeviationStatus.ActionAssigned,
                Details="Action assigned",AtUtc=new DateTime(2026,10,27,8,5,0,DateTimeKind.Utc)},
            new ProductionDeviationEvent{DeviationId=item.Id,
                Username="planner-close",Status=ProductionDeviationStatus.Closed,
                Details="Actual closure evidence",AtUtc=new DateTime(2026,10,27,8,10,0,DateTimeKind.Utc)}
        ];
        return item;
    }

    [Fact]
    public void Closed_case_is_not_certified_before_independent_quality_review()
    {
        var row=ClosedCase();
        var current=FactoryDeviationEffectiveness.Evaluate(row);
        Assert.Equal("AwaitingIndependentReview",current.State);
        Assert.True(current.AwaitingReview);
        Assert.False(current.IndependentlyVerified);
    }

    [Theory]
    [InlineData("planner-creator")]
    [InlineData("planner-action")]
    [InlineData("planner-close")]
    [InlineData("shift-controller")]
    [InlineData("")]
    public void Self_review_or_responsible_user_is_rejected(string actor)
    {
        Assert.False(FactoryDeviationEffectiveness.CanReview(ClosedCase(),actor,out _));
    }

    [Fact]
    public void Independent_quality_reviewer_can_certify_exactly_once()
    {
        var row=ClosedCase();
        Assert.True(FactoryDeviationEffectiveness.CanReview(row,"quality-inspector",out _));
        var proof=new FactoryDeviationEffectivenessProof(true,
            "QMS-2026-51","Effectiveness checked against shop floor record",
            "quality-inspector",new DateTime(2026,10,27,8,15,0,DateTimeKind.Utc));
        var encoded=FactoryDeviationEffectiveness.Encode(proof);
        Assert.StartsWith(FactoryDeviationEffectiveness.EventPrefix,encoded);
        Assert.Equal(proof,FactoryDeviationEffectiveness.Decode(encoded));
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Username="quality-inspector",Status=ProductionDeviationStatus.Closed,
            Details=encoded,AtUtc=proof.ReviewedUtc});
        var state=FactoryDeviationEffectiveness.Evaluate(row);
        Assert.True(state.IndependentlyVerified);
        Assert.Equal("Verified",state.State);
        Assert.False(FactoryDeviationEffectiveness.CanReview(row,"other-quality",out _));
    }

    [Fact]
    public void Rejection_reopens_corrective_action_and_new_independent_reviewer_can_certify_after_reclosure()
    {
        var row=ClosedCase();
        row.Status=ProductionDeviationStatus.ActionAssigned;row.ClosedUtc=null;
        var rejected=new FactoryDeviationEffectivenessProof(false,
            "QMS-REJECT-91","Effect not sustained during production check",
            "qa-1",new DateTime(2026,10,27,8,20,0,DateTimeKind.Utc));
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.ActionAssigned,Username="qa-1",
            Details=FactoryDeviationEffectiveness.Encode(rejected),AtUtc=rejected.ReviewedUtc});
        var status=FactoryDeviationEffectiveness.Evaluate(row);
        Assert.True(status.ReopenedForCorrection);
        Assert.False(status.IndependentlyVerified);
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.ActionAssigned,Username="planner-revision",
            Details="REVISION:Corrected",AtUtc=rejected.ReviewedUtc.AddMinutes(3)});
        row.Status=ProductionDeviationStatus.Closed;row.ClosedUtc=rejected.ReviewedUtc.AddMinutes(5);
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.Closed,Username="planner-close",
            Details="New closure evidence",AtUtc=row.ClosedUtc.Value});
        Assert.True(FactoryDeviationEffectiveness.CanReview(row,"qa-2",out _));
        Assert.False(FactoryDeviationEffectiveness.CanReview(row,"qa-1",out _));
        var passed=new FactoryDeviationEffectivenessProof(true,"QMS-PASS-92",
            "Valid sample shows effective intervention",
            "qa-2",rejected.ReviewedUtc.AddMinutes(6));
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.Closed,Username="qa-2",
            Details=FactoryDeviationEffectiveness.Encode(passed),AtUtc=passed.ReviewedUtc});
        Assert.True(FactoryDeviationEffectiveness.Evaluate(row).IndependentlyVerified);
    }

    [Fact]
    public void Malformed_or_duplicate_verification_evidence_fails_closed()
    {
        var row=ClosedCase();
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.Closed,Username="qa-1",
            Details=FactoryDeviationEffectiveness.EventPrefix+"invalid-json"});
        Assert.Equal("InvalidEvidence",FactoryDeviationEffectiveness.Evaluate(row).State);
        Assert.False(FactoryDeviationEffectiveness.CanReview(row,"qa-2",out _));
        row.Events.RemoveAt(row.Events.Count-1);
        var p=new FactoryDeviationEffectivenessProof(true,"REF-123",
            "Manufacturing line result", "qa-1",DateTime.UtcNow);
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.Closed,Username="qa-1",
            Details=FactoryDeviationEffectiveness.Encode(p)});
        row.Events.Add(new ProductionDeviationEvent{DeviationId=row.Id,
            Status=ProductionDeviationStatus.Closed,Username="qa-2",
            Details=FactoryDeviationEffectiveness.Encode(p with { ReviewedBy="qa-2" })});
        Assert.False(FactoryDeviationEffectiveness.Evaluate(row).IndependentlyVerified);
    }
}
