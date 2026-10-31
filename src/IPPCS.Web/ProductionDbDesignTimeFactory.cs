using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Design-time EF tooling must resolve the SAME development User Secrets as
/// ASP.NET Core. Previous code silently switched to IPPCS_DesignTime, so
/// `dotnet ef database update` could migrate the wrong SQL database.
/// Only explicitly opted-in metadata generation may use a dummy connection;
/// it must never open a SQL connection.
/// </summary>
public sealed class ProductionDbDesignTimeFactory : IDesignTimeDbContextFactory<ProductionDb>
{
    public const string ModelOnlyFlag="IPPCS_EF_MODEL_ONLY";

    public static string ResolveConnection(IConfiguration configuration, bool modelOnly=false)
    {
        var connection=configuration.GetConnectionString("Production");
        if(string.IsNullOrWhiteSpace(connection))
        {
            if(modelOnly)
                return "Server=127.0.0.1;Database=IPPCS_ModelOnly;Trusted_Connection=True;TrustServerCertificate=True;";
            throw new InvalidOperationException(
                "No IPPCS development connection string. Configure 'ConnectionStrings:Production' with " +
                "dotnet user-secrets set before running 'dotnet ef database update'. " +
                "For SQL script/model inspection use IPPCS_EF_MODEL_ONLY=1 (never for database update).");
        }
        var sql=new SqlConnectionStringBuilder(connection);
        if(string.IsNullOrWhiteSpace(sql.DataSource) ||
           !sql.InitialCatalog.StartsWith("IPPCS_",StringComparison.OrdinalIgnoreCase) ||
           sql.InitialCatalog.Length<=6)
            throw new InvalidOperationException(
                "EF tooling is restricted to an isolated database named IPPCS_*. " +
                "Never apply IPPCS migrations to another company database.");
        return connection;
    }

    public ProductionDb CreateDbContext(string[] args)
    {
        // Same UserSecretsId as the web project. Environment overrides secrets,
        // making CI disposable SQL tests deterministic.
        var config=new ConfigurationBuilder()
            .AddUserSecrets<ProductionDbDesignTimeFactory>(optional:true)
            .AddEnvironmentVariables()
            .Build();
        var allowModelOnly=string.Equals(
            Environment.GetEnvironmentVariable(ModelOnlyFlag),"1",StringComparison.Ordinal);
        var connection=ResolveConnection(config,allowModelOnly);
        var options=new DbContextOptionsBuilder<ProductionDb>().UseSqlServer(connection).Options;
        return new ProductionDb(options);
    }
}
