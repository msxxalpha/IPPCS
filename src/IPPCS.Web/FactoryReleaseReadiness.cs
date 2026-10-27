using Microsoft.EntityFrameworkCore;

// First-run and factory UAT guidance only: database presence and green CI
// cannot certify physical production, gauges, labels or authorized release.
public record FactoryModuleReleaseGate(int Number,string Title,string Page,
    string ImplementedScope,string OutstandingFactoryAcceptance);
public record FactorySetupGate(string Code,string Title,int Records,bool Configured,
    string Page,string Advice);

public static class FactoryReleaseReadiness
{
    public static readonly IReadOnlyList<FactoryModuleReleaseGate> Modules =
    [
        new(1,"اطلاعات پایه، BOM و Routing","/app.html",
            "اقلام، مراکز، ساختار و مسیر نسخه‌دار، پیش‌نمایش ECO","تأیید مهندسی مدل‌های واقعی و آزمون برش نسخه"),
        new(2,"تقاضا و پیش‌بینی","/planning-advanced.html",
            "تقاضای مستقل، پیش‌بینی ماه شمسی و Backtest","هدف دقت، داده ۱۲ ماه و تصویب سیاست پیش‌بینی"),
        new(3,"S&OP","/planning-advanced.html",
            "تراز بار و ظرفیت، سوابق سناریو","تأیید مشترک برنامه و ظرفیت کارخانه"),
        new(4,"MPS","/planning.html",
            "برنامه نسخه‌دار، تصویب و بازنگری ممیزی‌شده","امضای مالک برنامه و سیاست افق انجماد"),
        new(5,"MRP","/planning.html",
            "انفجار چندسطحی، خالص‌سازی و ردیابی منشأ فرضیات","اعتبارسنجی واقعی پارامترها و جانشینی مواد"),
        new(6,"CRP","/planning.html",
            "بار مراکز، شیفت شب، گلوگاه و رزرو ظرفیت","تقویم واقعی ماشین، ابزار و تعمیرات"),
        new(7,"APS","/planning-advanced.html",
            "زمان‌بندی محدود، گانت و محدودیت منابع","تأیید کارایی با تغییر ابزار و خطوط واقعی"),
        new(8,"دستورات ساخت","/execution.html",
            "صدور، آزادسازی، تعلیق، ادامه و تاریخچه","آزمون عملیاتی رقابت، نقش و اختیار امضا"),
        new(9,"تولید نیمه‌ساخته و WIP","/execution.html",
            "تأیید مراحل، ضایعات، بازکاری و پیوند بچ","تست اختلاط و انتقال واقعی بین خطوط"),
        new(10,"مونتاژ نهایی و EOL","/control-plan.html",
            "شجره مونتاژ، گیت EOL و شماره سریال داخلی","معیار آزمون فیزیکی هر نوع کمک‌فنر"),
        new(11,"مصرف مواد پای خط","/execution.html",
            "مصرف ثبت‌شده مستقل از انبار و برگشت ممیزی‌شده","جانشینی مجاز و اندازه‌گیری مصرف واقعی"),
        new(12,"انحراف و اقدامات اصلاحی CAPA","/production-deviations.html",
            "انحراف MPS، علت مرجع، پیگیری و اثربخشی مستقل","تصویب فرایند کنترل اثربخشی در کارخانه"),
        new(13,"کیفیت و کنترل پلن","/control-plan.html",
            "کنترل پلن، مشخصه اندازه‌گیری، قرنطینه و SPC اکتشافی","MSA، کالیبراسیون و حدود SPC مصوب"),
        new(14,"OEE و توقفات","/execution.html",
            "ساعت واقعی و توقف کدگذاری‌شده، محاسبه با پرچم نقص","تطبیق ساعت ماشین، شیفت و تفکیک اتلاف"),
        new(15,"ردیابی بچ و سریال","/factory-recall.html",
            "پیوند بچ چندمرحله‌ای و فراخوان محدود داخلی","آزمون لیبل، اسکن و فراخوان واقعی"),
        new(16,"سناریوهای What-if","/planning-advanced.html",
            "سناریو بدون تغییر برنامه مصوب و مقایسه ظرفیت","سیاست انتخاب سناریو و آزمون چندفرضی"),
        new(17,"هزینه تبدیل","/planning-advanced.html",
            "نرخ داخلی و مقایسه استاندارد با ساعت ثبت‌شده","تأیید روش بهای تمام‌شده بدون اتصال مالی"),
        new(18,"داشبوردهای تولید","/app.html",
            "شاخص‌ها، هشدارها و دسترسی گزارش‌های عملیاتی","تعریف اهداف، گزارش مدیران و تأیید منبع KPI")
    ];

    public static IReadOnlyList<FactorySetupGate> Setup(
        int items,int centers,int approvedRouting,int approvedBom,int shifts,
        int approvedMps,int approvedControlPlans,int downtimeReasons,int holdReasons)
    {
        return [
            new("Items","اقلام و مدل‌های تولید",items,items>0,"/app.html",
                "یک محصول نهایی و قطعات موردنیاز را در اطلاعات پایه تولید تعریف کنید."),
            new("Centers","مراکز کاری",centers,centers>0,"/app.html",
                "برای خطوط واقعی، مراکز و ظرفیت‌های اسمی را تعریف کنید."),
            new("Routing","مسیر ساخت مصوب",approvedRouting,approvedRouting>0,"/app.html",
                "برای محصول آزمایشی Routing معتبر و عملیات آن را تصویب کنید."),
            new("BOM","BOM نسخه‌دار مصوب",approvedBom,approvedBom>0,"/planning.html",
                "BOM محصول آزمایشی و تاریخ اعتبار را تصویب کنید."),
            new("Shifts","شیفت‌های کاری",shifts,shifts>0,"/app.html",
                "شیفت و ظرفیت هر مرکز کاری آزمایشی را تعریف کنید."),
            new("MPS","نسخهٔ مصوب MPS",approvedMps,approvedMps>0,"/planning.html",
                "برنامه اصلی تولید آزمایشی و تاریخ نیاز را ثبت و تصویب کنید."),
            new("ControlPlan","کنترل پلن مصوب",approvedControlPlans,approvedControlPlans>0,
                "/control-plan.html","مشخصه‌های بازرسی واقعی مدل را در کنترل پلن تعریف کنید."),
            new("DowntimeReasons","دلایل توقف فعال",downtimeReasons,downtimeReasons>0,
                "/factory-master-data.html","کدهای دلیل توقف را بررسی و متناسب با کارخانه تنظیم کنید."),
            new("HoldReasons","دلایل قرنطینه فعال",holdReasons,holdReasons>0,
                "/factory-master-data.html","کدهای قرنطینه کیفیت را با مسئول کیفیت بازبینی کنید.")
        ];
    }

    public static IEndpointRouteBuilder MapFactoryReleaseReadiness(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/factory-release-readiness",
            async(ProductionDb db,CancellationToken ct)=>{
                var items=await db.Items.AsNoTracking().CountAsync(ct);
                var centers=await db.WorkCenters.AsNoTracking().CountAsync(ct);
                var routing=await db.RoutingRevisions.AsNoTracking()
                    .CountAsync(x=>x.State==RoutingState.Approved,ct);
                var bom=await db.ManufacturingBomRevisions.AsNoTracking()
                    .CountAsync(x=>x.State==ManufacturingRevisionState.Approved,ct);
                var shifts=await db.WorkShifts.AsNoTracking().CountAsync(ct);
                var mps=await db.MpsVersions.AsNoTracking()
                    .CountAsync(x=>x.State==PlanningApproval.Approved,ct);
                var plans=await db.ManufacturingControlPlans.AsNoTracking()
                    .CountAsync(x=>x.State==ControlPlanState.Approved,ct);
                var stopCodes=await db.FactoryMasterOptions.AsNoTracking()
                    .CountAsync(x=>x.Kind==FactoryMasterData.DowntimeReason&&x.IsActive,ct);
                var holdCodes=await db.FactoryMasterOptions.AsNoTracking()
                    .CountAsync(x=>x.Kind==FactoryMasterData.QualityHoldReason&&x.IsActive,ct);
                var setup=Setup(items,centers,routing,bom,shifts,mps,plans,stopCodes,holdCodes);
                return Results.Ok(new{
                    kind="FactoryUatPreparationOnly",
                    productionReleaseAuthorized=false,
                    factoryAcceptedModules=0,
                    declaredModules=Modules.Count,
                    configuredSetupGates=setup.Count(x=>x.Configured),
                    totalSetupGates=setup.Count,
                    setup,modules=Modules.Select(x=>new{
                        x.Number,x.Title,x.Page,x.ImplementedScope,
                        x.OutstandingFactoryAcceptance,
                        status="ImplementedForTest",
                        factoryAcceptance="NotRecorded",
                        productionReleaseAuthorized=false
                    }),
                    warning="این صفحه فقط آمادگی داده‌های پایه در محیط IPPCS را نشان می‌دهد. شمارش رکورد، Build و موفقیت CI جایگزین تأیید کاربران کارخانه، نتایج فیزیکی آزمون، امنیت یا مجوز استقرار نیست."
                });
            }).RequireAuthorization("IpProductionUser");
        return routes;
    }
}
