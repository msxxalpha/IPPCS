using System.Data;
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
    public string Algorithm { get; set; } = "FINITE-GREEDY-V1";
    public string InputJson { get; set; } = "";
    public string OutputJson { get; set; } = "";
    public DateTime CalculatedUtc { get; set; } = DateTime.UtcNow;
}
public record FactoryDemandInput(Guid ItemId,string Reference,string DueOnJalali,decimal Quantity,string Category);
public record FactorySopInput(string Code,string FromJalali,string ThroughJalali);
public record FactoryApsInput(Guid MpsVersionId,string StartJalali,int HorizonDays,decimal CapacityMultiplier=1m);
public record ApsTask(Guid DemandLineId,Guid ItemId,DateOnly DueOn,decimal Quantity,Guid RoutingRevisionId,
    int OperationSequence,Guid WorkCenterId,decimal DurationMinutes,string OperationName);
public record ApsAllocation(Guid ItemId,Guid WorkCenterId,int Sequence,decimal Quantity,
    DateOnly DueOn,DateOnly StartDate,TimeOnly StartsAt,DateOnly FinishDate,
    TimeOnly FinishesAt,decimal DurationMinutes,string OperationName,bool Late);
public record ApsUnscheduled(Guid ItemId,int Sequence,string Reason);
public record ApsResult(IReadOnlyList<ApsAllocation> Allocations,
    IReadOnlyList<ApsUnscheduled> Unscheduled,decimal TotalLateOperations);

/// <summary>
/// Finite, single-shift, non-preemptive scheduling preview. Exact work-center intervals
/// and sequential-operation precedence are enforced. Material/alternate-tool/maintenance
/// calendars and optimizer objective search are explicitly not included yet.
/// </summary>
public static class FiniteFactoryScheduler
{
    public static ApsResult Schedule(IReadOnlyList<ApsTask> tasks,
        IReadOnlyList<WorkShift> shifts,DateOnly start,int horizonDays,decimal multiplier=1)
    {
        if(horizonDays is <1 or >90 || multiplier<=0 || multiplier>2)
            throw new ArgumentException("افق برنامه‌ریزی یا ضریب ظرفیت نامعتبر است.");
        var result=new List<ApsAllocation>();
        var missing=new List<ApsUnscheduled>();
        var occupied=new Dictionary<(Guid,DateOnly),List<(int start,int end)>>();
        var sorted=tasks.OrderBy(x=>x.DueOn).ThenBy(x=>x.DemandLineId).ThenBy(x=>x.OperationSequence).ToList();
        var chainReady=new Dictionary<Guid,DateTime>();
        var chainFailed=new HashSet<Guid>();
        foreach(var job in sorted)
        {
            if(job.DurationMinutes<=0 || job.DurationMinutes>20000 || job.Quantity<=0)
            {
                missing.Add(new(job.ItemId,job.OperationSequence,"مقدار یا زمان عملیات نامعتبر است."));
                chainFailed.Add(job.DemandLineId);continue;
            }
            if(chainFailed.Contains(job.DemandLineId))
            {
                missing.Add(new(job.ItemId,job.OperationSequence,"عملیات قبلی قابل زمان‌بندی نبود."));continue;
            }
            var earliest=chainReady.GetValueOrDefault(job.DemandLineId,start.ToDateTime(TimeOnly.MinValue));
            bool allocated=false;
            for(int d=0;d<horizonDays && !allocated;d++)
            {
                var day=start.AddDays(d);
                if(day.ToDateTime(TimeOnly.MaxValue)<earliest)continue;
                foreach(var shift in shifts.Where(x=>x.WorkCenterId==job.WorkCenterId
                    && x.DayOfWeek==(int)day.DayOfWeek).OrderBy(x=>x.StartTime))
                {
                    if(shift.EndTime<=shift.StartTime || shift.EffectiveCapacityPercent<=0)continue;
                    int left=(int)shift.StartTime.ToTimeSpan().TotalMinutes;
                    int max=(int)shift.EndTime.ToTimeSpan().TotalMinutes;
                    // Reduced calendar utilization is treated as a shorter effective shift.
                    int effective=(int)Math.Floor((max-left)*shift.EffectiveCapacityPercent*multiplier/100m);
                    max=Math.Min(max,left+effective);
                    if(max<=left)continue;
                    var duration=(int)Math.Ceiling(job.DurationMinutes);
                    if(duration>max-left)continue; // non-preemptive operation cannot cross a shift
                    if(day==DateOnly.FromDateTime(earliest))
                        left=Math.Max(left,(int)Math.Ceiling(earliest.TimeOfDay.TotalMinutes));
                    if(max-left<duration)continue;
                    var key=(job.WorkCenterId,day);
                    if(!occupied.TryGetValue(key,out var intervals))
                        occupied[key]=intervals=new();
                    foreach(var block in intervals.OrderBy(x=>x.start).Append((start:max,end:max)))
                    {
                        if(left+duration<=block.start)
                        {
                            var end=left+duration;
                            intervals.Add((left,end));
                            var from=day.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(left)));
                            var until=day.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(end)));
                            chainReady[job.DemandLineId]=until;
                            result.Add(new ApsAllocation(job.ItemId,job.WorkCenterId,
                                job.OperationSequence,job.Quantity,job.DueOn,day,
                                TimeOnly.FromDateTime(from),day,TimeOnly.FromDateTime(until),
                                job.DurationMinutes,job.OperationName,day>job.DueOn));
                            allocated=true;break;
                        }
                        left=Math.Max(left,block.end);
                        if(left+duration>max)break;
                    }
                    if(allocated)break;
                }
            }
            if(!allocated)
            {
                missing.Add(new(job.ItemId,job.OperationSequence,
                    "عملیات با ظرفیت و شیفت مؤثر موجود، بدون تداخل در افق انتخابی جا نمی‌شود."));
                chainFailed.Add(job.DemandLineId);
            }
        }
        return new(result,missing,result.Count(x=>x.Late));
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
                foreach(var shift in shifts.Where(x=>x.DayOfWeek==(int)d.DayOfWeek))
                    workCenterCapacity[shift.WorkCenterId]=
                        workCenterCapacity.GetValueOrDefault(shift.WorkCenterId)
                        +(decimal)(shift.EndTime-shift.StartTime).TotalMinutes*
                            shift.EffectiveCapacityPercent/100m;
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
            return Results.Ok(new{run.Id,run.Algorithm,
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
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var tasks=new List<ApsTask>();
            var errors=new List<string>();
            foreach(var line in plan.Lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId))
            {
                var active=routes.Where(x=>x.ItemId==line.ItemId&&x.EffectiveFrom<=line.NeededOn
                    && (x.EffectiveTo==null||line.NeededOn<=x.EffectiveTo)).ToList();
                if(active.Count!=1 || active[0].Operations.Count==0)
                {errors.Add($"مسیر ساخت محصول {line.ItemId} معتبر یا یکتا نیست.");continue;}
                foreach(var op in active[0].Operations.OrderBy(x=>x.Sequence))
                {
                    if(op.YieldPercent<=0) {errors.Add($"بازده عملیات {op.Name} نامعتبر است.");continue;}
                    tasks.Add(new(line.Id,line.ItemId,line.NeededOn,line.Quantity,active[0].Id,op.Sequence,
                        op.WorkCenterId,op.SetupMinutes+op.RunMinutesPerUnit*line.Quantity*100m/op.YieldPercent,
                        op.Name));
                }
            }
            if(errors.Count>0)return Results.UnprocessableEntity(new{message=string.Join("؛ ",errors)});
            ApsResult result;
            try {result=FiniteFactoryScheduler.Schedule(tasks,shifts,start,input.HorizonDays,input.CapacityMultiplier);}
            catch(ArgumentException ex){return Results.BadRequest(new{message=ex.Message});}
            var snapshot=new FactoryApsRun{MpsVersionId=plan.Id,StartOn=start,HorizonDays=input.HorizonDays,
                InputJson=JsonSerializer.Serialize(new{mps=plan.Lines.Select(x=>new{x.ItemId,x.Quantity,x.NeededOn}),
                    tasks,shifts=shifts.Select(x=>new{x.WorkCenterId,x.DayOfWeek,x.StartTime,x.EndTime,
                        x.EffectiveCapacityPercent}),input.CapacityMultiplier}),
                OutputJson=JsonSerializer.Serialize(result)};
            db.FactoryApsRuns.Add(snapshot);await db.SaveChangesAsync(ct);
            return Results.Ok(new{runId=snapshot.Id,snapshot.Algorithm,
                allocations=result.Allocations.Select(x=>new{x.ItemId,x.WorkCenterId,x.Sequence,
                    x.Quantity,x.OperationName,x.DurationMinutes,x.Late,
                    startJalali=PersianPlanningDates.Format(x.StartDate),
                    startTime=x.StartsAt.ToString("HH:mm"),
                    finishJalali=PersianPlanningDates.Format(x.FinishDate),
                    finishTime=x.FinishesAt.ToString("HH:mm")}),
                result.Unscheduled,result.TotalLateOperations,
                disclaimer="پیش‌نویس زمان‌بندی با ظرفیت محدود؛ مواد، خرابی پیش‌بینی‌نشده، ابزار و بهینه‌سازی سراسری اعمال نشده‌اند."});
        }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
