/// <summary>
/// Expands a finished-good MPS demand into operation-level APS tasks.
/// Each upstream operation must process enough quantity to cover yield losses
/// at that operation and every downstream operation. Task.Quantity therefore
/// means quantity processed at that operation, not the final-good MPS quantity.
/// </summary>
public static class FactoryApsTaskExpansion
{
    public static IReadOnlyList<ApsTask> Expand(MpsDemandLine line, RoutingRevision route)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(route);

        if (line.ItemId == Guid.Empty || line.Quantity <= 0 ||
            route.ItemId != line.ItemId || route.State != RoutingState.Approved ||
            route.EffectiveFrom > line.NeededOn ||
            (route.EffectiveTo is not null && line.NeededOn > route.EffectiveTo.Value))
            throw new InvalidOperationException("محصول، مقدار تقاضا یا مسیر ساخت مصوب و معتبر برای تاریخ نیاز نامعتبر است.");

        var operations = route.Operations.OrderBy(x => x.Sequence).ToArray();
        if (operations.Length == 0 ||
            operations.Any(x => x.Sequence <= 0) ||
            operations.Select(x => x.Sequence).Distinct().Count() != operations.Length)
            throw new InvalidOperationException("مسیر APS باید عملیات با شماره‌های مثبت و یکتا داشته باشد.");

        if (operations.Any(x => x.WorkCenterId == Guid.Empty ||
            string.IsNullOrWhiteSpace(x.Name) || x.SetupMinutes < 0 ||
            x.RunMinutesPerUnit <= 0 || x.YieldPercent is <= 0 or > 100))
            throw new InvalidOperationException("مرکز، زمان استاندارد، نام یا بازده یکی از عملیات مسیر APS نامعتبر است.");

        var tasks = new List<ApsTask>(operations.Length);
        for (var i = 0; i < operations.Length; i++)
        {
            var operation = operations[i];
            decimal cumulativeYield = 1m;
            for (var j = i; j < operations.Length; j++)
                cumulativeYield *= operations[j].YieldPercent / 100m;

            if (cumulativeYield <= 0m)
                throw new InvalidOperationException("بازده تجمعی مسیر APS نامعتبر است.");

            var operationQuantity = line.Quantity / cumulativeYield;
            var durationMinutes = operation.SetupMinutes +
                operation.RunMinutesPerUnit * operationQuantity;

            if (operationQuantity <= 0m || durationMinutes <= 0m)
                throw new InvalidOperationException("مقدار عبوری یا زمان محاسبه‌شده عملیات APS نامعتبر است.");

            tasks.Add(new ApsTask(line.Id, line.ItemId, line.NeededOn,
                operationQuantity, route.Id, operation.Sequence, operation.WorkCenterId,
                durationMinutes, operation.Name));
        }

        return tasks;
    }
}
