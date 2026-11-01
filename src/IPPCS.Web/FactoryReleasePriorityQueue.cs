using System.Text.Json;
using Microsoft.EntityFrameworkCore;

// Read-only factory exception queue. These are software observations for an
// operator's investigation, not a factory acceptance certificate or approval.
public sealed record FactoryPriorityCase(string Area,string Severity,Guid ReferenceId,
    string Identifier,string Description,string Page);
public static class FactoryReleasePriorityQueue
{
    public static string EolCertificateState(
        IReadOnlyList<ManufacturingOrderAudit> certificates,
        IReadOnlyList<ManufacturingConfirmation> finalLots)
    {
        if(certificates.Count==0)return "Missing";
        if(certificates.Count!=1)return "Duplicate";
        var details=certificates[0].Details;
        if(!FactoryEndOfLineAcceptance.CertificateMatches(details,finalLots))
            return "Invalid";
        return details?.Split(';').Length==4?"SnapshotV2":"LegacyTotalsOnly";
    }

    public static IEndpointRouteBuilder MapFactoryReleasePriorityQueue(
        this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/factory-release-readiness/priority-queue",
            async(ProductionDb db,CancellationToken ct)=>
            {
                var pendingEco=await db.FactoryEcoApprovals.AsNoTracking()
                    .Where(x=>x.Status==FactoryEcoApprovalStatus.Submitted||
                        x.Status==FactoryEcoApprovalStatus.QualityApproved)
                    .OrderBy(x=>x.RequestedUtc).Take(501).ToListAsync(ct);
                if(pendingEco.Count>500)
                    return Results.Conflict(new{message="درخواست‌های فعال ECO بیش از سقف ۵۰۰ است؛ صف ناقص نمایش داده نمی‌شود."});
                var deviations=await db.ProductionDeviations.AsNoTracking()
                    .Include(x=>x.Events).OrderBy(x=>x.CreatedUtc)
                    .Take(501).ToListAsync(ct);
                if(deviations.Count>500)
                    return Results.Conflict(new{message="پرونده‌های CAPA بیش از سقف ۵۰۰ است؛ برای صف جامع از پالایش سروری استفاده شود."});
                var assemblies=await db.ManufacturingOrders.AsNoTracking()
                    .Where(x=>x.Kind==ManufacturingOrderKind.FinalAssembly &&
                        x.Status==ManufacturingOrderStatus.Completed)
                    .OrderBy(x=>x.CompletedUtc).Take(501).ToListAsync(ct);
                if(assemblies.Count>500)
                    return Results.Conflict(new{message="دستورهای نهایی تکمیل‌شده بیش از سقف ۵۰۰ است؛ تأییدهای EOL به صورت ناقص گزارش نمی‌شوند."});
                var orderIds=assemblies.Select(x=>x.Id).ToArray();
                var signed=await db.ManufacturingOrderAudits.AsNoTracking()
                    .Where(x=>orderIds.Contains(x.OrderId)&&
                        x.Action==FactoryEndOfLineAcceptance.ApprovedAction)
                    .ToListAsync(ct);
                var ops=await db.ManufacturingOrderOperations.AsNoTracking()
                    .Where(x=>orderIds.Contains(x.OrderId))
                    .Select(x=>new{x.Id,x.OrderId,x.Sequence}).ToListAsync(ct);
                var finalIds=ops.GroupBy(x=>x.OrderId).Select(x=>
                    x.OrderByDescending(o=>o.Sequence).First().Id).ToArray();
                var lots=await db.ManufacturingConfirmations.AsNoTracking()
                    .Where(x=>finalIds.Contains(x.OperationId)).ToListAsync(ct);
                var now=FactoryEcoApprovalWorkflow.Today();
                var result=new List<FactoryPriorityCase>();
                foreach(var eco in pendingEco)
                {
                    var expired=eco.CutoverOn<now;
                    result.Add(new("ECO",expired?"High":"Medium",eco.Id,
                        eco.ApprovalReference,
                        expired?"تاریخ برش درخواست مهندسی گذشته است؛ تأیید جدید مجاز نیست و تعیین تکلیف یا رد مستند لازم است.":
                            eco.Status==FactoryEcoApprovalStatus.Submitted?
                            "بررسی مستقل کیفیت برای درخواست مهندسی انجام نشده است.":
                            "برش نسخه پس از تأیید مستقل کیفیت هنوز اجرا نشده است.",
                        "/engineering-rebind.html"));
                }
                foreach(var deviation in deviations)
                {
                    var proof=FactoryDeviationEffectiveness.Evaluate(deviation);
                    var overdue=deviation.Status!=ProductionDeviationStatus.Closed&&
                        deviation.ActionDueOn.HasValue&&deviation.ActionDueOn.Value<now;
                    if(deviation.Status==ProductionDeviationStatus.Closed&&
                        proof.IndependentlyVerified)continue;
                    result.Add(new("CAPA",overdue||proof.State=="InvalidEvidence"?
                            "High":"Medium",deviation.Id,deviation.Id.ToString("N")[..8],
                        proof.State=="InvalidEvidence"?"شواهد تأیید اثربخشی مخدوش است.":
                        overdue?"سررسید اقدام اصلاحی گذشته و پرونده هنوز مختومه نیست.":
                        deviation.Status==ProductionDeviationStatus.Closed?
                            "اختتام ثبت شده، اما تأیید مستقل اثربخشی وجود ندارد.":
                            "اقدام اصلاحی هنوز تکمیل و تأیید نشده است.",
                        "/production-deviations.html"));
                }
                foreach(var order in assemblies)
                {
                    var final=ops.Where(x=>x.OrderId==order.Id)
                        .OrderByDescending(x=>x.Sequence).FirstOrDefault();
                    var finalLots=lots.Where(x=>x.OperationId==final?.Id).ToArray();
                    var history=signed.Where(x=>x.OrderId==order.Id).ToArray();
                    var status=EolCertificateState(history,finalLots);
                    if(status=="SnapshotV2")continue;
                    result.Add(new("EOL",status is "Invalid" or "Duplicate"?
                            "Critical":"High",order.Id,order.Number,
                        status switch {
                            "Missing"=>"گواهی داخلی کنترل نهایی برای دستور تکمیل‌شده ثبت نشده است.",
                            "Duplicate"=>"چند گواهی EOL برای یک دستور وجود دارد؛ بررسی تاریخچه لازم است.",
                            "Invalid"=>"سند کنترل نهایی با شواهد بچ‌ها سازگار نیست.",
                            _=>"گواهی قدیمی صرفاً جمع مقادیر را کنترل می‌کند؛ بازبینی شواهد بچ‌های فردی لازم است."
                        },"/control-plan.html"));
                }

                // APS represents a scenario, not proof of real machine capacity.
                var latest=await db.FactoryApsRuns.AsNoTracking()
                    .OrderByDescending(x=>x.CalculatedUtc).FirstOrDefaultAsync(ct);
                string apsState="NoSnapshot";
                if(latest is not null)
                {
                    try
                    {
                        var snapshot=JsonSerializer.Deserialize<ApsResult>(latest.OutputJson);
                        if(snapshot?.Unscheduled is null||snapshot.Allocations is null)
                            throw new JsonException("Incomplete APS snapshot.");
                        apsState=latest.StartOn.AddDays(latest.HorizonDays)<=now?
                            "ExpiredHorizon":snapshot.Unscheduled.Count>0?
                                "Unscheduled":"SnapshotOnly";
                        if(apsState=="Unscheduled")
                            result.Add(new("APS","High",latest.Id,latest.Id.ToString("N")[..8],
                                $"{snapshot.Unscheduled.Count} عملیات در آخرین سناریوی APS زمان‌بندی نشده‌اند.",
                                "/planning-advanced.html"));
                        if(apsState=="ExpiredHorizon")
                            result.Add(new("APS","Medium",latest.Id,latest.Id.ToString("N")[..8],
                                "افق آخرین سناریوی APS خاتمه یافته است؛ برای تصمیم جدید محاسبهٔ به‌روز لازم است.",
                                "/planning-advanced.html"));
                    }
                    catch(JsonException)
                    {
                        apsState="InvalidSnapshot";
                        result.Add(new("APS","Critical",latest.Id,
                            latest.Id.ToString("N")[..8],
                            "خروجی آخرین APS قابل خواندن نیست؛ پیش‌نمایش باید بررسی شود.",
                            "/planning-advanced.html"));
                    }
                }
                var ordered=result.OrderBy(x=>x.Severity=="Critical"?0:
                    x.Severity=="High"?1:2).ThenBy(x=>x.Area)
                    .ThenBy(x=>x.Identifier).ToArray();
                return Results.Ok(new{
                    kind="FactoryPriorityExceptionsOnly",
                    generatedUtc=DateTime.UtcNow,asOfJalali=PersianPlanningDates.Format(now),
                    apsState,latestApsRunId=latest?.Id,
                    total=ordered.Length,
                    critical=ordered.Count(x=>x.Severity=="Critical"),
                    high=ordered.Count(x=>x.Severity=="High"),
                    medium=ordered.Count(x=>x.Severity=="Medium"),
                    byArea=ordered.GroupBy(x=>x.Area).Select(x=>new{
                        area=x.Key,count=x.Count()}).ToArray(),
                    rows=ordered,
                    productionReleaseAuthorized=false,factoryAcceptedModules=0,
                    disclaimer="فقط موارد قابل بررسی از داده داخلی IPPCS. نبود هشدار به معنی پذیرش کارخانه نیست؛ بررسی تجهیزات، شواهد واقعی، گیت‌های ۱۸ ماژول و آزمون کاربر همچنان الزامی است."
                });
            }).RequireAuthorization("IpProductionUser");
        return routes;
    }
}
