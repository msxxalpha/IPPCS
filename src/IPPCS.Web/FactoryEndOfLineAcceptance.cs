using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Final in-factory quality sign-off, NOT sales shipment, warehouse goods issue
// or automotive customer release. Uses only frozen IPPCS records and audit.
public record FactoryEolIssue(string Code,string Description,Guid? LotId=null,Guid? ComponentItemId=null,
    decimal? MissingQuantity=null);
public record FactoryEolLotCoverage(Guid LotId,string LotCode,Guid ComponentItemId,
    decimal RequiredQuantity,decimal LinkedQuantity,decimal MissingQuantity);
public record FactoryEolApproval(Guid OrderId,string OrderNumber,bool CanApprove,bool AlreadyApproved,
    int FinalLotCount,decimal FinalGoodQuantity,int MandatoryFinalChecks,int UntracedStagePairs,
    IReadOnlyList<FactoryEolLotCoverage> Coverage,IReadOnlyList<FactoryEolIssue> Issues,
    string? ApprovedBy,DateTime? ApprovedUtc,string Disclaimer);

public static class FactoryEndOfLineAcceptance
{
    public const string ApprovedAction="EndOfLineApproved";
    public static FactoryEolApproval Evaluate(ManufacturingOrder order,
        ManufacturingBomRevision? bom,IReadOnlyList<ManufacturingConfirmation> finalLots,
        IReadOnlyList<ManufacturingLotGenealogy> links,
        IReadOnlyDictionary<Guid,Guid> sourceItems,
        IReadOnlySet<Guid> semiItemIds,IReadOnlyList<ManufacturingQualityHold> holds,
        IReadOnlyList<ManufacturingOrderAudit> audits,
        IReadOnlyList<FactoryStageHandoff>? stageHandoffs=null)
    {
        var issues=new List<FactoryEolIssue>();
        var certificates=audits.Where(x=>x.Action==ApprovedAction).ToArray();
        var latest=certificates.OrderByDescending(x=>x.AtUtc).FirstOrDefault();
        if(order.Kind!=ManufacturingOrderKind.FinalAssembly)
            issues.Add(new("NotFinalAssembly","گواهی کنترل نهایی فقط برای دستور مونتاژ محصول نهایی است."));
        if(order.Status!=ManufacturingOrderStatus.Completed)
            issues.Add(new("NotCompleted","دستور مونتاژ باید ابتدا به وضعیت تکمیل‌شده برسد."));
        if(holds.Any(x=>x.Status==ManufacturingQualityStatus.Open))
            issues.Add(new("OpenQualityHold","قرنطینه باز یا عدم‌انطباق تعیین تکلیف‌نشده وجود دارد."));
        if(order.Operations.Count==0 || order.Operations.Any(x=>!x.IsComplete||
            x.QualityPendingQuantity!=0||!ManufacturingInspectionRules.GatePassed(x)))
            issues.Add(new("OperationQualityGate","تمام مراحل باید کامل، فاقد قرنطینه و دارای قبول کنترل‌های اجباری باشند."));
        var last=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
        var requiredLast=last?.InspectionRequirements.Where(x=>x.Mandatory).ToArray()??[];
        if(requiredLast.Length==0||requiredLast.Any(x=>x.LastPassed!=true))
            issues.Add(new("FinalQualityChecks","آخرین عملیات باید کنترل پلن مصوب دارای حداقل یک مشخصه اجباریِ قبول‌شده داشته باشد."));
        if(bom is null||bom.Id!=order.BomRevisionId||bom.ParentItemId!=order.ItemId)
            issues.Add(new("MissingFrozenBom","ساختار محصول منجمد دستور معتبر نیست."));
        var lots=finalLots.Where(x=>x.OperationId==last?.Id &&
            (x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity)>0).ToArray();
        if(lots.Length==0||lots.Sum(x=>x.GoodQuantity)<=0)
            issues.Add(new("MissingFinalOutput","هیچ بچ خروجی سالم برای کنترل نهایی ثبت نشده است."));
        foreach(var lot in lots)
            if(string.IsNullOrWhiteSpace(lot.LotCode))
                issues.Add(new("MissingLotCode","کد بچ مونتاژ برای گواهی نهایی الزامی است.",lot.Id));
        var stagePairs=stageHandoffs??[];
        if(order.Operations.Count>1)
        {
            if(stagePairs.Any(x=>x.OverAllocatedUpstreamGood>0||
                x.DestinationLots.Any(lot=>lot.OverAllocatedGood>0)))
                issues.Add(new("OverAllocatedStageHandoff",
                    "در شجره مراحل ساخت، مقدار پیوند ثبت‌شده از خروجی سالم بچ مبدأ یا مقصد بیشتر است."));
            if(stagePairs.Count!=order.Operations.Count-1 ||
                stagePairs.Any(x=>x.UntracedDownstreamGood>0||!x.QualityGatePassed))
                issues.Add(new("UndocumentedStageHandoff",
                    "تمام بچ‌های خروجی عملیات بعدی باید پیوند مستند با عملیات قبلی و گیت کیفیت معتبر داشته باشند."));
        }
        var coverage=new List<FactoryEolLotCoverage>();
        if(bom is not null)
        {
            var semi=bom.Lines.Where(x=>semiItemIds.Contains(x.ComponentItemId))
                .GroupBy(x=>x.ComponentItemId).ToArray();
            foreach(var group in semi)
                if(group.Any(x=>x.QuantityPer<=0||x.ScrapPercent is <0 or >=100))
                    issues.Add(new("InvalidBomRatio","ضریب یا ضایعات جزء نیمه‌ساخته نامعتبر است.",null,group.Key));
            if(!issues.Any(x=>x.Code=="InvalidBomRatio"))
                foreach(var lot in lots)
                    foreach(var group in semi)
                    {
                        var demand=(lot.GoodQuantity+lot.ScrapQuantity+lot.ReworkQuantity)*
                            group.Sum(x=>x.QuantityPer*100m/(100m-x.ScrapPercent));
                        var allocated=links.Where(x=>x.DestinationConfirmationId==lot.Id&&
                            sourceItems.TryGetValue(x.SourceConfirmationId,out var sourceItem)&&
                            sourceItem==group.Key).Sum(x=>x.Quantity);
                        var missing=Math.Max(0m,demand-allocated);
                        coverage.Add(new(lot.Id,lot.LotCode??"",group.Key,demand,allocated,missing));
                        if(missing>0)
                            issues.Add(new("MissingSemiOrigin","پیوند مستند بچ نیمه‌ساخته به مونتاژ ناقص است.",
                                lot.Id,group.Key,missing));
                        if(allocated>demand)
                            issues.Add(new("OriginOverAllocated","انتساب نیمه‌ساخته از BOM مقصد بیشتر است.",
                                lot.Id,group.Key,allocated-demand));
                    }
            var finalIds=lots.Select(x=>x.Id).ToHashSet();
            if(links.Any(x=>finalIds.Contains(x.DestinationConfirmationId)&&
                (!sourceItems.TryGetValue(x.SourceConfirmationId,out var item)||
                 !semiItemIds.Contains(item))))
                issues.Add(new("InvalidOriginEvidence","پیوند ساختِ فاقد منشأ نیمه‌ساخته قابل اتکا مشاهده شد."));
        }
        if(latest is not null)
        {
            issues.Add(new("AlreadyApproved","این دستور قبلاً گواهی نهایی ثبت کرده است."));
            if(certificates.Length!=1||!CertificateMatches(latest.Details,finalLots))
                issues.Add(new("InvalidFinalCertificate",
                    "سند امضای داخلی کنترل نهایی ناقص، تکراری یا با مقدار بچ‌ها ناسازگار است."));
        }
        return new(order.Id,order.Number,issues.Count==0,latest is not null,lots.Length,
            lots.Sum(x=>x.GoodQuantity),requiredLast.Length,
            stagePairs.Count(x=>x.UntracedDownstreamGood>0),coverage,issues,
            latest is null?null:ReadApprovedBy(latest.Details),latest?.AtUtc,
            "تأیید کنترل نهایی صرفاً ثبت داخلی کنترل کیفیت تولید است؛ اجازه ارسال محصول، گواهی مشتری، انتقال موجودی یا ثبت فروش نیست.");
    }
    // Version 2 seals the complete identities and quantities of individual
    // final confirmations. The legacy three-field certificate remains readable
    // for existing records, but all NEW signatures use the strong snapshot.
    // SHA256 detects data drift; it is NOT an external PKI signature.
    public static string FinalLotDigest(IReadOnlyList<ManufacturingConfirmation> finalLots)
    {
        var lines=finalLots.Where(x=>x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity>0)
            .OrderBy(x=>x.Id).Select(x=>string.Join("|",
                x.Id.ToString("N"),x.OperationId.ToString("N"),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(x.LotCode??"")),
                x.GoodQuantity.ToString("G29",CultureInfo.InvariantCulture),
                x.ScrapQuantity.ToString("G29",CultureInfo.InvariantCulture),
                x.ReworkQuantity.ToString("G29",CultureInfo.InvariantCulture),
                x.OccurredUtc.Ticks.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n",lines))));
    }

    public static string CreateCertificate(string actor,
        IReadOnlyList<ManufacturingConfirmation> finalLots)
    {
        if(string.IsNullOrWhiteSpace(actor)||actor.Length>128||
            actor.Any(c=>char.IsControl(c)||c==';'))
            throw new ArgumentException("Invalid QA reviewer identity.",nameof(actor));
        var rows=finalLots.Where(x=>x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity>0)
            .ToArray();
        if(rows.Length==0||rows.Sum(x=>x.GoodQuantity)<=0)
            throw new ArgumentException("Finished lot evidence is missing.",nameof(finalLots));
        return FormattableString.Invariant(
            $"By={actor};Lots={rows.Length};Good={rows.Sum(x=>x.GoodQuantity)};SHA256={FinalLotDigest(rows)}");
    }

    public static bool TryReadCertificate(string? details,out string actor,
        out int lotCount,out decimal goodQuantity)
    {
        actor="";lotCount=0;goodQuantity=0;
        var parts=details?.Split(';');
        if(parts is null||parts.Length is not (3 or 4)||
            !parts[0].StartsWith("By=",StringComparison.Ordinal)||
            !parts[1].StartsWith("Lots=",StringComparison.Ordinal)||
            !parts[2].StartsWith("Good=",StringComparison.Ordinal))
            return false;
        if(parts.Length==4 && (!parts[3].StartsWith("SHA256=",StringComparison.Ordinal)||
            parts[3].Length!=71||!parts[3][7..].All(c=>
                c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            return false;
        actor=parts[0][3..];
        return !string.IsNullOrWhiteSpace(actor)&&actor.Length<=128&&
            !actor.Any(char.IsControl)&&
            int.TryParse(parts[1][5..],NumberStyles.None,
                CultureInfo.InvariantCulture,out lotCount)&&lotCount>0&&
            decimal.TryParse(parts[2][5..],NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,out goodQuantity)&&goodQuantity>0;
    }
    public static bool CertificateMatches(string? details,
        IReadOnlyList<ManufacturingConfirmation> finalLots)
    {
        var confirmed=finalLots.Where(x=>
            x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity>0).ToArray();
        if(!TryReadCertificate(details,out _,out var lotCount,out var good)||
            confirmed.Length!=lotCount||confirmed.Sum(x=>x.GoodQuantity)!=good)
            return false;
        var hashPart=details!.Split(';');
        return hashPart.Length==3 ||
            string.Equals(hashPart[3][7..],FinalLotDigest(confirmed),
                StringComparison.Ordinal);
    }
    private static string? ReadApprovedBy(string? details)=>
        TryReadCertificate(details,out var actor,out _,out _)?actor:null;

    private static async Task<FactoryEolApproval?> ReadCheck(ProductionDb db,Guid id,CancellationToken ct)
    {
        var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
            .ThenInclude(x=>x.InspectionRequirements).SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(order is null)return null;
        var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
            .SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
        var lastId=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault()?.Id;
        var lots=await db.ManufacturingConfirmations.AsNoTracking()
            .Where(x=>x.OperationId==lastId).ToListAsync(ct);
        var lotIds=lots.Select(x=>x.Id).ToArray();
        var links=await db.ManufacturingLotGenealogies.AsNoTracking()
            .Where(x=>x.OrderId==id&&lotIds.Contains(x.DestinationConfirmationId))
            .ToListAsync(ct);
        var sourceIds=links.Select(x=>x.SourceConfirmationId).Distinct().ToArray();
        var sourceConfirmations=await db.ManufacturingConfirmations.AsNoTracking()
            .Join(db.ManufacturingOrderOperations.AsNoTracking(),x=>x.OperationId,x=>x.Id,
                (confirmation,operation)=>new{confirmation.Id,operation.OrderId})
            .Join(db.ManufacturingOrders.AsNoTracking(),x=>x.OrderId,x=>x.Id,
                (entry,sourceOrder)=>new{ConfirmationId=entry.Id,entry.OrderId,sourceOrder.ItemId})
            .Where(x=>sourceIds.Contains(x.ConfirmationId)).ToListAsync(ct);
        // Internal adjacent-step links are not consumed semi-finished
        // components; exclude them from cross-order BOM provenance coverage.
        var crossLinks=links.Where(x=>sourceConfirmations.Any(y=>
            y.ConfirmationId==x.SourceConfirmationId&&y.OrderId!=id)).ToArray();
        var sourceItems=sourceConfirmations.Where(x=>x.OrderId!=id)
            .ToDictionary(x=>x.ConfirmationId,x=>x.ItemId);
        var componentIds=bom?.Lines.Select(x=>x.ComponentItemId).Distinct().ToArray()??[];
        var semiItems=await db.Items.AsNoTracking()
            .Where(x=>componentIds.Contains(x.Id)&&x.Type==PlanningItemType.SemiFinished)
            .Select(x=>x.Id).ToListAsync(ct);
        var holds=await db.ManufacturingQualityHolds.AsNoTracking()
            .Where(x=>x.OrderId==id&&x.Status==ManufacturingQualityStatus.Open).ToListAsync(ct);
        var audits=await db.ManufacturingOrderAudits.AsNoTracking()
            .Where(x=>x.OrderId==id&&x.Action==ApprovedAction).ToListAsync(ct);
        var operationIds=order.Operations.Select(x=>x.Id).ToArray();
        var stageConfirmations=await db.ManufacturingConfirmations.AsNoTracking()
            .Where(x=>operationIds.Contains(x.OperationId)).ToListAsync(ct);
        var stageIds=stageConfirmations.Select(x=>x.Id).ToArray();
        var internalLinks=await db.ManufacturingLotGenealogies.AsNoTracking()
            .Where(x=>x.OrderId==id&&stageIds.Contains(x.SourceConfirmationId)&&
                stageIds.Contains(x.DestinationConfirmationId)).ToListAsync(ct);
        var handoffs=FactoryInProcessHandoffs.Analyze(order,stageConfirmations,internalLinks,holds);
        return Evaluate(order,bom,lots,crossLinks,sourceItems,semiItems.ToHashSet(),holds,
            audits,handoffs.Pairs);
    }

    public static IEndpointRouteBuilder MapEndOfLineAcceptance(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/end-of-line-check",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var report=await ReadCheck(db,id,ct);
                return report is null?Results.NotFound():Results.Ok(report);
            });
        api.MapPost("/manufacturing-orders/{id:guid}/approve-end-of-line",
            async(Guid id,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{
                    // Must share lock ordering with inter-order genealogy writer.
                    var key="IPPCS:AssemblyGenealogy";
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $@"DECLARE @rc int;
                        EXEC @rc=sys.sp_getapplock @Resource={key},
                        @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                        IF @rc < 0 THROW 51052, 'EOL genealogy approval lock unavailable', 1;",ct);
                    await FactoryCapacityReservations.LockOrder(db,id,ct);
                }catch(SqlException){return Results.Conflict(new{message="قفل کنترل نهایی مونتاژ در دسترس نیست."});}
                var report=await ReadCheck(db,id,ct);
                if(report is null)return Results.NotFound();
                if(!report.CanApprove)
                    return Results.Conflict(new{message="گیت‌های کیفیت یا ردیابی بچ مونتاژ تکمیل نیست.",report});
                var actor=ctx.User.Identity?.Name?.Trim();
                if(string.IsNullOrWhiteSpace(actor)||actor.Length>128||
                    actor.Any(c=>char.IsControl(c)||c==';'))
                    return Results.Forbid();
                var finalId=await db.ManufacturingOrderOperations.AsNoTracking()
                    .Where(x=>x.OrderId==id).OrderByDescending(x=>x.Sequence)
                    .Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
                if(!finalId.HasValue)
                    return Results.Conflict(new{message="آخرین عملیات دستور قابل تشخیص نیست."});
                var finalLots=await db.ManufacturingConfirmations.AsNoTracking()
                    .Where(x=>x.OperationId==finalId.Value).ToListAsync(ct);
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                    OrderId=id,Action=ApprovedAction,
                    Details=CreateCertificate(actor,finalLots)
                });
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{orderId=id,approved=true,approvedBy=actor,
                    report.FinalLotCount,report.FinalGoodQuantity,
                    disclaimer=report.Disclaimer});
            }).RequireAuthorization("IpQualityApprove");
        return routes;
    }
}
