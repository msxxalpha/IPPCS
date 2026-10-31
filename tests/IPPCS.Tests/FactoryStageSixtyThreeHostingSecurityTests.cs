using Microsoft.AspNetCore.Http;
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
    [Theory]
    [InlineData(true,CookieSecurePolicy.SameAsRequest)]
    [InlineData(false,CookieSecurePolicy.Always)]
    public void Authentication_cookie_is_secure_only_outside_development(
        bool isDevelopment,CookieSecurePolicy expected)
    {
        Assert.Equal(expected,IpSecuritySetup.AuthenticationCookieSecurePolicy(isDevelopment));
    }
}
