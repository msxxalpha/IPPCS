using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Frozen local production conversion assumptions at release, not financial
// accounting, ERP integration, real payroll, purchase or warehouse valuation.
public record FactoryCostRateSnapshot(Guid OperationId,Guid WorkCenterId,
    decimal? ConversionRialsPerMinute,decimal? ScrapRialsPerUnit,DateOnly? EffectiveFrom);
public record FactoryCostRateEvidence(Guid OrderId,int FrozenOperations,int MissingRates,
    bool IsHistoricalSnapshot,IReadOnlyList<FactoryCostRateSnapshot> Rates,string Disclaimer);

public static class FactoryCostRateHistory
{
    public const string Action="ConversionRateFrozen";
    public static string Serialize(FactoryCostRateSnapshot snapshot)=>
        JsonSerializer.Serialize(new{operationId=snapshot.OperationId,
            workCenterId=snapshot.WorkCenterId,
            conversionRialsPerMinute=snapshot.ConversionRialsPerMinute,
            scrapRialsPerUnit=snapshot.ScrapRialsPerUnit,
            effectiveFrom=snapshot.EffectiveFrom});
    public static FactoryCostRateSnapshot Parse(string? json)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(json))throw new JsonException();
            using var parsed=JsonDocument.Parse(json);
            var j=parsed.RootElement;
            var op=j.GetProperty("operationId").GetGuid();
            var center=j.GetProperty("workCenterId").GetGuid();
            var rate=j.GetProperty("conversionRialsPerMinute").ValueKind==JsonValueKind.Null?
                (decimal?)null:j.GetProperty("conversionRialsPerMinute").GetDecimal();
            var scrap=j.GetProperty("scrapRialsPerUnit").ValueKind==JsonValueKind.Null?
                (decimal?)null:j.GetProperty("scrapRialsPerUnit").GetDecimal();
            var from=j.GetProperty("effectiveFrom").ValueKind==JsonValueKind.Null?
                (DateOnly?)null:DateOnly.Parse(j.GetProperty("effectiveFrom").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);
            if(op==Guid.Empty||center==Guid.Empty||
                (rate is null)!=(scrap is null)||
                rate<0||scrap<0||(rate.HasValue&&from is null))
                throw new InvalidOperationException("سند نرخ تبدیل منجمد معتبر نیست.");
            return new(op,center,rate,scrap,from);
        }
        catch(Exception ex)when(ex is JsonException or KeyNotFoundException or
            FormatException or InvalidOperationException)
        {throw new InvalidOperationException("تاریخچه نرخ تبدیل منجمد خراب یا ناقص است.",ex);}
    }
    public static IReadOnlyList<FactoryCostRateSnapshot> Read(
        IReadOnlyList<ManufacturingOrderOperation> operations,
        IReadOnlyList<ManufacturingOrderAudit> history)
    {
        var entries=history.Where(x=>x.Action==Action).ToArray();
        if(entries.Length==0)return [];
        var accepted=entries.Select(x=>Parse(x.Details)).ToArray();
        if(accepted.Length!=operations.Count||
            accepted.GroupBy(x=>x.OperationId).Any(x=>x.Count()!=1)||
            accepted.Any(x=>!operations.Any(op=>
                op.Id==x.OperationId&&op.WorkCenterId==x.WorkCenterId)))
            throw new InvalidOperationException("مجموعه نرخ‌های منجمد با عملیات دستور ساخت تطابق ندارد.");
        return accepted;
    }
    public static IReadOnlyList<FactoryCostRateSnapshot> Snapshots(
        IReadOnlyList<ManufacturingOrderOperation> operations,
        IReadOnlyDictionary<Guid,FactoryCenterCostRate> rates,
        DateOnly plannedStart)
    {
        return operations.Select(op=>{
            rates.TryGetValue(op.WorkCenterId,out var r);
            var applied=r is not null&&r.EffectiveFrom<=plannedStart?r:null;
            return new FactoryCostRateSnapshot(op.Id,op.WorkCenterId,
                applied?.ConversionRialsPerMinute,applied?.ScrapRialsPerUnit,
                applied?.EffectiveFrom);
        }).ToArray();
    }
    internal static async Task LockRateSchedule(ProductionDb db,CancellationToken ct)
    {
        const string key="IPPCS:FactoryConversionRates";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @rc int; EXEC @rc=sys.sp_getapplock
               @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',
               @LockTimeout=10000; IF @rc<0 THROW 51074,'Conversion rate lock unavailable',1;",ct);
    }

    public static IEndpointRouteBuilder MapCostRateHistory(this IEndpointRouteBuilder endpoints)
    {
        var api=endpoints.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-cost/orders/{orderId:guid}/rate-snapshot",
            async(Guid orderId,ProductionDb db,CancellationToken ct)=>{
                var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                    .SingleOrDefaultAsync(x=>x.Id==orderId,ct);
                if(order is null)return Results.NotFound();
                var audits=await db.ManufacturingOrderAudits.AsNoTracking()
                    .Where(x=>x.OrderId==orderId&&x.Action==Action).ToListAsync(ct);
                IReadOnlyList<FactoryCostRateSnapshot> rows;
                try{rows=Read(order.Operations,audits);}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
                return Results.Ok(new FactoryCostRateEvidence(orderId,rows.Count,
                    rows.Count(x=>x.ConversionRialsPerMinute is null),audits.Count>0,
                    rows,rows.Count>0?
                    "نرخ‌های داخلی هزینه تبدیل در لحظه آزادسازی دستور منجمد شده‌اند؛ تغییر بعدی نرخ استاندارد به‌صورت گذشته‌نگر این دستور را تغییر نمی‌دهد.":
                    "این دستور دارای سند نرخ منجمد نیست؛ نرخ جاری نباید به عنوان نرخ تاریخی قطعی معرفی شود."));
            });
        return endpoints;
    }
}
