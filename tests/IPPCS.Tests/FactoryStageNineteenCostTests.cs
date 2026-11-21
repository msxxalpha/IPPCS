using Xunit;
public sealed class FactoryStageNineteenCostTests
{
    [Fact]
    public void Released_work_order_cost_rates_remain_frozen_after_rate_update()
    {
        var center=Guid.NewGuid();var order=Guid.NewGuid();
        var operation=new ManufacturingOrderOperation{OrderId=order,WorkCenterId=center,Sequence=10};
        var first=new FactoryCenterCostRate{WorkCenterId=center,EffectiveFrom=new DateOnly(2026,10,1),
            ConversionRialsPerMinute=120, ScrapRialsPerUnit=300};
        var original=FactoryCostRateHistory.Snapshots([operation],
            new Dictionary<Guid,FactoryCenterCostRate>{{center,first}},
            new DateOnly(2026,10,27));
        var frozen=Assert.Single(original);
        var audit=new ManufacturingOrderAudit{OrderId=order,Action=FactoryCostRateHistory.Action,
            Details=FactoryCostRateHistory.Serialize(frozen)};
        first.ConversionRialsPerMinute=99999;
        var recorded=Assert.Single(FactoryCostRateHistory.Read([operation],[audit]));
        Assert.Equal(120,recorded.ConversionRialsPerMinute);
        Assert.Equal(300,recorded.ScrapRialsPerUnit);
        Assert.Equal(new DateOnly(2026,10,1),recorded.EffectiveFrom);
    }
    [Fact]
    public void Missing_or_future_rate_is_locked_as_unknown_and_cannot_be_backfilled()
    {
        var center=Guid.NewGuid();var op=new ManufacturingOrderOperation{
            WorkCenterId=center,Sequence=10};
        var future=new FactoryCenterCostRate{WorkCenterId=center,
            EffectiveFrom=new DateOnly(2026,12,1),ConversionRialsPerMinute=200};
        var unknown=Assert.Single(FactoryCostRateHistory.Snapshots([op],
            new Dictionary<Guid,FactoryCenterCostRate>{{center,future}},
            new DateOnly(2026,10,27)));
        Assert.Null(unknown.ConversionRialsPerMinute);
        Assert.Null(unknown.EffectiveFrom);
        var evidence=new ManufacturingOrderAudit{
            Action=FactoryCostRateHistory.Action,Details=FactoryCostRateHistory.Serialize(unknown)};
        var back=Assert.Single(FactoryCostRateHistory.Read([op],[evidence]));
        Assert.Null(back.ConversionRialsPerMinute);
        Assert.Throws<InvalidOperationException>(()=>
            FactoryCostRateHistory.Read([op],[evidence,evidence]));
        Assert.Throws<InvalidOperationException>(()=>
            FactoryCostRateHistory.Read([new ManufacturingOrderOperation{WorkCenterId=center}],
                [evidence]));
    }
}
