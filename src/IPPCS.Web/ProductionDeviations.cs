using Microsoft.EntityFrameworkCore;
using System.Data;

public enum ProductionDeviationStatus { Open, ActionAssigned, Closed }
public enum ProductionDeviationCategory { Quantity, DeliveryDate, Quality, Material, Capacity, Downtime, Other }

public sealed class ProductionDeviation
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid MpsLineId { get; set; }
    public Guid? OrderId { get; set; }
    public ProductionDeviationCategory Category { get; set; }
    public string Description { get; set; }="";
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public string? Responsible { get; set; }
    public DateOnly? ActionDueOn { get; set; }
    public ProductionDeviationStatus Status { get; set; }=ProductionDeviationStatus.Open;
    public DateTime CreatedUtc { get; set; }=DateTime.UtcNow;
    public DateTime? ClosedUtc { get; set; }
    public string CreatedBy { get; set; }="";
    public List<ProductionDeviationEvent> Events { get; set; }=new();
}
public sealed class ProductionDeviationEvent
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid DeviationId { get; set; }
    public ProductionDeviation Deviation { get; set; }=null!;
    public ProductionDeviationStatus Status { get; set; }
    public string Details { get; set; }="";
    public string Username { get; set; }="";
    public DateTime AtUtc { get; set; }=DateTime.UtcNow;
}
public record DeviationInput(Guid MpsLineId,Guid? OrderId,ProductionDeviationCategory Category,string Description);
public record DeviationActionInput(string RootCause,string CorrectiveAction,string Responsible,string ActionDueJalali);
public record DeviationCloseInput(string Verification);

public static class ProductionDeviationEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ProductionDeviation>().HasOne<MpsDemandLine>().WithMany()
            .HasForeignKey(x=>x.MpsLineId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionDeviation>().HasOne<ManufacturingOrder>().WithMany()
            .HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionDeviation>().Property(x=>x.Description).HasMaxLength(1000);
        model.Entity<ProductionDeviation>().Property(x=>x.RootCause).HasMaxLength(1000);
        model.Entity<ProductionDeviation>().Property(x=>x.CorrectiveAction).HasMaxLength(1000);
        model.Entity<ProductionDeviation>().Property(x=>x.Responsible).HasMaxLength(200);
        model.Entity<ProductionDeviation>().Property(x=>x.CreatedBy).HasMaxLength(256);
        model.Entity<ProductionDeviation>().HasIndex(x=>new{x.MpsLineId,x.Status});
        model.Entity<ProductionDeviationEvent>().HasOne(x=>x.Deviation).WithMany(x=>x.Events)
            .HasForeignKey(x=>x.DeviationId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ProductionDeviationEvent>().Property(x=>x.Details).HasMaxLength(1000);
        model.Entity<ProductionDeviationEvent>().Property(x=>x.Username).HasMaxLength(256);
    }
    private static object Dto(ProductionDeviation row)=>new{
        row.Id,row.MpsLineId,row.OrderId,row.Category,row.Description,row.RootCause,
        row.CorrectiveAction,row.Responsible,row.Status,row.CreatedUtc,row.ClosedUtc,row.CreatedBy,
        actionDueJalali=row.ActionDueOn.HasValue?PersianPlanningDates.Format(row.ActionDueOn.Value):null,
        events=row.Events.OrderBy(x=>x.AtUtc).Select(x=>new{x.Status,x.Details,x.Username,x.AtUtc})
    };
    public static IEndpointRouteBuilder MapProductionDeviations(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/production-deviations",async(Guid? mpsLineId,ProductionDb db,CancellationToken ct)=>{
            var q=db.ProductionDeviations.AsNoTracking().Include(x=>x.Events).AsQueryable();
            if(mpsLineId.HasValue)q=q.Where(x=>x.MpsLineId==mpsLineId.Value);
            var list=await q.OrderByDescending(x=>x.CreatedUtc).Take(300).ToListAsync(ct);
            return Results.Ok(list.Select(Dto));
        });
        api.MapPost("/production-deviations",async(DeviationInput input,ProductionDb db,
            HttpContext ctx,CancellationToken ct)=>{
            if(!Enum.IsDefined(input.Category)||string.IsNullOrWhiteSpace(input.Description)
                ||input.Description.Trim().Length>1000)
                return Results.BadRequest(new{message="نوع و شرح انحراف معتبر لازم است."});
            var line=await db.MpsDemandLines.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.MpsLineId,ct);
            if(line is null)return Results.BadRequest(new{message="ردیف MPS وجود ندارد."});
            var approved=await db.MpsVersions.AsNoTracking().AnyAsync(x=>x.Id==line.MpsVersionId
                && x.State==PlanningApproval.Approved,ct);
            if(!approved)return Results.Conflict(new{message="انحراف فقط برای برنامه مصوب ثبت می‌شود."});
            if(input.OrderId.HasValue)
            {
                var linked=await db.ManufacturingOrderPegs.AsNoTracking()
                    .AnyAsync(x=>x.OrderId==input.OrderId.Value&&x.MpsLineId==line.Id,ct);
                if(!linked)return Results.Conflict(new{message="دستور انتخابی به این ردیف MPS تخصیص داده نشده است."});
            }
            var user=ctx.User.Identity?.Name??"unknown";
            var item=new ProductionDeviation{MpsLineId=line.Id,OrderId=input.OrderId,
                Category=input.Category,Description=input.Description.Trim(),CreatedBy=user};
            item.Events.Add(new ProductionDeviationEvent{
                Status=ProductionDeviationStatus.Open,Username=user,Details="ایجاد انحراف برنامه"});
            db.ProductionDeviations.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/production-deviations/{item.Id}",Dto(item));
        }).RequireAuthorization("IpPlanningWrite");
        api.MapPost("/production-deviations/{id:guid}/action",async(Guid id,DeviationActionInput input,
            ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.RootCause)||input.RootCause.Length>1000
                ||string.IsNullOrWhiteSpace(input.CorrectiveAction)||input.CorrectiveAction.Length>1000
                ||string.IsNullOrWhiteSpace(input.Responsible)||input.Responsible.Length>200
                ||!PersianPlanningDates.TryParse(input.ActionDueJalali,out var due))
                return Results.BadRequest(new{message="علت ریشه‌ای، اقدام، مسئول و موعد شمسی معتبر لازم است."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var row=await db.ProductionDeviations.SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(row is null)return Results.NotFound();
            if(row.Status==ProductionDeviationStatus.Closed)
                return Results.Conflict(new{message="انحراف مختومه قابل بازنویسی نیست."});
            row.RootCause=input.RootCause.Trim();row.CorrectiveAction=input.CorrectiveAction.Trim();
            row.Responsible=input.Responsible.Trim();row.ActionDueOn=due;
            row.Status=ProductionDeviationStatus.ActionAssigned;
            db.ProductionDeviationEvents.Add(new ProductionDeviationEvent{
                DeviationId=id,Status=row.Status,
                Details="بررسی علت ریشه‌ای و تعیین اقدام اصلاحی",
                Username=ctx.User.Identity?.Name??"unknown"});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Ok(new{row.Id,row.Status});
        }).RequireAuthorization("IpPlanningWrite");
        api.MapPost("/production-deviations/{id:guid}/close",async(Guid id,DeviationCloseInput input,
            ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Verification)||input.Verification.Trim().Length>1000)
                return Results.BadRequest(new{message="مدرک یا شرح اثربخشی اقدام الزامی است."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var row=await db.ProductionDeviations.SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(row is null)return Results.NotFound();
            if(row.Status!=ProductionDeviationStatus.ActionAssigned)
                return Results.Conflict(new{message="ابتدا باید علت و اقدام اصلاحی ثبت شود."});
            row.Status=ProductionDeviationStatus.Closed;row.ClosedUtc=DateTime.UtcNow;
            db.ProductionDeviationEvents.Add(new ProductionDeviationEvent{
                DeviationId=id,Status=row.Status,Details=input.Verification.Trim(),
                Username=ctx.User.Identity?.Name??"unknown"});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Ok(new{row.Id,row.Status,row.ClosedUtc});
        }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
