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
    // Stable IPPCS master-data codes for reliable longitudinal reporting.
    // Nullable to retain legacy manually entered factory cases as-is.
    public string? ResponsibleCode { get; set; }
    public string? RootCauseCategoryCode { get; set; }
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
public record DeviationActionInput(string RootCause,string CorrectiveAction,string Responsible,string ActionDueJalali,
    string? ResponsibleCode=null,string? RootCauseCategoryCode=null);
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
        model.Entity<ProductionDeviation>().Property(x=>x.ResponsibleCode).HasMaxLength(40);
        model.Entity<ProductionDeviation>().Property(x=>x.RootCauseCategoryCode).HasMaxLength(40);
        model.Entity<ProductionDeviation>().HasIndex(x=>x.ResponsibleCode);
        model.Entity<ProductionDeviation>().HasIndex(x=>x.RootCauseCategoryCode);
        model.Entity<ProductionDeviation>().Property(x=>x.CreatedBy).HasMaxLength(256);
        model.Entity<ProductionDeviation>().HasIndex(x=>new{x.MpsLineId,x.Status});
        model.Entity<ProductionDeviationEvent>().HasOne(x=>x.Deviation).WithMany(x=>x.Events)
            .HasForeignKey(x=>x.DeviationId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ProductionDeviationEvent>().Property(x=>x.Details).HasMaxLength(1000);
        model.Entity<ProductionDeviationEvent>().Property(x=>x.Username).HasMaxLength(256);
    }
    private static object Dto(ProductionDeviation row)=>new{
        row.Id,row.MpsLineId,row.OrderId,row.Category,row.Description,row.RootCause,
        row.CorrectiveAction,row.Responsible,row.ResponsibleCode,row.RootCauseCategoryCode,
        row.Status,row.CreatedUtc,row.ClosedUtc,row.CreatedBy,
        actionDueJalali=row.ActionDueOn.HasValue?PersianPlanningDates.Format(row.ActionDueOn.Value):null,
        effectiveness=FactoryDeviationEffectiveness.Evaluate(row),
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
        // Filter and page on SQL Server. The historical /production-deviations
        // list is retained for older clients; high-volume factory screens use
        // this bounded query so entries past the first 300 remain searchable.
        api.MapGet("/production-deviations/search",async(string? text,int? status,
            int? category,int page,int pageSize,ProductionDb db,CancellationToken ct)=>{
            if(page is <1 or >100000||pageSize is <1 or >100||
                text is {Length:>120}||
                status is not null&&!Enum.IsDefined((ProductionDeviationStatus)status.Value)||
                category is not null&&!Enum.IsDefined((ProductionDeviationCategory)category.Value))
                return Results.BadRequest(new{message="فیلتر، شماره صفحه یا تعداد ردیف نامعتبر است."});
            var query=db.ProductionDeviations.AsNoTracking().AsQueryable();
            if(status.HasValue)query=query.Where(x=>(int)x.Status==status.Value);
            if(category.HasValue)query=query.Where(x=>(int)x.Category==category.Value);
            var term=text?.Trim();
            if(!string.IsNullOrWhiteSpace(term))
                query=query.Where(x=>x.Description.Contains(term)||
                    (x.RootCause!=null&&x.RootCause.Contains(term))||
                    (x.CorrectiveAction!=null&&x.CorrectiveAction.Contains(term))||
                    (x.Responsible!=null&&x.Responsible.Contains(term))||
                    (x.ResponsibleCode!=null&&x.ResponsibleCode.Contains(term))||
                    (x.RootCauseCategoryCode!=null&&x.RootCauseCategoryCode.Contains(term)));
            var total=await query.CountAsync(ct);
            var rows=await query.Include(x=>x.Events)
                .OrderByDescending(x=>x.CreatedUtc).ThenByDescending(x=>x.Id)
                .Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new{total,page,pageSize,
                totalPages=total==0?0:(total+pageSize-1)/pageSize,
                hasPrevious=page>1,hasNext=(long)page*pageSize<total,
                rows=rows.Select(Dto).ToArray(),
                disclaimer="جست‌وجوی سروری و صفحه‌بندی بر اساس کدهای ثابت دسته/مسئول؛ برای سوابق قدیمیِ فاقد کد، متن قبلی حفظ می‌شود."});
        });
        api.MapPost("/production-deviations",async(DeviationInput input,ProductionDb db,
            HttpContext ctx,CancellationToken ct)=>{
            if(!Enum.IsDefined(input.Category)||string.IsNullOrWhiteSpace(input.Description)
                ||input.Description.Trim().Length>1000)
                return Results.BadRequest(new{message="نوع و شرح انحراف معتبر لازم است."});
            if(await FactoryMasterData.Active(db,FactoryMasterData.DeviationCategory,
                ((int)input.Category).ToString(),ct) is null)
                return Results.Conflict(new{message="نوع انحراف در اطلاعات پایه غیرفعال یا نامعتبر است."});
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
            try{await FactoryDeviationGovernance.LockDeviation(db,id,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){
                return Results.Conflict(new{message="قفل ثبت تغییر انحراف در دسترس نیست."});
            }
            var row=await db.ProductionDeviations.SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(row is null)return Results.NotFound();
            if(row.Status!=ProductionDeviationStatus.Open)
                return Results.Conflict(new{message="اقدام اولیه قبلاً ثبت شده است؛ برای تغییر از بازنگری مستند اقدام استفاده کنید."});
            var ownerCode=string.IsNullOrWhiteSpace(input.ResponsibleCode)
                ?null:FactoryMasterData.NormalizeCode(input.ResponsibleCode);
            var causeCode=string.IsNullOrWhiteSpace(input.RootCauseCategoryCode)
                ?null:FactoryMasterData.NormalizeCode(input.RootCauseCategoryCode);
            var owner=ownerCode is null?null:
                await FactoryMasterData.Active(db,FactoryMasterData.ActionOwner,ownerCode,ct);
            var cause=causeCode is null?null:
                await FactoryMasterData.Active(db,FactoryMasterData.RootCauseCategory,causeCode,ct);
            if(ownerCode is not null&&owner is null||causeCode is not null&&cause is null)
                return Results.Conflict(new{message="مسئول یا طبقه علت ریشه‌ای در اطلاعات پایه وجود ندارد یا غیرفعال است."});
            row.RootCause=input.RootCause.Trim();row.CorrectiveAction=input.CorrectiveAction.Trim();
            row.Responsible=owner?.Label??input.Responsible.Trim();
            row.ResponsibleCode=owner?.Code;row.RootCauseCategoryCode=cause?.Code;row.ActionDueOn=due;
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
            try{await FactoryDeviationGovernance.LockDeviation(db,id,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){
                return Results.Conflict(new{message="قفل ثبت تغییر انحراف در دسترس نیست."});
            }
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
