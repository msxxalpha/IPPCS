using Xunit;

public sealed class FactoryStageFortyThreeFirstRunTests
{
    [Theory]
    [InlineData(false,12,"database-unavailable")]
    [InlineData(true,12,"migrations-pending")]
    [InlineData(true,0,"ready")]
    public void Health_does_not_call_an_empty_or_unmigrated_database_ready(
        bool canConnect,int pending,string expected)
    {
        Assert.Equal(expected,FactoryFirstRunHealth.Status(canConnect,pending));
    }
}
