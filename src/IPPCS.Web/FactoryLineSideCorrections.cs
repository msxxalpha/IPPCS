using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Factory-only evidence journal. No stock transfer, goods receipt, warehouse
// balance, accounting valuation or purchasing action is created.
public record FactoryLotMaterialInput(Guid ConfirmationId,Guid ComponentItemId,
    decimal Quantity,string Reference);
public record FactoryUsageReversalInput(string Reason);
public record FactoryConsumptionLedgerRow(Guid EntryId,Guid ComponentItemId,
    decimal RecordedQuantity,decimal NetQuantity,Guid? ConfirmationId,
    string Reference,string RecordedBy,DateTime RecordedUtc,Guid? ReversedById,
    bool IsLegacyUnattributed);
public record FactoryLotMaterialBalance(Guid ComponentItemId,decimal StandardForReportedLot,
    decimal RecordedNetQuantity,decimal Variance,int ActiveEntries,
    string Disclaimer);
public static class FactoryLineSideCorrections
{
    public const string LotPrefix="IPLOT:";
    public const string ReversalPrefix="IPREV:";
    public static bool IsReservedReference(string? value)=>
        value?.StartsWith(LotPrefix,StringComparison.OrdinalIgnoreCase)==true||
        value?.StartsWith(ReversalPrefix,StringComparison.OrdinalIgnoreCase)==true;


    public static string LotReference(Guid confirmationId,string note)
        =>LotPrefix+confirmationId.ToString("N")+"|"+note.Trim();
    public static string ReverseReference(Guid originalId,string reason)
        =>ReversalPrefix+originalId.ToString("N")+"|"+reason.Trim();

    public static Guid? ParseId(string? reference,string prefix)
    {
        if(reference is null||!reference.StartsWith(prefix,StringComparison.Ordinal))
            return null;
        var tail=reference.AsSpan(prefix.Length);
        return tail.Length>=33&&tail[32]=='|'&&Guid.TryParseExact(tail[..32],"N",out var id)
            ?id:null;
    }

    public static IReadOnlyList<FactoryConsumptionLedgerRow> Ledger(
        IReadOnlyList<FactoryMaterialUsage> entries)
    {
        var positives=entries.Where(x=>x.QuantityConsumed>0).ToArray();
        var lookup=positives.ToDictionary(x=>x.Id);
        var reversals=new Dictionary<Guid,FactoryMaterialUsage>();
        foreach(var entry in entries.Where(x=>x.QuantityConsumed<=0))
        {
            var referenced=ParseId(entry.Reference,ReversalPrefix);
            if(referenced is null||!lookup.TryGetValue(referenced.Value,out var original)||
                entry.QuantityConsumed!=-original.QuantityConsumed ||
                entry.OrderId!=original.OrderId ||
                entry.ComponentItemId!=original.ComponentItemId ||
                !reversals.TryAdd(referenced.Value,entry))
                throw new InvalidOperationException("تاریخچه اصلاح مصرف ناقص، تکراری یا دارای مقدار نامعتبر است.");
        }
        return positives.OrderBy(x=>x.RecordedUtc).ThenBy(x=>x.Id).Select(x=>{
            reversals.TryGetValue(x.Id,out var reverse);
            var lot=ParseId(x.Reference,LotPrefix);
            return new FactoryConsumptionLedgerRow(x.Id,x.ComponentItemId,
                x.QuantityConsumed,reverse is null?x.QuantityConsumed:0m,lot,
                x.Reference,x.RecordedBy,x.RecordedUtc,reverse?.Id,lot is null);
        }).ToArray();
    }

    public static IReadOnlyList<FactoryLotMaterialBalance> LotBalances(
        ManufacturingConfirmation confirmation,IReadOnlyList<ManufacturingBomLine> bom,
        IReadOnlyList<FactoryConsumptionLedgerRow> entries)
    {
        if(bom.Any(x=>x.QuantityPer<=0||x.ScrapPercent is <0 or >=100))
            throw new InvalidOperationException("ضرایب BOM دستور نامعتبر است.");
        var reported=confirmation.GoodQuantity+confirmation.ScrapQuantity+
            confirmation.ReworkQuantity;
        return bom.GroupBy(x=>x.ComponentItemId).Select(group=>{
            var standard=group.Sum(x=>reported*x.QuantityPer*100m/(100m-x.ScrapPercent));
            var matching=entries.Where(x=>x.ConfirmationId==confirmation.Id&&
                x.ComponentItemId==group.Key).ToArray();
            var net=matching.Sum(x=>x.NetQuantity);
            return new FactoryLotMaterialBalance(group.Key,standard,net,net-standard,
                matching.Count(x=>x.NetQuantity>0),
                "این مقادیر مصرف پای خطِ ثبت‌شده در IPPCS هستند، نه برداشت یا مانده انبار.");
        }).OrderBy(x=>x.ComponentItemId).ToArray();
    }

    public static IEndpointRouteBuilder MapLineSideCorrections(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-material/{orderId:guid}/evidence-ledger",
            async(Guid orderId,ProductionDb db,CancellationToken ct)=>{
                if(!await db.ManufacturingOrders.AsNoTracking().AnyAsync(x=>x.Id==orderId,ct))
                    return Results.NotFound();
                var entries=await db.FactoryMaterialUsages.AsNoTracking()
                    .Where(x=>x.OrderId==orderId).ToListAsync(ct);
                try{return Results.Ok(new{
                    orderId,rows=Ledger(entries),
                    warning="اصلاح مصرف، رویداد جدید با مقدار منفی است؛ ثبت اولیه پاک نمی‌شود. ردیف‌های قدیمی بدون شناسه بچ، قابل انتساب قطعی به بچ نیستند."
                });}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            });
        api.MapGet("/factory-material/{orderId:guid}/lot/{confirmationId:guid}",
            async(Guid orderId,Guid confirmationId,ProductionDb db,CancellationToken ct)=>{
                var order=await db.ManufacturingOrders.AsNoTracking()
                    .SingleOrDefaultAsync(x=>x.Id==orderId,ct);
                if(order is null)return Results.NotFound();
                var lot=await db.ManufacturingConfirmations.AsNoTracking()
                    .Include(x=>x.Operation).SingleOrDefaultAsync(x=>x.Id==confirmationId,ct);
                if(lot is null||lot.Operation.OrderId!=orderId)return Results.NotFound();
                var revision=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
                if(revision is null)return Results.Conflict(new{message="BOM دستور موجود نیست."});
                var history=await db.FactoryMaterialUsages.AsNoTracking()
                    .Where(x=>x.OrderId==orderId).ToListAsync(ct);
                try{
                    var entries=Ledger(history);
                    return Results.Ok(new{orderId,confirmationId,lot.LotCode,
                        rows=LotBalances(lot,revision.Lines,entries),
                        history=entries.Where(x=>x.ConfirmationId==confirmationId).ToArray()});
                }catch(InvalidOperationException ex){
                    return Results.Conflict(new{message=ex.Message});
                }
            });
        api.MapPost("/factory-material/{orderId:guid}/lot-consumption",
            async(Guid orderId,FactoryLotMaterialInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                if(input.ConfirmationId==Guid.Empty||input.ComponentItemId==Guid.Empty||
                    input.Quantity<=0||input.Quantity>1000000000m||
                    string.IsNullOrWhiteSpace(input.Reference)||
                    input.Reference.Trim().Length>140)
                    return Results.BadRequest(new{message="بچ، قطعه، مقدار و مرجع کوتاه معتبر لازم است."});
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await FactoryCapacityReservations.LockOrder(db,orderId,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل مصرف دستور در دسترس نیست."});}
                var order=await db.ManufacturingOrders.SingleOrDefaultAsync(x=>x.Id==orderId,ct);
                if(order is null)return Results.NotFound();
                if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                    return Results.Conflict(new{message="ثبت مصرف جدید بچ فقط در دستور در حال اجرا مجاز است."});
                var lot=await db.ManufacturingConfirmations.AsNoTracking()
                    .Include(x=>x.Operation).SingleOrDefaultAsync(x=>x.Id==input.ConfirmationId,ct);
                if(lot is null||lot.Operation.OrderId!=orderId||
                    lot.GoodQuantity+lot.ScrapQuantity+lot.ReworkQuantity<=0)
                    return Results.Conflict(new{message="گزارش عملکرد بچ به دستور انتخابی تعلق ندارد."});
                var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
                if(bom is null||!bom.Lines.Any(x=>x.ComponentItemId==input.ComponentItemId))
                    return Results.Conflict(new{message="این قطعه در BOM مصوب منجمد دستور وجود ندارد."});
                var entries=await db.FactoryMaterialUsages.AsNoTracking()
                    .Where(x=>x.OrderId==orderId).ToListAsync(ct);
                IReadOnlyList<FactoryConsumptionLedgerRow> history;
                try{history=Ledger(entries);}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
                var cap=LotBalances(lot,bom.Lines,history)
                    .Single(x=>x.ComponentItemId==input.ComponentItemId);
                if(cap.RecordedNetQuantity+input.Quantity>cap.StandardForReportedLot)
                    return Results.Conflict(new{
                        message="مصرف ثبت‌شده این بچ از نیاز استاندارد BOM برای گزارش عملکرد آن بیشتر می‌شود."});
                var record=new FactoryMaterialUsage{
                    OrderId=orderId,ComponentItemId=input.ComponentItemId,
                    QuantityConsumed=input.Quantity,
                    Reference=LotReference(input.ConfirmationId,input.Reference),
                    RecordedBy=ctx.User.Identity?.Name??"unknown"};
                db.FactoryMaterialUsages.Add(record);
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                    OrderId=orderId,Action="MaterialLotUsage",
                    Details=$"Entry={record.Id:N};Lot={input.ConfirmationId:N};Part={input.ComponentItemId:N};Qty={input.Quantity}"});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Created($"/api/v1/factory-material/{orderId}/lot/{input.ConfirmationId}",
                    new{record.Id,record.QuantityConsumed,record.Reference});
            }).RequireAuthorization("IpShopFloorWrite");
        api.MapPost("/factory-material/{orderId:guid}/consumption/{entryId:guid}/reverse",
            async(Guid orderId,Guid entryId,FactoryUsageReversalInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length>140)
                    return Results.BadRequest(new{message="علت اصلاح مصرف معتبر و اجباری است."});
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await FactoryCapacityReservations.LockOrder(db,orderId,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل اصلاح مصرف در دسترس نیست."});}
                var order=await db.ManufacturingOrders.SingleOrDefaultAsync(x=>x.Id==orderId,ct);
                if(order is null)return Results.NotFound();
                // Corrections after production completion are auditable; no original rows
                // may be mutated or physically deleted, even on a completed order.
                var entries=await db.FactoryMaterialUsages.AsNoTracking()
                    .Where(x=>x.OrderId==orderId).ToListAsync(ct);
                IReadOnlyList<FactoryConsumptionLedgerRow> ledger;
                try{ledger=Ledger(entries);}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
                var original=entries.SingleOrDefault(x=>x.Id==entryId);
                if(original is null)return Results.NotFound();
                if(original.QuantityConsumed<=0||ledger.Single(x=>x.EntryId==entryId).ReversedById is not null)
                    return Results.Conflict(new{message="این ثبت مصرف قبلاً اصلاح شده یا قابل اصلاح نیست."});
                var reversal=new FactoryMaterialUsage{
                    OrderId=orderId,ComponentItemId=original.ComponentItemId,
                    QuantityConsumed=-original.QuantityConsumed,
                    Reference=ReverseReference(original.Id,input.Reason),
                    RecordedBy=ctx.User.Identity?.Name??"unknown"};
                db.FactoryMaterialUsages.Add(reversal);
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                    OrderId=orderId,Action="MaterialReversed",
                    Details=$"Source={original.Id:N};Reversal={reversal.Id:N};Qty={original.QuantityConsumed};Reason={input.Reason.Trim()}"});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Created($"/api/v1/factory-material/{orderId}/evidence-ledger",
                    new{reversal.Id,originalId=original.Id,reversal.QuantityConsumed,
                        warning="مقدار اصلاح در دفتر ثبت تولید منفی است؛ موجودی واقعی انبار تغییر نکرد."});
            }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
