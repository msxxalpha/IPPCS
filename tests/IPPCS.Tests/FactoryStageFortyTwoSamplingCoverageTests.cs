using Xunit;

public sealed class FactoryStageFortyTwoSamplingCoverageTests
{
    private static ManufacturingOrderOperation Operation(decimal units)=>new(){
        GoodQuantity=units
    };
    private static ManufacturingInspectionRequirement Requirement()=>new(){
        Code="DAMP-FORCE",Mandatory=true
    };
    private static ManufacturingInspectionResult Sample()=>new(){Value=42,Passed=true};

    [Fact]
    public void Frequency_scenario_shows_gap_but_cannot_claim_approved_sampling_or_msa()
    {
        var req=Requirement();
        var a=FactorySamplingCoverage.Evaluate(Guid.NewGuid(),Operation(11),req,
            [Sample(),Sample()],5);
        Assert.Equal(3,a.MinimumSamplesUnderHypothesis);
        Assert.Equal(2,a.ActualRecordedSamples);
        Assert.Equal(1,a.ApparentCountGap);
        Assert.False(a.SamplingPolicyApproved);
        Assert.False(a.MeasurementSystemVerified);
        Assert.False(a.PerLotSamplingVerified);
        Assert.Equal("PotentialSampleCountGap",a.Assessment);
    }

    [Fact]
    public void Enough_samples_by_count_does_not_certify_lot_sampling()
    {
        var report=FactorySamplingCoverage.Evaluate(Guid.NewGuid(),Operation(10),
            Requirement(),[Sample(),Sample(),Sample()],5);
        Assert.Equal(0,report.ApparentCountGap);
        Assert.Equal("CountOnlyUnverified",report.Assessment);
        Assert.False(report.PerLotSamplingVerified);
    }

    [Fact]
    public void Zero_output_is_explicitly_not_compliant_production()
    {
        var result=FactorySamplingCoverage.Evaluate(Guid.NewGuid(),Operation(0),
            Requirement(),[],1);
        Assert.Equal(0,result.MinimumSamplesUnderHypothesis);
        Assert.Equal("NoProduction",result.Assessment);
        Assert.False(result.SamplingPolicyApproved);
    }

    [Fact]
    public void Invalid_frequency_and_out_of_range_output_fail_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>FactorySamplingCoverage.Evaluate(
            Guid.NewGuid(),Operation(10),Requirement(),[],0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>FactorySamplingCoverage.Evaluate(
            Guid.NewGuid(),Operation(1_000_001m),Requirement(),[],1));
    }
}
