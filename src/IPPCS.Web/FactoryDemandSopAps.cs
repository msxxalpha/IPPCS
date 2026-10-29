using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

// Standalone planning inputs: no sales module, no sales/warehouse/purchasing/HR integration.
public sealed class FactoryDemand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = "";
    public Guid ItemId { get; set; }
    public DateOnly DueOn { get; set; }
    public decimal Quantity { get; set; }
    public string Category { get; set; } = "Forecast";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class FactorySopReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public DateOnly From { get; set; }
    public DateOnly Through { get; set; }
    public decimal DemandQuantity { get; set; }
    public decimal RoughCutMinutes { get; set; }
    public decimal AvailableMinutes { get; set; }
    public decimal OverloadMinutes { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTime CalculatedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class FactoryApsRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MpsVersionId { get; set; }
    public DateOnly StartOn { get; set; }
    public int HorizonDays { get; set; }
    public string Algorithm { get; set; } = "FINITE-GREEDY-V2-NIGHT";
    public string InputJson { get; set; } = "";
    public string OutputJson { get; set; } = "";
    public DateTime CalculatedUtc { get; set; } = DateTime.UtcNow;
}
public record FactoryDemandInput(Guid ItemId,string Reference,string DueOnJalali,decimal Quantity,string Category);
public record FactorySopInput(string Code,string FromJalali,string ThroughJalali);
public record FactoryApsUnavailableDay(Guid WorkCenterId,string DateJalali,string Reason,string? ReasonCode=null);
public record FactoryApsUnavailableSlot(Guid WorkCenterId,string StartJalali,string StartTime,
    string EndJalali,string EndTime,string Reason,string? ReasonCode=null);
public record FactoryApsSharedTool(Guid WorkCenterId,string ToolGroupCode);
public record FactoryApsInput(Guid MpsVersionId,string StartJalali,int HorizonDays,
    decimal CapacityMultiplier=1m,List<FactoryApsUnavailableDay>? UnavailableDays=null,
    List<FactoryApsUnavailableSlot>? UnavailableSlots=null,
    List<FactoryApsSharedTool>? SharedTools=null);
public record ApsTask(Guid DemandLineId,Guid ItemId,DateOnly DueOn,decimal Quantity,Guid RoutingRevisionId,
    int OperationSequence,Guid WorkCenterId,decimal DurationMinutes,string OperationName);
public record ApsAllocation(Guid ItemId,Guid WorkCenterId,int Sequence,decimal Quantity,
    DateOnly DueOn,DateOnly StartDate,TimeOnly StartsAt,DateOnly FinishDate,
    TimeOnly FinishesAt,decimal DurationMinutes,string OperationName,bool Late,Guid DemandLineId=default);
public record ApsUnscheduled(Guid ItemId,int Sequence,string Reason,Guid DemandLineId=default);
public record ApsResult(IReadOnlyList<ApsAllocation> Allocations,
    IReadOnlyList<ApsUnscheduled> Unscheduled,decimal TotalLateOperations);

/// <summary>
/// Finite, non-preemptive weekly-shift preview, including shifts ending on the
/// following Gregorian/solar-local calendar day. Center intervals are blocked
/// on every touched day and operation precedence is enforced. External HR,
/// maintenance calendars, alternate-tool optimization and global objectives
/// are not part of this internal planning preview.
/// </summary>
public static class FiniteFactoryScheduler
{
    public static ApsResult Schedule(IReadOnlyList<ApsTask> tasks,
        IReadOnlyList<WorkShift> shifts,DateOnly start,int horizonDays,decimal multiplier=1,
        IReadOnlySet<(Guid WorkCenterId,DateOnly Date)>? unavailableDays=null,
        IReadOnlyDictionary<Guid,DateOnly>? earliestStartDates=null,
        IReadOnlyDictionary<Guid,IReadOnlyCollection<Guid>>? prerequisites=null,
        IReadOnlyList<WorkCenterOccupiedInterval>? occupiedIntervals=null,
        IReadOnlyDictionary<Guid,string>? sharedToolGroups=null)
    {
        if(horizonDays is <1 or >90 || multiplier<=0 || multiplier>2)
            throw new ArgumentException("افق برنامه‌ریزی یا ضریب ظرفیت نامعتبر است.");
        var result=new List<ApsAllocation>();
        var missing=new List<ApsUnscheduled>();
        var occupied=new Dictionary<(Guid,DateOnly),List<(int start,int end)>>();
        // Group reservations include recorded machine clocks, planned machine
        // slots and scenario downtime, not only newly scheduled operations.
        var toolBusy=new Dictionary<string,List<(DateTime start,DateTime end)>>(
            StringComparer.Ordinal);
        var initialOccupied=(occupiedIntervals??[]).ToArray();
        foreach(var evidence in initialOccupied)
        {
            if(sharedToolGroups is null||
                !sharedToolGroups.TryGetValue(evidence.WorkCenterId,out var tool))continue;
            if(!toolBusy.TryGetValue(tool,out var windows))
                toolBusy[tool]=windows=new();
            windows.Add((evidence.StartLocal,evidence.EndLocal));
        }
        foreach(var windows in toolBusy.Values)
        {
            var ordered=windows.OrderBy(x=>x.start).ToArray();
            for(int n=1;n<ordered.Length;n++)
                if(ordered[n].start<ordered[n-1].end)
                    throw new InvalidOperationException(
                        "اشغال واقعی/سناریویی ابزار مشترک دارای تداخل است و قابل زمان‌بندی نیست.");
        }
        // Recorded actual center occupancy is measured in Tehran-local wall time.
        // Half-open intervals [start,end) are rounded outward to whole minutes.
        var horizonEnd=start.AddDays(horizonDays);
        foreach(var record in initialOccupied)
        {
            if(record.WorkCenterId==Guid.Empty || record.StartLocal.Kind!=DateTimeKind.Unspecified ||
               record.EndLocal.Kind!=DateTimeKind.Unspecified ||
               record.EndLocal<=record.StartLocal)
                throw new ArgumentException("بازه اشغال مرکز کاری نامعتبر است.");
            var first=DateOnly.FromDateTime(record.StartLocal);
            if(first<start)first=start;
            for(var day=first;day<horizonEnd &&
                day.ToDateTime(TimeOnly.MinValue)<record.EndLocal;day=day.AddDays(1))
            {
                var midnight=day.ToDateTime(TimeOnly.MinValue);
                var from=(int)Math.Floor(Math.Max(0,(record.StartLocal-midnight).TotalMinutes));
                var through=(int)Math.Ceiling(Math.Min(1440,(record.EndLocal-midnight).TotalMinutes));
                if(from>=through)continue;
                var key=(record.WorkCenterId,day);
                if(!occupied.TryGetValue(key,out var windows))
                    occupied[key]=windows=new();
                windows.Add((from,through));
            }
        }
        // Topological lot order precedes due-date order: a manufactured component must finish
        // before its parent lot consumes it. Every lot remains non-preemptive per operation.
        // Every allocation, including a shift that runs across midnight, owns
        // its occupied minutes in each local calendar day. This prevents a
        // next-day operation from silently reusing the overnight center.
        DateTime? FindFree(Guid center,DateTime from,DateTime latest,int length)
        {
            var current=from;
            for(var guard=0;guard<1442&&current.AddMinutes(length)<=latest;guard++)
            {
                var finish=current.AddMinutes(length);
                DateTime? next=null;
                for(var localDay=DateOnly.FromDateTime(current);
                    localDay<=DateOnly.FromDateTime(finish);localDay=localDay.AddDays(1))
                {
                    if(!occupied.TryGetValue((center,localDay),out var blocks))continue;
                    var midnight=localDay.ToDateTime(TimeOnly.MinValue);
                    foreach(var block in blocks)
                    {
                        var a=midnight.AddMinutes(block.start);
                        var b=midnight.AddMinutes(block.end);
                        if(current<b&&a<finish&&(next is null||b<next))next=b;
                    }
                }
                if(sharedToolGroups is not null &&
                    sharedToolGroups.TryGetValue(center,out var groupCode) &&
                    toolBusy.TryGetValue(groupCode,out var groupWindows))
                {
                    foreach(var span in groupWindows)
                        if(current<span.end&&span.start<finish&&
                            (next is null||span.end<next))next=span.end;
                }
                if(next is null)return current;
                if(next<=current)throw new InvalidOperationException("تاریخچه رزرو زمانی مرکز کاری نامعتبر است.");
                current=next.Value;
            }
            return null;
        }
        void Occupy(Guid center,DateTime from,DateTime until)
        {
            if(sharedToolGroups is not null &&
                sharedToolGroups.TryGetValue(center,out var groupCode))
            {
                if(!toolBusy.TryGetValue(groupCode,out var list))
                    toolBusy[groupCode]=list=new();
                list.Add((from,until));
            }
            for(var localDay=DateOnly.FromDateTime(from);
                localDay<=DateOnly.FromDateTime(until);localDay=localDay.AddDays(1))
            {
                var midnight=localDay.ToDateTime(TimeOnly.MinValue);
                var a=(int)Math.Floor(Math.Max(0,(from-midnight).TotalMinutes));
                var b=(int)Math.Ceiling(Math.Min(1440,(until-midnight).TotalMinutes));
                if(b<=a)continue;
                var key=(center,localDay);
                if(!occupied.TryGetValue(key,out var entries))
                    occupied[key]=entries=new();
                entries.Add((a,b));
            }
        }
        var byChain=tasks.GroupBy(x=>x.DemandLineId)
            .ToDictionary(g=>g.Key,g=>g.OrderBy(t=>t.OperationSequence).ToArray());
        var remaining=byChain.Keys.ToHashSet();
        var sorted=new List<ApsTask>(tasks.Count);
        while(remaining.Count>0)
        {
            var eligible=remaining.Where(id=>
                prerequisites?.GetValueOrDefault(id)?.All(pre=>!remaining.Contains(pre))!=false)
                .OrderBy(id=>byChain[id][0].DueOn).ThenBy(id=>id).ToArray();
            if(eligible.Length==0)
                throw new ArgumentException("چرخه وابستگی میان بچ‌های پیشنهادی ساخت شناسایی شد.");
            var next=eligible[0];
            sorted.AddRange(byChain[next]);
            remaining.Remove(next);
        }
        var lastSequence=byChain.ToDictionary(x=>x.Key,x=>x.Value[^1].OperationSequence);
        var completedChains=new HashSet<Guid>();
        var chainReady=new Dictionary<Guid,DateTime>();
        var chainFailed=new HashSet<Guid>();
        foreach(var job in sorted)
        {
            if(job.DurationMinutes<=0 || job.DurationMinutes>20000 || job.Quantity<=0)
            {
                missing.Add(new(job.ItemId,job.OperationSequence,"مقدار یا زمان عملیات نامعتبر است.",job.DemandLineId));
                chainFailed.Add(job.DemandLineId);continue;
            }
            if(chainFailed.Contains(job.DemandLineId))
            {
                missing.Add(new(job.ItemId,job.OperationSequence,"عملیات قبلی قابل زمان‌بندی نبود.",job.DemandLineId));continue;
            }
            var release=(earliestStartDates?.GetValueOrDefault(job.DemandLineId)??start)
                .ToDateTime(TimeOnly.MinValue);
            var earliest=chainReady.GetValueOrDefault(job.DemandLineId,release);
            if(prerequisites is not null &&
                prerequisites.TryGetValue(job.DemandLineId,out var dependencies))
            {
                var missingDependencies=dependencies.Where(pre=>!completedChains.Contains(pre)).ToArray();
                if(missingDependencies.Length>0)
                {
                    missing.Add(new(job.ItemId,job.OperationSequence,
                        "بچ نیمه‌ساخته پیش‌نیاز هنوز تکمیل یا قابل زمان‌بندی نیست.",job.DemandLineId));
                    chainFailed.Add(job.DemandLineId);continue;
                }
                foreach(var pre in dependencies)
                    if(chainReady.TryGetValue(pre,out var completedAt) && completedAt>earliest)
                        earliest=completedAt;
            }
            if(release>earliest)earliest=release;
            bool allocated=false;
            for(int d=0;d<horizonDays && !allocated;d++)
            {
                var day=start.AddDays(d);
                if(unavailableDays?.Contains((job.WorkCenterId,day))==true)continue;
                // A shift beginning on the previous date may still have
                // usable capacity after midnight. Never skip its start date
                // merely because the predecessor finished on the next day.
                // The shift-specific earliestInShift check below safely
                // rejects windows that actually end before the dependency.
                foreach(var shift in shifts.Where(x=>x.WorkCenterId==job.WorkCenterId
                    && x.DayOfWeek==(int)day.DayOfWeek).OrderBy(x=>x.StartTime))
                {
                    if(!FactoryShiftCalendar.Valid(shift))continue;
                    var (shiftFrom,physicalEnd)=FactoryShiftCalendar.Window(shift,day);
                    var shiftEnd=FactoryShiftCalendar.EffectiveEnd(shift,day,multiplier);
                    if(shiftEnd>physicalEnd)shiftEnd=physicalEnd;
                    if(shiftEnd>horizonEnd.ToDateTime(TimeOnly.MinValue))
                        shiftEnd=horizonEnd.ToDateTime(TimeOnly.MinValue);
                    var duration=(int)Math.Ceiling(job.DurationMinutes);
                    var earliestInShift=earliest>shiftFrom?earliest:shiftFrom;
                    if(shiftEnd<=earliestInShift||
                        duration>(shiftEnd-earliestInShift).TotalMinutes)continue;
                    // A blackout in tomorrow's local day also blocks the
                    // overnight section, not only a shift whose start is today.
                    if(shiftEnd.Date>day.ToDateTime(TimeOnly.MinValue).Date&&
                        unavailableDays?.Contains((job.WorkCenterId,day.AddDays(1)))==true)
                        shiftEnd=day.AddDays(1).ToDateTime(TimeOnly.MinValue);
                    var position=FindFree(job.WorkCenterId,earliestInShift,shiftEnd,duration);
                    if(position is null)continue;
                    var from=position.Value;var until=from.AddMinutes(duration);
                    Occupy(job.WorkCenterId,from,until);
                    chainReady[job.DemandLineId]=until;
                    result.Add(new ApsAllocation(job.ItemId,job.WorkCenterId,
                        job.OperationSequence,job.Quantity,job.DueOn,DateOnly.FromDateTime(from),
                        TimeOnly.FromDateTime(from),DateOnly.FromDateTime(until),TimeOnly.FromDateTime(until),
                        job.DurationMinutes,job.OperationName,DateOnly.FromDateTime(until)>job.DueOn,
                        job.DemandLineId));
                    if(job.OperationSequence==lastSequence[job.DemandLineId])
                        completedChains.Add(job.DemandLineId);
                    allocated=true;break;
                }
            }
            if(!allocated)
            {
                missing.Add(new(job.ItemId,job.OperationSequence,
                    "عملیات با ظرفیت و شیفت مؤثر موجود، بدون تداخل در افق انتخابی جا نمی‌شود.",job.DemandLineId));
                chainFailed.Add(job.DemandLineId);
            }
        }
        return new(result,missing,result.Count(x=>x.Late));
    }
}

// A scenario-only machine setup/maintenance window: half-open local Tehran
// interval, not an equipment maintenance module or operational clock record.
public static class FactoryApsScenarioSlots
{
    public static bool TryWindow(FactoryApsUnavailableSlot slot,DateOnly from,
        int horizonDays,out WorkCenterOccupiedInterval window)
    {
        window=default!;
        if(horizonDays is <1 or >90||slot.WorkCenterId==Guid.Empty||
            !PersianPlanningDates.TryParse(slot.StartJalali,out var startDay)||
            !PersianPlanningDates.TryParse(slot.EndJalali,out var endDay)||
            !TimeOnly.TryParseExact(PersianPlanningDates.NormalizeDigits(slot.StartTime??""),
                "HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var startTime)||
            !TimeOnly.TryParseExact(PersianPlanningDates.NormalizeDigits(slot.EndTime??""),
                "HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var endTime))
            return false;
        var start=startDay.ToDateTime(startTime);
        var end=endDay.ToDateTime(endTime);
        if(start<from.ToDateTime(TimeOnly.MinValue)||
            end>from.AddDays(horizonDays).ToDateTime(TimeOnly.MinValue)||
            end<=start||end-start>TimeSpan.FromDays(2))return false;
        window=new WorkCenterOccupiedInterval(slot.WorkCenterId,start,end);
        return true;
    }

    public static bool Overlaps(IEnumerable<WorkCenterOccupiedInterval> source)
    {
        foreach(var group in source.GroupBy(x=>x.WorkCenterId))
        {
            var sorted=group.OrderBy(x=>x.StartLocal).ToArray();
            for(int i=1;i<sorted.Length;i++)
                if(sorted[i].StartLocal<sorted[i-1].EndLocal)return true;
        }
        return false;
    }
}

public record FactoryForecastCoverage(int RecordedMonths,IReadOnlyList<string> MissingMonths)
{
    public bool Complete=>MissingMonths.Count==0;
}
public static class FactoryForecasting
{
    private static readonly PersianCalendar Jalali = new();
    // Missing months are UNKNOWN, never an approved zero-demand statement.
    public static FactoryForecastCoverage Coverage(IEnumerable<FactoryDemand> history,
        DateOnly forecastMonth,int lookbackMonths)
    {
        if(lookbackMonths is <2 or >12)
            throw new ArgumentOutOfRangeException(nameof(lookbackMonths));
        var stamp=forecastMonth.ToDateTime(TimeOnly.MinValue);
        var ordinal=Jalali.GetYear(stamp)*12+Jalali.GetMonth(stamp)-1;
        var observed=history.Where(x=>x.Category=="Confirmed")
            .Select(x=>{
                var t=x.DueOn.ToDateTime(TimeOnly.MinValue);
                return Jalali.GetYear(t)*12+Jalali.GetMonth(t)-1;
            }).ToHashSet();
        var missing=Enumerable.Range(0,lookbackMonths)
            .Select(i=>ordinal-lookbackMonths+i)
            .Where(x=>!observed.Contains(x))
            .Select(x=>(x/12).ToString("0000")+"/"+(x%12+1).ToString("00"))
            .ToArray();
        return new(lookbackMonths-missing.Length,missing);
    }
    public static decimal MonthlyMovingAverage(IEnumerable<FactoryDemand> history,DateOnly forecastMonth,
        int lookbackMonths)
    {
        if(lookbackMonths is <2 or >12)throw new ArgumentOutOfRangeException(nameof(lookbackMonths));
        var d=forecastMonth.ToDateTime(TimeOnly.MinValue);
        var baseOrdinal=Jalali.GetYear(d)*12+Jalali.GetMonth(d)-1;
        decimal sum=0;
        foreach(var demand in history.Where(x=>x.Category=="Confirmed"))
        {
            var date=demand.DueOn.ToDateTime(TimeOnly.MinValue);
            var ord=Jalali.GetYear(date)*12+Jalali.GetMonth(date)-1;
            if(ord>=baseOrdinal-lookbackMonths&&ord<baseOrdinal)sum+=demand.Quantity;
        }
        return Math.Round(sum/lookbackMonths,4,MidpointRounding.AwayFromZero);
    }
}

public static class FactoryDemandApsEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<FactoryDemand>().HasIndex(x=>new{x.ItemId,x.DueOn});
        model.Entity<FactoryDemand>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<FactoryDemand>().Property(x=>x.Reference).HasMaxLength(100);
        model.Entity<FactoryDemand>().Property(x=>x.Category).HasMaxLength(32);
        model.Entity<FactoryDemand>().Property(x=>x.Quantity).HasPrecision(18,4);
        model.Entity<FactorySopReview>().HasIndex(x=>x.Code).IsUnique();
        model.Entity<FactorySopReview>().Property(x=>x.Code).HasMaxLength(80);
        model.Entity<FactorySopReview>().Property(x=>x.DemandQuantity).HasPrecision(18,4);
        model.Entity<FactorySopReview>().Property(x=>x.RoughCutMinutes).HasPrecision(18,4);
        model.Entity<FactorySopReview>().Property(x=>x.AvailableMinutes).HasPrecision(18,4);
        model.Entity<FactorySopReview>().Property(x=>x.OverloadMinutes).HasPrecision(18,4);
        model.Entity<FactoryApsRun>().HasIndex(x=>x.CalculatedUtc);
        model.Entity<FactoryApsRun>().HasOne<MpsVersion>().WithMany()
            .HasForeignKey(x=>x.MpsVersionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FactoryApsRun>().Property(x=>x.Algorithm).HasMaxLength(64);
    }

    public static IEndpointRouteBuilder MapDemandAps(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-demand",async(ProductionDb db,CancellationToken ct)=>{
            var rows=await db.FactoryDemands.AsNoTracking()
                .OrderBy(x=>x.DueOn).Take(600).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.Id,x.ItemId,x.Reference,x.Quantity,x.Category,
                dueOnJalali=PersianPlanningDates.Format(x.DueOn)}));
        });
        api.MapGet("/factory-demand/forecast/{itemId:guid}",
            async(Guid itemId,string fromJalali,int lookback,int months,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(fromJalali,out var from)
                ||lookback is <2 or >12||months is <1 or >6)
                return Results.BadRequest(new{message="ماه مبنای شمسی و تعداد دوره‌ها معتبر نیست."});
            if(!await db.Items.AnyAsync(x=>x.Id==itemId&&x.Type==PlanningItemType.Finished,ct))
                return Results.NotFound();
            var history=await db.FactoryDemands.AsNoTracking()
                .Where(x=>x.ItemId==itemId&&x.Category=="Confirmed").ToListAsync(ct);
            var calendar=new PersianCalendar();
            var dt=from.ToDateTime(TimeOnly.MinValue);
            var year=calendar.GetYear(dt);var month=calendar.GetMonth(dt);
            var ordinal=year*12+month-1;
            var forecast=FactoryForecasting.MonthlyMovingAverage(history,from,lookback);
            var coverage=FactoryForecasting.Coverage(history,from,lookback);
            var rows=Enumerable.Range(0,months).Select(offset=>{
                var next=ordinal+offset;
                return new{jalaliYear=next/12,jalaliMonth=next%12+1,
                    forecastQuantity=forecast};
            }).ToArray();
            return Results.Ok(new{itemId,lookbackMonths=lookback,historySamples=history.Count,
                historyMonthsWithRecords=coverage.RecordedMonths,
                missingHistoryMonths=coverage.MissingMonths,
                historyComplete=coverage.Complete,rows,
                warning=coverage.Complete?null:
                    "برای برخی ماه‌های مبنا هیچ تقاضای قطعی ثبت نشده است؛ میانگین شامل صفر فرضی است و نباید پیش‌بینی معتبر کارخانه تلقی شود.",
                disclaimer="میانگین متحرک ساده نیاز قطعی داخلی در ماه‌های گذشته؛ ماه‌های بدون ثبت به عنوان نبود داده پرچم‌گذاری می‌شوند و در محاسبه صفر هستند، نه صفر تأییدشده. برآورد آماری تاییدشده نیست."});
        });

        api.MapPost("/factory-demand",async(FactoryDemandInput input,ProductionDb db,CancellationToken ct)=>{
            if(input.Quantity<=0||input.Quantity>1000000000
                ||!PersianPlanningDates.TryParse(input.DueOnJalali,out var date)
                ||string.IsNullOrWhiteSpace(input.Reference)||input.Reference.Length>100
                ||input.Category is not ("Forecast" or "Confirmed" or "SafetyBuild"))
                return Results.BadRequest(new{message="مشخصات تقاضای مستقل تولید نامعتبر است."});
            if(!await db.Items.AnyAsync(x=>x.Id==input.ItemId&&x.Type==PlanningItemType.Finished,ct))
                return Results.BadRequest(new{message="تقاضا فقط برای محصول نهایی ثبت شود."});
            var demand=new FactoryDemand{ItemId=input.ItemId,Reference=input.Reference.Trim(),
                DueOn=date,Quantity=input.Quantity,Category=input.Category};
            db.FactoryDemands.Add(demand);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/factory-demand/{demand.Id}",new{demand.Id});
        }).RequireAuthorization("IpPlanningWrite");
        api.MapDelete("/factory-demand/{id:guid}",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var demand=await db.FactoryDemands.FindAsync([id],ct);
            if(demand is null)return Results.NotFound();
            db.FactoryDemands.Remove(demand);await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization("IpPlanningWrite");

        api.MapPost("/factory-demand/{demandId:guid}/to-mps/{mpsId:guid}",
            async(Guid demandId,Guid mpsId,ProductionDb db,CancellationToken ct)=>{
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try{await FactoryMpsChangeControl.LockMps(db,ct);}
            catch(Microsoft.Data.SqlClient.SqlException)
                {return Results.Conflict(new{message="قفل انتقال تقاضا به MPS در دسترس نیست."});}
            var demand=await db.FactoryDemands.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==demandId,ct);
            var plan=await db.MpsVersions.SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(demand is null||plan is null)return Results.NotFound();
            if(plan.State!=PlanningApproval.Draft)
                return Results.Conflict(new{message="انتقال تقاضا فقط به MPS پیش‌نویس مجاز است."});
            var reference="DEMAND:"+demand.Id.ToString("N");
            if(await db.MpsDemandLines.AnyAsync(x=>x.MpsVersionId==mpsId&&x.Reference==reference,ct))
                return Results.Conflict(new{message="این تقاضا قبلاً به نسخه انتخاب‌شده MPS منتقل شده است."});
            var line=new MpsDemandLine{MpsVersionId=mpsId,ItemId=demand.ItemId,
                NeededOn=demand.DueOn,Quantity=demand.Quantity,Reference=reference};
            db.MpsDemandLines.Add(line);await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            return Results.Created($"/api/v1/mps/{mpsId}",new{line.Id,demandId,mpsId});
        }).RequireAuthorization("IpPlanningWrite");

        api.MapGet("/factory-sop",async(ProductionDb db,CancellationToken ct)=>{
            var rows=await db.FactorySopReviews.AsNoTracking()
                .OrderByDescending(x=>x.CalculatedUtc).Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.Id,x.Code,x.DemandQuantity,x.RoughCutMinutes,
                x.AvailableMinutes,x.OverloadMinutes,x.CalculatedUtc,
                fromJalali=PersianPlanningDates.Format(x.From),
                throughJalali=PersianPlanningDates.Format(x.Through)}));
        });
        api.MapPost("/factory-sop/calculate",async(FactorySopInput input,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(input.FromJalali,out var from)
               ||!PersianPlanningDates.TryParse(input.ThroughJalali,out var through)
               ||through<from||through.DayNumber-from.DayNumber>90
               ||string.IsNullOrWhiteSpace(input.Code)||input.Code.Length>80)
                return Results.BadRequest(new{message="کد و بازه شمسی S&OP نامعتبر است (حداکثر ۹۱ روز)."});
            if(await db.FactorySopReviews.AnyAsync(x=>x.Code==input.Code.Trim(),ct))
                return Results.Conflict(new{message="کد بررسی S&OP تکراری است."});
            var demands=await db.FactoryDemands.AsNoTracking()
                .Where(x=>x.DueOn>=from&&x.DueOn<=through).ToListAsync(ct);
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.State==RoutingState.Approved).ToListAsync(ct);
            var capacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
            WorkCenterProductCapacityRules.ApplyToRoutes(routes,capacityRates);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var workCenterLoad=new Dictionary<Guid,decimal>();
            var warnings=new List<string>();
            foreach(var demand in demands)
            {
                var selected=routes.Where(x=>x.ItemId==demand.ItemId&&x.EffectiveFrom<=demand.DueOn
                    && (x.EffectiveTo==null||demand.DueOn<=x.EffectiveTo)).ToList();
                if(selected.Count!=1){warnings.Add($"مسیر ساخت مصوب برای نیاز {demand.Reference} یکتا نیست.");continue;}
                if(selected[0].Operations.Any(op=>op.YieldPercent<=0)){warnings.Add($"بازده مسیر {demand.Reference} نامعتبر است.");continue;}
                foreach(var operation in selected[0].Operations)
                {
                    var required=operation.SetupMinutes+operation.RunMinutesPerUnit*
                        demand.Quantity*100m/operation.YieldPercent;
                    workCenterLoad[operation.WorkCenterId]=
                        workCenterLoad.GetValueOrDefault(operation.WorkCenterId)+required;
                }
            }
            var workCenterCapacity=new Dictionary<Guid,decimal>();
            for(var d=from;d<=through;d=d.AddDays(1))
                foreach(var center in shifts.Select(x=>x.WorkCenterId).Distinct())
                    workCenterCapacity[center]=workCenterCapacity.GetValueOrDefault(center)+
                        FactoryShiftCalendar.AvailableMinutes(center,d,shifts);
            var centerIds=workCenterLoad.Keys.Union(workCenterCapacity.Keys).OrderBy(x=>x).ToList();
            var byCenter=centerIds.Select(id=>new{
                workCenterId=id,requiredMinutes=workCenterLoad.GetValueOrDefault(id),
                availableMinutes=workCenterCapacity.GetValueOrDefault(id),
                overloadMinutes=Math.Max(0m,workCenterLoad.GetValueOrDefault(id)-
                    workCenterCapacity.GetValueOrDefault(id))
            }).ToList();
            var load=byCenter.Sum(x=>x.requiredMinutes);
            var capacity=byCenter.Sum(x=>x.availableMinutes);
            var overload=byCenter.Sum(x=>x.overloadMinutes);
            var snapshot=new{demands=demands.Select(x=>new{x.ItemId,x.Quantity,x.DueOn,x.Reference}),
                routingVersions=routes.Select(x=>new{x.Id,x.ItemId,x.RevisionCode}),warnings,
                shiftCount=shifts.Count,byCenter,load,capacity,overload};
            var record=new FactorySopReview{Code=input.Code.Trim(),From=from,Through=through,
                DemandQuantity=demands.Sum(x=>x.Quantity),RoughCutMinutes=load,
                AvailableMinutes=capacity,OverloadMinutes=overload,
                SnapshotJson=JsonSerializer.Serialize(snapshot)};
            db.FactorySopReviews.Add(record);await db.SaveChangesAsync(ct);
            return Results.Ok(new{record.Id,record.Code,record.DemandQuantity,record.RoughCutMinutes,
                record.AvailableMinutes,record.OverloadMinutes,byCenter,warnings,
                note="اضافه‌بار مراکز کاری جداگانه محاسبه شده است؛ بار روزانه، مواد و توالی عملیات در APS بررسی می‌شوند."});
        }).RequireAuthorization("IpPlanningWrite");

        api.MapGet("/factory-aps/runs",async(ProductionDb db,CancellationToken ct)=>{
            var rows=await db.FactoryApsRuns.AsNoTracking()
                .OrderByDescending(x=>x.CalculatedUtc).Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.Id,x.MpsVersionId,x.HorizonDays,x.Algorithm,x.CalculatedUtc,
                startJalali=PersianPlanningDates.Format(x.StartOn)}));
        });
        api.MapGet("/factory-aps/runs/{id:guid}",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var run=await db.FactoryApsRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(run is null)return Results.NotFound();
            return Results.Ok(new{run.Id,run.Algorithm,run.MpsVersionId,run.HorizonDays,
                startJalali=PersianPlanningDates.Format(run.StartOn),run.CalculatedUtc,
                result=JsonSerializer.Deserialize<JsonElement>(run.OutputJson)});
        });
        api.MapPost("/factory-aps/calculate",async(FactoryApsInput input,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(input.StartJalali,out var start)
                ||input.HorizonDays is <1 or >90||input.CapacityMultiplier is <=0 or >2)
                return Results.BadRequest(new{message="تاریخ شمسی شروع، افق ۱ تا ۹۰ روز یا ضریب ظرفیت نامعتبر است."});
            var plan=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==input.MpsVersionId,ct);
            if(plan is null)return Results.NotFound();
            if(plan.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="ابتدا نسخه MPS را تأیید کنید."});
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.State==RoutingState.Approved).ToListAsync(ct);
            var capacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
            WorkCenterProductCapacityRules.ApplyToRoutes(routes,capacityRates);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var unavailable=new HashSet<(Guid WorkCenterId,DateOnly Date)>();
            foreach(var block in input.UnavailableDays??new())
            {
                if(block.WorkCenterId==Guid.Empty ||
                    !PersianPlanningDates.TryParse(block.DateJalali,out var blockedOn) ||
                    string.IsNullOrWhiteSpace(block.Reason) || block.Reason.Length>180 ||
                    blockedOn<start || blockedOn>=start.AddDays(input.HorizonDays))
                    return Results.BadRequest(new{message="مرکز، علت یا تاریخ شمسی روز عدم‌دسترسی معتبر نیست."});
                if(!string.IsNullOrWhiteSpace(block.ReasonCode))
                {
                    var reason=await FactoryMasterData.Active(db,
                        FactoryMasterData.CapacityBlackoutReason,block.ReasonCode,ct);
                    if(reason is null)return Results.Conflict(new{
                        message="دلیل عدم دسترسی مرکز کاری در اطلاعات پایه فعال یا معتبر نیست."});
                    if(block.Reason.Trim()!=reason.Label&&
                        !block.Reason.Trim().StartsWith(reason.Code+" — ",StringComparison.Ordinal))
                        return Results.Conflict(new{
                            message="عنوان و کد دلیل توقف برنامه‌ای با اطلاعات پایه تطابق ندارند."});
                }
                unavailable.Add((block.WorkCenterId,blockedOn));
            }
            if(unavailable.Count>0)
            {
                var validCenterIds=await db.WorkCenters.AsNoTracking()
                    .Select(x=>x.Id).ToHashSetAsync(ct);
                if(unavailable.Any(x=>!validCenterIds.Contains(x.WorkCenterId)))
                    return Results.BadRequest(new{message="مرکز کاری انتخاب‌شده برای روز عدم‌دسترسی وجود ندارد."});
            }
            var scenarioSlots=new List<WorkCenterOccupiedInterval>();
            var inputs=input.UnavailableSlots??new();
            if(inputs.Count>100)return Results.BadRequest(new{
                message="حداکثر ۱۰۰ بازه عدم‌دسترسی ساعتی در هر سناریوی APS مجاز است."});
            foreach(var slot in inputs)
            {
                if(!FactoryApsScenarioSlots.TryWindow(slot,start,input.HorizonDays,
                    out var interval)||string.IsNullOrWhiteSpace(slot.ReasonCode)||
                    string.IsNullOrWhiteSpace(slot.Reason)||slot.Reason.Length>180)
                    return Results.BadRequest(new{
                        message="مرکز، تاریخ/ساعت شروع و پایان در افق، یا دلیل بازه عدم‌دسترسی نامعتبر است."});
                var registered=await FactoryMasterData.Active(db,
                    FactoryMasterData.CapacityBlackoutReason,slot.ReasonCode,ct);
                if(registered is null||slot.Reason.Trim()!=registered.Label)
                    return Results.Conflict(new{
                        message="کد و عنوان دلیل توقف ساعتی باید با اطلاعات پایه فعال برابر باشند."});
                scenarioSlots.Add(interval);
            }
            if(FactoryApsScenarioSlots.Overlaps(scenarioSlots))
                return Results.Conflict(new{
                    message="بازه‌های توقف ساعتی یک مرکز کاری همپوشانی دارند؛ سناریو را اصلاح کنید."});
            if(scenarioSlots.Count>0)
            {
                var centerIds=await db.WorkCenters.AsNoTracking()
                    .Select(x=>x.Id).ToHashSetAsync(ct);
                if(scenarioSlots.Any(x=>!centerIds.Contains(x.WorkCenterId)))
                    return Results.BadRequest(new{
                        message="مرکز کاری یکی از بازه‌های توقف ساعتی وجود ندارد."});
            }
            var sharedToolInputs=input.SharedTools??new();
            if(sharedToolInputs.Count>100)
                return Results.BadRequest(new{message="در هر سناریوی APS حداکثر ۱۰۰ انتساب ابزار مشترک مجاز است."});
            var sharedTools=new Dictionary<Guid,string>();
            foreach(var assignment in sharedToolInputs)
            {
                var code=FactoryMasterData.NormalizeCode(assignment.ToolGroupCode);
                if(assignment.WorkCenterId==Guid.Empty ||
                    !FactoryMasterData.ValidCode(FactoryMasterData.SharedToolGroup,code)||
                    !sharedTools.TryAdd(assignment.WorkCenterId,code))
                    return Results.BadRequest(new{message="انتساب مرکز به ابزار مشترک معتبر یا یکتا نیست."});
                if(await FactoryMasterData.Active(db,
                    FactoryMasterData.SharedToolGroup,code,ct) is null)
                    return Results.Conflict(new{message="کد ابزار مشترک در اطلاعات پایه تعریف نشده یا غیرفعال است."});
            }
            if(sharedTools.Count>0)
            {
                var assignedIds=sharedTools.Keys.ToArray();
                var centerIds=await db.WorkCenters.AsNoTracking()
                    .Where(x=>assignedIds.Contains(x.Id)).Select(x=>x.Id)
                    .ToHashSetAsync(ct);
                if(centerIds.Count!=sharedTools.Count)
                    return Results.BadRequest(new{message="یک یا چند مرکز ابزار مشترک وجود ندارند."});
            }
            var bomRevisions=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            var expectedReceipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            var itemTypes=await db.Items.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Type,ct);
            var material=MaterialConstrainedPlanning.Check(
                plan.Lines,bomRevisions,balances,expectedReceipts,itemTypes,start);
            var failedMaterial=material.Where(x=>!x.Feasible).ToList();
            if(failedMaterial.Count>0)
                return Results.UnprocessableEntity(new{
                    message="برنامه از نظر تامین مواد اولیه قابل اجرا نیست. ابتدا موجودی‌ها و دریافت‌های برنامه‌ای را بازنگری کنید.",
                    materialWarnings=failedMaterial
                });
            var tasks=new List<ApsTask>();
            var errors=new List<string>();
            foreach(var line in plan.Lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId))
            {
                var active=routes.Where(x=>x.ItemId==line.ItemId&&x.EffectiveFrom<=line.NeededOn
                    && (x.EffectiveTo==null||line.NeededOn<=x.EffectiveTo)).ToList();
                if(active.Count!=1)
                {errors.Add($"مسیر ساخت محصول {line.ItemId} معتبر یا یکتا نیست.");continue;}
                try
                {
                    // Match CRP: operation quantity and minutes account for the
                    // yield of this operation plus every downstream operation.
                    tasks.AddRange(FactoryApsTaskExpansion.Expand(line,active[0]));
                }
                catch(InvalidOperationException ex)
                {errors.Add(ex.Message);}
                catch(OverflowException)
                {errors.Add($"محاسبه مقدار عبوری یا زمان عملیات محصول {line.ItemId} از محدوده عددی خارج شد.");}
            }
            if(errors.Count>0)return Results.UnprocessableEntity(new{message=string.Join("؛ ",errors)});
            var actualEvents=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==FactoryOperationClock.Started||
                    x.Action==FactoryOperationClock.Stopped||
                    x.Action==FactoryCapacityReservations.Reserved||
                    x.Action==FactoryCapacityReservations.Cancelled).ToListAsync(ct);
            var activeOrders=await db.ManufacturingOrders.AsNoTracking()
                .Include(x=>x.Operations)
                .Where(x=>x.Status==ManufacturingOrderStatus.Released||
                    x.Status==ManufacturingOrderStatus.InProgress||
                    x.Status==ManufacturingOrderStatus.OnHold).ToListAsync(ct);
            FactoryApsCapacityEvidence evidence;
            ApsResult result;
            try
            {
                var clocks=FactoryOperationClock.Read(actualEvents);
                var reservations=FactoryCapacityReservations.Read(actualEvents);
                evidence=FactoryApsExecutionGuard.Evaluate(activeOrders,clocks,reservations,
                    start,input.HorizonDays);
                unavailable.UnionWith(evidence.ReservedDays);
                result=FiniteFactoryScheduler.Schedule(tasks,shifts,start,
                    input.HorizonDays,input.CapacityMultiplier,unavailable,
                    occupiedIntervals:evidence.OccupiedIntervals.Concat(scenarioSlots).ToArray(),
                    sharedToolGroups:sharedTools);
            }
            catch(Exception ex)when(ex is ArgumentException or InvalidOperationException
                or OverflowException)
            {return Results.UnprocessableEntity(new{message=ex.Message});}
            var snapshot=new FactoryApsRun{MpsVersionId=plan.Id,StartOn=start,HorizonDays=input.HorizonDays,
                InputJson=JsonSerializer.Serialize(new{mps=plan.Lines.Select(x=>new{x.ItemId,x.Quantity,x.NeededOn}),
                    tasks,materialFeasibility=material,shifts=shifts.Select(x=>new{x.WorkCenterId,x.DayOfWeek,x.StartTime,x.EndTime,
                        x.EffectiveCapacityPercent}),input.CapacityMultiplier,
                        executionEvidence=new{evidence.ActiveOrders,evidence.ActiveReservations,
                            evidence.OpenClocks,evidence.BlockedOperations,
                            reservedDays=evidence.ReservedDays.Count,
                            occupiedIntervals=evidence.OccupiedIntervals.Count},
                        unavailableDays=input.UnavailableDays?.Select(x=>new{x.WorkCenterId,x.DateJalali,x.Reason,x.ReasonCode}),
                        unavailableSlots=inputs.Select(x=>new{x.WorkCenterId,x.StartJalali,x.StartTime,
                            x.EndJalali,x.EndTime,x.Reason,x.ReasonCode}),
                        sharedTools=sharedToolInputs.Select(x=>new{x.WorkCenterId,x.ToolGroupCode})}),
                OutputJson=JsonSerializer.Serialize(result)};
            db.FactoryApsRuns.Add(snapshot);await db.SaveChangesAsync(ct);
            return Results.Ok(new{runId=snapshot.Id,snapshot.Algorithm,
                allocations=result.Allocations.Select(x=>new{x.ItemId,x.WorkCenterId,x.Sequence,
                    x.Quantity,x.OperationName,x.DurationMinutes,x.Late,
                    startDateIso=x.StartDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                    finishDateIso=x.FinishDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                    startJalali=PersianPlanningDates.Format(x.StartDate),
                    startTime=x.StartsAt.ToString("HH:mm"),
                    finishJalali=PersianPlanningDates.Format(x.FinishDate),
                    finishTime=x.FinishesAt.ToString("HH:mm")}),
                unavailableDays=unavailable.Select(x=>new{workCenterId=x.WorkCenterId,dateJalali=PersianPlanningDates.Format(x.Date)}),
                result.Unscheduled,result.TotalLateOperations,
                executionEvidence=new{evidence.ActiveOrders,evidence.ActiveReservations,
                    evidence.OpenClocks,evidence.BlockedOperations,
                    reservedDays=evidence.ReservedDays.Count,
                    occupiedIntervals=evidence.OccupiedIntervals.Count,
                    scenarioUnavailableSlots=scenarioSlots.Count,
                    sharedToolAssignments=sharedTools.Count},
                disclaimer="پیش‌نمایش ظرفیت محدود با فرضیات مواد مستقل IPPCS، رزرو و ساعت‌های عملیاتی ثبت‌شده. ابزار مشترک و خرابی ثبت‌نشده هنوز اعمال نمی‌شوند؛ مجوز آزادسازی نیست."});
        }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
