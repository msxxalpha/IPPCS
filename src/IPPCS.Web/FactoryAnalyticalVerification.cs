using System.Globalization;
using Microsoft.EntityFrameworkCore;

// Live, read-only preparation checks for analytical modules. A ready input set
// is not an accepted algorithm, signed planning decision or plant KPI.
public record FactoryAnalyticalCheck(string Key,string Title,int Count,int Minimum,
    string Advice)
{
    public bool Ready=>Count>=Minimum;
}
public record FactoryAnalyticalGate(int Number,string Title,string Section,string Page,
    IReadOnlyList<FactoryAnalyticalCheck> Checks,int EvidenceCount,
    string EvidenceKind,string LiveTestRoute,string Warning)
{
    public bool CanRun=>Checks.All(x=>x.Ready);
    public bool HasRecordedEvidence=>EvidenceCount>0;
    public string State=>!CanRun?"MissingInputs":
        HasRecordedEvidence?"EvidenceAvailable":"ReadyForFirstTest";
}
public record FactoryAnalyticalExamples(Guid? FinishedItemId,Guid? ApprovedMpsId,
    Guid? RecentMrpRunId,Guid? RecentSopId,Guid? RecentApsRunId,
    Guid? RecentOrderId,string ClosedMonthJalali,string TodayJalali);

public static class FactoryAnalyticalVerification
{
    public static readonly int[] ModuleNumbers=[2,3,5,6,7,14,16,17];
    public static FactoryAnalyticalGate Create(int module,string title,string section,
        string page,string route,int evidence,string evidenceKind,string warning,
        params FactoryAnalyticalCheck[] checks)=>
        new(module,title,section,page,checks,evidence,evidenceKind,route,warning);

    public static IReadOnlyList<FactoryAnalyticalGate> Build(
        int confirmedDemand,int confirmedMonths,int sopRuns,int approvedMps,
        int approvedBom,int balances,int mrpRuns,int centers,int routings,int shifts,
        int apsRuns,int oeePlans,int clockStops,int closedTimedStops,
        int costRates,int frozenRates,int orders)
    {
        FactoryAnalyticalCheck Ch(string key,string title,int count,int minimum,string advice)=>
            new(key,title,count,minimum,advice);
        return [
            Create(2,"تقاضا و پیش‌بینی","demand","/planning-advanced.html",
                "forecast-backtest",confirmedMonths,"ماه‌های دارای تقاضای قطعی ثبت‌شده",
                "ماه بدون رکورد صفر محاسبه می‌شود؛ قبل از تفسیر WAPE، کامل‌بودن دادهٔ تاریخی باید توسط برنامه‌ریزی تأیید شود.",
                Ch("confirmed","نیاز قطعی داخلی",confirmedDemand,1,"حداقل یک تقاضای قطعی برای محصول نهایی وارد کنید."),
                Ch("months","ماه‌های مستقل تقاضا",confirmedMonths,2,"برای ارزیابی ابتدایی، نیاز قطعی در حداقل دو ماه شمسی مستقل ثبت کنید.")),
            Create(3,"تراز کلان S&OP","sop","/planning-advanced.html",
                "sop-snapshot",sopRuns,"Snapshotهای S&OP ذخیره‌شده",
                "Snapshot ثبت‌شده به معنی مصوبهٔ جلسه نیست؛ بار مراکز و ظرفیت باید جدا بررسی شود.",
                Ch("demand","تقاضای مستقل",confirmedDemand,1,"برای دورهٔ هدف تقاضای مستقل وارد کنید."),
                Ch("routes","مسیر ساخت مصوب",routings,1,"مسیر ساخت مدل آزمایشی را تصویب کنید."),
                Ch("shifts","تقویم شیفت",shifts,1,"شیفت مرکز کاری مرتبط را تعریف کنید.")),
            Create(5,"MRP و منشأ نیاز","mrp","/planning.html",
                "mrp-snapshot",mrpRuns,"محاسبات MRP ثبت‌شده",
                "موجودی و دریافت‌ها مفروضات دستی IPPCS هستند؛ نتیجه مجوز خرید یا تخصیص انبار نیست.",
                Ch("mps","MPS مصوب",approvedMps,1,"یک برنامهٔ MPS با ردیف مصوب بسازید."),
                Ch("bom","BOM مصوب",approvedBom,1,"برای محصول BOM نسخه‌دار معتبر تصویب کنید."),
                Ch("balances","موجودی برنامه‌ای مفروض",balances,1,"برای کالاها فرضیات اولیه برنامه‌ریزی را تعریف کنید.")),
            Create(6,"CRP و ظرفیت","mrp","/planning.html",
                "crp-preview",0,"پیش‌نمایش زنده؛ بدون ذخیرهٔ مستقل",
                "محاسبه ظرفیت تقریبی است؛ زمان‌های واقعی تجهیزات و تأیید شیفت هنوز لازم است.",
                Ch("mps","MPS مصوب",approvedMps,1,"نسخهٔ MPS مصوب ایجاد کنید."),
                Ch("routes","مسیر ساخت مصوب",routings,1,"Routing عملیات برنامه را تصویب کنید."),
                Ch("centers","مراکز کاری",centers,1,"مرکز کاری آزمایشی تعریف کنید."),
                Ch("shifts","شیفت‌های کاری",shifts,1,"شیفت قابل استفاده وارد کنید.")),
            Create(7,"APS زمان‌بندی محدود","aps","/planning-advanced.html",
                "aps-snapshot",apsRuns,"سناریوهای APS ذخیره‌شده",
                "الگوریتم محدود ظرفیت آزمایشی است؛ ابزار جایگزین و تغییرات ستاپ واقعی در کارخانه تایید نشده‌اند.",
                Ch("mps","MPS مصوب",approvedMps,1,"نسخه MPS را تصویب کنید."),
                Ch("routing","Routing مصوب",routings,1,"مسیر معتبر با زمان استاندارد تعریف کنید."),
                Ch("shifts","تقویم شیفت",shifts,1,"حداقل یک شیفت ظرفیت مؤثر تعریف کنید.")),
            Create(14,"OEE مبتنی بر ساعت واقعی","oee","/execution.html",
                "oee-clock",clockStops+closedTimedStops,"رخدادهای ساعت/توقف بسته‌شده",
                "OEE از شواهد دستی IPPCS محاسبه می‌شود و بدون تأیید ساعت تجهیز نباید KPI رسمی تلقی شود.",
                Ch("centers","مرکز کاری",centers,1,"مراکز کاری را تعریف کنید."),
                Ch("planned","بودجه دقیقهٔ تولید",oeePlans,1,"برای یک مرکز و روز، دقیقهٔ برنامه‌ای OEE ثبت کنید."),
                Ch("clock","جلسه‌های ساعت عملیات بسته‌شده",clockStops,1,
                    "شروع و پایان ساعت عملیات واقعی آزمایشی را ثبت کنید.")),
            Create(16,"مقایسه سناریوی What-if","whatif","/planning-advanced.html",
                "whatif-live",0,"سناریو فقط محاسبه می‌شود؛ تاریخچه‌ای ذخیره نمی‌شود",
                "این آزمون برنامه/دستور تولید و دادهٔ تأییدشده را تغییر نمی‌دهد و قابلیت‌های آن محدود به ظرفیت است.",
                Ch("mps","MPS مصوب",approvedMps,1,"یک MPS مصوب انتخاب کنید."),
                Ch("routes","Routing مصوب",routings,1,"مسیر ساخت قابل زمان‌بندی تعریف کنید."),
                Ch("shifts","شیفت مرکز کاری",shifts,1,"تقویم شیفت برای ظرفیت سناریو وارد کنید.")),
            Create(17,"هزینه تبدیل و بهره‌وری","cost","/planning-advanced.html",
                "cost-variance",frozenRates,"رویدادهای نرخ منجمد شده",
                "این هزینه‌ها فرضیات داخلی هستند؛ سربار و سیاست حسابداری کارخانه تأیید نشده‌اند.",
                Ch("rates","نرخ تبدیل مرکز",costRates,1,"برای حداقل یک مرکز نرخ داخلی تبدیل تعریف کنید."),
                Ch("orders","دستور ساخت",orders,1,"دستور ساخت آزمایشی آزاد کنید."),
                Ch("frozen","سند نرخ منجمد دستور",frozenRates,1,
                    "پس از تعریف نرخ، دستور آزمایشی جدید آزاد کنید تا نرخ ثابت ثبت شود."))
        ];
    }

    public static IEndpointRouteBuilder MapFactoryAnalyticalVerification(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/factory-analytical-verification",
            async(ProductionDb db,CancellationToken ct)=>{
                var confirmed=await db.FactoryDemands.AsNoTracking()
                    .Where(x=>x.Category=="Confirmed")
                    .Select(x=>new{x.ItemId,x.DueOn})
                    .ToListAsync(ct);
                var cal=new PersianCalendar();
                var confirmedMonths=confirmed
                    .Select(x=>cal.GetYear(x.DueOn.ToDateTime(TimeOnly.MinValue))*12+
                        cal.GetMonth(x.DueOn.ToDateTime(TimeOnly.MinValue)))
                    .Distinct().Count();
                var sopRuns=await db.FactorySopReviews.AsNoTracking().CountAsync(ct);
                var mps=await db.MpsVersions.AsNoTracking()
                    .Where(x=>x.State==PlanningApproval.Approved&&x.Lines.Any())
                    .OrderByDescending(x=>x.CreatedUtc).Select(x=>x.Id).FirstOrDefaultAsync(ct);
                var mpsCount=await db.MpsVersions.AsNoTracking()
                    .CountAsync(x=>x.State==PlanningApproval.Approved&&x.Lines.Any(),ct);
                var boms=await db.ManufacturingBomRevisions.AsNoTracking()
                    .CountAsync(x=>x.State==ManufacturingRevisionState.Approved,ct);
                var balances=await db.PlanningBalances.AsNoTracking().CountAsync(ct);
                var recentMrp=await db.PlanningCalculations.AsNoTracking()
                    .OrderByDescending(x=>x.CalculatedUtc).Select(x=>x.Id).FirstOrDefaultAsync(ct);
                var mrpRuns=await db.PlanningCalculations.AsNoTracking().CountAsync(ct);
                var centers=await db.WorkCenters.AsNoTracking().CountAsync(ct);
                var routing=await db.RoutingRevisions.AsNoTracking()
                    .CountAsync(x=>x.State==RoutingState.Approved&&x.Operations.Any(),ct);
                var shifts=await db.WorkShifts.AsNoTracking().CountAsync(ct);
                var aps=await db.FactoryApsRuns.AsNoTracking()
                    .OrderByDescending(x=>x.CalculatedUtc).Select(x=>x.Id).FirstOrDefaultAsync(ct);
                var apsRuns=await db.FactoryApsRuns.AsNoTracking().CountAsync(ct);
                var oeePlans=await db.ManufacturingOeePlans.AsNoTracking().CountAsync(ct);
                var clocks=await db.ManufacturingOrderAudits.AsNoTracking()
                    .CountAsync(x=>x.Action==FactoryOperationClock.Stopped,ct);
                var timedStops=await db.ManufacturingOrderAudits.AsNoTracking()
                    .CountAsync(x=>x.Action==FactoryTimedDowntime.StopAction,ct);
                var rates=await db.FactoryCenterCostRates.AsNoTracking().CountAsync(ct);
                var frozen=await db.ManufacturingOrderAudits.AsNoTracking()
                    .CountAsync(x=>x.Action==FactoryCostRateHistory.Action,ct);
                var orders=await db.ManufacturingOrders.AsNoTracking().CountAsync(ct);
                var recentOrder=await db.ManufacturingOrders.AsNoTracking()
                    .OrderByDescending(x=>x.CreatedUtc).Select(x=>x.Id).FirstOrDefaultAsync(ct);
                var recentSop=await db.FactorySopReviews.AsNoTracking()
                    .OrderByDescending(x=>x.CalculatedUtc).Select(x=>x.Id).FirstOrDefaultAsync(ct);
                var item=await db.FactoryDemands.AsNoTracking()
                    .Where(x=>x.Category=="Confirmed")
                    .OrderByDescending(x=>x.DueOn).Select(x=>x.ItemId).FirstOrDefaultAsync(ct);

                // Iran-local calendar, never server/visitor-local UTC date.
                var tz=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
                var iranNow=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,tz);
                var today=DateOnly.FromDateTime(iranNow);
                var closedMonthFirst=new DateTime(cal.GetYear(iranNow),
                    cal.GetMonth(iranNow),1,cal);
                var lastClosedDay=DateOnly.FromDateTime(closedMonthFirst.AddDays(-1));
                var examples=new FactoryAnalyticalExamples(
                    item==Guid.Empty?null:item,mps==Guid.Empty?null:mps,
                    recentMrp==Guid.Empty?null:recentMrp,
                    recentSop==Guid.Empty?null:recentSop,
                    aps==Guid.Empty?null:aps,
                    recentOrder==Guid.Empty?null:recentOrder,
                    PersianPlanningDates.Format(lastClosedDay),
                    PersianPlanningDates.Format(today));
                var gates=Build(confirmed.Count,confirmedMonths,sopRuns,mpsCount,boms,
                    balances,mrpRuns,centers,routing,shifts,apsRuns,oeePlans,
                    clocks,timedStops,rates,frozen,orders);
                return Results.Ok(new{
                    type="DevelopmentAnalyticalVerification",
                    releaseAuthorized=false,
                    factoryAccepted=0,
                    targetModules=ModuleNumbers,
                    readyForGuidedTrial=gates.Count(x=>x.CanRun),
                    recordedEvidenceModules=gates.Count(x=>x.HasRecordedEvidence),
                    modules=gates,examples,
                    disclaimer="داده‌های SQL داخلی و نتایج محاسبه، تأیید صحت طرح، داده واقعی کارخانه یا مجوز انتشار نیست. هر آزمون باید با نمونه واقعی و امضای مسئول فرایند بازبینی شود."
                });
            }).RequireAuthorization("IpProductionUser");
        return routes;
    }
}
