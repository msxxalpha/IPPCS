using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

// Stage 68: convert one immutable saved MRP run into planner-owned draft orders.
// Draft creation never reserves physical stock or finite work-center capacity.
public sealed record MrpDraftOrderRequest(IReadOnlyList<string> LotKeys);
public sealed record MrpDraftDemandSource(string DemandKey, decimal Quantity,
    Guid? MpsLineId, string? ParentLotKey);
public sealed record MrpDraftMpsLink(Guid MpsLineId, decimal Quantity);
public sealed record MrpDraftSourceEvidence(string IdempotencyKey,
    IReadOnlyList<MrpDraftDemandSource> Demands,
    IReadOnlyList<MrpDraftMpsLink> DirectMpsLinks,
    IReadOnlyList<string> ParentLotKeys);

public static class MrpDraftOrderConversion
{
    public const string SourceAction = "MrpDraftSource";
    public const string DemandAction = "MrpDraftDemandPeg";

    public static string SourceKey(Guid runId, string lotKey) => runId.ToString("N") + "|" + lotKey;

    public static string OrderNumber(Guid runId, string lotKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(SourceKey(runId, lotKey)));
        return "MRP-" + Convert.ToHexString(bytes.AsSpan(0, 12));
    }

    public static bool TryBuildEvidence(Guid runId, NetPegPlannedLot lot,
        NetPeggingReport graph, out MrpDraftSourceEvidence? evidence, out string error)
    {
        evidence = null;
        error = "";
        if (runId == Guid.Empty || string.IsNullOrWhiteSpace(lot.LotKey) ||
            lot.ProposalType != "Manufacturing" || lot.ItemId == Guid.Empty ||
            lot.BomRevisionId is null || lot.Quantity <= 0 || lot.ReleaseOn > lot.ReceiptOn)
        {
            error = "فقط بچ ساخت معتبر از اجرای ذخیره‌شده MRP قابل تبدیل است.";
            return false;
        }

        var demandMap = graph.Demands.GroupBy(x => x.DemandKey, StringComparer.Ordinal);
        if (demandMap.Any(x => x.Count() != 1))
        {
            error = "شناسه نیاز در گراف MRP تکراری است.";
            return false;
        }
        var demandsByKey = demandMap.ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
        var allocations = graph.Allocations
            .Where(x => x.SourceType == "ProposedManufacturingReceipt" && x.SourceKey == lot.LotKey)
            .OrderBy(x => x.DemandKey, StringComparer.Ordinal).ToArray();
        if (allocations.Length == 0 || allocations.Any(x => x.ItemId != lot.ItemId ||
            x.Quantity <= 0 || !demandsByKey.ContainsKey(x.DemandKey)))
        {
            error = "منشأ تخصیص این بچ در گراف MRP کامل یا معتبر نیست.";
            return false;
        }

        var assignedDemands = allocations.Select(x => (Allocation: x, Demand: demandsByKey[x.DemandKey])).ToArray();
        if (assignedDemands.Any(x => x.Demand.ItemId != lot.ItemId ||
            x.Demand.NeededOn != lot.ReceiptOn))
        {
            error = "تخصیص بچ به نیاز با قلم یا تاریخ دریافت سازگار نیست.";
            return false;
        }
        var demandSources = assignedDemands.Select(x => new MrpDraftDemandSource(
            x.Demand.DemandKey, x.Allocation.Quantity, x.Demand.MpsLineId,
            x.Demand.ParentLotKey)).ToArray();
        if (demandSources.Select(x => x.DemandKey).Distinct(StringComparer.Ordinal).Count() != demandSources.Length ||
            demandSources.Sum(x => x.Quantity) > lot.Quantity)
        {
            error = "مقادیر منشأ نیاز بچ تکراری یا بیش از مقدار دستور هستند.";
            return false;
        }

        var hasParentDemand = demandSources.Any(x => !string.IsNullOrWhiteSpace(x.ParentLotKey));
        var directLinks = hasParentDemand ? Array.Empty<MrpDraftMpsLink>() :
            demandSources.Where(x => x.MpsLineId.HasValue)
                .GroupBy(x => x.MpsLineId!.Value)
                .Select(g => new MrpDraftMpsLink(g.Key, g.Sum(x => x.Quantity))).ToArray();
        evidence = new MrpDraftSourceEvidence(SourceKey(runId, lot.LotKey),
            demandSources, directLinks,
            demandSources.Where(x => !string.IsNullOrWhiteSpace(x.ParentLotKey))
                .Select(x => x.ParentLotKey!).Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray());
        return true;
    }

    public static IEndpointRouteBuilder MapMrpDraftOrderConversion(this IEndpointRouteBuilder routes)
    {
        var readApi = routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        var writeApi = routes.MapGroup("/api/v1").RequireAuthorization("IpPlanningWrite");

        readApi.MapGet("/mrp/runs/{runId:guid}/manufacturing-proposals",
            async (Guid runId, ProductionDb db, CancellationToken ct) =>
        {
            var run = await db.PlanningCalculations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == runId, ct);
            if (run is null) return Results.NotFound();
            if (!NetMrpDemandPegging.TryBuildFromRunSnapshot(run, out var graph, out var error))
                return Results.Conflict(new { message = error });
            var prefix = runId.ToString("N") + "|";
            var sourceEvents = await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x => x.Action == SourceAction && x.Details != null && x.Details.StartsWith(prefix))
                .ToListAsync(ct);
            var existingIds = sourceEvents.ToDictionary(x => x.Details!, x => x.OrderId, StringComparer.Ordinal);
            var ids = existingIds.Values.Distinct().ToArray();
            var orders = await db.ManufacturingOrders.AsNoTracking()
                .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var rows = new List<object>();
            foreach (var lot in graph.PlannedLots.Where(x => x.ProposalType == "Manufacturing"))
            {
                var key = SourceKey(runId, lot.LotKey);
                var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(x => x.Id == lot.ItemId, ct);
                var routeCount = await db.RoutingRevisions.AsNoTracking().CountAsync(x =>
                    x.ItemId == lot.ItemId && x.State == RoutingState.Approved &&
                    x.EffectiveFrom <= lot.ReleaseOn &&
                    (x.EffectiveTo == null || lot.ReleaseOn <= x.EffectiveTo), ct);
                var already = existingIds.TryGetValue(key, out var orderId) && orders.TryGetValue(orderId, out var order);
                rows.Add(new {
                    lot.LotKey, lot.ItemId, itemCode = item?.Code, itemName = item?.Name,
                    lot.Quantity, lot.NetShortage, lot.LotSizingSurplus, lot.BomRevisionId,
                    releaseOnJalali = PersianPlanningDates.Format(lot.ReleaseOn),
                    receiptOnJalali = PersianPlanningDates.Format(lot.ReceiptOn),
                    lot.ReleaseBeforeBaseline, alreadyCreated = already,
                    orderId = already ? order!.Id : (Guid?)null,
                    orderNumber = already ? order!.Number : null,
                    orderStatus = already ? order!.Status.ToString() : null,
                    routingReady = routeCount == 1,
                    routingIssue = routeCount == 0 ? "مسیر ساخت مصوب برای تاریخ شروع وجود ندارد." :
                        routeCount > 1 ? "چند مسیر ساخت هم‌زمان معتبر است." : null
                });
            }
            return Results.Ok(new {
                runId, mpsVersionId = run.MpsVersionId, calculatedUtc = run.CalculatedUtc,
                count = rows.Count, proposals = rows,
                disclaimer = "فقط پیشنهادهای ساخت نمایش داده می‌شوند. پیشنهادهای تأمین خارجی به دستور ساخت تبدیل نمی‌شوند؛ ایجاد پیش‌نویس نیز رزرو مواد یا ظرفیت نیست."
            });
        });

        writeApi.MapPost("/mrp/runs/{runId:guid}/manufacturing-drafts",
            async (Guid runId, MrpDraftOrderRequest request, ProductionDb db,
                HttpContext context, ILogger<ManufacturingOrder> logger, CancellationToken ct) =>
        {
            if (request.LotKeys is null || request.LotKeys.Count is < 1 or > 100 ||
                request.LotKeys.Any(string.IsNullOrWhiteSpace) ||
                request.LotKeys.Distinct(StringComparer.Ordinal).Count() != request.LotKeys.Count)
                return Results.BadRequest(new { message = "بین ۱ تا ۱۰۰ شناسه بچ یکتا انتخاب کنید." });

            var run = await db.PlanningCalculations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == runId, ct);
            if (run is null) return Results.NotFound();
            if (!NetMrpDemandPegging.TryBuildFromRunSnapshot(run, out var graph, out var error))
                return Results.Conflict(new { message = error });

            var selected = request.LotKeys.ToHashSet(StringComparer.Ordinal);
            var lots = graph.PlannedLots.Where(x => selected.Contains(x.LotKey)).ToArray();
            if (lots.Length != selected.Count || lots.Any(x => x.ProposalType != "Manufacturing"))
                return Results.BadRequest(new { message = "یک یا چند بچ انتخاب‌شده در این اجرای ذخیره‌شده، پیشنهاد ساخت معتبر نیستند؛ پیشنهاد تأمین خارجی قابل تبدیل نیست." });

            var contexts = new List<(NetPegPlannedLot Lot, MrpDraftSourceEvidence Evidence,
                PlanningItemType Type, ManufacturingBomRevision Bom, RoutingRevision Route)>();
            foreach (var lot in lots)
            {
                if (!MrpDraftOrderConversion.TryBuildEvidence(runId, lot, graph, out var evidence, out error) || evidence is null)
                    return Results.Conflict(new { message = error, lotKey = lot.LotKey });
                if (!NetMrpDemandPegging.TryRestoreRunSnapshot(run, out var restored, out error) || restored is null)
                    return Results.Conflict(new { message = error });
                if (!restored.Types.TryGetValue(lot.ItemId, out var type) ||
                    type is not (PlanningItemType.Finished or PlanningItemType.SemiFinished))
                    return Results.Conflict(new { message = "نوع قلم بچ برای دستور ساخت معتبر نیست.", lotKey = lot.LotKey });
                var bom = restored.Boms.SingleOrDefault(x => x.Id == lot.BomRevisionId &&
                    x.ParentItemId == lot.ItemId && x.State == ManufacturingRevisionState.Approved &&
                    x.ValidFrom <= lot.ReleaseOn && (x.ValidUntil == null || lot.ReleaseOn <= x.ValidUntil));
                if (bom is null || bom.Lines.Count == 0 ||
                    !await db.ManufacturingBomRevisions.AsNoTracking().AnyAsync(x =>
                        x.Id == bom.Id && x.ParentItemId == lot.ItemId, ct))
                    return Results.Conflict(new { message = "نسخه BOM منجمد در اجرای MRP در پایگاه داده موجود نیست یا معتبر نیست.", lotKey = lot.LotKey });
                var routes = await db.RoutingRevisions.AsNoTracking().Include(x => x.Operations)
                    .Where(x => x.ItemId == lot.ItemId && x.State == RoutingState.Approved &&
                        x.EffectiveFrom <= lot.ReleaseOn &&
                        (x.EffectiveTo == null || lot.ReleaseOn <= x.EffectiveTo))
                    .ToListAsync(ct);
                if (routes.Count != 1 || routes[0].Operations.Count == 0)
                    return Results.Conflict(new { message = "برای تاریخ آزادسازی بچ دقیقاً یک Routing مصوب دارای عملیات لازم است.", lotKey = lot.LotKey });
                contexts.Add((lot, evidence, type, bom, routes[0]));
            }

            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var prefix = runId.ToString("N") + "|";
            var existingEvents = await db.ManufacturingOrderAudits
                .Where(x => x.Action == SourceAction && x.Details != null && x.Details.StartsWith(prefix))
                .ToListAsync(ct);
            var existingByKey = existingEvents.ToDictionary(x => x.Details!, x => x.OrderId, StringComparer.Ordinal);
            var results = new List<object>();
            var createdCount = 0;
            var actor = context.User.Identity?.Name ?? "unknown";
            foreach (var entry in contexts)
            {
                var key = SourceKey(runId, entry.Lot.LotKey);
                if (existingByKey.TryGetValue(key, out var existingId))
                {
                    var existing = await db.ManufacturingOrders.AsNoTracking()
                        .SingleOrDefaultAsync(x => x.Id == existingId, ct);
                    if (existing is null)
                        return Results.Conflict(new { message = "سابقه تبدیل به دستور متناظر ندارد؛ برای جلوگیری از تکرار، عملیات متوقف شد.", lotKey = entry.Lot.LotKey });
                    results.Add(new { lotKey = entry.Lot.LotKey, orderId = existing.Id,
                        orderNumber = existing.Number, status = existing.Status.ToString(), outcome = "AlreadyCreated" });
                    continue;
                }

                var number = OrderNumber(runId, entry.Lot.LotKey);
                if (await db.ManufacturingOrders.AnyAsync(x => x.Number == number, ct))
                    return Results.Conflict(new { message = "شماره قطعی این بچ قبلاً برای یک دستور دیگر استفاده شده است.", lotKey = entry.Lot.LotKey });
                var order = new ManufacturingOrder {
                    Number = number, ItemId = entry.Lot.ItemId,
                    Kind = entry.Type == PlanningItemType.SemiFinished
                        ? ManufacturingOrderKind.SemiFinished : ManufacturingOrderKind.FinalAssembly,
                    RoutingRevisionId = entry.Route.Id, BomRevisionId = entry.Bom.Id,
                    PlannedQuantity = entry.Lot.Quantity, PlannedStart = entry.Lot.ReleaseOn,
                    DueOn = entry.Lot.ReceiptOn, Status = ManufacturingOrderStatus.Draft
                };
                createdCount++;
                db.ManufacturingOrders.Add(order);
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit {
                    OrderId = order.Id, Action = "Created", Details = "Source=SavedMrpRun"
                });
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit {
                    OrderId = order.Id, Action = SourceAction, Details = key
                });
                foreach (var demand in entry.Evidence.Demands)
                {
                    db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit {
                        OrderId = order.Id, Action = DemandAction,
                        Details = JsonSerializer.Serialize(new {
                            runId, lotKey = entry.Lot.LotKey, demand.DemandKey,
                            demand.Quantity, demand.MpsLineId, demand.ParentLotKey
                        })
                    });
                }
                // Only a pure direct-MPS lot receives classic MPS pegging rows. A
                // subassembly lot keeps all parent-lot genealogy in the audit journal.
                foreach (var link in entry.Evidence.DirectMpsLinks)
                {
                    var lineExists = await db.MpsDemandLines.AsNoTracking()
                        .AnyAsync(x => x.Id == link.MpsLineId && x.MpsVersionId == run.MpsVersionId, ct);
                    if (!lineExists)
                        return Results.Conflict(new { message = "ردیف MPS منشأ بچ دیگر در دسترس نیست.", lotKey = entry.Lot.LotKey });
                    db.ManufacturingOrderPegs.Add(new ManufacturingOrderPeg {
                        OrderId = order.Id, MpsLineId = link.MpsLineId,
                        LinkedQuantity = link.Quantity, LinkedBy = actor
                    });
                }
                results.Add(new { lotKey = entry.Lot.LotKey, orderId = order.Id,
                    orderNumber = order.Number, status = "Draft", outcome = "Created",
                    parentLotKeys = entry.Evidence.ParentLotKeys });
            }
            try {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) {
                await tx.RollbackAsync(ct);
                logger.LogWarning(ex, "MRP draft conversion conflict for run {RunId}", runId);
                return Results.Conflict(new { message = "تبدیل هم‌زمان یا تعارض کلید یکتا رخ داد؛ فهرست پیشنهادها را تازه‌سازی و دوباره بررسی کنید." });
            }
            return Results.Ok(new {
                runId, created = createdCount,
                results, warning = "همه دستورها در وضعیت پیش‌نویس باقی مانده‌اند؛ هیچ موجودی یا ظرفیتی رزرو نشده و آزادسازی باید از کنترل‌های استاندارد دستور تولید عبور کند."
            });
        });
        return routes;
    }
}
