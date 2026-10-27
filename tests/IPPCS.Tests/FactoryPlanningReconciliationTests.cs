using Xunit;

public sealed class FactoryPlanningReconciliationTests
{
    [Fact]
    public void Historical_sop_snapshot_is_compared_to_current_mix_and_approved_mps_without_rewriting_it()
    {
        var from=new DateOnly(2026,10,26);
        var sop=new FactorySopReview{From=from,Through=from.AddDays(5),DemandQuantity=20};
        var version=new MpsVersion{State=PlanningApproval.Approved,Lines=[
            new MpsDemandLine{Quantity=12,NeededOn=from.AddDays(1)},
            new MpsDemandLine{Quantity=5,NeededOn=from.AddDays(3)},
            new MpsDemandLine{Quantity=40,NeededOn=from.AddDays(8)}
        ]};
        var report=FactoryPlanningReconciliation.ReconcileSop(sop,version,[
            new FactoryDemand{DueOn=from.AddDays(1),Quantity=9,Category="Confirmed"},
            new FactoryDemand{DueOn=from.AddDays(2),Quantity=8,Category="Forecast"},
            new FactoryDemand{DueOn=from.AddDays(8),Quantity=99,Category="Confirmed"}
        ]);
        Assert.Equal(20,report.HistoricalSopDemand);
        Assert.Equal(17,report.CurrentScopedDemand);
        Assert.Equal(17,report.ApprovedMpsQuantity);
        Assert.Equal(-3,report.DifferenceToSop);
        Assert.True(report.DemandSnapshotChanged);
        Assert.Equal(2,report.InWindowMpsLines);
        Assert.Equal(20,sop.DemandQuantity);
    }

    [Fact]
    public void Mps_confirmed_demand_audit_does_not_pretend_manual_lines_are_explicitly_pegged()
    {
        var demand=new FactoryDemand{ItemId=Guid.NewGuid(),Category="Confirmed",
            DueOn=new DateOnly(2026,10,27),Quantity=10,Reference="factory-confirmation"};
        var another=new FactoryDemand{ItemId=demand.ItemId,Category="Confirmed",
            DueOn=demand.DueOn.AddDays(1),Quantity=3};
        var mps=new MpsVersion{State=PlanningApproval.Approved,Lines=[
            new MpsDemandLine{ItemId=demand.ItemId,NeededOn=demand.DueOn,
                Quantity=6,Reference="DEMAND:"+demand.Id.ToString("N")},
            new MpsDemandLine{ItemId=demand.ItemId,NeededOn=demand.DueOn,
                Quantity=4,Reference="Manual"}
        ]};
        var report=FactoryPlanningReconciliation.AuditDemandReferences(mps,[demand,another]);
        Assert.Equal(13,report.ConfirmedDemandQuantity);
        Assert.Equal(7,report.ConfirmedUncoveredQuantity);
        Assert.Equal(4,report.OtherMpsQuantity);
        Assert.Empty(report.UnmatchedReferenceLines);
        mps.Lines[0].ItemId=Guid.NewGuid();
        Assert.True(FactoryPlanningReconciliation.AuditDemandReferences(mps,[demand]).DemandGaps[0].ReferenceChanged);
        Assert.Throws<InvalidOperationException>(()=>FactoryPlanningReconciliation.AuditDemandReferences(
            new MpsVersion{State=PlanningApproval.Draft},[demand]));
    }

    [Fact]
    public void Mrp_source_risk_separates_assumed_balances_receipts_and_unfirm_plans()
    {
        var item=Guid.NewGuid(),today=new DateOnly(2026,10,27);
        var demand=new NetPegDemand("A",item,today,10,Guid.NewGuid(),null,null,"A",0);
        var source=new NetPeggingReport([demand],[
            new NetPegAllocation("A",item,today,"PlanningOpeningBalance","BASE",today,2,true),
            new NetPegAllocation("A",item,today,"ExpectedReceipt","EXPECTED",today,3,true),
            new NetPegAllocation("A",item,today,"ProposedExternalReceipt","PLAN",today,5,false)
        ],[new NetPegPlannedLot("PLAN",item,today,today.AddDays(-2),
            6,5,1,"ExternalPlanningOnly",null,true,1)]);
        var report=FactoryPlanningReconciliation.MrpRisk(Guid.NewGuid(),source);
        var row=Assert.Single(report.Rows);
        Assert.Equal(50,row.AssumedCoveragePercent);
        Assert.Equal(5,row.ProposedReceipt);
        Assert.Equal(1,row.UnallocatedProposalQuantity);
        Assert.Equal(1,row.PastReleaseLots);
        Assert.Equal(1,report.ReleaseBeforeBaselineLots);
        Assert.Equal(0,report.ProposedManufacturingLots);
    }

    [Fact]
    public void Crp_ranks_bottlenecks_and_preserves_undefined_utilization_on_zero_capacity()
    {
        var center=Guid.NewGuid(),noShift=Guid.NewGuid(),date=new DateOnly(2026,10,27);
        var mps=Guid.NewGuid();
        var report=FactoryPlanningReconciliation.CenterStress(mps,[
            new CrpRow(center,date,120,60,60,200),
            new CrpRow(center,date.AddDays(1),60,120,0,50),
            new CrpRow(noShift,date,100,0,100,null)
        ]);
        Assert.Equal(3,report.DayCenterRows);
        Assert.Equal(noShift,report.Centers[0].WorkCenterId);
        Assert.Null(report.Centers[0].UtilizationPercent);
        Assert.Equal(1,report.Centers[0].DaysWithoutCapacity);
        Assert.Equal(100,report.Centers[1].UtilizationPercent);
        Assert.Equal(1,report.Centers[1].OverloadedDays);
    }

    [Fact]
    public void Whatif_maintenance_disables_center_without_mutating_shifts_or_schedules()
    {
        var day=new DateOnly(2026,10,27),center=Guid.NewGuid(),mps=Guid.NewGuid();
        var shifts=new[]{new WorkShift{WorkCenterId=center,DayOfWeek=(int)day.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(10,0),
            EffectiveCapacityPercent=100}};
        var tasks=new[]{new ApsTask(Guid.NewGuid(),Guid.NewGuid(),day,10,Guid.NewGuid(),
            10,center,30,"Production")};
        var without=FactoryPlanningReconciliation.CompareScenario(mps,day,1,tasks,shifts,1,1,
            new HashSet<(Guid,DateOnly)>());
        Assert.Equal(1,without.Baseline.ScheduledOperations);
        Assert.Equal(0,without.ScheduledChange);
        var result=FactoryPlanningReconciliation.CompareScenario(mps,day,1,tasks,shifts,
            1,1,new HashSet<(Guid,DateOnly)>{(center,day)});
        Assert.Equal(1,result.Baseline.ScheduledOperations);
        Assert.Equal(0,result.Scenario.ScheduledOperations);
        Assert.Equal(-1,result.ScheduledChange);
        Assert.Equal(1,result.Scenario.UnscheduledOperations);
        Assert.Equal(100,shifts[0].EffectiveCapacityPercent);
    }
}
