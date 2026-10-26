using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

// Factory execution only; this is NOT warehouse inventory, purchasing, sales, or HR.
public enum ManufacturingOrderStatus { Draft, Released, InProgress, OnHold, Completed, Cancelled }
public enum ManufacturingOrderKind { SemiFinished, FinalAssembly }

public sealed class ManufacturingOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public Guid ItemId { get; set; }
    public ManufacturingOrderKind Kind { get; set; }
    public Guid RoutingRevisionId { get; set; }
    public Guid BomRevisionId { get; set; }
    public decimal PlannedQuantity { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly DueOn { get; set; }
    public ManufacturingOrderStatus Status { get; set; } = ManufacturingOrderStatus.Draft;
    public ManufacturingOrderStatus ResumeStatus { get; set; } = ManufacturingOrderStatus.Released;
    public string? HoldReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public byte[] RowVersion { get; set; } = [];
    public List<ManufacturingOrderOperation> Operations { get; set; } = new();
}
public sealed class ManufacturingOrderOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    [JsonIgnore] public ManufacturingOrder Order { get; set; } = null!;
    public int Sequence { get; set; }
    public Guid WorkCenterId { get; set; }
    public string Name { get; set; } = "";
    public decimal SetupMinutes { get; set; }
    public decimal RunMinutesPerUnit { get; set; }
    public decimal GoodQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public decimal ReworkQuantity { get; set; }
    public bool IsComplete { get; set; }
    public List<ManufacturingConfirmation> Confirmations { get; set; } = new();
}
public sealed class ManufacturingConfirmation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    [JsonIgnore] public ManufacturingOrderOperation Operation { get; set; } = null!;
    public decimal GoodQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public decimal ReworkQuantity { get; set; }
    public string? LotCode { get; set; }
    public string? Note { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}
public sealed class ManufacturingDowntime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid OperationId { get; set; }
    public string Reason { get; set; } = "";
    public int DurationMinutes { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}
public sealed class ManufacturingOrderAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Action { get; set; } = "";
    public string? Details { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
public record ManufacturingOrderInput(string Number, Guid ItemId, decimal PlannedQuantity, string PlannedStartJalali, string DueOnJalali);
public record ManufacturingConfirmationInput(decimal GoodQuantity, decimal ScrapQuantity, decimal ReworkQuantity, string? LotCode, string? Note);
public record ManufacturingHoldInput(string Reason);
public record ManufacturingDowntimeInput(Guid OperationId, string Reason, int DurationMinutes);

public static class ManufacturingExecutionEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingOrder>().HasIndex(x=>x.Number).IsUnique();
        model.Entity<ManufacturingOrder>().Property(x=>x.Number).HasMaxLength(64);
        model.Entity<ManufacturingOrder>().Property(x=>x.HoldReason).HasMaxLength(500);
        model.Entity<ManufacturingOrder>().Property(x=>x.PlannedQuantity).HasPrecision(18,4);
        model.Entity<ManufacturingOrder>().Property(x=>x.RowVersion).IsRowVersion();
        model.Entity<ManufacturingOrder>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrder>().HasOne<RoutingRevision>().WithMany().HasForeignKey(x=>x.RoutingRevisionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrder>().HasOne<ManufacturingBomRevision>().WithMany().HasForeignKey(x=>x.BomRevisionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderOperation>().HasOne(x=>x.Order).WithMany(x=>x.Operations).HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ManufacturingOrderOperation>().HasIndex(x=>new{x.OrderId,x.Sequence}).IsUnique();
        model.Entity<ManufacturingOrderOperation>().Property(x=>x.Name).HasMaxLength(180);
        foreach(var prop in new[]{nameof(ManufacturingOrderOperation.SetupMinutes),nameof(ManufacturingOrderOperation.RunMinutesPerUnit),
            nameof(ManufacturingOrderOperation.GoodQuantity),nameof(ManufacturingOrderOperation.ScrapQuantity),nameof(ManufacturingOrderOperation.ReworkQuantity)})
            model.Entity<ManufacturingOrderOperation>().Property<decimal>(prop).HasPrecision(18,4);
        model.Entity<ManufacturingOrderOperation>().HasOne<WorkCenter>().WithMany().HasForeignKey(x=>x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingConfirmation>().HasOne(x=>x.Operation).WithMany(x=>x.Confirmations).HasForeignKey(x=>x.OperationId).OnDelete(DeleteBehavior.Cascade);
        foreach(var prop in new[]{nameof(ManufacturingConfirmation.GoodQuantity),nameof(ManufacturingConfirmation.ScrapQuantity),nameof(ManufacturingConfirmation.ReworkQuantity)})
            model.Entity<ManufacturingConfirmation>().Property<decimal>(prop).HasPrecision(18,4);
        model.Entity<ManufacturingConfirmation>().Property(x=>x.LotCode).HasMaxLength(100);
        model.Entity<ManufacturingConfirmation>().Property(x=>x.Note).HasMaxLength(500);
        model.Entity<ManufacturingDowntime>().Property(x=>x.Reason).HasMaxLength(180);
        model.Entity<ManufacturingDowntime>().HasOne<ManufacturingOrder>().WithMany().HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingDowntime>().HasOne<ManufacturingOrderOperation>().WithMany().HasForeignKey(x=>x.OperationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderAudit>().Property(x=>x.Action).HasMaxLength(64);
        model.Entity<ManufacturingOrderAudit>().Property(x=>x.Details).HasMaxLength(500);
        model.Entity<ManufacturingOrderAudit>().HasOne<ManufacturingOrder>().WithMany().HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }

    public static bool IsActive(ManufacturingOrderStatus status) =>
        status is ManufacturingOrderStatus.Released or ManufacturingOrderStatus.InProgress;

    // Locks the order's routing and BOM revision at creation; op snapshot is created only at release.
    public static bool TryApplyConfirmation(ManufacturingOrder order, Guid operationId, ManufacturingConfirmationInput input, out string error)
    {
        error="";
        if(!IsActive(order.Status)){error="دستور در وضعیت قابل ثبت عملکرد نیست.";return false;}
        if(input.GoodQuantity<0||input.ScrapQuantity<0||input.ReworkQuantity<0
            ||input.GoodQuantity+input.ScrapQuantity+input.ReworkQuantity<=0)
        {error="مقادیر تولید سالم، ضایعات و دوباره‌کاری نامعتبر است.";return false;}
        if(input.GoodQuantity+input.ScrapQuantity+input.ReworkQuantity>1_000_000_000m)
        {error="مقدار ثبت بیش از حد مجاز است.";return false;}
        var ordered=order.Operations.OrderBy(x=>x.Sequence).ToList();
        var ix=ordered.FindIndex(x=>x.Id==operationId);
        if(ix<0){error="عملیات به دستور تعلق ندارد.";return false;}
        var op=ordered[ix];
        if(op.IsComplete){error="عملیات تکمیل شده و قابل ثبت دوباره نیست.";return false;}
        if(ix>0 && !ordered[ix-1].IsComplete){error="عملیات قبلی هنوز تکمیل نشده است.";return false;}
        var allowed=ix==0?order.PlannedQuantity:ordered[ix-1].GoodQuantity;
        var total=op.GoodQuantity+op.ScrapQuantity+op.ReworkQuantity+
            input.GoodQuantity+input.ScrapQuantity+input.ReworkQuantity;
        if(total>allowed){error="مجموع خروجی از ورودی مجاز عملیات بیشتر است.";return false;}
        op.GoodQuantity+=input.GoodQuantity;
        op.ScrapQuantity+=input.ScrapQuantity;
        op.ReworkQuantity+=input.ReworkQuantity;
        op.IsComplete=total==allowed;
        if(order.Status==ManufacturingOrderStatus.Released)order.Status=ManufacturingOrderStatus.InProgress;
        if(ordered.All(x=>x.IsComplete))
        {
            order.Status=ManufacturingOrderStatus.Completed;
            order.CompletedUtc=DateTime.UtcNow;
        }
        order.UpdatedUtc=DateTime.UtcNow;
        return true;
    }
    public static IEndpointRouteBuilder MapManufacturingExecution(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1");
        api.MapGet("/manufacturing-orders",async(ProductionDb db,CancellationToken ct)=>{
            var orders=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .OrderByDescending(x=>x.CreatedUtc).Take(200).ToListAsync(ct);
            return Results.Ok(orders.Select(x=>ToDto(x)));
        });
        api.MapGet("/manufacturing-orders/{id:guid}",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .ThenInclude(x=>x.Confirmations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            return order is null?Results.NotFound():Results.Ok(ToDto(order));
        });
        api.MapPost("/manufacturing-orders",async(ManufacturingOrderInput input,ProductionDb db,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Number)||input.Number.Trim().Length>64||input.PlannedQuantity<=0
                ||input.PlannedQuantity>1_000_000_000m
                ||!PersianPlanningDates.TryParse(input.PlannedStartJalali,out var start)
                ||!PersianPlanningDates.TryParse(input.DueOnJalali,out var due)||due<start)
                return Results.BadRequest(new{message="شماره دستور، مقدار و تاریخ‌ها معتبر نیستند."});
            var item=await db.Items.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.ItemId,ct);
            if(item is null||item.Type is not (PlanningItemType.Finished or PlanningItemType.SemiFinished))
                return Results.BadRequest(new{message="دستور باید برای محصول نهایی یا نیمه‌ساخته باشد."});
            var number=input.Number.Trim();
            if(await db.ManufacturingOrders.AnyAsync(x=>x.Number==number,ct))
                return Results.Conflict(new{message="شماره دستور تکراری است."});
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.ItemId==input.ItemId && x.State==RoutingState.Approved
                    && x.EffectiveFrom<=start && (x.EffectiveTo==null||start<=x.EffectiveTo)).ToListAsync(ct);
            var boms=await db.ManufacturingBomRevisions.AsNoTracking()
                .Where(x=>x.ParentItemId==input.ItemId && x.State==ManufacturingRevisionState.Approved
                    && x.ValidFrom<=start && (x.ValidUntil==null||start<=x.ValidUntil)).ToListAsync(ct);
            if(routes.Count!=1||routes[0].Operations.Count==0||boms.Count!=1)
                return Results.Conflict(new{message="یک مسیر ساخت و یک BOM مصوب و معتبر برای تاریخ شروع دستور لازم است."});
            var order=new ManufacturingOrder{
                Number=number,ItemId=input.ItemId,Kind=item.Type==PlanningItemType.SemiFinished?
                ManufacturingOrderKind.SemiFinished:ManufacturingOrderKind.FinalAssembly,
                PlannedQuantity=input.PlannedQuantity,PlannedStart=start,DueOn=due,
                RoutingRevisionId=routes[0].Id,BomRevisionId=boms[0].Id
            };
            db.ManufacturingOrders.Add(order);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=order.Id,Action="Created"});
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/manufacturing-orders/{order.Id}",ToDto(order));
        });
        api.MapPost("/manufacturing-orders/{id:guid}/release",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(order.Status!=ManufacturingOrderStatus.Draft)
                return Results.Conflict(new{message="فقط دستور پیش‌نویس قابل آزادسازی است."});
            var route=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==order.RoutingRevisionId,ct);
            var bom=await db.ManufacturingBomRevisions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
            if(route?.State!=RoutingState.Approved||bom?.State!=ManufacturingRevisionState.Approved
                ||route.EffectiveFrom>order.PlannedStart||(route.EffectiveTo.HasValue&&route.EffectiveTo.Value<order.PlannedStart)
                ||bom.ValidFrom>order.PlannedStart||(bom.ValidUntil.HasValue&&bom.ValidUntil.Value<order.PlannedStart)
                ||route.Operations.Count==0)
                return Results.Conflict(new{message="مسیر و BOM معتبر مصوب وجود ندارند."});
            foreach(var op in route.Operations.OrderBy(x=>x.Sequence))
                order.Operations.Add(new ManufacturingOrderOperation{
                    Sequence=op.Sequence,Name=op.Name,WorkCenterId=op.WorkCenterId,
                    SetupMinutes=op.SetupMinutes,RunMinutesPerUnit=op.RunMinutesPerUnit
                });
            order.Status=ManufacturingOrderStatus.Released;
            order.ReleasedUtc=DateTime.UtcNow;order.UpdatedUtc=DateTime.UtcNow;
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="Released"});
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="دستور هم‌زمان تغییر یافته است."});}
            return Results.Ok(ToDto(order));
        });
        api.MapPost("/manufacturing-orders/{id:guid}/operations/{operationId:guid}/confirm",
            async(Guid id,Guid operationId,ManufacturingConfirmationInput input,ProductionDb db,CancellationToken ct)=>{
            if((input.LotCode?.Length??0)>100||(input.Note?.Length??0)>500)
                return Results.BadRequest(new{message="شرح یا شناسه بچ بیش از حد مجاز است."});
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(!TryApplyConfirmation(order,operationId,input,out var error))
                return Results.Conflict(new{message=error});
            db.ManufacturingConfirmations.Add(new ManufacturingConfirmation{
                OperationId=operationId,GoodQuantity=input.GoodQuantity,ScrapQuantity=input.ScrapQuantity,
                ReworkQuantity=input.ReworkQuantity,LotCode=input.LotCode?.Trim(),Note=input.Note?.Trim()
            });
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="Confirmed",
                Details=$"Seq={order.Operations.Single(x=>x.Id==operationId).Sequence};Good={input.GoodQuantity};Scrap={input.ScrapQuantity};Rework={input.ReworkQuantity}"});
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="ثبت هم‌زمان رخ داد؛ داده‌ها را بازخوانی کنید."});}
            return Results.Ok(ToDto(order));
        });
        api.MapPost("/manufacturing-orders/{id:guid}/hold",async(Guid id,ManufacturingHoldInput input,ProductionDb db,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length>500)
                return Results.BadRequest(new{message="علت توقف معتبر نیست."});
            var order=await db.ManufacturingOrders.FindAsync([id],ct);
            if(order is null)return Results.NotFound();
            if(!IsActive(order.Status))return Results.Conflict(new{message="وضعیت دستور اجازه تعلیق ندارد."});
            order.ResumeStatus=order.Status;order.Status=ManufacturingOrderStatus.OnHold;
            order.HoldReason=input.Reason.Trim();order.UpdatedUtc=DateTime.UtcNow;
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="Held",Details=order.HoldReason});
            try{await db.SaveChangesAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="وضعیت دستور هم‌زمان تغییر کرده است."});}
            return Results.Ok(new{order.Id,order.Status});
        });
        api.MapPost("/manufacturing-orders/{id:guid}/resume",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.FindAsync([id],ct);
            if(order is null)return Results.NotFound();
            if(order.Status!=ManufacturingOrderStatus.OnHold)return Results.Conflict(new{message="دستور معلق نیست."});
            order.Status=order.ResumeStatus;order.HoldReason=null;order.UpdatedUtc=DateTime.UtcNow;
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="Resumed"});
            try{await db.SaveChangesAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="وضعیت دستور هم‌زمان تغییر کرده است."});}
            return Results.Ok(new{order.Id,order.Status});
        });
        api.MapPost("/manufacturing-orders/{id:guid}/cancel",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(order.Status is not (ManufacturingOrderStatus.Draft or ManufacturingOrderStatus.Released)
                ||order.Operations.Any(x=>x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity>0))
                return Results.Conflict(new{message="دستور اجراشده را نمی‌توان لغو کرد."});
            order.Status=ManufacturingOrderStatus.Cancelled;order.UpdatedUtc=DateTime.UtcNow;
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="Cancelled"});
            try{await db.SaveChangesAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="وضعیت دستور هم‌زمان تغییر کرده است."});}
            return Results.Ok(new{order.Id,order.Status});
        });
        api.MapPost("/manufacturing-orders/{id:guid}/downtime",async(Guid id,ManufacturingDowntimeInput input,ProductionDb db,CancellationToken ct)=>{
            if(input.DurationMinutes is <=0 or >1440||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length>180)
                return Results.BadRequest(new{message="مدت توقف (۱ تا ۱۴۴۰ دقیقه) و علت معتبر لازم است."});
            var order=await db.ManufacturingOrders.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(!IsActive(order.Status))return Results.Conflict(new{message="دستور فعال نیست."});
            if(!await db.ManufacturingOrderOperations.AnyAsync(x=>x.OrderId==id&&x.Id==input.OperationId,ct))
                return Results.BadRequest(new{message="عملیات انتخابی متعلق به این دستور نیست."});
            var stop=new ManufacturingDowntime{
                OrderId=id,OperationId=input.OperationId,Reason=input.Reason.Trim(),DurationMinutes=input.DurationMinutes};
            db.ManufacturingDowntimes.Add(stop);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="Downtime",Details=$"{stop.Reason};{stop.DurationMinutes}m"});
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/manufacturing-orders/{id}/downtimes",new{stop.Id});
        });
        api.MapGet("/manufacturing-orders/{id:guid}/history",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            return Results.Ok(new{
                events=await db.ManufacturingOrderAudits.AsNoTracking().Where(x=>x.OrderId==id).OrderBy(x=>x.AtUtc).ToListAsync(ct),
                downtime=await db.ManufacturingDowntimes.AsNoTracking().Where(x=>x.OrderId==id).OrderBy(x=>x.OccurredUtc).ToListAsync(ct)
            });
        });
        return routes;
    }
    private static object ToDto(ManufacturingOrder order) => new{
        order.Id,order.Number,order.ItemId,order.Kind,order.PlannedQuantity,order.Status,
        plannedStartJalali=PersianPlanningDates.Format(order.PlannedStart),
        dueOnJalali=PersianPlanningDates.Format(order.DueOn),
        order.RoutingRevisionId,order.BomRevisionId,order.CreatedUtc,order.ReleasedUtc,order.CompletedUtc,order.HoldReason,
        operations=order.Operations.OrderBy(x=>x.Sequence).Select(x=>new{
            x.Id,x.Sequence,x.Name,x.WorkCenterId,x.SetupMinutes,x.RunMinutesPerUnit,
            x.GoodQuantity,x.ScrapQuantity,x.ReworkQuantity,x.IsComplete
        }).ToArray()
    };
}
