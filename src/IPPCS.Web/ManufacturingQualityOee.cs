using Microsoft.EntityFrameworkCore;

public enum ManufacturingQualityStatus { Open, Released, Scrapped }

public sealed class ManufacturingQualityHold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid OperationId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = "";
    public ManufacturingQualityStatus Status { get; set; } = ManufacturingQualityStatus.Open;
    public string? DispositionNote { get; set; }
    public string RaisedBy { get; set; } = "";
    public string? ResolvedBy { get; set; }
    public DateTime RaisedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedUtc { get; set; }
}
public sealed class ManufacturingOeePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkCenterId { get; set; }
    public DateOnly ProductionDate { get; set; }
    public decimal PlannedMinutes { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
public record ManufacturingQualityHoldInput(Guid OperationId, decimal Quantity, string Reason);
public record ManufacturingQualityDecisionInput(string Note);
public record ManufacturingOeePlanInput(Guid WorkCenterId, string ProductionDateJalali, decimal PlannedMinutes, string? Note);
public record ManufacturingOeeResult(decimal? AvailabilityPercent, decimal? PerformancePercent,
    decimal? QualityPercent, decimal? OeePercent, string? Warning);
public record ManufacturingWipRow(Guid OperationId, int Sequence, string OperationName,
    decimal ReceivedGood, decimal Scrap, decimal Rework, decimal PendingQuality, decimal RejectedQuality,
    decimal ForwardAvailable, decimal NextStageReported, decimal AwaitingNextOperation, bool Complete);

public static class ManufacturingPerformance
{
    public static ManufacturingOeeResult CalculateOee(decimal? planned, decimal stopped,
        decimal idealProductiveMinutes, decimal good, decimal scrap, decimal rework)
    {
        if(planned is null)return new(null,null,null,null,"زمان برنامه‌ریزی‌شده برای مرکز کاری ثبت نشده است.");
        if(planned<=0 || stopped<0 || idealProductiveMinutes<0 || good<0 || scrap<0 || rework<0
            || stopped>planned)
            return new(null,null,null,null,"اطلاعات ظرفیت، توقف یا مقدار تولید نامعتبر است.");
        var operating=planned.Value-stopped;
        var total=good+scrap+rework;
        if(operating<=0 || total<=0)
            return new(Math.Round(100m*operating/planned.Value,2),null,null,null,
                "برای محاسبه کامل OEE زمان عملیاتی و تعداد قطعات ثبت‌شده ضروری است.");
        var avail=operating/planned.Value*100m;
        var perf=idealProductiveMinutes/operating*100m;
        var quality=good/total*100m;
        var oee=avail*perf*quality/10000m;
        var warning=perf>100m ?
            "عملکرد بالاتر از ۱۰۰٪ است؛ زمان سیکل استاندارد، تداخل توقف‌ها و داده‌های تولید را بازبینی کنید." : null;
        return new(Math.Round(avail,2),Math.Round(perf,2),Math.Round(quality,2),Math.Round(oee,2),warning);
    }

    public static List<ManufacturingWipRow> CalculateWip(ManufacturingOrder order)
    {
        var operations=order.Operations.OrderBy(x=>x.Sequence).ToList();
        var output=new List<ManufacturingWipRow>(operations.Count);
        for(var i=0;i<operations.Count;i++)
        {
            var op=operations[i];
            var nextReported=i+1<operations.Count
                ?operations[i+1].GoodQuantity+operations[i+1].ScrapQuantity+operations[i+1].ReworkQuantity:0m;
            var available=op.GoodQuantity-op.QualityRejectedQuantity-op.QualityPendingQuantity;
            output.Add(new(op.Id,op.Sequence,op.Name,op.GoodQuantity,op.ScrapQuantity,op.ReworkQuantity,
                op.QualityPendingQuantity,op.QualityRejectedQuantity,available,nextReported,
                Math.Max(0m,available-nextReported),op.IsComplete));
        }
        return output;
    }

    public static bool CanHold(ManufacturingOrder order, Guid operationId, decimal quantity, out string error)
    {
        error="";
        if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
        {error="دستور تولید در وضعیت فعال نیست.";return false;}
        if(quantity<=0){error="مقدار قرنطینه باید بزرگ‌تر از صفر باشد.";return false;}
        var ops=order.Operations.OrderBy(x=>x.Sequence).ToList();
        var index=ops.FindIndex(x=>x.Id==operationId);
        if(index<0){error="عملیات متعلق به دستور نیست.";return false;}
        var operation=ops[index];
        if(index+1<ops.Count)
        {
            var next=ops[index+1];
            if(next.GoodQuantity+next.ScrapQuantity+next.ReworkQuantity>0)
            {error="برای جلوگیری از اختلاف سوابق، پس از شروع عملیات بعدی ثبت قرنطینه مجاز نیست.";return false;}
        }
        var available=operation.GoodQuantity-operation.QualityPendingQuantity-operation.QualityRejectedQuantity;
        if(quantity>available){error="مقدار قرنطینه از محصول سالم در دسترس بیشتر است.";return false;}
        return true;
    }
    public static bool TryResolveQuality(ManufacturingOrder order,ManufacturingQualityHold hold,
        ManufacturingQualityStatus disposition,out string error)
    {
        error="";
        if(hold.Status!=ManufacturingQualityStatus.Open ||
            disposition is not (ManufacturingQualityStatus.Released or ManufacturingQualityStatus.Scrapped))
        {error="این مورد قبلاً تعیین تکلیف شده یا تصمیم نامعتبر است.";return false;}
        if(order.Status is ManufacturingOrderStatus.Cancelled or ManufacturingOrderStatus.Completed)
        {error="دستور در وضعیت قابل تعیین تکلیف نیست.";return false;}
        var operation=order.Operations.SingleOrDefault(x=>x.Id==hold.OperationId);
        if(operation is null || operation.QualityPendingQuantity<hold.Quantity)
        {error="مقدار قرنطینه و اطلاعات عملیات سازگار نیست.";return false;}
        operation.QualityPendingQuantity-=hold.Quantity;
        if(disposition==ManufacturingQualityStatus.Scrapped)
            operation.QualityRejectedQuantity+=hold.Quantity;
        hold.Status=disposition;
        hold.ResolvedUtc=DateTime.UtcNow;
        ManufacturingExecutionEndpoints.CloseZeroInputOperations(order);
        if(ManufacturingExecutionEndpoints.IsActive(order.Status) &&
            order.Operations.Count>0 &&
            order.Operations.All(x=>x.IsComplete && x.QualityPendingQuantity==0))
        {
            order.Status=ManufacturingOrderStatus.Completed;
            order.CompletedUtc=DateTime.UtcNow;
        }
        order.UpdatedUtc=DateTime.UtcNow;
        return true;
    }
}

public static class ManufacturingQualityAndOeeEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingQualityHold>().HasOne<ManufacturingOrder>().WithMany()
            .HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingQualityHold>().HasOne<ManufacturingOrderOperation>().WithMany()
            .HasForeignKey(x=>x.OperationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingQualityHold>().Property(x=>x.Quantity).HasPrecision(18,4);
        model.Entity<ManufacturingQualityHold>().Property(x=>x.Reason).HasMaxLength(500);
        model.Entity<ManufacturingQualityHold>().Property(x=>x.DispositionNote).HasMaxLength(500);
        model.Entity<ManufacturingQualityHold>().Property(x=>x.RaisedBy).HasMaxLength(256);
        model.Entity<ManufacturingQualityHold>().Property(x=>x.ResolvedBy).HasMaxLength(256);
        model.Entity<ManufacturingQualityHold>().HasIndex(x=>new{x.OrderId,x.OperationId,x.Status});
        model.Entity<ManufacturingOeePlan>().HasIndex(x=>new{x.WorkCenterId,x.ProductionDate}).IsUnique();
        model.Entity<ManufacturingOeePlan>().HasOne<WorkCenter>().WithMany()
            .HasForeignKey(x=>x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOeePlan>().Property(x=>x.PlannedMinutes).HasPrecision(18,4);
        model.Entity<ManufacturingOeePlan>().Property(x=>x.Note).HasMaxLength(400);
    }

    public static IEndpointRouteBuilder MapQualityAndOee(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/wip",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            return order is null?Results.NotFound():Results.Ok(new{orderId=id,rows=ManufacturingPerformance.CalculateWip(order)});
        });
        api.MapGet("/manufacturing-orders/{id:guid}/quality-holds",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            return Results.Ok(await db.ManufacturingQualityHolds.AsNoTracking()
                .Where(x=>x.OrderId==id).OrderByDescending(x=>x.RaisedUtc).ToListAsync(ct));
        });
        api.MapPost("/manufacturing-orders/{id:guid}/quality-holds",
            async(Guid id,ManufacturingQualityHoldInput input,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>500)
                return Results.BadRequest(new{message="علت عدم‌انطباق معتبر لازم است."});
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(!ManufacturingPerformance.CanHold(order,input.OperationId,input.Quantity,out var error))
                return Results.Conflict(new{message=error});
            var operation=order.Operations.Single(x=>x.Id==input.OperationId);
            operation.QualityPendingQuantity+=input.Quantity;
            order.UpdatedUtc=DateTime.UtcNow;
            var hold=new ManufacturingQualityHold{
                OrderId=id,OperationId=input.OperationId,Quantity=input.Quantity,Reason=input.Reason.Trim(),
                RaisedBy=ctx.User.Identity?.Name??"unknown"};
            db.ManufacturingQualityHolds.Add(hold);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action="QualityHold",Details=$"Operation={operation.Sequence};Qty={input.Quantity}"});
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="دستور هم‌زمان به‌روزرسانی شده است."});}
            return Results.Created($"/api/v1/manufacturing-orders/{id}/quality-holds",new{hold.Id,hold.Status});
        });

        api.MapPost("/manufacturing-orders/{id:guid}/quality-holds/{holdId:guid}/disposition",
            async(Guid id,Guid holdId,ManufacturingQualityStatus decision,ManufacturingQualityDecisionInput input,
                ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(decision is not (ManufacturingQualityStatus.Released or ManufacturingQualityStatus.Scrapped)
                ||string.IsNullOrWhiteSpace(input.Note)||input.Note.Length>500)
                return Results.BadRequest(new{message="تصمیم و شرح تعیین تکلیف معتبر لازم است."});
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            var hold=await db.ManufacturingQualityHolds.SingleOrDefaultAsync(x=>x.Id==holdId&&x.OrderId==id,ct);
            if(hold is null)return Results.NotFound();
            if(!ManufacturingPerformance.TryResolveQuality(order,hold,decision,out var error))
                return Results.Conflict(new{message=error});
            hold.DispositionNote=input.Note.Trim();
            hold.ResolvedBy=ctx.User.Identity?.Name??"unknown";
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action=decision==ManufacturingQualityStatus.Released?"QualityRelease":"QualityScrap",
                Details=$"Hold={holdId};Qty={hold.Quantity}"});
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="تغییر هم‌زمان؛ لطفاً اطلاعات را بازخوانی کنید."});}
            return Results.Ok(new{hold.Id,hold.Status,orderStatus=order.Status});
        }).RequireAuthorization("IpQualityApprove");

        api.MapGet("/oee/plans",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.ManufacturingOeePlans.AsNoTracking().OrderByDescending(x=>x.ProductionDate).Take(400).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.Id,x.WorkCenterId,x.PlannedMinutes,x.Note,
                productionDateJalali=PersianPlanningDates.Format(x.ProductionDate)}));
        });
        api.MapPut("/oee/plans",async(ManufacturingOeePlanInput input,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(input.ProductionDateJalali,out var date)
                ||input.PlannedMinutes<=0||input.PlannedMinutes>1440||(input.Note?.Length??0)>400)
                return Results.BadRequest(new{message="تاریخ، زمان برنامه یا توضیحات معتبر نیست."});
            if(!await db.WorkCenters.AnyAsync(x=>x.Id==input.WorkCenterId,ct))
                return Results.BadRequest(new{message="مرکز کاری وجود ندارد."});
            var plan=await db.ManufacturingOeePlans.SingleOrDefaultAsync(
                x=>x.WorkCenterId==input.WorkCenterId&&x.ProductionDate==date,ct);
            if(plan is null){
                plan=new ManufacturingOeePlan{WorkCenterId=input.WorkCenterId,ProductionDate=date};
                db.ManufacturingOeePlans.Add(plan);
            }
            plan.PlannedMinutes=input.PlannedMinutes;plan.Note=input.Note?.Trim();plan.UpdatedUtc=DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new{plan.Id});
        }).RequireAuthorization("IpOeePlanning");

        api.MapGet("/oee/daily",async(string dateJalali,ProductionDb db,IConfiguration config,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(dateJalali,out var date))
                return Results.BadRequest(new{message="تاریخ شمسی معتبر نیست."});
            TimeZoneInfo timezone;
            try{timezone=TimeZoneInfo.FindSystemTimeZoneById(config["Production:TimeZoneId"]??"Asia/Tehran");}
            catch(TimeZoneNotFoundException){return Results.Problem("زمان محلی کارخانه درست پیکربندی نشده است.");}
            var startLocal=date.ToDateTime(TimeOnly.MinValue);
            var endLocal=date.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var startUtc=TimeZoneInfo.ConvertTimeToUtc(startLocal,timezone);
            var endUtc=TimeZoneInfo.ConvertTimeToUtc(endLocal,timezone);
            var centers=await db.WorkCenters.AsNoTracking().ToListAsync(ct);
            var plans=await db.ManufacturingOeePlans.AsNoTracking().Where(x=>x.ProductionDate==date).ToListAsync(ct);
            var records=await db.ManufacturingConfirmations.AsNoTracking()
                .Where(x=>x.OccurredUtc>=startUtc&&x.OccurredUtc<endUtc)
                .Join(db.ManufacturingOrderOperations.AsNoTracking(),x=>x.OperationId,op=>op.Id,(x,op)=>new{
                    op.WorkCenterId,op.RunMinutesPerUnit,x.GoodQuantity,x.ScrapQuantity,x.ReworkQuantity
                }).ToListAsync(ct);
            var stops=await db.ManufacturingDowntimes.AsNoTracking()
                .Where(x=>x.OccurredUtc>=startUtc&&x.OccurredUtc<endUtc)
                .Join(db.ManufacturingOrderOperations.AsNoTracking(),x=>x.OperationId,op=>op.Id,
                    (x,op)=>new{op.WorkCenterId,x.DurationMinutes}).ToListAsync(ct);
            return Results.Ok(new{dateJalali=PersianPlanningDates.Format(date),timeZone=timezone.Id,
                rows=centers.Select(center=>{
                    var day=records.Where(x=>x.WorkCenterId==center.Id).ToList();
                    var planned=plans.FirstOrDefault(x=>x.WorkCenterId==center.Id)?.PlannedMinutes;
                    var stop=stops.Where(x=>x.WorkCenterId==center.Id).Sum(x=>x.DurationMinutes);
                    var good=day.Sum(x=>x.GoodQuantity);
                    var scrap=day.Sum(x=>x.ScrapQuantity);
                    var rework=day.Sum(x=>x.ReworkQuantity);
                    var ideal=day.Sum(x=>(x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity)*x.RunMinutesPerUnit);
                    var calculation=ManufacturingPerformance.CalculateOee(planned,stop,ideal,good,scrap,rework);
                    return new{workCenterId=center.Id,workCenter=center.Name,plannedMinutes=planned,
                        stoppedMinutes=stop,good,scrap,rework,idealProductiveMinutes=ideal,
                        calculation.AvailabilityPercent,calculation.PerformancePercent,
                        calculation.QualityPercent,calculation.OeePercent,calculation.Warning};
                }).ToList()});
        });
        return routes;
    }
}
