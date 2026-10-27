using Xunit;

public sealed class FactoryProductionIntelligenceTests
{
    private static FactoryDemand Demand(int ordinal,decimal quantity)=>new(){
        ItemId=Guid.NewGuid(),Category="Confirmed",
        DueOn=FactoryProductionIntelligence.JalaliMonth(ordinal),Quantity=quantity
    };

    [Fact]
    public void Backtest_uses_only_prior_persian_months_not_actual_target_leakage()
    {
        var end=1405*12+5;
        var history=new[]{
            Demand(end-3,10),Demand(end-2,20),Demand(end-1,30),Demand(end,50)
        };
        var report=FactoryProductionIntelligence.Backtest(history,
            FactoryProductionIntelligence.JalaliMonth(end),2,2);
        Assert.Equal(2,report.Months.Count);
        Assert.Equal(15m,report.Months[0].Predicted);
        Assert.Equal(30m,report.Months[0].Actual);
        Assert.Equal(25m,report.Months[1].Predicted);
        Assert.Equal(50m,report.Months[1].Actual);
        Assert.Equal(20m,report.MeanAbsoluteError);
        Assert.Equal(50m,report.WapePercent);
        Assert.Equal(-20m,report.Bias);
        Assert.Equal(2,report.MonthsWithHistory);
    }

    [Fact]
    public void Backtest_with_no_actuals_never_divides_by_zero_or_claims_accuracy()
    {
        var anchor=FactoryProductionIntelligence.JalaliMonth(1405*12+3);
        var report=FactoryProductionIntelligence.Backtest([],anchor,6,3);
        Assert.Null(report.WapePercent);
        Assert.Equal(0,report.MonthsWithHistory);
        Assert.Contains("WAPE",report.Warning);
        Assert.All(report.Months,x=>Assert.Null(x.AbsolutePercentageError));
    }

    [Fact]
    public void Conversion_cost_respects_rate_effective_date_and_actual_closed_sessions()
    {
        var order=new ManufacturingOrder{Id=Guid.NewGuid(),PlannedQuantity=10,
            PlannedStart=new DateOnly(2026,10,26)};
        var op=new ManufacturingOrderOperation{Id=Guid.NewGuid(),Sequence=10,
            WorkCenterId=Guid.NewGuid(),SetupMinutes=10,RunMinutesPerUnit=6};
        var rate=new FactoryCenterCostRate{WorkCenterId=op.WorkCenterId,
            EffectiveFrom=order.PlannedStart,ConversionRialsPerMinute=1000};
        var started=new DateTime(2026,10,26,4,30,0,DateTimeKind.Utc);
        var clock=new FactoryClockSession(order.Id,op.Id,op.WorkCenterId,
            started,started.AddMinutes(90));
        var result=FactoryProductionIntelligence.OperationCost(order,op,rate,[clock]);
        Assert.True(result.HasApplicableRate);
        Assert.Equal(70m,result.StandardMinutes);
        Assert.Equal(90m,result.ActualMinutes);
        Assert.Equal(20m,result.MinutesVariance);
        Assert.Equal(70000m,result.StandardConversionRials);
        Assert.Equal(90000m,result.ActualConversionRials);
        Assert.Equal(20000m,result.ConversionVarianceRials);
        var totals=FactoryProductionIntelligence.TotalCosts([result]);
        Assert.Equal(20000m,totals.ConversionVarianceRials);
    }

    [Fact]
    public void Missing_or_future_rates_and_running_clocks_are_not_fabricated_as_zero_cost()
    {
        var order=new ManufacturingOrder{Id=Guid.NewGuid(),PlannedQuantity=2,
            PlannedStart=new DateOnly(2026,10,26)};
        var op=new ManufacturingOrderOperation{Id=Guid.NewGuid(),WorkCenterId=Guid.NewGuid(),
            SetupMinutes=5,RunMinutesPerUnit=10};
        var future=new FactoryCenterCostRate{WorkCenterId=op.WorkCenterId,
            EffectiveFrom=order.PlannedStart.AddDays(1),ConversionRialsPerMinute=100};
        var started=new DateTime(2026,10,26,4,30,0,DateTimeKind.Utc);
        var running=new FactoryClockSession(order.Id,op.Id,op.WorkCenterId,started,null);
        var row=FactoryProductionIntelligence.OperationCost(order,op,future,[running]);
        Assert.False(row.HasApplicableRate);
        Assert.True(row.HasOpenClock);
        Assert.Null(row.ActualMinutes);
        Assert.Null(row.ActualConversionRials);
        var totals=FactoryProductionIntelligence.TotalCosts([row]);
        Assert.Equal(1,totals.CountMissingRates);
        Assert.Equal(1,totals.CountMissingActualTime);
        Assert.Null(totals.ConversionVarianceRials);
    }

    [Fact]
    public void Clock_derived_oee_uses_real_minutes_and_missing_values_remain_unknown()
    {
        var day=new DateOnly(2026,10,26);
        var center=Guid.NewGuid();
        var started=new DateTime(2026,10,26,4,30,0,DateTimeKind.Utc);
        var session=new FactoryClockSession(Guid.NewGuid(),Guid.NewGuid(),center,
            started,started.AddMinutes(60));
        var complete=FactoryProductionIntelligence.EvaluateClockOee(
            center,day,120,[session],45,8,1,1);
        Assert.Equal(60m,complete.ActualMinutes);
        Assert.Equal(50m,complete.AvailabilityPercent);
        Assert.Equal(75m,complete.PerformancePercent);
        Assert.Equal(80m,complete.QualityPercent);
        Assert.Equal(30m,complete.OeePercent);
        Assert.Null(FactoryProductionIntelligence.EvaluateClockOee(
            center,day,null,[session],45,8,1,1).OeePercent);
        Assert.Null(FactoryProductionIntelligence.EvaluateClockOee(
            center,day,30,[session],45,8,1,1).OeePercent);
    }

    [Fact]
    public void Open_clock_or_empty_output_does_not_claim_measured_oee()
    {
        var center=Guid.NewGuid();
        var start=new DateTime(2026,10,26,4,30,0,DateTimeKind.Utc);
        var clock=new FactoryClockSession(Guid.NewGuid(),Guid.NewGuid(),center,start,null);
        var result=FactoryProductionIntelligence.EvaluateClockOee(
            center,new DateOnly(2026,10,26),120,[clock],60,10,0,0);
        Assert.True(result.HasOpenClock);
        Assert.Null(result.OeePercent);
        var noOutput=FactoryProductionIntelligence.EvaluateClockOee(
            center,new DateOnly(2026,10,26),120,
            [clock with{StoppedUtc=start.AddMinutes(60)}],0,0,0,0);
        Assert.Null(noOutput.OeePercent);
    }

    [Fact]
    public void Cross_midnight_clock_is_clipped_to_requested_tehran_day()
    {
        var center=Guid.NewGuid();
        // 2026-10-26 23:30 Tehran = 20:00 UTC. Stops next day at 00:45 Tehran.
        var start=new DateTime(2026,10,26,20,0,0,DateTimeKind.Utc);
        var clock=new FactoryClockSession(Guid.NewGuid(),Guid.NewGuid(),center,start,
            start.AddMinutes(75));
        var day1=FactoryProductionIntelligence.EvaluateClockOee(
            center,new DateOnly(2026,10,26),480,[clock],20,1,0,0);
        var day2=FactoryProductionIntelligence.EvaluateClockOee(
            center,new DateOnly(2026,10,27),480,[clock],20,1,0,0);
        Assert.Equal(30m,day1.ActualMinutes);
        Assert.Equal(45m,day2.ActualMinutes);
    }
}
