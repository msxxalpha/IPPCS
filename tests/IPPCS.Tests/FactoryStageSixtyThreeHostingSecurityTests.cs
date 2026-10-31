using Xunit;

public sealed class FactoryStageSixtyThreeHostingSecurityTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void Initial_admin_bootstrap_is_opt_in_outside_development_and_never_repromotes(
        bool isDevelopment,bool explicitlyEnabled,bool administratorExists,bool expected)
    {
        Assert.Equal(expected,IpDevelopmentBootstrap.ShouldRunInitialAdminBootstrap(
            isDevelopment,explicitlyEnabled,administratorExists));
    }
}
