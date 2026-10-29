using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

// Periodic and annual product/process verification only. Calibration remains outside IPPCS.
public enum FactoryPeriodicTestCadence { Periodic = 0, Annual = 1 }
public enum FactoryPeriodicTestRunState { Scheduled = 0, Completed = 1 }
public enum FactoryPeriodicTestOutcome { None = 0, Passed = 1, Failed = 2, Inconclusive = 3 }

public sealed class FactoryPeriodicTestPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid ItemId { get; set; }
    public FactoryPeriodicTestCadence Cadence { get; set; }
    public int IntervalMonths { get; set; }
    public DateOnly FirstDueOn { get; set; }
    public DateOnly? ValidThrough { get; set; }
    public string MethodReference { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public int SampleCount { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FactoryPeriodicTestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public DateOnly DueOn { get; set; }
    public FactoryPeriodicTestRunState State { get; set; } = FactoryPeriodicTestRunState.Scheduled;
    public DateOnly? PerformedOn { get; set; }
    public string? SampleReference { get; set; }
    public decimal? MeasuredValue { get; set; }
    public FactoryPeriodicTestOutcome Outcome { get; set; } = FactoryPeriodicTestOutcome.None;
    public string? ReportReference { get; set; }
    public string? Notes { get; set; }
    public string? RecordedBy { get; set; }
    public DateTime? RecordedUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed record FactoryPeriodicTestPlanInput(
    string Code, string Name, Guid ItemId, string Cadence, int IntervalMonths,
    string FirstDueJalali, string? ValidThroughJalali,
    string MethodReference, string AcceptanceCriteria, string? Unit,
    decimal? Minimum, decimal? Maximum, int SampleCount);

public sealed record FactoryPeriodicTestScheduleInput(string FromJalali, string ThroughJalali);
public sealed record FactoryPeriodicTestResultInput(
    string PerformedOnJalali, string SampleReference, decimal? MeasuredValue,
    string Outcome, string? ReportReference, string? Notes);

public static class FactoryPeriodicTestRules
{
    private static readonly PersianCalendar Persian = new();

    // Calculate each recurrence from the original first due date to prevent
    // month-end clamping in one period from drifting all subsequent periods.
    public static DateOnly AddPersianMonths(DateOnly date, int months)
    {
        if (months < 0) throw new ArgumentOutOfRangeException(nameof(months));
        var source = date.ToDateTime(TimeOnly.MinValue);
        var totalMonth = checked((Persian.GetYear(source) - 1) * 12 +
            Persian.GetMonth(source) - 1 + months);
        var year = totalMonth / 12 + 1;
        var month = totalMonth % 12 + 1;
        var day = Math.Min(Persian.GetDayOfMonth(source), Persian.GetDaysInMonth(year, month));
        return DateOnly.FromDateTime(Persian.ToDateTime(year, month, day, 0, 0, 0, 0));
    }

    public static IReadOnlyList<DateOnly> DueDates(FactoryPeriodicTestPlan plan,
        DateOnly from, DateOnly through, int maximum = 5000)
    {
        if (from > through) throw new ArgumentException("بازه تاریخ آزمون نامعتبر است.");
        if (plan.IntervalMonths is < 1 or > 12 ||
            plan.Cadence == FactoryPeriodicTestCadence.Annual && plan.IntervalMonths != 12 ||
            plan.Cadence == FactoryPeriodicTestCadence.Periodic && plan.IntervalMonths is < 1 or > 11)
            throw new InvalidOperationException("تناوب آزمون ادواری/سالانه نامعتبر است.");
        if (!plan.IsActive || through < plan.FirstDueOn ||
            plan.ValidThrough.HasValue && from > plan.ValidThrough.Value)
            return Array.Empty<DateOnly>();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var firstDate = plan.FirstDueOn.ToDateTime(TimeOnly.MinValue);
        var monthDelta = (Persian.GetYear(fromDate) - Persian.GetYear(firstDate)) * 12 +
            Persian.GetMonth(fromDate) - Persian.GetMonth(firstDate);
        var index = Math.Max(0, monthDelta / plan.IntervalMonths);
        var due = AddPersianMonths(plan.FirstDueOn, checked(index * plan.IntervalMonths));
        while (due < from)
        {
            index++;
            due = AddPersianMonths(plan.FirstDueOn, checked(index * plan.IntervalMonths));
        }

        var result = new List<DateOnly>();
        while (due <= through && (!plan.ValidThrough.HasValue || due <= plan.ValidThrough.Value))
        {
            result.Add(due);
            if (result.Count > maximum)
                throw new InvalidOperationException("تعداد سررسیدهای آزمون از سقف هر درخواست بیشتر است؛ بازه را کوتاه کنید.");
            index++;
            due = AddPersianMonths(plan.FirstDueOn, checked(index * plan.IntervalMonths));
        }
        return result;
    }

    public static string? ValidateOutcome(FactoryPeriodicTestPlan plan, decimal? measuredValue,
        FactoryPeriodicTestOutcome outcome, string? notes)
    {
        if (outcome == FactoryPeriodicTestOutcome.None)
            return "نتیجه آزمون باید قبول، مردود یا نامعین باشد.";
        var hasNumericLimits = plan.Minimum.HasValue || plan.Maximum.HasValue;
        var outside = measuredValue.HasValue &&
            ((plan.Minimum.HasValue && measuredValue.Value < plan.Minimum.Value) ||
             (plan.Maximum.HasValue && measuredValue.Value > plan.Maximum.Value));
        if (outside && outcome != FactoryPeriodicTestOutcome.Failed)
            return "مقدار اندازه‌گیری خارج از حدود ثبت‌شده است؛ نتیجه نمی‌تواند قبول یا نامعین باشد.";
        if (hasNumericLimits && !measuredValue.HasValue && outcome == FactoryPeriodicTestOutcome.Passed)
            return "برای آزمونی با حدود عددی، ثبت نتیجه قبول بدون مقدار اندازه‌گیری مجاز نیست.";
        if (outcome is FactoryPeriodicTestOutcome.Failed or FactoryPeriodicTestOutcome.Inconclusive &&
            string.IsNullOrWhiteSpace(notes))
            return "برای نتیجه مردود یا نامعین، توضیح قابل ممیزی لازم است.";
        return null;
    }

    public static TimeZoneInfo TehranZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    public static DateOnly TodayTehran() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TehranZone));
}

public static class FactoryPeriodicProductTests
{
    private static object PlanView(FactoryPeriodicTestPlan x, PlanningItem item) => new
    {
        x.Id, x.Code, x.Name, x.ItemId, itemCode = item.Code, itemName = item.Name,
        cadence = x.Cadence.ToString(), x.IntervalMonths,
        firstDueJalali = PersianPlanningDates.Format(x.FirstDueOn),
        validThroughJalali = x.ValidThrough.HasValue ? PersianPlanningDates.Format(x.ValidThrough.Value) : null,
        x.MethodReference, x.AcceptanceCriteria, x.Unit, x.Minimum, x.Maximum,
        x.SampleCount, x.IsActive, x.CreatedBy, x.CreatedUtc
    };

    private static object RunView(FactoryPeriodicTestRun x, FactoryPeriodicTestPlan plan, PlanningItem item, DateOnly today) => new
    {
        x.Id, x.PlanId, planCode = plan.Code, planName = plan.Name,
        itemId = plan.ItemId, itemCode = item.Code, itemName = item.Name,
        cadence = plan.Cadence.ToString(), plan.IntervalMonths,
        dueJalali = PersianPlanningDates.Format(x.DueOn),
        state = x.State.ToString(),
        scheduleStatus = x.State == FactoryPeriodicTestRunState.Completed ? "Completed" :
            x.DueOn < today ? "Overdue" : "Scheduled",
        performedOnJalali = x.PerformedOn.HasValue ? PersianPlanningDates.Format(x.PerformedOn.Value) : null,
        x.SampleReference, x.MeasuredValue, outcome = x.Outcome.ToString(),
        x.ReportReference, x.Notes, x.RecordedBy, x.RecordedUtc, x.CreatedUtc,
        plan.Unit, plan.Minimum, plan.Maximum, plan.AcceptanceCriteria
    };

    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<FactoryPeriodicTestPlan>().HasKey(x => x.Id);
        model.Entity<FactoryPeriodicTestPlan>().HasIndex(x => x.Code).IsUnique();
        model.Entity<FactoryPeriodicTestPlan>().HasIndex(x => new { x.ItemId, x.IsActive });
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.Code).HasMaxLength(64);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.Name).HasMaxLength(180);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.MethodReference).HasMaxLength(120);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.AcceptanceCriteria).HasMaxLength(500);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.Unit).HasMaxLength(32);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.Minimum).HasPrecision(18, 6);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.Maximum).HasPrecision(18, 6);
        model.Entity<FactoryPeriodicTestPlan>().Property(x => x.CreatedBy).HasMaxLength(256);
        model.Entity<FactoryPeriodicTestPlan>().HasOne<PlanningItem>().WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<FactoryPeriodicTestRun>().HasKey(x => x.Id);
        model.Entity<FactoryPeriodicTestRun>().HasIndex(x => new { x.PlanId, x.DueOn }).IsUnique();
        model.Entity<FactoryPeriodicTestRun>().HasIndex(x => new { x.DueOn, x.State });
        model.Entity<FactoryPeriodicTestRun>().Property(x => x.SampleReference).HasMaxLength(100);
        model.Entity<FactoryPeriodicTestRun>().Property(x => x.MeasuredValue).HasPrecision(18, 6);
        model.Entity<FactoryPeriodicTestRun>().Property(x => x.ReportReference).HasMaxLength(120);
        model.Entity<FactoryPeriodicTestRun>().Property(x => x.Notes).HasMaxLength(500);
        model.Entity<FactoryPeriodicTestRun>().Property(x => x.RecordedBy).HasMaxLength(256);
        model.Entity<FactoryPeriodicTestRun>().HasOne<FactoryPeriodicTestPlan>().WithMany()
            .HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }

    public static IEndpointRouteBuilder MapFactoryPeriodicProductTests(this IEndpointRouteBuilder routes)
    {
        var read = routes.MapGroup("/api/v1/periodic-product-tests").RequireAuthorization("IpProductionUser");
        var quality = routes.MapGroup("/api/v1/periodic-product-tests").RequireAuthorization("IpQualityApprove");

        read.MapGet("/plans", async (ProductionDb db, CancellationToken ct) =>
        {
            var rows = await db.FactoryPeriodicTestPlans.AsNoTracking()
                .OrderBy(x => x.Code).Take(501).ToListAsync(ct);
            if (rows.Count > 500)
                return Results.Conflict(new { message = "تعداد برنامه‌های آزمون بیش از سقف نمایش است؛ فهرست را محدود کنید." });
            var itemIds = rows.Select(x => x.ItemId).Distinct().ToArray();
            var items = await db.Items.AsNoTracking().Where(x => itemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
            return Results.Ok(rows.Where(x => items.ContainsKey(x.ItemId)).Select(x => PlanView(x, items[x.ItemId])));
        });

        read.MapGet("/runs", async (string? fromJalali, string? throughJalali,
            ProductionDb db, CancellationToken ct) =>
        {
            if (!PersianPlanningDates.TryParse(fromJalali, out var from) ||
                !PersianPlanningDates.TryParse(throughJalali, out var through) ||
                from > through || through.DayNumber - from.DayNumber > 3660)
                return Results.BadRequest(new { message = "تاریخ شروع/پایان شمسی معتبر و بازه حداکثر ده‌ساله لازم است." });
            var rows = await db.FactoryPeriodicTestRuns.AsNoTracking()
                .Where(x => x.DueOn >= from && x.DueOn <= through)
                .OrderBy(x => x.DueOn).ThenBy(x => x.PlanId).Take(501).ToListAsync(ct);
            if (rows.Count > 500)
                return Results.Conflict(new { message = "تعداد رخدادهای آزمون بیش از سقف ۵۰۰ ردیف است؛ بازه را کوتاه کنید." });
            var today = FactoryPeriodicTestRules.TodayTehran();
            var planIds = rows.Select(x => x.PlanId).Distinct().ToArray();
            var plans = await db.FactoryPeriodicTestPlans.AsNoTracking().Where(x => planIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
            var itemIds = plans.Values.Select(x => x.ItemId).Distinct().ToArray();
            var items = await db.Items.AsNoTracking().Where(x => itemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
            return Results.Ok(rows.Where(x => plans.ContainsKey(x.PlanId) && items.ContainsKey(plans[x.PlanId].ItemId))
                .Select(x => RunView(x, plans[x.PlanId], items[plans[x.PlanId].ItemId], today)));
        });

        quality.MapPost("/plans", async (FactoryPeriodicTestPlanInput input, ProductionDb db,
            HttpContext http, CancellationToken ct) =>
        {
            var code = input.Code?.Trim() ?? "";
            var name = input.Name?.Trim() ?? "";
            var method = input.MethodReference?.Trim() ?? "";
            var criteria = input.AcceptanceCriteria?.Trim() ?? "";
            if (code.Length is < 1 or > 64 || name.Length is < 1 or > 180 ||
                method.Length is < 1 or > 120 || criteria.Length is < 1 or > 500 ||
                !Enum.TryParse<FactoryPeriodicTestCadence>(input.Cadence, true, out var cadence) ||
                !Enum.IsDefined(cadence) ||
                cadence == FactoryPeriodicTestCadence.Annual && input.IntervalMonths != 12 ||
                cadence == FactoryPeriodicTestCadence.Periodic && input.IntervalMonths is < 1 or > 11 ||
                !PersianPlanningDates.TryParse(input.FirstDueJalali, out var firstDue) ||
                input.SampleCount is < 1 or > 1000 ||
                input.Minimum.HasValue && input.Maximum.HasValue && input.Minimum > input.Maximum ||
                (input.Minimum.HasValue || input.Maximum.HasValue) && string.IsNullOrWhiteSpace(input.Unit))
                return Results.BadRequest(new { message = "کد، نام، روش آزمون، معیار پذیرش، تناوب، تاریخ و حدود آزمون باید معتبر باشند." });

            DateOnly? validThrough = null;
            if (!string.IsNullOrWhiteSpace(input.ValidThroughJalali))
            {
                if (!PersianPlanningDates.TryParse(input.ValidThroughJalali, out var end) || end < firstDue)
                    return Results.BadRequest(new { message = "پایان اعتبار آزمون باید پس از اولین سررسید باشد." });
                validThrough = end;
            }
            var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.ItemId, ct);
            if (item is null || item.Type is not (PlanningItemType.Finished or PlanningItemType.SemiFinished))
                return Results.BadRequest(new { message = "آزمون ادواری/سالانه باید به محصول نهایی یا نیمه‌ساخته داخلی مرتبط باشد." });
            string? unit = null;
            if (!string.IsNullOrWhiteSpace(input.Unit))
            {
                var activeUnit = await FactoryMasterData.Active(db, FactoryMasterData.UnitOfMeasure, input.Unit.Trim(), ct);
                if (activeUnit is null)
                    return Results.Conflict(new { message = "واحد آزمون در فهرست واحدهای فعال اطلاعات پایه موجود نیست." });
                unit = activeUnit.Code;
            }
            if (await db.FactoryPeriodicTestPlans.AnyAsync(x => x.Code == code, ct))
                return Results.Conflict(new { message = "کد برنامه آزمون تکراری است." });

            var plan = new FactoryPeriodicTestPlan
            {
                Code = code, Name = name, ItemId = item.Id, Cadence = cadence,
                IntervalMonths = input.IntervalMonths, FirstDueOn = firstDue, ValidThrough = validThrough,
                MethodReference = method, AcceptanceCriteria = criteria, Unit = unit,
                Minimum = input.Minimum, Maximum = input.Maximum, SampleCount = input.SampleCount,
                CreatedBy = http.User.Identity?.Name ?? "unknown", CreatedUtc = DateTime.UtcNow
            };
            db.FactoryPeriodicTestPlans.Add(plan);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { message = "کد برنامه آزمون هم‌زمان توسط درخواست دیگری ثبت شده است." });
            }
            return Results.Created($"/api/v1/periodic-product-tests/plans/{plan.Id}",
                new { plan.Id, plan.Code, plan.Name, cadence = plan.Cadence.ToString() });
        });

        quality.MapPost("/schedule", async (FactoryPeriodicTestScheduleInput input,
            ProductionDb db, CancellationToken ct) =>
        {
            if (!PersianPlanningDates.TryParse(input.FromJalali, out var from) ||
                !PersianPlanningDates.TryParse(input.ThroughJalali, out var through) ||
                from > through || through.DayNumber - from.DayNumber > 3660)
                return Results.BadRequest(new { message = "بازه شمسی معتبر و حداکثر ده‌ساله برای تولید سررسیدها لازم است." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DECLARE @rc int; EXEC @rc=sys.sp_getapplock @Resource=N'IPPCS:PeriodicProductTestSchedule', " +
                    "@LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; " +
                    "IF @rc < 0 THROW 51000, 'Periodic test schedule lock unavailable', 1;", ct);
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                return Results.Conflict(new { message = "قفل تولید سررسیدهای آزمون در دسترس نیست؛ درخواست را دوباره اجرا کنید." });
            }

            var plans = await db.FactoryPeriodicTestPlans.AsNoTracking()
                .Where(x => x.IsActive && x.FirstDueOn <= through &&
                    (!x.ValidThrough.HasValue || x.ValidThrough.Value >= from))
                .OrderBy(x => x.Code).Take(501).ToListAsync(ct);
            if (plans.Count > 500)
                return Results.Conflict(new { message = "بیش از ۵۰۰ برنامه آزمون فعال است؛ ابتدا فهرست برنامه‌ها را بازبینی کنید." });

            var inserts = new List<FactoryPeriodicTestRun>();
            var skipped = 0;
            foreach (var plan in plans)
            {
                IReadOnlyList<DateOnly> dates;
                try { dates = FactoryPeriodicTestRules.DueDates(plan, from, through, 5000); }
                catch (InvalidOperationException ex)
                {
                    return Results.UnprocessableEntity(new { message = ex.Message, planCode = plan.Code });
                }
                var existing = await db.FactoryPeriodicTestRuns.AsNoTracking()
                    .Where(x => x.PlanId == plan.Id && x.DueOn >= from && x.DueOn <= through)
                    .Select(x => x.DueOn).ToListAsync(ct);
                var seen = existing.ToHashSet();
                foreach (var date in dates)
                {
                    if (seen.Contains(date)) { skipped++; continue; }
                    inserts.Add(new FactoryPeriodicTestRun { PlanId = plan.Id, DueOn = date });
                    seen.Add(date);
                    if (inserts.Count > 5000)
                        return Results.Conflict(new { message = "بیش از ۵۰۰۰ سررسید در این بازه ایجاد می‌شود؛ بازه را کوتاه کنید." });
                }
            }
            db.FactoryPeriodicTestRuns.AddRange(inserts);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new { message = "تداخل ثبت سررسیدها رخ داد؛ فهرست را بازخوانی و دوباره تلاش کنید." });
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { created = inserts.Count, alreadyExisted = skipped,
                fromJalali = PersianPlanningDates.Format(from), throughJalali = PersianPlanningDates.Format(through),
                disclaimer = "سررسیدها برنامه‌ریزی داخلی آزمون محصول/فرآیند هستند؛ هیچ نتیجه فیزیکی یا مجوز آزادسازی به‌صورت خودکار صادر نمی‌شود." });
        });

        quality.MapPost("/runs/{id:guid}/result", async (Guid id, FactoryPeriodicTestResultInput input,
            ProductionDb db, HttpContext http, CancellationToken ct) =>
        {
            if (!PersianPlanningDates.TryParse(input.PerformedOnJalali, out var performed) ||
                string.IsNullOrWhiteSpace(input.SampleReference) || input.SampleReference.Trim().Length > 100 ||
                !Enum.TryParse<FactoryPeriodicTestOutcome>(input.Outcome, true, out var outcome) ||
                !Enum.IsDefined(outcome) || outcome == FactoryPeriodicTestOutcome.None ||
                input.ReportReference?.Trim().Length > 120 || input.Notes?.Trim().Length > 500)
                return Results.BadRequest(new { message = "تاریخ اجرا، مرجع نمونه و نتیجه معتبر لازم است." });
            if (performed > FactoryPeriodicTestRules.TodayTehran())
                return Results.BadRequest(new { message = "تاریخ اجرای آزمون نمی‌تواند در آینده باشد." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DECLARE @rc int; EXEC @rc=sys.sp_getapplock @Resource=@resource, " +
                    "@LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; " +
                    "IF @rc < 0 THROW 51000, 'Periodic test result lock unavailable', 1;",
                    new Microsoft.Data.SqlClient.SqlParameter("@resource", "IPPCS:PeriodicProductTestRun:" + id.ToString("N")), ct);
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                return Results.Conflict(new { message = "قفل ثبت نتیجه آزمون در دسترس نیست؛ رخداد را بازخوانی و دوباره تلاش کنید." });
            }

            var run = await db.FactoryPeriodicTestRuns.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (run is null) return Results.NotFound();
            if (run.State != FactoryPeriodicTestRunState.Scheduled)
                return Results.Conflict(new { message = "نتیجه آزمون فقط یک بار و پیش از تکمیل رخداد ثبت می‌شود." });
            var plan = await db.FactoryPeriodicTestPlans.SingleOrDefaultAsync(x => x.Id == run.PlanId, ct);
            if (plan is null) return Results.Conflict(new { message = "برنامه مبنای آزمون موجود نیست." });
            var notes = input.Notes?.Trim();
            var resultIssue = FactoryPeriodicTestRules.ValidateOutcome(plan, input.MeasuredValue, outcome, notes);
            if (resultIssue is not null)
                return Results.Conflict(new { message = resultIssue });

            run.PerformedOn = performed;
            run.SampleReference = input.SampleReference.Trim();
            run.MeasuredValue = input.MeasuredValue;
            run.Outcome = outcome;
            run.ReportReference = string.IsNullOrWhiteSpace(input.ReportReference) ? null : input.ReportReference.Trim();
            run.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
            run.RecordedBy = http.User.Identity?.Name ?? "unknown";
            run.RecordedUtc = DateTime.UtcNow;
            run.State = FactoryPeriodicTestRunState.Completed;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { run.Id, state = run.State.ToString(), outcome = run.Outcome.ToString(),
                run.RecordedBy, run.RecordedUtc, dueJalali = PersianPlanningDates.Format(run.DueOn),
                performedOnJalali = PersianPlanningDates.Format(performed) });
        }).RequireAuthorization("IpQualityApprove");

        quality.MapPost("/plans/{id:guid}/deactivate", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            var plan = await db.FactoryPeriodicTestPlans.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (plan is null) return Results.NotFound();
            if (!plan.IsActive) return Results.Conflict(new { message = "برنامه آزمون قبلاً غیرفعال شده است." });
            plan.IsActive = false;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { plan.Id, plan.IsActive,
                message = "برنامه غیرفعال شد؛ سوابق و سررسیدهای قبلی حفظ شدند." });
        }).RequireAuthorization("IpQualityApprove");

        return routes;
    }
}
