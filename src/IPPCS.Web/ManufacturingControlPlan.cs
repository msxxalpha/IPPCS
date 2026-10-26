using System.Data;
using Microsoft.EntityFrameworkCore;

public enum ControlPlanState { Draft, Approved }
public sealed class ManufacturingControlPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoutingRevisionId { get; set; }
    public string Code { get; set; } = "";
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public ControlPlanState State { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public List<ControlPlanCharacteristic> Characteristics { get; set; } = new();
}
public sealed class ControlPlanCharacteristic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public ManufacturingControlPlan Plan { get; set; } = null!;
    public int OperationSequence { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool Mandatory { get; set; } = true;
}
public sealed class ManufacturingInspectionRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderOperationId { get; set; }
    public ManufacturingOrderOperation Operation { get; set; } = null!;
    public Guid SourceCharacteristicId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool Mandatory { get; set; }
    public bool? LastPassed { get; set; }
    public DateTime? LastMeasuredUtc { get; set; }
}
public sealed class ManufacturingInspectionResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid RequirementId { get; set; }
    public ManufacturingInspectionRequirement Requirement { get; set; } = null!;
    public decimal Value { get; set; }
    public bool Passed { get; set; }
    public decimal QuarantinedQuantity { get; set; }
    public string? Note { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class ManufacturingLotGenealogy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid SourceConfirmationId { get; set; }
    public Guid DestinationConfirmationId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime LinkedUtc { get; set; } = DateTime.UtcNow;
    public string LinkedBy { get; set; } = "";
}
public record ControlPlanInput(Guid RoutingRevisionId, string Code, string ValidFromJalali, string? ValidUntilJalali);
public record ControlCharacteristicInput(int OperationSequence, string Code, string Description,
    string Unit, decimal? Minimum, decimal? Maximum, bool Mandatory);
public record ManufacturingInspectionInput(decimal Value, decimal NonconformingQuantity, string? Note);
public record ManufacturingLotLinkInput(Guid SourceConfirmationId, Guid DestinationConfirmationId, decimal Quantity);

public static class ManufacturingInspectionRules
{
    public static bool Passed(ManufacturingInspectionRequirement item,decimal value) =>
        (!item.Minimum.HasValue || value>=item.Minimum.Value) &&
        (!item.Maximum.HasValue || value<=item.Maximum.Value);
    public static bool GatePassed(ManufacturingOrderOperation operation) =>
        operation.GoodQuantity+operation.ScrapQuantity+operation.ReworkQuantity==0 ||
        operation.InspectionRequirements.All(x=>!x.Mandatory || x.LastPassed==true);
    public static bool CanComplete(ManufacturingOrder order) =>
        order.Operations.Count>0 &&
        order.Operations.All(op=>op.IsComplete && op.QualityPendingQuantity==0 && GatePassed(op));
    public static bool TryLinkLot(ManufacturingOrder order,ManufacturingConfirmation source,
        ManufacturingConfirmation destination,decimal quantity,decimal alreadyFrom,decimal alreadyTo,out string error)
    {
        error="";
        if(quantity<=0){error="مقدار ارتباط بچ باید مثبت باشد.";return false;}
        var steps=order.Operations.OrderBy(x=>x.Sequence).ToList();
        var from=steps.FindIndex(x=>x.Id==source.OperationId);
        var to=steps.FindIndex(x=>x.Id==destination.OperationId);
        if(from<0||to!=from+1 || source.Id==destination.Id){
            error="بچ‌های مبدا و مقصد باید متعلق به دو عملیات متوالی همین دستور باشند.";return false;
        }
        if(source.GoodQuantity-alreadyFrom<quantity || destination.GoodQuantity-alreadyTo<quantity){
            error="ارتباط بچ از تولید سالم ثبت‌شده یا مقدار تخصیص‌نیافته بیشتر است.";return false;
        }
        if(source.OccurredUtc>destination.OccurredUtc){
            error="ثبت بچ مقصد نباید قبل از ثبت بچ مبدا باشد.";return false;
        }
        if(string.IsNullOrWhiteSpace(source.LotCode)||string.IsNullOrWhiteSpace(destination.LotCode)){
            error="هر دو گزارش عملیات باید کد بچ داشته باشند.";return false;
        }
        // Linking records provenance, not inventory movement; quality holds on the operation
        // remain separately enforced in manufacturing confirmations and work-in-process.
        return true;
    }
}

public static class ManufacturingControlPlanEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingControlPlan>().HasIndex(x=>new{x.RoutingRevisionId,x.Code}).IsUnique();
        model.Entity<ManufacturingControlPlan>().HasOne<RoutingRevision>().WithMany()
            .HasForeignKey(x=>x.RoutingRevisionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingControlPlan>().Property(x=>x.Code).HasMaxLength(64);
        model.Entity<ManufacturingControlPlan>().Property(x=>x.ApprovedBy).HasMaxLength(256);
        model.Entity<ControlPlanCharacteristic>().HasOne(x=>x.Plan).WithMany(x=>x.Characteristics)
            .HasForeignKey(x=>x.PlanId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ControlPlanCharacteristic>().HasIndex(x=>new{x.PlanId,x.OperationSequence,x.Code}).IsUnique();
        model.Entity<ControlPlanCharacteristic>().Property(x=>x.Code).HasMaxLength(64);
        model.Entity<ControlPlanCharacteristic>().Property(x=>x.Description).HasMaxLength(240);
        model.Entity<ControlPlanCharacteristic>().Property(x=>x.Unit).HasMaxLength(32);
        model.Entity<ControlPlanCharacteristic>().Property(x=>x.Minimum).HasPrecision(18,6);
        model.Entity<ControlPlanCharacteristic>().Property(x=>x.Maximum).HasPrecision(18,6);
        model.Entity<ManufacturingInspectionRequirement>().HasOne(x=>x.Operation)
            .WithMany(x=>x.InspectionRequirements).HasForeignKey(x=>x.OrderOperationId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ManufacturingInspectionRequirement>().HasIndex(x=>new{x.OrderOperationId,x.Code}).IsUnique();
        model.Entity<ManufacturingInspectionRequirement>().HasOne<ControlPlanCharacteristic>().WithMany()
            .HasForeignKey(x=>x.SourceCharacteristicId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingInspectionRequirement>().Property(x=>x.Code).HasMaxLength(64);
        model.Entity<ManufacturingInspectionRequirement>().Property(x=>x.Description).HasMaxLength(240);
        model.Entity<ManufacturingInspectionRequirement>().Property(x=>x.Unit).HasMaxLength(32);
        model.Entity<ManufacturingInspectionRequirement>().Property(x=>x.Minimum).HasPrecision(18,6);
        model.Entity<ManufacturingInspectionRequirement>().Property(x=>x.Maximum).HasPrecision(18,6);
        model.Entity<ManufacturingInspectionResult>().HasOne(x=>x.Requirement).WithMany()
            .HasForeignKey(x=>x.RequirementId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingInspectionResult>().HasOne<ManufacturingOrder>().WithMany()
            .HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingInspectionResult>().Property(x=>x.Value).HasPrecision(18,6);
        model.Entity<ManufacturingInspectionResult>().Property(x=>x.QuarantinedQuantity).HasPrecision(18,4);
        model.Entity<ManufacturingInspectionResult>().Property(x=>x.RecordedBy).HasMaxLength(256);
        model.Entity<ManufacturingInspectionResult>().Property(x=>x.Note).HasMaxLength(500);
        model.Entity<ManufacturingLotGenealogy>().HasIndex(x=>new{x.SourceConfirmationId,x.DestinationConfirmationId}).IsUnique();
        model.Entity<ManufacturingLotGenealogy>().HasOne<ManufacturingOrder>().WithMany()
            .HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingLotGenealogy>().HasOne<ManufacturingConfirmation>().WithMany()
            .HasForeignKey(x=>x.SourceConfirmationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingLotGenealogy>().HasOne<ManufacturingConfirmation>().WithMany()
            .HasForeignKey(x=>x.DestinationConfirmationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingLotGenealogy>().Property(x=>x.Quantity).HasPrecision(18,4);
        model.Entity<ManufacturingLotGenealogy>().Property(x=>x.LinkedBy).HasMaxLength(256);
    }

    public static IEndpointRouteBuilder MapControlPlanEndpoints(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/control-plans",async(ProductionDb db,CancellationToken ct)=>{
            var rows=await db.ManufacturingControlPlans.AsNoTracking()
                .Include(x=>x.Characteristics).OrderBy(x=>x.Code).Take(500).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.Id,x.RoutingRevisionId,x.Code,x.State,
                validFromJalali=PersianPlanningDates.Format(x.ValidFrom),
                validUntilJalali=x.ValidUntil.HasValue?PersianPlanningDates.Format(x.ValidUntil.Value):null,
                x.ApprovedBy,x.ApprovedUtc,
                characteristics=x.Characteristics.OrderBy(y=>y.OperationSequence).Select(y=>new{
                    y.Id,y.OperationSequence,y.Code,y.Description,y.Unit,y.Minimum,y.Maximum,y.Mandatory})}));
        });
        api.MapPost("/control-plans",async(ControlPlanInput input,ProductionDb db,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Code)||input.Code.Trim().Length>64
                ||!PersianPlanningDates.TryParse(input.ValidFromJalali,out var from))
                return Results.BadRequest(new{message="کد و تاریخ شمسی معتبر لازم است."});
            DateOnly? end=null;
            if(!string.IsNullOrWhiteSpace(input.ValidUntilJalali)){
                if(!PersianPlanningDates.TryParse(input.ValidUntilJalali,out var parsed)||parsed<from)
                    return Results.BadRequest(new{message="تاریخ پایان کنترل پلن نامعتبر است."});
                end=parsed;
            }
            if(!await db.RoutingRevisions.AnyAsync(x=>x.Id==input.RoutingRevisionId,ct))
                return Results.BadRequest(new{message="مسیر ساخت وجود ندارد."});
            var code=input.Code.Trim();
            if(await db.ManufacturingControlPlans.AnyAsync(x=>x.RoutingRevisionId==input.RoutingRevisionId&&x.Code==code,ct))
                return Results.Conflict(new{message="کد کنترل پلن قبلاً در این مسیر ثبت شده است."});
            var plan=new ManufacturingControlPlan{RoutingRevisionId=input.RoutingRevisionId,
                Code=code,ValidFrom=from,ValidUntil=end};
            db.ManufacturingControlPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/control-plans/{plan.Id}",new{plan.Id,plan.Code,plan.State});
        }).RequireAuthorization("IpQualityApprove");
        api.MapPost("/control-plans/{id:guid}/characteristics",async(Guid id,ControlCharacteristicInput input,
            ProductionDb db,CancellationToken ct)=>{
            if(input.OperationSequence<=0||string.IsNullOrWhiteSpace(input.Code)||input.Code.Trim().Length>64
                ||string.IsNullOrWhiteSpace(input.Description)||input.Description.Length>240
                ||string.IsNullOrWhiteSpace(input.Unit)||input.Unit.Length>32
                ||(input.Minimum.HasValue&&input.Maximum.HasValue&&input.Minimum>input.Maximum))
                return Results.BadRequest(new{message="مشخصات کنترل کیفی و حدود مجاز معتبر نیست."});
            var plan=await db.ManufacturingControlPlans.FindAsync([id],ct);
            if(plan is null)return Results.NotFound();
            if(plan.State!=ControlPlanState.Draft)return Results.Conflict(new{message="کنترل پلن مصوب قابل ویرایش نیست."});
            if(!await db.RoutingOperations.AnyAsync(x=>x.RoutingRevisionId==plan.RoutingRevisionId
                && x.Sequence==input.OperationSequence,ct))
                return Results.BadRequest(new{message="شماره عملیات در مسیر مربوطه وجود ندارد."});
            if(await db.ControlPlanCharacteristics.AnyAsync(x=>x.PlanId==id
                && x.OperationSequence==input.OperationSequence && x.Code==input.Code.Trim(),ct))
                return Results.Conflict(new{message="کد ویژگی در این عملیات تکراری است."});
            var value=new ControlPlanCharacteristic{PlanId=id,OperationSequence=input.OperationSequence,
                Code=input.Code.Trim(),Description=input.Description.Trim(),Unit=input.Unit.Trim(),
                Minimum=input.Minimum,Maximum=input.Maximum,Mandatory=input.Mandatory};
            db.ControlPlanCharacteristics.Add(value);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/control-plans/{id}",new{value.Id});
        }).RequireAuthorization("IpQualityApprove");
        api.MapPost("/control-plans/{id:guid}/approve",async(Guid id,ProductionDb db,
            HttpContext ctx,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var plan=await db.ManufacturingControlPlans.Include(x=>x.Characteristics)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(plan is null)return Results.NotFound();
            if(plan.State!=ControlPlanState.Draft)return Results.Conflict(new{message="این کنترل پلن قبلاً تأیید شده است."});
            if(plan.Characteristics.Count==0)return Results.BadRequest(new{message="کنترل پلن بدون مشخصه قابل تأیید نیست."});
            var overlap=await db.ManufacturingControlPlans.AsNoTracking().AnyAsync(x=>
                x.Id!=id && x.RoutingRevisionId==plan.RoutingRevisionId && x.State==ControlPlanState.Approved
                && x.ValidFrom<=(plan.ValidUntil??DateOnly.MaxValue)
                && plan.ValidFrom<=(x.ValidUntil??DateOnly.MaxValue),ct);
            if(overlap)return Results.Conflict(new{message="بازه کنترل پلن با نسخه مصوب دیگری همپوشانی دارد."});
            plan.State=ControlPlanState.Approved;plan.ApprovedUtc=DateTime.UtcNow;
            plan.ApprovedBy=ctx.User.Identity?.Name??"unknown";
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Ok(new{plan.Id,plan.State});
        }).RequireAuthorization("IpQualityApprove");

        api.MapGet("/manufacturing-orders/{id:guid}/inspection-requirements",async(Guid id,
            ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements).SingleOrDefaultAsync(x=>x.Id==id,ct);
            return order is null?Results.NotFound():Results.Ok(new{orderId=id,
                requirements=order.Operations.OrderBy(x=>x.Sequence).SelectMany(op=>
                    op.InspectionRequirements.Select(x=>new{
                        x.Id,operationId=op.Id,operationSequence=op.Sequence,operation=op.Name,x.Code,x.Description,
                        x.Unit,x.Minimum,x.Maximum,x.Mandatory,x.LastPassed,x.LastMeasuredUtc}))});
        });
        api.MapGet("/manufacturing-orders/{id:guid}/inspection-results",async(Guid id,
            ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            var rows=await db.ManufacturingInspectionResults.AsNoTracking().Include(x=>x.Requirement)
                .Where(x=>x.OrderId==id).OrderByDescending(x=>x.RecordedUtc).Take(500).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.Id,x.RequirementId,x.Value,x.Passed,x.QuarantinedQuantity,
                x.Note,x.RecordedBy,x.RecordedUtc,characteristic=x.Requirement.Code}));
        });
        api.MapPost("/manufacturing-orders/{id:guid}/inspections/{requirementId:guid}",
            async(Guid id,Guid requirementId,ManufacturingInspectionInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
            if((input.Note?.Length??0)>500||input.NonconformingQuantity<0)
                return Results.BadRequest(new{message="مقدار عدم‌انطباق یا توضیح نامعتبر است."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                return Results.Conflict(new{message="دستور ساخت در وضعیت فعال نیست."});
            var req=order.Operations.SelectMany(x=>x.InspectionRequirements)
                .SingleOrDefault(x=>x.Id==requirementId);
            if(req is null)return Results.NotFound();
            var op=order.Operations.Single(x=>x.Id==req.OrderOperationId);
            if(op.GoodQuantity+op.ScrapQuantity+op.ReworkQuantity<=0)
                return Results.Conflict(new{message="ابتدا باید نتیجه عملیات ساخت ثبت شود."});
            var passed=ManufacturingInspectionRules.Passed(req,input.Value);
            if(!passed && input.NonconformingQuantity<=0)
                return Results.BadRequest(new{message="برای اندازه‌گیری مردود، مقدار نامنطبق باید مشخص شود."});
            if(passed && input.NonconformingQuantity!=0)
                return Results.BadRequest(new{message="برای اندازه‌گیری قبول، مقدار نامنطبق باید صفر باشد."});
            if(!passed && !ManufacturingPerformance.CanHold(order,op.Id,input.NonconformingQuantity,out var error))
                return Results.Conflict(new{message=error});
            if(!passed){
                op.QualityPendingQuantity+=input.NonconformingQuantity;
                db.ManufacturingQualityHolds.Add(new ManufacturingQualityHold{
                    OrderId=id,OperationId=op.Id,Quantity=input.NonconformingQuantity,
                    Reason=$"ControlPlan:{req.Code}; measured={input.Value}",
                    RaisedBy=ctx.User.Identity?.Name??"unknown"
                });
            }
            req.LastPassed=passed;req.LastMeasuredUtc=DateTime.UtcNow;
            db.ManufacturingInspectionResults.Add(new ManufacturingInspectionResult{
                OrderId=id,RequirementId=req.Id,Value=input.Value,Passed=passed,
                QuarantinedQuantity=passed?0:input.NonconformingQuantity,
                Note=input.Note?.Trim(),RecordedBy=ctx.User.Identity?.Name??"unknown"
            });
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action=passed?"InspectionPass":"InspectionFail",
                Details=$"Operation={op.Sequence};Characteristic={req.Code};Pass={passed}"
            });
            if(ManufacturingInspectionRules.CanComplete(order)){
                order.Status=ManufacturingOrderStatus.Completed;
                order.CompletedUtc=DateTime.UtcNow;
            }
            order.UpdatedUtc=DateTime.UtcNow;
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="ثبت هم‌زمان بازرسی رخ داد؛ بازخوانی کنید."});}
            return Results.Ok(new{requirementId=req.Id,passed,orderStatus=order.Status});
        }).RequireAuthorization("IpQualityApprove");
        api.MapGet("/manufacturing-orders/{id:guid}/confirmations",async(Guid id,Guid? operationId,
            ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            var rows=await db.ManufacturingConfirmations.AsNoTracking()
                .Join(db.ManufacturingOrderOperations.AsNoTracking(),
                    conf=>conf.OperationId,op=>op.Id,(conf,op)=>new{conf,op})
                .Where(x=>x.op.OrderId==id && (!operationId.HasValue||x.op.Id==operationId.Value))
                .OrderBy(x=>x.conf.OccurredUtc).Take(1500).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{
                x.conf.Id,x.conf.OperationId,operationSequence=x.op.Sequence,x.conf.LotCode,
                x.conf.GoodQuantity,x.conf.ScrapQuantity,x.conf.ReworkQuantity,x.conf.OccurredUtc
            }));
        });
        api.MapGet("/manufacturing-orders/{id:guid}/lot-genealogy",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            var links=await db.ManufacturingLotGenealogies.AsNoTracking()
                .Where(x=>x.OrderId==id).OrderBy(x=>x.LinkedUtc).Take(1000).ToListAsync(ct);
            return Results.Ok(links);
        });
        api.MapPost("/manufacturing-orders/{id:guid}/lot-genealogy",
            async(Guid id,ManufacturingLotLinkInput input,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(order.Status is ManufacturingOrderStatus.Draft or ManufacturingOrderStatus.Cancelled)
                return Results.Conflict(new{message="دستور فاقد عملیات ثبت‌شده است."});
            var confirmations=await db.ManufacturingConfirmations.AsNoTracking()
                .Where(x=>x.Id==input.SourceConfirmationId||x.Id==input.DestinationConfirmationId).ToListAsync(ct);
            var source=confirmations.SingleOrDefault(x=>x.Id==input.SourceConfirmationId);
            var destination=confirmations.SingleOrDefault(x=>x.Id==input.DestinationConfirmationId);
            if(source is null||destination is null)return Results.BadRequest(new{message="گزارش ساخت مبدا یا مقصد یافت نشد."});
            var links=await db.ManufacturingLotGenealogies.AsNoTracking()
                .Where(x=>x.SourceConfirmationId==source.Id || x.DestinationConfirmationId==destination.Id).ToListAsync(ct);
            if(links.Any(x=>x.SourceConfirmationId==source.Id&&x.DestinationConfirmationId==destination.Id))
                return Results.Conflict(new{message="این ارتباط قبلاً ثبت شده است."});
            var fromAllocated=links.Where(x=>x.SourceConfirmationId==source.Id).Sum(x=>x.Quantity);
            var toAllocated=links.Where(x=>x.DestinationConfirmationId==destination.Id).Sum(x=>x.Quantity);
            if(!ManufacturingInspectionRules.TryLinkLot(order,source,destination,input.Quantity,
                fromAllocated,toAllocated,out var error))
                return Results.Conflict(new{message=error});
            var link=new ManufacturingLotGenealogy{OrderId=id,SourceConfirmationId=source.Id,
                DestinationConfirmationId=destination.Id,Quantity=input.Quantity,
                LinkedBy=ctx.User.Identity?.Name??"unknown"};
            db.ManufacturingLotGenealogies.Add(link);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=id,Action="LotLinked",
                Details=$"From={source.LotCode};To={destination.LotCode};Qty={input.Quantity}"});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/manufacturing-orders/{id}/lot-genealogy",new{link.Id});
        }).RequireAuthorization("IpQualityApprove");
        return routes;
    }
}
