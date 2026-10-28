using Microsoft.EntityFrameworkCore;

// Reporting over IPPCS-owned observations only. This creates no stock, sales,
// employee or accounting records and never treats operation passes as finished goods.
public static class FactoryManagementReports
{
    private static readonly TimeZoneInfo Tehran =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static bool TryWindow(string? fromJalali, string? throughJalali,
        out DateOnly from, out DateOnly through, out DateTime startUtc, out DateTime endUtc)
    {
        from = through = default;
        startUtc = endUtc = default;
        if (!PersianPlanningDates.TryParse(fromJalali, out from) ||
            !PersianPlanningDates.TryParse(throughJalali, out through) ||
            through < from || through.DayNumber - from.DayNumber > 365)
            return false;
        startUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), Tehran);
        endUtc = TimeZoneInfo.ConvertTimeToUtc(
            through.AddDays(1).ToDateTime(TimeOnly.MinValue), Tehran);
        return true;
    }

    public static decimal? Percent(decimal numerator, decimal denominator) =>
        denominator > 0m ? Math.Round(100m * numerator / denominator, 2) : null;

    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tehran));

    public static IEndpointRouteBuilder MapFactoryManagementReports(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1/factory-reports")
            .RequireAuthorization("IpProductionUser");

        // No silent truncation of the 500-row general item selector on report filters.
        api.MapGet("/dimensions", async (ProductionDb db, CancellationToken ct) =>
        {
            var items = await db.Items.AsNoTracking().OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name }).Take(1501).ToListAsync(ct);
            var centers = await db.WorkCenters.AsNoTracking().OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name }).Take(1501).ToListAsync(ct);
            if (items.Count > 1500 || centers.Count > 1500)
                return Results.Conflict(new
                {
                    message = "تعداد گزینه‌های فیلتر بیش از ۱۵۰۰ است؛ برای جلوگیری از گزارش ناقص از فیلتر جست‌وجوی سروری استفاده کنید."
                });
            return Results.Ok(new { items, centers });
        });

        api.MapGet("/overview", async (string fromJalali, string throughJalali,
            Guid? itemId, Guid? workCenterId, ProductionDb db, CancellationToken ct) =>
        {
            if (!TryWindow(fromJalali, throughJalali, out var from, out var through,
                out var startUtc, out var endUtc) ||
                itemId == Guid.Empty || workCenterId == Guid.Empty)
                return Results.BadRequest(new
                {
                    message = "تاریخ شروع/پایان شمسی معتبر با حداکثر ۳۶۶ روز و شناسه فیلتر صحیح لازم است."
                });

            if (itemId.HasValue && !await db.Items.AsNoTracking()
                .AnyAsync(x => x.Id == itemId.Value, ct))
                return Results.BadRequest(new { message = "محصول فیلتر وجود ندارد." });
            if (workCenterId.HasValue && !await db.WorkCenters.AsNoTracking()
                .AnyAsync(x => x.Id == workCenterId.Value, ct))
                return Results.BadRequest(new { message = "مرکز کاری فیلتر وجود ندارد." });

            // Cohort A: orders due in the selected range (NOT quantity produced).
            var dueQuery = db.ManufacturingOrders.AsNoTracking()
                .Where(x => x.DueOn >= from && x.DueOn <= through &&
                    x.Status != ManufacturingOrderStatus.Cancelled);
            if (itemId.HasValue)
                dueQuery = dueQuery.Where(x => x.ItemId == itemId.Value);
            if (await dueQuery.CountAsync(ct) > 10000)
                return Results.Conflict(new { message = "دستورات بازه بیش از سقف ۱۰ هزار است؛ بازه را کوچک‌تر کنید." });
            var dueOrders = await dueQuery.Include(x => x.Operations).ToListAsync(ct);

            // Cohort B: timestamped operation confirmations, not unique output.
            var eventsQuery =
                from c in db.ManufacturingConfirmations.AsNoTracking()
                join op in db.ManufacturingOrderOperations.AsNoTracking()
                    on c.OperationId equals op.Id
                join order in db.ManufacturingOrders.AsNoTracking()
                    on op.OrderId equals order.Id
                where c.OccurredUtc >= startUtc && c.OccurredUtc < endUtc
                select new { c.OccurredUtc, c.GoodQuantity, c.ScrapQuantity,
                    c.ReworkQuantity, op.WorkCenterId, order.ItemId };
            if (itemId.HasValue)
                eventsQuery = eventsQuery.Where(x => x.ItemId == itemId.Value);
            if (workCenterId.HasValue)
                eventsQuery = eventsQuery.Where(x => x.WorkCenterId == workCenterId.Value);
            if (await eventsQuery.CountAsync(ct) > 20000)
                return Results.Conflict(new { message = "ثبت عملیات بیش از سقف ۲۰ هزار است؛ بازه را کوچک‌تر کنید." });
            var events = await eventsQuery.ToListAsync(ct);

            // Cohort C: quality holds RAISED within the window. Status is CURRENT.
            var holdsQuery =
                from hold in db.ManufacturingQualityHolds.AsNoTracking()
                join order in db.ManufacturingOrders.AsNoTracking()
                    on hold.OrderId equals order.Id
                join op in db.ManufacturingOrderOperations.AsNoTracking()
                    on hold.OperationId equals op.Id
                where hold.RaisedUtc >= startUtc && hold.RaisedUtc < endUtc
                select new { hold.Quantity, hold.Status, order.ItemId, op.WorkCenterId };
            if (itemId.HasValue)
                holdsQuery = holdsQuery.Where(x => x.ItemId == itemId.Value);
            if (workCenterId.HasValue)
                holdsQuery = holdsQuery.Where(x => x.WorkCenterId == workCenterId.Value);
            if (await holdsQuery.CountAsync(ct) > 20000)
                return Results.Conflict(new { message = "موارد قرنطینه بیش از سقف گزارش است." });
            var holds = await holdsQuery.ToListAsync(ct);

            // Cohort D: deviations OPENED in window, with present status/due date.
            var deviationQuery =
                from d in db.ProductionDeviations.AsNoTracking()
                join line in db.MpsDemandLines.AsNoTracking()
                    on d.MpsLineId equals line.Id
                where d.CreatedUtc >= startUtc && d.CreatedUtc < endUtc
                select new { d.Id, d.Category, d.Status, d.ActionDueOn,
                    d.ResponsibleCode, d.RootCauseCategoryCode, line.ItemId };
            if (itemId.HasValue)
                deviationQuery = deviationQuery.Where(x => x.ItemId == itemId.Value);
            if (await deviationQuery.CountAsync(ct) > 20000)
                return Results.Conflict(new { message = "انحرافات ثبت‌شده بیش از سقف گزارش است." });
            var deviations = await deviationQuery.ToListAsync(ct);

            var today = LocalDate(DateTime.UtcNow);
            var itemIds = dueOrders.Select(x => x.ItemId)
                .Concat(events.Select(x => x.ItemId)).Distinct().ToArray();
            var itemNames = await db.Items.AsNoTracking()
                .Where(x => itemIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Name }).ToDictionaryAsync(x => x.Id, ct);
            var centerIds = events.Select(x => x.WorkCenterId).Distinct().ToArray();
            var centerNames = await db.WorkCenters.AsNoTracking()
                .Where(x => centerIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Name }).ToDictionaryAsync(x => x.Id, ct);

            var orderRows = dueOrders.GroupBy(x => x.ItemId)
                .Select(g =>
                {
                    var completed = g.Where(x => x.Status == ManufacturingOrderStatus.Completed).ToArray();
                    var dated = completed.Where(x => x.CompletedUtc.HasValue).ToArray();
                    var onTime = dated.Count(x => LocalDate(x.CompletedUtc!.Value) <= x.DueOn);
                    var withoutFinalEvidence = completed.Count(x => !x.Operations.Any());
                    var finishedGood = completed.Sum(x =>
                        x.Operations.OrderBy(o => o.Sequence).LastOrDefault()?.GoodQuantity ?? 0m);
                    return new
                    {
                        itemId = g.Key,
                        itemCode = itemNames.TryGetValue(g.Key, out var item) ? item.Code : "",
                        itemName = itemNames.TryGetValue(g.Key, out item) ? item.Name : "",
                        orderCount = g.Count(),
                        plannedQuantity = g.Sum(x => x.PlannedQuantity),
                        completedOrders = completed.Length,
                        finishedGoodWithEvidence = finishedGood,
                        missingFinalOperation = withoutFinalEvidence,
                        onTimeCompletedOrders = onTime,
                        missingCompletionTimestamp = completed.Length - dated.Length,
                        onTimePercent = Percent(onTime, dated.Length),
                        overdueOpenNow = g.Count(x => x.Status != ManufacturingOrderStatus.Completed &&
                            x.DueOn < today)
                    };
                }).OrderBy(x => x.itemCode).ToArray();

            var centers = events.GroupBy(x => x.WorkCenterId).Select(g =>
            {
                var good = g.Sum(x => x.GoodQuantity);
                var scrap = g.Sum(x => x.ScrapQuantity);
                var rework = g.Sum(x => x.ReworkQuantity);
                return new
                {
                    workCenterId = g.Key,
                    centerCode = centerNames.TryGetValue(g.Key, out var center) ? center.Code : "",
                    centerName = centerNames.TryGetValue(g.Key, out center) ? center.Name : "",
                    confirmationCount = g.Count(),
                    good, scrap, rework,
                    firstPassSharePercent = Percent(good, good + scrap + rework)
                };
            }).OrderBy(x => x.centerCode).ToArray();

            var daily = events.GroupBy(x => LocalDate(x.OccurredUtc))
                .OrderBy(g => g.Key).Select(g => new
                {
                    dateJalali = PersianPlanningDates.Format(g.Key),
                    confirmationCount = g.Count(),
                    good = g.Sum(x => x.GoodQuantity),
                    scrap = g.Sum(x => x.ScrapQuantity),
                    rework = g.Sum(x => x.ReworkQuantity)
                }).ToArray();

            var categories = deviations.GroupBy(x => new
                { x.Category, x.RootCauseCategoryCode })
                .Select(g => new
                {
                    category = g.Key.Category.ToString(),
                    rootCauseCode = g.Key.RootCauseCategoryCode ?? "LEGACY_UNCLASSIFIED",
                    opened = g.Count(),
                    unresolvedNow = g.Count(x => x.Status != ProductionDeviationStatus.Closed),
                    actionOverdueNow = g.Count(x => x.Status != ProductionDeviationStatus.Closed &&
                        x.ActionDueOn.HasValue && x.ActionDueOn.Value < today)
                }).OrderByDescending(x => x.opened).ToArray();

            return Results.Ok(new
            {
                fromJalali = PersianPlanningDates.Format(from),
                throughJalali = PersianPlanningDates.Format(through),
                itemId, workCenterId,
                generatedUtc = DateTime.UtcNow,
                orders = orderRows,
                centers,
                daily,
                quality = new
                {
                    raisedEvents = holds.Count,
                    heldQuantityAtEvent = holds.Sum(x => x.Quantity),
                    unresolvedEventsNow = holds.Count(x => x.Status == ManufacturingQualityStatus.Open),
                    unresolvedQuantityFromPeriodNow =
                        holds.Where(x => x.Status == ManufacturingQualityStatus.Open).Sum(x => x.Quantity)
                },
                capa = new
                {
                    opened = deviations.Count,
                    unresolvedNow = deviations.Count(x => x.Status != ProductionDeviationStatus.Closed),
                    actionOverdueNow = deviations.Count(x => x.Status != ProductionDeviationStatus.Closed &&
                        x.ActionDueOn.HasValue && x.ActionDueOn.Value < today),
                    categories
                },
                definitions = new
                {
                    orders = "بر اساس موعد دستورهای غیرلغوشده در بازه. موعد جاری و وضعیت کنونی؛ نه بازسازی تاریخی وضعیت.",
                    completion = "زمان پایان ثبت‌شده به تاریخ محلی تهران تبدیل می‌شود. درصد به‌موقع فقط از دستورهای تکمیل‌شده با زمان پایان معتبر محاسبه می‌شود.",
                    throughput = "مجموع تأیید عملیات در هر مرکز و روز؛ قطعه عبوری از چند عملیات ممکن است چند بار شمرده شود. این اعداد خروجی محصول نهایی یا موجودی نیست.",
                    quality = "رخدادهای قرنطینه ثبت‌شده در بازه؛ مقدار و وضعیت تعیین‌تکلیف کنونی هر رخداد، نه مانده انبار.",
                    capa = "انحرافات ایجادشده در بازه؛ وضعیت و سررسید فعلی. فیلتر مرکز کاری در CAPA و موعد دستورات اعمال نمی‌شود.",
                    timeZone = "Asia/Tehran",
                    missingData = "مقادیر فاقد داده تکمیل/زمان یا مخرج معتبر با صفر/درصد ساختگی جایگزین نمی‌شوند."
                }
            });
        });
        return endpoints;
    }
}
