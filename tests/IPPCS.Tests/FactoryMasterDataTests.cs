using Xunit;

public sealed class FactoryMasterDataTests
{
    [Theory]
    [InlineData("DeviationCategory","0",true)]
    [InlineData("DeviationCategory","6",true)]
    [InlineData("DeviationCategory","7",false)]
    [InlineData("DeviationCategory","04",false)]
    [InlineData("ActionOwner","SHIFT_LEAD",true)]
    [InlineData("ActionOwner","A-1",true)]
    [InlineData("ActionOwner","A",false)]
    [InlineData("ActionOwner","کد-۱",false)]
    [InlineData("Unknown","OTHER",false)]
    public void Only_supported_stable_codes_are_admitted(string kind,string code,bool expected)
    {
        Assert.Equal(expected,FactoryMasterData.ValidCode(kind,code));
    }

    [Fact]
    public void Factory_master_options_have_bounded_labels_sort_and_language_safe_code_normalization()
    {
        Assert.Equal("42",FactoryMasterData.NormalizeCode("۴۲"));
        Assert.Equal("QC_TEAM",FactoryMasterData.NormalizeCode(" qc_team "));
        Assert.True(FactoryMasterData.ValidLabel("سرپرست شیفت"));
        Assert.False(FactoryMasterData.ValidLabel(" "));
        Assert.False(FactoryMasterData.ValidLabel(new string('x',101)));
        Assert.True(FactoryMasterData.ValidSort(10000));
        Assert.False(FactoryMasterData.ValidSort(-1));
        Assert.False(FactoryMasterData.ValidSort(10001));
    }
}
