using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class FactoryFirstUatDatabaseConfigTests
{
    private static IConfiguration Config(string? connection=null)
    {
        var values=new Dictionary<string,string?>();
        if(connection!=null) values["ConnectionStrings:Production"]=connection;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Ef_tooling_without_explicit_connection_fails_closed_for_update()
    {
        var error=Assert.Throws<InvalidOperationException>(
            ()=>ProductionDbDesignTimeFactory.ResolveConnection(Config()));
        Assert.Contains("user-secrets",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Server=.;Database=Warehouse;Integrated Security=True;TrustServerCertificate=True;")]
    [InlineData("Server=.;Database=master;Integrated Security=True;TrustServerCertificate=True;")]
    [InlineData("Server=.;Database=IPPCS;Integrated Security=True;TrustServerCertificate=True;")]
    public void Ef_migrations_cannot_accidentally_run_on_unrelated_databases(string connection)
    {
        Assert.Throws<InvalidOperationException>(
            ()=>ProductionDbDesignTimeFactory.ResolveConnection(Config(connection)));
    }

    [Fact]
    public void Sql_development_user_secrets_connection_is_preserved_unchanged()
    {
        const string connection="Server=localhost;Database=IPPCS_Dev;Trusted_Connection=True;TrustServerCertificate=True;";
        Assert.Equal(connection,ProductionDbDesignTimeFactory.ResolveConnection(Config(connection)));
    }

    [Fact]
    public void Source_only_export_may_use_placeholder_but_not_when_real_target_is_invalid()
    {
        Assert.Contains("IPPCS_ModelOnly",ProductionDbDesignTimeFactory.ResolveConnection(Config(),true));
        Assert.Throws<InvalidOperationException>(()=>
            ProductionDbDesignTimeFactory.ResolveConnection(
                Config("Server=.;Database=CompanyERP;Trusted_Connection=True;"),true));
    }
}
