using Microsoft.EntityFrameworkCore;

// Health status refers to the actual EF database schema, not merely a running
// ASP.NET host. It never discloses connection strings or schema names.
public static class FactoryFirstRunHealth
{
    public static string Status(bool canConnect,int pendingMigrations)=>
        !canConnect?"database-unavailable":
        pendingMigrations>0?"migrations-pending":"ready";

    public static async Task<IResult> Check(ProductionDb db,CancellationToken ct)
    {
        try
        {
            if(!await db.Database.CanConnectAsync(ct))
                return Unavailable("database-unavailable");
            var pending=(await db.Database.GetPendingMigrationsAsync(ct)).Count();
            if(pending>0)return Unavailable("migrations-pending");
            return Results.Ok(new{service="IPPCS",status="ready",version="0.7.1"});
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return Unavailable("database-unavailable");
        }
    }

    private static IResult Unavailable(string reason)=>
        Results.Json(new{service="IPPCS",status="setup-required",reason},
            statusCode:StatusCodes.Status503ServiceUnavailable);
}
