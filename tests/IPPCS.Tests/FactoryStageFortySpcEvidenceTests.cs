using Xunit;

public sealed class FactoryStageFortySpcEvidenceTests
{
    private static readonly DateTime Start=new(2026,10,27,10,0,0,DateTimeKind.Utc);
    private static ManufacturingInspectionRequirement Requirement(decimal? minimum=0,
        decimal? maximum=100) => new(){Code="DAMPING-FORCE",Unit="N",
        Minimum=minimum,Maximum=maximum,Mandatory=true};
    private static ManufacturingInspectionResult[] Entries(params decimal[] values)=>
        values.Select((value,index)=>new ManufacturingInspectionResult{
            Value=value,Passed=value>=0&&value<=100,
            RecordedUtc=Start.AddMinutes(index)}).ToArray();

    [Fact]
    public void Initial_five_samples_are_not_a_validated_spc_reference()
    {
        var r=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(),Entries(10,11,10,12,11));
        Assert.Equal(5,r.SampleCount);
        Assert.False(r.ReferenceHistoryLongEnough);
        Assert.False(r.ProvisionalLimitAvailable);
        Assert.False(r.MeasurementSystemVerified);
        Assert.False(r.CapabilityApproved);
        Assert.Equal("InsufficientReferenceHistory",r.Assessment);
        Assert.Empty(r.Signals);
    }

    [Fact]
    public void Exploratory_series_flags_eight_consecutive_points_on_one_side()
    {
        var values=Enumerable.Range(0,17).Select(i=>i%2==0?9m:11m)
            .Concat(Enumerable.Repeat(20m,8)).ToArray();
        var r=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(),Entries(values));
        Assert.Equal(25,r.SampleCount);
        Assert.True(r.ProvisionalLimitAvailable);
        Assert.Equal("ExploratorySignals",r.Assessment);
        Assert.Contains(r.Signals,s=>s.Rule=="EightOnOneSide"&&s.StartIndex==18&&s.EndIndex==25);
        Assert.False(r.CapabilityApproved);
    }

    [Fact]
    public void Six_strictly_increasing_points_trigger_trend_but_flat_steps_break_it()
    {
        var values=Enumerable.Range(0,19).Select(i=>i%2==0?16m:12m)
            .Concat(new decimal[]{13,14,15,16,17,18}).ToArray();
        var r=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(),Entries(values));
        Assert.Contains(r.Signals,s=>s.Rule=="SixPointTrend"&&s.StartIndex==20&&s.EndIndex==25);
        var flat=Enumerable.Repeat(10m,25).ToArray();
        var noVariation=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(),Entries(flat));
        Assert.Equal("NoObservedVariation",noVariation.Assessment);
        Assert.False(noVariation.ProvisionalLimitAvailable);
        Assert.DoesNotContain(noVariation.Signals,s=>s.Rule=="SixPointTrend");
    }

    [Fact]
    public void Historical_result_mismatch_and_duplicate_timestamps_prevent_baseline()
    {
        var samples=Entries(Enumerable.Range(0,25)
            .Select(i=>i%2==0?10m:11m).ToArray());
        samples[3].Value=150;
        samples[3].Passed=true;
        samples[5].RecordedUtc=samples[4].RecordedUtc;
        var r=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(),samples);
        Assert.Equal(1,r.InvalidHistoricalPassFlags);
        Assert.Equal(1,r.SpecViolations);
        Assert.Equal(1,r.DuplicateTimestamps);
        Assert.False(r.ProvisionalLimitAvailable);
        Assert.Equal("HistoricalInconsistency",r.Assessment);
    }

    [Fact]
    public void Missing_specifications_are_not_treated_as_product_conformity()
    {
        var r=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(null,null),
            Entries(Enumerable.Range(0,25).Select(i=>(decimal)i).ToArray()));
        Assert.False(r.HasSpecificationLimits);
        Assert.Equal("SpecificationMissing",r.Assessment);
        Assert.False(r.CapabilityApproved);
        Assert.False(r.MeasurementSystemVerified);
        Assert.Contains(r.Findings,x=>x.Contains("مشخصات فنی"));
    }

    [Fact]
    public void Sample_days_are_counted_in_tehran_not_utc()
    {
        var samples=Entries(10,11);
        samples[0].RecordedUtc=new DateTime(2026,10,27,20,30,0,DateTimeKind.Utc);
        samples[1].RecordedUtc=new DateTime(2026,10,28,0,30,0,DateTimeKind.Utc);
        var r=FactorySpcEvidence.Review(Guid.NewGuid(),Requirement(),samples);
        Assert.Equal(1,r.DistinctSampleDays);
    }
}
