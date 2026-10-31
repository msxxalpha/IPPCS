using Xunit;

public sealed class FactoryStageThirtyFiveTests
{
    [Fact]
    public void All_eighteen_modules_have_distinct_nonempty_UI_routes_and_unresolved_factory_gates()
    {
        var modules=FactoryReleaseReadiness.Modules;
        Assert.Equal(18,modules.Count);
        Assert.Equal(Enumerable.Range(1,18),modules.Select(x=>x.Number));
        Assert.Equal(18,modules.Select(x=>x.Number).Distinct().Count());
        Assert.All(modules,module=>{
            Assert.StartsWith("/",module.Page);
            Assert.EndsWith(".html",module.Page);
            Assert.False(string.IsNullOrWhiteSpace(module.ImplementedScope));
            Assert.False(string.IsNullOrWhiteSpace(module.OutstandingFactoryAcceptance));
            Assert.False(string.IsNullOrWhiteSpace(module.Title));
        });
    }

    [Fact]
    public void First_run_empty_configuration_is_not_reported_ready_for_the_factory()
    {
        var checks=FactoryReleaseReadiness.Setup(0,0,0,0,0,0,0,0,0);
        Assert.Equal(9,checks.Count);
        Assert.All(checks,item=>{
            Assert.False(item.Configured);
            Assert.Equal(0,item.Records);
            Assert.NotEmpty(item.Advice);
            Assert.StartsWith("/",item.Page);
        });
    }

    [Fact]
    public void Setup_check_reflects_presence_not_certification_and_reports_partial_configuration()
    {
        var checks=FactoryReleaseReadiness.Setup(1,2,0,0,3,0,0,6,0);
        Assert.Equal(4,checks.Count(x=>x.Configured));
        Assert.All(checks,x=>Assert.Equal(x.Records>0,x.Configured));
        Assert.Contains(checks,x=>x.Code=="ControlPlan"&&!x.Configured);
        Assert.Contains(checks,x=>x.Code=="DowntimeReasons"&&x.Configured);
    }

    [Theory]
    [InlineData("N",true)]
    [InlineData("G",true)]
    [InlineData("L",true)]
    [InlineData("PCS",true)]
    [InlineData("KG",true)]
    [InlineData("MM",true)]
    [InlineData("N/MM",false)]
    [InlineData(" ",false)]
    [InlineData("A|B",false)]
    public void SI_and_factory_measurement_unit_keys_are_validated_independently_of_labels(string code,bool valid)
    {
        Assert.Equal(valid,FactoryMasterData.ValidCode(
            FactoryMasterData.UnitOfMeasure,code));
    }
}
