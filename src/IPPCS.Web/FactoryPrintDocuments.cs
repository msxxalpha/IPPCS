using Microsoft.EntityFrameworkCore;

// Server-sourced, read-only factory printing contracts. Never forge a signature
// or convert a draft/preview into an approved production release.
public sealed record FactoryPrintField(string Label,string Value);
public sealed record FactoryPrintTable(string Title,string[] Columns,string[][] Rows,string? Note=null);
public sealed record FactoryPrintDocument(string Kind,string Title,string Reference,
    string Status,bool OfficiallyApproved,DateTime ExtractedUtc,
    FactoryPrintField[] Fields,FactoryPrintTable[] Tables,string Warning,string SourceId);

public static class FactoryPrintDocuments
{
    private const string FactoryWarning=
        "این سند صرفاً داده داخلی برنامه‌ریزی و کنترل تولید IPPCS را نشان می‌دهد. چاپ سند به‌معنی مجوز تحویل، خروج از انبار، فروش، تأیید مشتری یا پذیرش نهایی استقرار کارخانه نیست.";
    private static string F(DateOnly value)=>PersianPlanningDates.Format(value);
    private static string? F(DateOnly? value)=>value.HasValue?F(value.Value):null;
    private static string Qty(decimal value)=>value.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture);
    private static string Stamp(DateTime? utc)
    {
        if(!utc.HasValue)return "—";
        var at=TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc.Value,DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"));
        return F(DateOnly.FromDateTime(at))+" "+at.ToString("HH:mm");
    }
    private static string Value(string? v)=>string.IsNullOrWhiteSpace(v)?"—":v;

    // Explicit pure status labels prevent a Draft record being printed as approved.
    public static string WorkOrderState(ManufacturingOrderStatus state)=>state switch
    {
        ManufacturingOrderStatus.Draft=>"پیش‌نویس / فاقد مجوز اجرا",
        ManufacturingOrderStatus.Released=>"آزادشده برای اجرای داخلی",
        ManufacturingOrderStatus.InProgress=>"در حال اجرا",
        ManufacturingOrderStatus.OnHold=>"تعلیق / توقف اجرا",
        ManufacturingOrderStatus.Completed=>"تکمیل عملیات",
        ManufacturingOrderStatus.Cancelled=>"لغوشده / غیرمعتبر",
        _=>"وضعیت ناشناخته"
    };
    public static string ApprovalState(bool approved)=>approved?"مصوب داخلی":"پیش‌نویس / فاقد تصویب";

    private static FactoryPrintDocument Document(string kind,string title,string code,
        string status,bool approved,Guid id,FactoryPrintField[] fields,
        FactoryPrintTable[] tables,string? specialWarning=null)
    {
        return new(kind,title,code,status,approved,DateTime.UtcNow,fields,tables,
            FactoryWarning+(specialWarning is null?"":" "+specialWarning),id.ToString("D"));
    }

    public static IEndpointRouteBuilder MapFactoryPrintDocuments(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1/factory-print")
            .RequireAuthorization("IpProductionUser");

        api.MapGet("/{kind}/{id:guid}",async(string kind,Guid id,ProductionDb db,CancellationToken ct)=>{
            FactoryPrintDocument? result=null;
            switch(kind)
            {
                case "order":
                {
                    var o=await db.ManufacturingOrders.AsNoTracking().AsSplitQuery()
                        .Include(x=>x.Operations).ThenInclude(x=>x.Confirmations)
                        .Include(x=>x.Operations).ThenInclude(x=>x.InspectionRequirements)
                        .SingleOrDefaultAsync(x=>x.Id==id,ct);
                    if(o is null)return Results.NotFound();
                    var item=await db.Items.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==o.ItemId,ct);
                    var route=await db.RoutingRevisions.AsNoTracking()
                        .SingleOrDefaultAsync(x=>x.Id==o.RoutingRevisionId,ct);
                    var bom=await db.ManufacturingBomRevisions.AsNoTracking()
                        .SingleOrDefaultAsync(x=>x.Id==o.BomRevisionId,ct);
                    var bomLines=await db.ManufacturingBomLines.AsNoTracking()
                        .Where(x=>x.RevisionId==o.BomRevisionId)
                        .OrderBy(x=>x.Sequence).ThenBy(x=>x.AlternativePriority).ToListAsync(ct);
                    var materialAllocations=await db.Set<ManufacturingOrderMaterialAllocation>().AsNoTracking()
                        .Where(x=>x.OrderId==o.Id).ToListAsync(ct);
                    var materialUsage=await db.Set<ManufacturingOrderMaterialUsage>().AsNoTracking()
                        .Where(x=>x.OrderId==o.Id).OrderBy(x=>x.OccurredUtc).ThenBy(x=>x.Id).ToListAsync(ct);
                    var materialItemIds=bomLines.Select(x=>x.ComponentItemId)
                        .Concat(materialUsage.Select(x=>x.ComponentItemId)).Distinct().ToArray();
                    var materialItems=await db.Items.AsNoTracking().Where(x=>materialItemIds.Contains(x.Id))
                        .ToDictionaryAsync(x=>x.Id,ct);
                    var allocationByLine=materialAllocations.ToDictionary(x=>x.BomLineId);
                    var usageByLine=materialUsage.GroupBy(x=>x.BomLineId)
                        .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Quantity));
                    var ids=o.Operations.Select(x=>x.WorkCenterId).Distinct().ToArray();
                    var centers=await db.WorkCenters.AsNoTracking().Where(x=>ids.Contains(x.Id))
                        .ToDictionaryAsync(x=>x.Id,ct);
                    var ops=o.Operations.OrderBy(x=>x.Sequence).ToArray();
                    var lots=ops.SelectMany(x=>x.Confirmations.Select(c=>new{x.Sequence,c}))
                        .OrderBy(x=>x.Sequence).ThenBy(x=>x.c.OccurredUtc).ToArray();
                    var requirements=ops.SelectMany(x=>x.InspectionRequirements.Select(q=>new{x.Sequence,q}))
                        .OrderBy(x=>x.Sequence).ThenBy(x=>x.q.Code).ToArray();
                    result=Document(kind,"برگه دستور ساخت و پیگیری عملیات",o.Number,
                        WorkOrderState(o.Status),o.Status is ManufacturingOrderStatus.Released
                            or ManufacturingOrderStatus.InProgress or ManufacturingOrderStatus.Completed,
                        o.Id,[
                        new("کد محصول",Value(item?.Code)),new("شرح محصول",Value(item?.Name)),
                        new("نوع ساخت",o.Kind==ManufacturingOrderKind.FinalAssembly?"مونتاژ نهایی":"نیمه‌ساخته"),
                        new("مقدار برنامه",Qty(o.PlannedQuantity)),
                        new("شروع برنامه",F(o.PlannedStart)),new("موعد",F(o.DueOn)),
                        new("نسخه Routing",Value(route?.RevisionCode)),
                        new("نسخه BOM",Value(bom?.Revision)),
                        new("تاریخ آزادسازی",Stamp(o.ReleasedUtc)),
                        new("تاریخ تکمیل",Stamp(o.CompletedUtc)),
                        new("علت تعلیق",Value(o.HoldReason))
                    ],[
                        new("مسیر ساخت و گزارش عملکرد عملیات",
                            ["ردیف","عملیات","مرکز کاری","آماده‌سازی / دقیقه","زمان واحد / دقیقه","سالم","ضایعات","بازکاری","کیفیت معلق","وضعیت"],
                            ops.Select(x=>new[]{
                                x.Sequence.ToString(),x.Name,
                                centers.GetValueOrDefault(x.WorkCenterId)?.Code??"نامشخص",
                                Qty(x.SetupMinutes),Qty(x.RunMinutesPerUnit),
                                Qty(x.GoodQuantity),Qty(x.ScrapQuantity),Qty(x.ReworkQuantity),
                                Qty(x.QualityPendingQuantity),
                                x.IsComplete?"تکمیل":"باز"
                            }).ToArray()),
                        new("شواهد ثبت تولید و کد بچ",
                            ["عملیات","بچ","سالم","ضایعات","بازکاری","ثبت‌شده / تهران"],
                            lots.Select(x=>new[]{
                                x.Sequence.ToString(),Value(x.c.LotCode),
                                Qty(x.c.GoodQuantity),Qty(x.c.ScrapQuantity),Qty(x.c.ReworkQuantity),
                                Stamp(x.c.OccurredUtc)
                            }).ToArray(),
                            "این ارقام تأییدهای عملیاتی ثبت‌شده هستند، نه موجودی انبار."),
                        new("مواد اجباری و ترکیب آلترناتیوهای دستور",
                            ["ردیف","گروه","کد قطعه","شرح قطعه","واحد","مصرف/محصول","ضایعات ٪","سهم تولید ٪","مقدار برنامه","مصرف واقعی"],
                            bomLines.Select(line=>{
                                var alternative=!string.IsNullOrWhiteSpace(line.AlternativeGroupCode);
                                var allocation=allocationByLine.GetValueOrDefault(line.Id);
                                var groupLines=alternative?bomLines.Where(x=>string.Equals(
                                    x.AlternativeGroupCode,line.AlternativeGroupCode,StringComparison.OrdinalIgnoreCase)).ToArray():Array.Empty<ManufacturingBomLine>();
                                var hasExplicit=alternative&&materialAllocations.Any(x=>groupLines.Any(g=>g.Id==x.BomLineId));
                                var share=alternative?(allocation?.SharePercent??(hasExplicit?0m:line.IsDefaultAlternative?100m:0m)):0m;
                                var planned=alternative?(allocation?.PlannedQuantity??(!hasExplicit&&line.IsDefaultAlternative
                                    ?BomAlternativeExecution.CalculatePlannedComponentQuantity(o.PlannedQuantity,line):0m))
                                    :BomAlternativeExecution.CalculatePlannedComponentQuantity(o.PlannedQuantity,line);
                                return new[]{
                                    line.Sequence.ToString(),alternative?Value(line.AlternativeGroupCode):"اجباری",
                                    Value(materialItems.GetValueOrDefault(line.ComponentItemId)?.Code),
                                    Value(materialItems.GetValueOrDefault(line.ComponentItemId)?.Name),
                                    Value(materialItems.GetValueOrDefault(line.ComponentItemId)?.Unit),
                                    Qty(line.QuantityPer),Qty(line.ScrapPercent),
                                    alternative?Qty(share):"—",Qty(planned),Qty(usageByLine.GetValueOrDefault(line.Id))
                                };
                            }).ToArray(),
                            "سهم آلترناتیو درصدی از مقدار تولید برنامه‌ریزی‌شده است؛ قطعات هر گروه هم‌نیاز و غیرتجمعی‌اند. این گزارش تراکنش انبار نیست."),
                        new("سوابق شارژ و مصرف واقعی مواد",
                            ["تاریخ / ساعت","گروه","کد قطعه","مقدار","بچ قطعه","ثبت‌کننده","شرح / علت"],
                            materialUsage.Select(x=>new[]{
                                Stamp(x.OccurredUtc),
                                Value(bomLines.SingleOrDefault(l=>l.Id==x.BomLineId)?.AlternativeGroupCode) is var g&&g!="—"?g:"اجباری",
                                Value(materialItems.GetValueOrDefault(x.ComponentItemId)?.Code),
                                Qty(x.Quantity),Value(x.LotCode),Value(x.RecordedBy),Value(x.Note)
                            }).ToArray(),
                            "مصرف واقعی داخلی IPPCS با شناسه بچ و ثبت‌کننده؛ بدون ثبت حواله یا کاهش موجودی انبار."),
                        new("کنترل‌های بازرسی نسخه منجمد دستور",
                            ["عملیات","مشخصه","شرح","حداقل","حداکثر","واحد","اجباری","آخرین وضعیت"],
                            requirements.Select(x=>new[]{
                                x.Sequence.ToString(),x.q.Code,x.q.Description,
                                x.q.Minimum.HasValue?Qty(x.q.Minimum.Value):"—",
                                x.q.Maximum.HasValue?Qty(x.q.Maximum.Value):"—",
                                x.q.Unit,x.q.Mandatory?"بله":"خیر",
                                x.q.LastPassed==true?"قبول":x.q.LastPassed==false?"مردود":"ثبت نشده"
                            }).ToArray(),
                            "آخرین نتیجه هر مشخصه جایگزین شواهد نمونه‌برداری مصوب یا MSA نیست.")
                    ],"رکوردهای نسخه BOM/Routing در زمان صدور دستور تثبیت شده‌اند؛ تغییر سند چاپی هیچ تغییری در دستور اصلی ایجاد نمی‌کند.");
                    break;
                }
                case "mps":
                {
                    var m=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                        .SingleOrDefaultAsync(x=>x.Id==id,ct);
                    if(m is null)return Results.NotFound();
                    var itemIds=m.Lines.Select(x=>x.ItemId).Distinct().ToArray();
                    var items=await db.Items.AsNoTracking().Where(x=>itemIds.Contains(x.Id))
                        .ToDictionaryAsync(x=>x.Id,ct);
                    var lines=m.Lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ToArray();
                    result=Document(kind,"برنامه اصلی تولید (MPS)",m.Code,
                        ApprovalState(m.State==PlanningApproval.Approved),
                        m.State==PlanningApproval.Approved,m.Id,[
                            new("کد نسخه",m.Code),new("وضعیت نسخه",ApprovalState(m.State==PlanningApproval.Approved)),
                            new("تاریخ ایجاد",Stamp(m.CreatedUtc)),
                            new("تعداد ردیف",lines.Length.ToString()),
                            new("جمع مقادیر ردیف‌ها",Qty(lines.Sum(x=>x.Quantity)))
                        ],[
                            new("ردیف‌های برنامه اصلی تولید",
                                ["ردیف","محصول","شرح","تاریخ نیاز شمسی","مقدار برنامه","مرجع نیاز داخلی"],
                                lines.Select((x,i)=>new[]{
                                    (i+1).ToString(),items.GetValueOrDefault(x.ItemId)?.Code??"نامشخص",
                                    items.GetValueOrDefault(x.ItemId)?.Name??"نامشخص",
                                    F(x.NeededOn),Qty(x.Quantity),Value(x.Reference)
                                }).ToArray(),
                                "جمع مقادیر ممکن است شامل واحدهای متفاوت باشد؛ ارزش مالی یا تعداد قابل تحویل تلقی نشود.")
                        ],"این برنامه منبع داخلی نیاز است و تأیید MPS به‌تنهایی به معنی تأمین قطعه، زمان‌بندی قطعی یا مجوز آزادسازی دستور نیست.");
                    break;
                }
                case "plan":
                {
                    var p=await db.ProductionPlans.AsNoTracking()
                        .SingleOrDefaultAsync(x=>x.Id==id,ct);
                    if(p is null)return Results.NotFound();
                    var item=await db.Items.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==p.ItemId,ct);
                    result=Document(kind,"برگه برنامه مقدماتی تولید",p.Id.ToString("D"),
                        "پیش‌نمایش / غیرمصوب",false,p.Id,[
                            new("کد محصول",Value(item?.Code)),new("شرح",Value(item?.Name)),
                            new("مقدار",Qty(p.Quantity)),new("موعد برنامه",Stamp(p.DueDateUtc)),
                            new("زمان ایجاد",Stamp(p.CreatedAtUtc))
                        ],[],"این فرم ثبت مقدماتی تولید است و جایگزین نسخه مصوب MPS نیست.");
                    break;
                }
                case "bom":
                {
                    var b=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                        .SingleOrDefaultAsync(x=>x.Id==id,ct);
                    if(b is null)return Results.NotFound();
                    var ids=b.Lines.Select(x=>x.ComponentItemId).Append(b.ParentItemId).Distinct().ToArray();
                    var items=await db.Items.AsNoTracking().Where(x=>ids.Contains(x.Id))
                        .ToDictionaryAsync(x=>x.Id,ct);
                    result=Document(kind,"صورت ساختار مواد تولید (BOM)",b.Revision,
                        ApprovalState(b.State==ManufacturingRevisionState.Approved),
                        b.State==ManufacturingRevisionState.Approved,b.Id,[
                            new("محصول",Value(items.GetValueOrDefault(b.ParentItemId)?.Code)),
                            new("شرح محصول",Value(items.GetValueOrDefault(b.ParentItemId)?.Name)),
                            new("نسخه BOM",b.Revision),new("شروع اعتبار",F(b.ValidFrom)),
                            new("پایان اعتبار",F(b.ValidUntil)??"نامحدود")
                        ],[
                            new("اجزاء اجباری و گزینه‌های جایگزین BOM",
                                ["ردیف","گروه آلترناتیو","کد جزء","شرح جزء","واحد","مصرف پایه","ضایعات استاندارد ٪","پیش‌فرض","اولویت"],
                                b.Lines.OrderBy(x=>x.Sequence).ThenBy(x=>x.AlternativePriority).Select(x=>new[]{
                                    x.Sequence.ToString(),
                                    string.IsNullOrWhiteSpace(x.AlternativeGroupCode)?"اجباری":x.AlternativeGroupCode,
                                    Value(items.GetValueOrDefault(x.ComponentItemId)?.Code),
                                    Value(items.GetValueOrDefault(x.ComponentItemId)?.Name),
                                    Value(items.GetValueOrDefault(x.ComponentItemId)?.Unit),
                                    Qty(x.QuantityPer),Qty(x.ScrapPercent),
                                    string.IsNullOrWhiteSpace(x.AlternativeGroupCode)?"—":x.IsDefaultAlternative?"بله":"خیر",
                                    string.IsNullOrWhiteSpace(x.AlternativeGroupCode)?"—":x.AlternativePriority.ToString()
                                }).ToArray(),
                                "گزینه‌های یک گروه آلترناتیو نیازهای جایگزین یکدیگرند و هم‌زمان به صورت تجمعی محاسبه نمی‌شوند. این سند حواله انبار نیست.")
                        ]);
                    break;
                }
                case "routing":
                {
                    var route=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                        .SingleOrDefaultAsync(x=>x.Id==id,ct);
                    if(route is null)return Results.NotFound();
                    var item=await db.Items.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==route.ItemId,ct);
                    var centerIds=route.Operations.Select(x=>x.WorkCenterId).Distinct().ToArray();
                    var centers=await db.WorkCenters.AsNoTracking().Where(x=>centerIds.Contains(x.Id))
                        .ToDictionaryAsync(x=>x.Id,ct);
                    result=Document(kind,"برگه مسیر و عملیات ساخت",route.RevisionCode,
                        route.State==RoutingState.Draft?"پیش‌نویس":route.State==RoutingState.Approved?
                            "مصوب داخلی":"منسوخ / غیرمعتبر",
                        route.State==RoutingState.Approved,route.Id,[
                            new("محصول",Value(item?.Code)),new("شرح محصول",Value(item?.Name)),
                            new("نسخه مسیر",route.RevisionCode),
                            new("شروع اعتبار",F(route.EffectiveFrom)),
                            new("پایان اعتبار",F(route.EffectiveTo)??"نامحدود")
                        ],[
                            new("ترتیب اجرای عملیات و مراکز کاری",
                                ["ترتیب","عملیات","مرکز کاری","Setup / دقیقه","زمان واحد / دقیقه","بازده هدف ٪"],
                                route.Operations.OrderBy(x=>x.Sequence).Select(x=>new[]{
                                    x.Sequence.ToString(),x.Name,
                                    centers.GetValueOrDefault(x.WorkCenterId)?.Code??"نامشخص",
                                    Qty(x.SetupMinutes),Qty(x.RunMinutesPerUnit),Qty(x.YieldPercent)
                                }).ToArray())
                        ],"این صورت مسیر ساخت، ثبت حضور نفرات یا رزرو واقعی ماشین نیست.");
                    break;
                }
                case "control-plan":
                {
                    var cp=await db.ManufacturingControlPlans.AsNoTracking()
                        .Include(x=>x.Characteristics).SingleOrDefaultAsync(x=>x.Id==id,ct);
                    if(cp is null)return Results.NotFound();
                    var route=await db.RoutingRevisions.AsNoTracking()
                        .SingleOrDefaultAsync(x=>x.Id==cp.RoutingRevisionId,ct);
                    var item=route is null?null:await db.Items.AsNoTracking()
                        .SingleOrDefaultAsync(x=>x.Id==route.ItemId,ct);
                    result=Document(kind,"برگه کنترل پلن فرایند",cp.Code,
                        ApprovalState(cp.State==ControlPlanState.Approved),
                        cp.State==ControlPlanState.Approved,cp.Id,[
                            new("کد کنترل پلن",cp.Code),new("محصول",Value(item?.Code)),
                            new("نسخه مسیر مرتبط",Value(route?.RevisionCode)),
                            new("شروع اعتبار",F(cp.ValidFrom)),
                            new("پایان اعتبار",F(cp.ValidUntil)??"نامحدود"),
                            new("تصویب‌کننده داخلی",Value(cp.ApprovedBy)),
                            new("زمان تصویب",Stamp(cp.ApprovedUtc))
                        ],[
                            new("فهرست مشخصه‌ها و حدود کنترل",
                                ["مرحله","کد مشخصه","شرح کنترل","حداقل مشخصات","حداکثر مشخصات","واحد","الزامی"],
                                cp.Characteristics.OrderBy(x=>x.OperationSequence).ThenBy(x=>x.Code)
                                .Select(x=>new[]{
                                    x.OperationSequence.ToString(),x.Code,x.Description,
                                    x.Minimum.HasValue?Qty(x.Minimum.Value):"—",
                                    x.Maximum.HasValue?Qty(x.Maximum.Value):"—",
                                    x.Unit,x.Mandatory?"بله":"خیر"
                                }).ToArray(),
                                "بسامد نمونه‌برداری، روش واکنش و نتایج MSA باید طبق مدارک مصوب کیفیت تکمیل شوند.")
                        ],"وجود این مشخصه‌ها بدون مدرک نمونه‌برداری مصوب و MSA مجوز ترخیص یا پایان آزمون فیزیکی نیست.");
                    break;
                }
                default: return Results.NotFound();
            }
            if(result.Tables.Any(x=>x.Rows.Length>800))
                return Results.Conflict(new{message="سند بیش از حد مجاز ردیف دارد؛ چاپ ناقص ممنوع است."});
            return Results.Ok(result);
        });
        return routes;
    }
}
