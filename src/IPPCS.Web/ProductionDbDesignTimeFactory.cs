using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Design-time only. EF tooling scaffolds migrations without running the unsecured prototype web host.
/// No connection is opened by this factory; use a real dev database for dotnet ef database update.
/// </summary>
public sealed class ProductionDbDesignTimeFactory : IDesignTimeDbContextFactory<ProductionDb>
{
    public ProductionDb CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Production");
        if (string.IsNullOrWhiteSpace(connection))
            connection = "Server=localhost;Database=IPPCS_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<ProductionDb>().UseSqlServer(connection).Options;
        return new ProductionDb(options);
    }
}
