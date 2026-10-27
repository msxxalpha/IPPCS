using System.Globalization;
using Microsoft.EntityFrameworkCore;

public enum RoutingState { Draft, Approved, Superseded }
public sealed class RoutingRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public string RevisionCode { get; set; } = "A";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public RoutingState State { get; set; } = RoutingState.Draft;
    public List<RoutingOperation> Operations { get; set; } = new();
}
public sealed class RoutingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoutingRevisionId { get; set; }
    public RoutingRevision RoutingRevision { get; set; } = null!;
    public int Sequence { get; set; }
    public string Name { get; set; } = "";
    public Guid WorkCenterId { get; set; }
    public decimal SetupMinutes { get; set; }
    public decimal RunMinutesPerUnit { get; set; }
    public decimal YieldPercent { get; set; } = 100m;
}
public sealed class WorkShift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkCenterId { get; set; }
    // Matches System.DayOfWeek: Sunday 0 ... Saturday 6
    public int DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal EffectiveCapacityPercent { get; set; } = 100m;
}
public record RoutingInput(Guid ItemId, string RevisionCode, string EffectiveFromJalali, string? EffectiveToJalali);
public record OperationInput(int Sequence, string Name, Guid WorkCenterId, decimal SetupMinutes, decimal RunMinutesPerUnit, decimal YieldPercent);
public record ShiftInput(Guid WorkCenterId, int DayOfWeek, string StartTime, string EndTime, decimal EffectiveCapacityPercent);
public record JalaliPlanInput(Guid ItemId, decimal Quantity, string DueDateJalali);

public static class PersianPlanningDates
{
    private static readonly PersianCalendar Calendar = new();
    public static bool TryParse(string? input, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var text = NormalizeDigits(input.Trim()).Replace('-', '/');
        var parts = text.Split('/');
        if (parts.Length != 3 || parts[0].Length != 4 || parts[1].Length is < 1 or > 2 ||
            parts[2].Length is < 1 or > 2 ||
            !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m) ||
            !int.TryParse(parts[2], out var d)) return false;
        try
        {
            value = DateOnly.FromDateTime(Calendar.ToDateTime(y, m, d, 0, 0, 0, 0));
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    public static string Format(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return $"{Calendar.GetYear(dt):0000}/{Calendar.GetMonth(dt):00}/{Calendar.GetDayOfMonth(dt):00}";
    }
    public static string NormalizeDigits(string text)
    {
        const string fa = "۰۱۲۳۴۵۶۷۸۹", ar = "٠١٢٣٤٥٦٧٨٩";
        return string.Concat(text.Select(c => fa.IndexOf(c) is var f && f >= 0 ? (char)('0' + f)
            : ar.IndexOf(c) is var a && a >= 0 ? (char)('0' + a) : c));
    }
}

public static class ManufacturingFoundationEndpoints
{
    public static IEndpointRouteBuilder MapManufacturingFoundation(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");

        api.MapGet("/calendar/convert", (string jalali) =>
            PersianPlanningDates.TryParse(jalali, out var date)
                ? Results.Ok(new { jalali = PersianPlanningDates.Format(date), gregorian = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })
                : Results.BadRequest(new { message = "تاریخ شمسی معتبر نیست." }));

        api.MapGet("/routings", async (ProductionDb db, CancellationToken ct) =>
        {
            var rows = await db.RoutingRevisions.AsNoTracking().Include(x => x.Operations)
                .OrderBy(x => x.ItemId).ThenBy(x => x.RevisionCode).Take(500).ToListAsync(ct);
            return Results.Ok(rows.Select(x => new {
                x.Id, x.ItemId, x.RevisionCode, x.State,
                effectiveFromJalali = PersianPlanningDates.Format(x.EffectiveFrom),
                effectiveToJalali = x.EffectiveTo.HasValue ? PersianPlanningDates.Format(x.EffectiveTo.Value) : null,
                operations = x.Operations.OrderBy(o => o.Sequence).Select(o => new { o.Id, o.Sequence, o.Name, o.WorkCenterId, o.SetupMinutes, o.RunMinutesPerUnit, o.YieldPercent }).ToArray()
            }));
        });

        api.MapPost("/routings", async (RoutingInput input, ProductionDb db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.RevisionCode) || input.RevisionCode.Length > 32 ||
                !PersianPlanningDates.TryParse(input.EffectiveFromJalali, out var from))
                return Results.BadRequest(new { message = "نسخه مسیر و تاریخ شروع معتبر لازم است." });
            DateOnly? to = null;
            if (!string.IsNullOrWhiteSpace(input.EffectiveToJalali))
            {
                if (!PersianPlanningDates.TryParse(input.EffectiveToJalali, out var end) || end < from)
                    return Results.BadRequest(new { message = "تاریخ پایان باید معتبر و بعد از شروع باشد." });
                to = end;
            }
            if (!await db.Items.AnyAsync(x => x.Id == input.ItemId, ct))
                return Results.BadRequest(new { message = "کالای انتخاب‌شده وجود ندارد." });
            var revision = input.RevisionCode.Trim();
            if (await db.RoutingRevisions.AnyAsync(x => x.ItemId == input.ItemId && x.RevisionCode == revision, ct))
                return Results.Conflict(new { message = "این نسخه مسیر قبلاً ثبت شده است." });
            var route = new RoutingRevision { ItemId = input.ItemId, RevisionCode = revision, EffectiveFrom = from, EffectiveTo = to };
            db.RoutingRevisions.Add(route);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/routings/{route.Id}", new { route.Id, route.RevisionCode, route.State });
        });

        api.MapPost("/routings/{id:guid}/operations", async (Guid id, OperationInput input, ProductionDb db, CancellationToken ct) =>
        {
            var route = await db.RoutingRevisions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (route is null) return Results.NotFound();
            if (route.State != RoutingState.Draft) return Results.Conflict(new { message = "فقط مسیر پیش‌نویس قابل تغییر است." });
            if (input.Sequence <= 0 || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 180 ||
                input.SetupMinutes < 0 || input.RunMinutesPerUnit <= 0 || input.YieldPercent is <= 0 or > 100)
                return Results.BadRequest(new { message = "مقادیر عملیات معتبر نیستند." });
            if (!await db.WorkCenters.AnyAsync(x => x.Id == input.WorkCenterId, ct))
                return Results.BadRequest(new { message = "مرکز کاری وجود ندارد." });
            if (await db.RoutingOperations.AnyAsync(x => x.RoutingRevisionId == id && x.Sequence == input.Sequence, ct))
                return Results.Conflict(new { message = "شماره ترتیب عملیات تکراری است." });
            var operation = new RoutingOperation {
                RoutingRevisionId = id, Sequence = input.Sequence, Name = input.Name.Trim(),
                WorkCenterId = input.WorkCenterId, SetupMinutes = input.SetupMinutes,
                RunMinutesPerUnit = input.RunMinutesPerUnit, YieldPercent = input.YieldPercent
            };
            db.RoutingOperations.Add(operation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/routings/{id}", new {
                operation.Id, operation.Sequence, operation.Name, operation.WorkCenterId,
                operation.SetupMinutes, operation.RunMinutesPerUnit, operation.YieldPercent
            });
        });

        api.MapPost("/routings/{id:guid}/approve", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            // Read only the immutable revision owner before taking the SQL lock.
            // Lock before SELECT under Serializable to avoid shared-lock/app-lock deadlock.
            var owner=await db.RoutingRevisions.AsNoTracking()
                .Where(x=>x.Id==id).Select(x=>(Guid?)x.ItemId).SingleOrDefaultAsync(ct);
            if(owner is null)return Results.NotFound();
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryRevisionGovernance.LockRevisionItem(db,"Routing",owner.Value,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل تصویب مسیر ساخت در دسترس نیست."});}
            var route = await db.RoutingRevisions.Include(x=>x.Operations)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (route is null) return Results.NotFound();
            if (route.State != RoutingState.Draft) return Results.Conflict(new { message = "مسیر پیش‌نویس نیست." });
            if (!await db.RoutingOperations.AnyAsync(x => x.RoutingRevisionId == id, ct))
                return Results.BadRequest(new { message = "مسیر بدون عملیات قابل تأیید نیست." });
            var other = await db.RoutingRevisions.AsNoTracking()
                .Where(x => x.Id != id && x.ItemId == route.ItemId && x.State == RoutingState.Approved)
                .ToListAsync(ct);
            var issues=FactoryRevisionGovernance.RoutingIssues(route,other);
            if(issues.Count>0)return Results.Conflict(new{message="مسیر ساخت برای تصویب آماده نیست.",issues});
            route.State = RoutingState.Approved;
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Ok(new { route.Id, route.State });
        });

        api.MapGet("/work-shifts", async (ProductionDb db, CancellationToken ct) =>
            await db.WorkShifts.AsNoTracking().OrderBy(x => x.WorkCenterId).ThenBy(x => x.DayOfWeek).ToListAsync(ct));

        api.MapPost("/work-shifts", async (ShiftInput input, ProductionDb db, CancellationToken ct) =>
        {
            if (input.DayOfWeek is < 0 or > 6 ||
                !TimeOnly.TryParseExact(PersianPlanningDates.NormalizeDigits(input.StartTime), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(PersianPlanningDates.NormalizeDigits(input.EndTime), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
                end <= start || input.EffectiveCapacityPercent is <= 0 or > 100)
                return Results.BadRequest(new { message = "شیفت معتبر نیست؛ فعلاً شیفت عبوری از نیمه‌شب پشتیبانی نمی‌شود." });
            if (!await db.WorkCenters.AnyAsync(x => x.Id == input.WorkCenterId, ct))
                return Results.BadRequest(new { message = "مرکز کاری وجود ندارد." });
            var shift = new WorkShift { WorkCenterId = input.WorkCenterId, DayOfWeek = input.DayOfWeek, StartTime = start, EndTime = end, EffectiveCapacityPercent = input.EffectiveCapacityPercent };
            var overlapping = await db.WorkShifts.AnyAsync(x => x.WorkCenterId == input.WorkCenterId &&
                x.DayOfWeek == input.DayOfWeek && x.StartTime < end && start < x.EndTime, ct);
            if (overlapping) return Results.Conflict(new { message = "ساعات شیفت با شیفت دیگری همپوشانی دارد." });
            db.WorkShifts.Add(shift);
            await db.SaveChangesAsync(ct);
            return Results.Created("/api/v1/work-shifts", shift);
        });

        api.MapPost("/production-plans/jalali", async (JalaliPlanInput input, ProductionDb db, CancellationToken ct) =>
        {
            if (!PersianPlanningDates.TryParse(input.DueDateJalali, out var dueDate) || input.Quantity <= 0)
                return Results.BadRequest(new { message = "تاریخ و مقدار برنامه معتبر نیست." });
            // Date-only planning deadline is represented by end of Gregorian UTC day for this prototype.
            var dueUtc = DateTime.SpecifyKind(dueDate.ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Utc);
            if (dueUtc <= DateTime.UtcNow) return Results.BadRequest(new { message = "موعد برنامه باید در آینده باشد." });
            if (!await db.Items.AnyAsync(x => x.Id == input.ItemId, ct)) return Results.BadRequest(new { message = "کالا وجود ندارد." });
            var plan = new ProductionPlan { ItemId = input.ItemId, Quantity = input.Quantity, DueDateUtc = dueUtc };
            db.ProductionPlans.Add(plan); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/production-plans/{plan.Id}", new {
                plan.Id, plan.ItemId, plan.Quantity, dueDateJalali = PersianPlanningDates.Format(dueDate)
            });
        });

        return routes;
    }
    public static void ConfigureManufacturingModel(ModelBuilder model)
    {
        model.Entity<RoutingRevision>().HasIndex(x => new { x.ItemId, x.RevisionCode }).IsUnique();
        model.Entity<RoutingRevision>().Property(x => x.RevisionCode).HasMaxLength(32);
        model.Entity<RoutingRevision>().HasOne<PlanningItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RoutingOperation>().HasIndex(x => new { x.RoutingRevisionId, x.Sequence }).IsUnique();
        model.Entity<RoutingOperation>().HasOne(x => x.RoutingRevision).WithMany(x => x.Operations).HasForeignKey(x => x.RoutingRevisionId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoutingOperation>().HasOne<WorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RoutingOperation>().Property(x => x.Name).HasMaxLength(180);
        model.Entity<RoutingOperation>().Property(x => x.SetupMinutes).HasPrecision(18, 4);
        model.Entity<RoutingOperation>().Property(x => x.RunMinutesPerUnit).HasPrecision(18, 6);
        model.Entity<RoutingOperation>().Property(x => x.YieldPercent).HasPrecision(8, 4);
        model.Entity<WorkShift>().HasOne<WorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<WorkShift>().Property(x => x.EffectiveCapacityPercent).HasPrecision(8, 4);
    }
}
