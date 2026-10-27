using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Factory-only serial evidence. Not a printed physical label, sales invoice,
// shipment registration, warehouse serial ledger or customer warranty record.
public record FactorySerialInput(Guid ConfirmationId,string SerialNumber);
public record FactorySerialEvidence(string SerialNumber,Guid OrderId,Guid ConfirmationId,
    string LotCode,string OrderNumber,Guid ItemId,string RecordedBy,DateTime RecordedUtc,
    Guid AuditId);
public record FactorySerialLedger(Guid OrderId,int Assigned,int FinishedLotCount,
    IReadOnlyList<FactorySerialEvidence> Serials,string Disclaimer);
public record FactorySerialTrace(FactorySerialEvidence Serial,FactoryTraceResult Ancestry,
    string Disclaimer);

public static class FactorySerialGenealogy
{
    public const string AssignmentAction="FactorySerialAssigned";
    private const int MaxJournal=20000;
    private static readonly Regex NumberPattern=new(
        "^[A-Z0-9][A-Z0-9._-]{2,63}$",RegexOptions.Compiled|RegexOptions.CultureInvariant);

    public static string Normalize(string? input)=>PersianPlanningDates.NormalizeDigits(
        input?.Trim()??"").ToUpperInvariant();
    public static bool Valid(string? serial)=>NumberPattern.IsMatch(Normalize(serial));
    public static string Encode(string serial,Guid lot,string actor)=>
        JsonSerializer.Serialize(new{serial,confirmationId=lot,recordedBy=actor});
    public static (string Serial,Guid ConfirmationId,string Actor) Decode(string? details)
    {
        if(string.IsNullOrWhiteSpace(details))
            throw new InvalidOperationException("ثبت شماره سریال فاقد سند معتبر است.");
        try
        {
            using var doc=JsonDocument.Parse(details);
            var root=doc.RootElement;
            var serial=root.GetProperty("serial").GetString()??"";
            var lot=root.GetProperty("confirmationId").GetGuid();
            var actor=root.GetProperty("recordedBy").GetString()??"";
            if(!Valid(serial)||serial!=Normalize(serial)||lot==Guid.Empty||
                string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("سند سریال نامعتبر است.");
            return (serial,lot,actor);
        }
        catch(Exception ex)when(ex is JsonException or KeyNotFoundException or
            FormatException or InvalidOperationException)
        {throw new InvalidOperationException("سند ردیابی سریال ناقص یا مخدوش است.",ex);}
    }
    // Fails closed on duplicate numbers, wrong order, missing batch or an
    // over-issued finished lot. The complete journal is checked before writes.
    public static IReadOnlyList<FactorySerialEvidence> BuildLedger(
        IReadOnlyList<ManufacturingOrderAudit> audits,
        IReadOnlyList<ManufacturingConfirmation> lots,
        IReadOnlyDictionary<Guid,ManufacturingOrder> orders)
    {
        var byId=lots.ToDictionary(x=>x.Id);
        var serials=new HashSet<string>(StringComparer.Ordinal);
        var issued=new Dictionary<Guid,int>();
        var result=new List<FactorySerialEvidence>();
        foreach(var entry in audits.OrderBy(x=>x.AtUtc).ThenBy(x=>x.Id))
        {
            if(entry.Action!=AssignmentAction)continue;
            var data=Decode(entry.Details);
            if(!serials.Add(data.Serial))
                throw new InvalidOperationException("شماره سریال تکراری در دفتر تولید شناسایی شد.");
            if(!byId.TryGetValue(data.ConfirmationId,out var lot)||
                !orders.TryGetValue(entry.OrderId,out var order))
                throw new InvalidOperationException("سریال به دستور یا بچ ناموجود منتسب شده است.");
            if(order.Kind!=ManufacturingOrderKind.FinalAssembly||
                order.Operations.OrderBy(x=>x.Sequence).LastOrDefault()?.Id!=lot.OperationId||
                order.Operations.All(x=>x.Id!=lot.OperationId))
                throw new InvalidOperationException("سریال باید به بچ آخرین عملیات مونتاژ نهایی تعلق داشته باشد.");
            if(lot.GoodQuantity<0||decimal.Truncate(lot.GoodQuantity)!=lot.GoodQuantity)
                throw new InvalidOperationException("مقدار خروجی سالم بچ برای تخصیص سریال عدد صحیح مثبت نیست.");
            var count=issued.GetValueOrDefault(lot.Id)+1;
            if(count>lot.GoodQuantity)
                throw new InvalidOperationException("سریال‌های بچ از خروجی سالم آن بیشتر شده‌اند.");
            issued[lot.Id]=count;
            result.Add(new(data.Serial,order.Id,lot.Id,lot.LotCode??"",
                order.Number,order.ItemId,data.Actor,entry.AtUtc,entry.Id));
        }
        return result;
    }

    internal static async Task LockGlobal(ProductionDb db,CancellationToken ct)
    {
        const string key="IPPCS:FactorySerials:All";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @rc int;EXEC @rc=sys.sp_getapplock @Resource={key},
               @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
               IF @rc<0 THROW 51071, 'Serial register lock unavailable', 1;",ct);
    }
    internal static async Task<(IReadOnlyList<FactorySerialEvidence> Rows,string? Error)>
        LoadLedger(ProductionDb db,CancellationToken ct)
    {
        var journal=await db.ManufacturingOrderAudits.AsNoTracking()
            .Where(x=>x.Action==AssignmentAction).OrderBy(x=>x.AtUtc).Take(MaxJournal+1)
            .ToListAsync(ct);
        if(journal.Count>MaxJournal)
            return ([], "دفتر ثبت سریال از سقف جستجوی آزمایشی گذشته است؛ گزارش ناقص ارائه نمی‌شود.");
        var orderIds=journal.Select(x=>x.OrderId).Distinct().ToArray();
        var orders=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
            .Where(x=>orderIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var operationIds=orders.Values.SelectMany(x=>x.Operations).Select(x=>x.Id).ToArray();
        var lots=await db.ManufacturingConfirmations.AsNoTracking()
            .Where(x=>operationIds.Contains(x.OperationId)).ToListAsync(ct);
        // A read-only trace is still a quality claim. Re-validate that each
        // recorded serial belongs to a final lot with one intact EOL approval,
        // rather than silently trusting historical serial journal existence.
        var certificates=await db.ManufacturingOrderAudits.AsNoTracking()
            .Where(x=>orderIds.Contains(x.OrderId)&&
                x.Action==FactoryEndOfLineAcceptance.ApprovedAction).ToListAsync(ct);
        foreach(var id in orderIds)
        {
            if(!orders.TryGetValue(id,out var order)||
                order.Kind!=ManufacturingOrderKind.FinalAssembly||
                order.Status!=ManufacturingOrderStatus.Completed)
                return ([],"دفتر سریال به دستور مونتاژ نهایی تکمیل‌نشده یا ناموجود ارجاع دارد.");
            var last=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
            var certificatesForOrder=certificates.Where(x=>x.OrderId==id).ToArray();
            if(last is null||certificatesForOrder.Length!=1||
                !FactoryEndOfLineAcceptance.CertificateMatches(
                    certificatesForOrder[0].Details,
                    lots.Where(x=>x.OperationId==last.Id).ToArray()))
                return ([],"سند کنترل نهایی یکی از دستورهای سریال‌دار معتبر نیست؛ ردیابی قابل تأیید نیست.");
        }
        try{return (BuildLedger(journal,lots,orders),null);}
        catch(InvalidOperationException ex){return ([],ex.Message);}
    }

    public static IEndpointRouteBuilder MapSerialGenealogy(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/serials",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                if(!await db.ManufacturingOrders.AsNoTracking().AnyAsync(x=>x.Id==id,ct))
                    return Results.NotFound();
                var (all,error)=await LoadLedger(db,ct);
                if(error is not null)return Results.Conflict(new{message=error});
                var rows=all.Where(x=>x.OrderId==id).ToArray();
                return Results.Ok(new FactorySerialLedger(id,rows.Length,
                    rows.Select(x=>x.ConfirmationId).Distinct().Count(),rows,
                    "تخصیص شناسه سریال پس از گواهی کیفیت داخلی فقط سند ساخت IPPCS است؛ برچسب فیزیکی یا تحویل مشتری را تأیید نمی‌کند."));
            });
        api.MapPost("/manufacturing-orders/{id:guid}/serials",
            async(Guid id,FactorySerialInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                var serial=Normalize(input.SerialNumber);
                if(!Valid(serial)||input.ConfirmationId==Guid.Empty)
                    return Results.BadRequest(new{message="شماره سریال ۳ تا ۶۴ کاراکتر لاتین/عدد/خط تیره/نقطه و بچ معتبر لازم است."});
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{
                    await LockGlobal(db,ct);
                    await FactoryCapacityReservations.LockOrder(db,id,ct);
                }catch(SqlException){
                    return Results.Conflict(new{message="قفل ثبت سریال در دسترس نیست."});}
                var order=await db.ManufacturingOrders.AsNoTracking()
                    .Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(order is null)return Results.NotFound();
                if(order.Kind!=ManufacturingOrderKind.FinalAssembly||
                    order.Status!=ManufacturingOrderStatus.Completed)
                    return Results.Conflict(new{message="سریال فقط پس از اتمام مونتاژ نهایی قابل ثبت است."});
                var certificates=await db.ManufacturingOrderAudits.AsNoTracking()
                    .Where(x=>x.OrderId==id&&
                        x.Action==FactoryEndOfLineAcceptance.ApprovedAction).ToListAsync(ct);
                if(certificates.Count!=1)
                    return Results.Conflict(new{message="یک گواهی کیفیت داخلی معتبر پایان خط برای تخصیص سریال لازم است."});
                var last=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
                if(last is null)return Results.Conflict(new{message="آخرین عملیات مونتاژ تعریف نشده است."});
                var lot=await db.ManufacturingConfirmations.AsNoTracking()
                    .SingleOrDefaultAsync(x=>x.Id==input.ConfirmationId,ct);
                if(lot is null||lot.OperationId!=last.Id||lot.GoodQuantity<=0||
                    decimal.Truncate(lot.GoodQuantity)!=lot.GoodQuantity)
                    return Results.Conflict(new{message="بچ آخرین عملیات و مقدار خروجی سالم عدد صحیح معتبر لازم است."});
                var finalLots=await db.ManufacturingConfirmations.AsNoTracking()
                    .Where(x=>x.OperationId==last.Id).ToListAsync(ct);
                if(!FactoryEndOfLineAcceptance.CertificateMatches(
                    certificates[0].Details,finalLots))
                    return Results.Conflict(new{
                        message="مقدار/تعداد بچ آخرین عملیات با سند کنترل نهایی تطبیق ندارد؛ تخصیص سریال مسدود است."});
                var (ledger,error)=await LoadLedger(db,ct);
                if(error is not null)return Results.Conflict(new{message=error});
                if(ledger.Any(x=>x.SerialNumber==serial))
                    return Results.Conflict(new{message="این شماره سریال قبلاً در کارخانه ثبت شده است."});
                if(ledger.Count(x=>x.ConfirmationId==lot.Id)>=lot.GoodQuantity)
                    return Results.Conflict(new{message="تعداد سریال بچ به خروجی سالم ثبت‌شده رسیده است."});
                var actor=ctx.User.Identity?.Name??"unknown";
                if(actor.Length>120)actor=actor[..120];
                var record=new ManufacturingOrderAudit{OrderId=id,Action=AssignmentAction,
                    Details=Encode(serial,lot.Id,actor)};
                if(record.Details.Length>500)
                    return Results.BadRequest(new{message="طول سند شماره سریال از حد مجاز فراتر است."});
                db.ManufacturingOrderAudits.Add(record);
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Created($"/api/v1/factory-serials/{serial}/trace",
                    new{record.Id,serialNumber=serial,confirmationId=lot.Id,orderId=id,
                        message="سریال فقط در تاریخچه داخلی تولید ثبت شد؛ برچسب/ارسال محصول ثبت نشده است."});
            }).RequireAuthorization("IpQualityApprove");
        api.MapGet("/factory-serials/{serial}/trace",
            async(string serial,int? depth,ProductionDb db,CancellationToken ct)=>{
                var value=Normalize(serial);var maxDepth=depth??8;
                if(!Valid(value)||maxDepth is <1 or >16)
                    return Results.BadRequest(new{message="سریال یا عمق ردیابی نامعتبر است."});
                var (ledger,error)=await LoadLedger(db,ct);
                if(error is not null)return Results.Conflict(new{message=error});
                var found=ledger.SingleOrDefault(x=>x.SerialNumber==value);
                if(found is null)return Results.NotFound();
                var total=await db.ManufacturingLotGenealogies.AsNoTracking().CountAsync(ct);
                if(total>10000)return Results.Conflict(new{
                    message="اندازه شجره از سقف بررسی دقیق فراتر است؛ گزارش ناقص ارائه نمی‌شود."});
                var source=await db.ManufacturingLotGenealogies.AsNoTracking().ToListAsync(ct);
                var ids=source.SelectMany(x=>new[]{x.SourceConfirmationId,x.DestinationConfirmationId})
                    .Append(found.ConfirmationId).Distinct().ToArray();
                var confirmations=await db.ManufacturingConfirmations.AsNoTracking()
                    .Include(x=>x.Operation).ThenInclude(x=>x.Order)
                    .Where(x=>ids.Contains(x.Id)).ToListAsync(ct);
                var nodes=confirmations.Select(x=>new FactoryTraceNode(
                    x.Id,x.Operation.OrderId,x.Operation.Order.Number,x.Operation.Order.ItemId,
                    x.OperationId,x.Operation.Sequence,x.LotCode??"",x.GoodQuantity,x.OccurredUtc)).ToArray();
                var edges=source.Select(x=>new FactoryTraceEdge(x.Id,
                    x.SourceConfirmationId,x.DestinationConfirmationId,
                    x.Quantity,x.OrderId,x.LinkedUtc)).ToArray();
                try{
                    var trace=FactoryGenealogyExplorer.Explore(found.ConfirmationId,"ancestors",
                        maxDepth,nodes,edges);
                    return Results.Ok(new FactorySerialTrace(found,trace,
                        "شجره بر پیوندهای واقعی ثبت‌شده IPPCS متکی است؛ نبود سند منشأ به معنای نبود قطعه نیست. این ردیابی، سوابق مشتری، ارسال یا انبار را شامل نمی‌شود."));
                }catch(InvalidOperationException ex){
                    return Results.Conflict(new{message=ex.Message});
                }
            });
        return routes;
    }
}
