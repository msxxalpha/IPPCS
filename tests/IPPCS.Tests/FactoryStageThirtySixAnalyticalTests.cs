using Xunit;

public sealed class FactoryStageThirtySixAnalyticalTests
{
    [Fact]
    public void Missing_inputs_on_first_install_block_all_eight_trial_calculations()
    {
        var gates=FactoryAnalyticalVerification.Build(
            confirmedDemand:0,confirmedMonths:0,sopRuns:0,approvedMps:0,
            approvedBom:0,balances:0,mrpRuns:0,centers:0,routings:0,shifts:0,
            apsRuns:0,oeePlans:0,clockStops:0,closedTimedStops:0,costRates:0,
            frozenRates:0,orders:0);
        Assert.Equal(FactoryAnalyticalVerification.ModuleNumbers,gates.Select(x=>x.Number));
        Assert.Equal(8,gates.Count);
        Assert.All(gates,gate=>{
            Assert.False(gate.CanRun);
            Assert.False(gate.HasRecordedEvidence);
            Assert.Equal("MissingInputs",gate.State);
            Assert.NotEmpty(gate.Warning);
            Assert.All(gate.Checks,c=>Assert.True(c.Minimum>0));
        });
    }

    [Fact]
    public void Real_example_data_changes_only_test_readiness_and_recorded_evidence()
    {
        var gates=FactoryAnalyticalVerification.Build(
            confirmedDemand:3,confirmedMonths:2,sopRuns:1,approvedMps:1,
            approvedBom:1,balances:2,mrpRuns:1,centers:1,routings:1,shifts:2,
            apsRuns:1,oeePlans:1,clockStops:1,closedTimedStops:1,costRates:1,
            frozenRates:2,orders:1);
        Assert.All(gates,x=>Assert.True(x.CanRun));
        Assert.Equal(new[]{6,16},gates.Where(x=>!x.HasRecordedEvidence)
            .Select(x=>x.Number));
        Assert.All(gates.Where(x=>x.HasRecordedEvidence),
            x=>Assert.Equal("EvidenceAvailable",x.State));
        Assert.All(gates.Where(x=>!x.HasRecordedEvidence),
            x=>Assert.Equal("ReadyForFirstTest",x.State));
    }

    [Fact]
    public void Missing_frozen_cost_rate_and_observed_clock_must_not_be_inferred_from_orders()
    {
        var gates=FactoryAnalyticalVerification.Build(
            confirmedDemand:2,confirmedMonths:2,sopRuns:2,approvedMps:1,
            approvedBom:1,balances:1,mrpRuns:2,centers:1,routings:1,shifts:1,
            apsRuns:2,oeePlans:1,clockStops:0,closedTimedStops:2,costRates:1,
            frozenRates:0,orders:3);
        var oee=gates.Single(x=>x.Number==14);
        Assert.False(oee.CanRun);
        Assert.Equal("MissingInputs",oee.State);
        Assert.Contains(oee.Checks,x=>x.Key=="clock"&&!x.Ready);
        var cost=gates.Single(x=>x.Number==17);
        Assert.False(cost.CanRun);
        Assert.False(cost.HasRecordedEvidence);
        Assert.Contains(cost.Checks,x=>x.Key=="frozen"&&!x.Ready);
    }
}
