using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

// IPPCS-owned reference lists. Not warehouse, procurement, sales, HR or AD.
// Stable codes are reporting keys; changing a label never rewrites old records.
public sealed class FactoryMasterOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed record FactoryMasterOptionInput(string Kind,string Code,string Label,int SortOrder=0);
public sealed record FactoryMasterOptionChangeInput(string Label,bool IsActive,int SortOrder);

public static class FactoryMasterData
{
    public const string DeviationCategory = "DeviationCategory";
    public const string ActionOwner = "ActionOwner";
    public const string RootCauseCategory = "RootCauseCategory";
    public const string DowntimeReason = "DowntimeReason";
    public const string QualityHoldReason = "QualityHoldReason";

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        DeviationCategory,ActionOwner,RootCauseCategory,DowntimeReason,QualityHoldReason
    };
    private static readonly Regex CodePattern=new(
        "^[A-Z0-9][A-Z0-9_-]{1,39}$",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);

    public static bool ValidKind(string? kind)=>kind is not null&&Kinds.Contains(kind);
    public static string NormalizeCode(string? code)=>
        PersianPlanningDates.NormalizeDigits(code?.Trim()??"").ToUpperInvariant();
    public static bool ValidCode(string kind,string code)
    {
        if(!ValidKind(kind))return false;
        if(kind==DeviationCategory)
            return int.TryParse(code,out var n)&&n is >=0 and <=6&&code==n.ToString();
        return CodePattern.IsMatch(code);
    }
    public static bool ValidLabel(string? label)=>
        !string.IsNullOrWhiteSpace(label)&&label.Trim().Length is >=2 and <=100&&
        !label.Contains('|')&&!label.Contains('\r')&&!label.Contains('\n');
    public static bool ValidSort(int sort)=>sort is >=0 and <=10000;

    public static async Task<FactoryMasterOption?> Active(
        ProductionDb db,string kind,string? input,CancellationToken ct)
    {
        var code=NormalizeCode(input);
        if(!ValidCode(kind,code))return null;
        return await db.FactoryMasterOptions.AsNoTracking()
            .SingleOrDefaultAsync(x=>x.Kind==kind&&x.Code==code&&x.IsActive,ct);
    }

    // Retain legacy free-text observations, but prefix new categorized entries
    // with a machine-readable stable code for historical reports without
    // rewriting the current confirmed-production audit schema.
    public static async Task<string?> ResolveReason(ProductionDb db,string kind,
        string? code,string? detail,int maxLength,CancellationToken ct)
    {
        var explanation=detail?.Trim()??"";
        if(string.IsNullOrWhiteSpace(code))
            return explanation.Length is >0 and <=500&&explanation.Length<=maxLength?
                explanation:null;
        var option=await Active(db,kind,code,ct);
        if(option is null||!ValidLabel(option.Label)||
            explanation.Contains('|')||explanation.Contains('\r')||
            explanation.Contains('\n'))return null;
        var value="["+option.Code+"] "+option.Label+
            (explanation.Length>0?" — "+explanation:"");
        return value.Length<=maxLength?value:null;
    }

    public static IEndpointRouteBuilder MapFactoryMasterData(this IEndpointRouteBuilder routes)
    {
        var read=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        // Report dimensions remain stable when administrators rename or
        // deactivate catalog labels. No ERP/maintenance system is consulted.
        read.MapGet("/factory-master-options/production-reason-summary",
            async(string kind,string fromJalali,string throughJalali,ProductionDb db,CancellationToken ct)=>{
                if(kind!=DowntimeReason&&kind!=QualityHoldReason)
                    return Results.BadRequest(new{message="گزارش فقط برای علل توقف یا قرنطینه معتبر است."});
                if(!PersianPlanningDates.TryParse(fromJalali,out var from)||
                    !PersianPlanningDates.TryParse(throughJalali,out var through)||
                    through<from||through.DayNumber-from.DayNumber>366)
                    return Results.BadRequest(new{message="بازه تاریخ شمسی حداکثر ۳۶۶ روز و معتبر لازم است."});
                var tz=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
                var begin=TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue),tz);
                var end=TimeZoneInfo.ConvertTimeToUtc(through.AddDays(1).ToDateTime(TimeOnly.MinValue),tz);
                var options=await db.FactoryMasterOptions.AsNoTracking()
                    .Where(x=>x.Kind==kind).ToListAsync(ct);
                var summaries=new Dictionary<string,(int Records,decimal Quantity)>(StringComparer.Ordinal);
                void Add(string? reason,decimal quantity)
                {
                    var code=reason is not null&&reason.StartsWith("[",StringComparison.Ordinal)?
                        reason[1..].Split(']',2)[0]:"";
                    if(!options.Any(x=>x.Code==code))code="LEGACY_UNCLASSIFIED";
                    var old=summaries.GetValueOrDefault(code);
                    summaries[code]=(old.Records+1,old.Quantity+quantity);
                }
                if(kind==QualityHoldReason)
                {
                    var rows=await db.ManufacturingQualityHolds.AsNoTracking()
                        .Where(x=>x.RaisedUtc>=begin&&x.RaisedUtc<end)
                        .Select(x=>new{x.Reason,x.Quantity}).ToListAsync(ct);
                    foreach(var r in rows)Add(r.Reason,r.Quantity);
                }
                else
                {
                    var historic=await db.ManufacturingDowntimes.AsNoTracking()
                        .Where(x=>x.OccurredUtc>=begin&&x.OccurredUtc<end)
                        .Select(x=>new{x.Reason,x.DurationMinutes}).ToListAsync(ct);
                    foreach(var r in historic)Add(r.Reason,r.DurationMinutes);
                    // Count the *start events*, not ends, to avoid double-counting.
                    var timed=await db.ManufacturingOrderAudits.AsNoTracking()
                        .Where(x=>x.Action==FactoryTimedDowntime.StartAction&&
                            x.AtUtc>=begin&&x.AtUtc<end)
                        .Select(x=>x.Details).ToListAsync(ct);
                    foreach(var detail in timed)
                    {
                        var parts=detail.Split('|');
                        if(parts.Length!=4)return Results.Conflict(new{
                            message="سابقه شروع توقف زمان‌دار دارای قالب نامعتبر است."});
                        Add(parts[3],0);
                    }
                }
                var result=summaries.OrderBy(x=>x.Key).Select(x=>new{
                    code=x.Key,label=options.FirstOrDefault(o=>o.Code==x.Key)?.Label??
                        "بدون کد طبقه‌بندی (داده تاریخی)",
                    active=options.FirstOrDefault(o=>o.Code==x.Key)?.IsActive??false,
                    records=x.Value.Records,measuredQuantity=x.Value.Quantity
                }).ToArray();
                return Results.Ok(new{kind,fromJalali,throughJalali,
                    metric=kind==QualityHoldReason?"QuarantinedQuantity":
                        "LegacyDowntimeMinutes;TimedStopEventCountOnly",
                    rows=result,
                    warning=kind==DowntimeReason?
                        "توقفات زمان‌دار به تعداد رخداد شمارش می‌شوند، نه دقیقه؛ برای زمان واقعی از تطبیق OEE استفاده کنید.":
                        "مقادیر قرنطینه منعکس‌کننده رخداد ثبت‌شده هستند، نه مانده موجودی انبار."});
            });

        read.MapGet("/factory-master-options",async(string kind,bool? includeInactive,
            ProductionDb db,CancellationToken ct)=>{
            if(!ValidKind(kind))return Results.BadRequest(new{message="نوع فهرست اطلاعات پایه معتبر نیست."});
            var rows=await db.FactoryMasterOptions.AsNoTracking()
                .Where(x=>x.Kind==kind&&(includeInactive==true||x.IsActive))
                .OrderBy(x=>x.SortOrder).ThenBy(x=>x.Code)
                .Take(1501).ToListAsync(ct);
            if(rows.Count>1500)return Results.Conflict(new{message="فهرست بیش از ۱۵۰۰ گزینه دارد؛ نمایش ناقص ممنوع است."});
            return Results.Ok(rows);
        });
        var admin=routes.MapGroup("/api/v1/factory-master-options")
            .RequireAuthorization(new AuthorizeAttribute{Roles="Administrator"});
        admin.MapPost("",async(FactoryMasterOptionInput input,ProductionDb db,
            CancellationToken ct)=>{
            var kind=input.Kind?.Trim()??"";
            var code=NormalizeCode(input.Code);
            if(!ValidCode(kind,code)||!ValidLabel(input.Label)||!ValidSort(input.SortOrder))
                return Results.BadRequest(new{message="نوع، کد پایدار، عنوان یا ترتیب گزینه معتبر نیست."});
            if(await db.FactoryMasterOptions.AnyAsync(x=>x.Kind==kind&&x.Code==code,ct))
                return Results.Conflict(new{message="این کد در فهرست اطلاعات پایه قبلاً ثبت شده است."});
            var row=new FactoryMasterOption{Kind=kind,Code=code,
                Label=input.Label.Trim(),SortOrder=input.SortOrder};
            db.FactoryMasterOptions.Add(row);
            try{await db.SaveChangesAsync(ct);}
            catch(DbUpdateException){return Results.Conflict(new{message="کد گزینه هم‌زمان توسط کاربر دیگری ثبت شده است."});}
            return Results.Created("/api/v1/factory-master-options?kind="+kind,row);
        });
        admin.MapPut("/{id:guid}",async(Guid id,FactoryMasterOptionChangeInput input,
            ProductionDb db,CancellationToken ct)=>{
            if(!ValidLabel(input.Label)||!ValidSort(input.SortOrder))
                return Results.BadRequest(new{message="عنوان یا ترتیب نمایش معتبر نیست."});
            var row=await db.FactoryMasterOptions.SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(row is null)return Results.NotFound();
            row.Label=input.Label.Trim();
            row.SortOrder=input.SortOrder;
            row.IsActive=input.IsActive;
            row.UpdatedUtc=DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(row);
        });
        return routes;
    }
}
