using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Stage 21: controlled, explicit rebinding of DRAFT production orders to an
// already-approved engineering revision. Not an ECO signature/approval workflow.
// No ERP/warehouse/HR integration, no revision mutation or schema changes.
public sealed record FactoryDraftEngineeringRebind(
    string Kind, Guid BaselineId, Guid CandidateId, Guid[] OrderIds,
    string Reason, bool DryRun = true);

public sealed record FactoryDraftEngineeringRebindRow(
    Guid OrderId, string OrderNumber, string PlannedStartJalali, bool CanRebind,
    IReadOnlyList<FactoryRevisionIssue> Issues);

public static class FactoryEngineeringDraftRebind
{
    public const string AuditAction = "EngineeringDraftRebind";

    public static IReadOnlyList<FactoryRevisionIssue> Assess(
        string kind, Guid baselineId, Guid candidateId, ManufacturingOrder order,
        RoutingRevision? route, ManufacturingBomRevision? bom)
    {
        var issues = new List<FactoryRevisionIssue>();
        if (kind is not ("BOM" or "Routing") || baselineId == candidateId ||
            baselineId == Guid.Empty || candidateId == Guid.Empty)
            issues.Add(new("InvalidRevisionPair", "نوع یا جفت نسخه‌های مهندسی معتبر نیست."));
        if (order.Status != ManufacturingOrderStatus.Draft || order.Operations.Count != 0)
            issues.Add(new("OrderNotDraft", "دستور آزادشده، اجراشده یا دارای عملیات قابل تغییر نسخه نیست."));
        if (order.PlannedQuantity <= 0 || order.PlannedStart > order.DueOn)
            issues.Add(new("InvalidOrder", "تاریخ یا مقدار دستور معتبر نیست."));
        if (kind == "Routing" && order.RoutingRevisionId != baselineId ||
            kind == "BOM" && order.BomRevisionId != baselineId)
            issues.Add(new("BaselineMismatch", "دستور به نسخه مبدأ معرفی‌شده متصل نیست."));
        if (route is null || route.ItemId != order.ItemId ||
            route.State != RoutingState.Approved || route.Operations.Count == 0 ||
            route.EffectiveFrom > order.PlannedStart ||
            route.EffectiveTo is { } routeEnd && routeEnd < order.PlannedStart ||
            (kind == "Routing" && route.Id != candidateId) ||
            (kind == "BOM" && route.Id != order.RoutingRevisionId))
            issues.Add(new("RoutingUnavailable", "مسیر ساخت مصوب و دارای عملیات در تاریخ شروع این دستور معتبر نیست."));
        if (bom is null || bom.ParentItemId != order.ItemId ||
            bom.State != ManufacturingRevisionState.Approved || bom.Lines.Count == 0 ||
            bom.ValidFrom > order.PlannedStart ||
            bom.ValidUntil is { } bomEnd && bomEnd < order.PlannedStart ||
            (kind == "BOM" && bom.Id != candidateId) ||
            (kind == "Routing" && bom.Id != order.BomRevisionId))
            issues.Add(new("BomUnavailable", "BOM مصوب و دارای جزء در تاریخ شروع این دستور معتبر نیست."));
        return issues;
    }

    public static IEndpointRouteBuilder MapEngineeringDraftRebind(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1").RequireAuthorization("IpPlanningWrite");
        api.MapPost("/factory-engineering/draft-rebind",
            async (FactoryDraftEngineeringRebind input, ProductionDb db,
                   HttpContext context, CancellationToken ct) =>
        {
            var kind = input.Kind?.Trim();
            if (kind is not ("BOM" or "Routing") || input.BaselineId == Guid.Empty ||
                input.CandidateId == Guid.Empty || input.BaselineId == input.CandidateId ||
                input.OrderIds is null || input.OrderIds.Length is < 1 or > 100 ||
                input.OrderIds.Any(id => id == Guid.Empty) ||
                input.OrderIds.Distinct().Count() != input.OrderIds.Length ||
                string.IsNullOrWhiteSpace(input.Reason) ||
                input.Reason.Trim().Length > 300)
                return Results.BadRequest(new { message = "نوع نسخه، ۱ تا ۱۰۰ دستور غیرتکراری و دلیل تغییر (تا ۳۰۰ حرف) الزامی است." });

            // Only the owner is read before locking. The definitive version
            // state and all order rows are reread within a serializable transaction.
            Guid? owner = kind == "BOM"
                ? await db.ManufacturingBomRevisions.AsNoTracking()
                    .Where(x => x.Id == input.BaselineId)
                    .Select(x => (Guid?)x.ParentItemId).SingleOrDefaultAsync(ct)
                : await db.RoutingRevisions.AsNoTracking()
                    .Where(x => x.Id == input.BaselineId)
                    .Select(x => (Guid?)x.ItemId).SingleOrDefaultAsync(ct);
            if (owner is null) return Results.NotFound();

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                await FactoryRevisionGovernance.LockRevisionItem(db, kind, owner.Value, ct);
                // Same lock order for every request, including opposing concurrent
                // edits/replans/releases. Fail the entire batch rather than partially.
                foreach (var id in input.OrderIds.OrderBy(x => x))
                    await FactoryCapacityReservations.LockOrder(db, id, ct);
            }
            catch (SqlException)
            {
                return Results.Conflict(new { message = "قفل نسخه مهندسی یا دستور تولید در دسترس نیست." });
            }

            RoutingRevision? approvedRoute = null;
            ManufacturingBomRevision? approvedBom = null;
            if (kind == "Routing")
                approvedRoute = await db.RoutingRevisions.AsNoTracking()
                    .Include(x => x.Operations)
                    .SingleOrDefaultAsync(x => x.Id == input.CandidateId, ct);
            else
                approvedBom = await db.ManufacturingBomRevisions.AsNoTracking()
                    .Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == input.CandidateId, ct);

            if (kind == "Routing" && (approvedRoute is null ||
                approvedRoute.ItemId != owner.Value || approvedRoute.State != RoutingState.Approved) ||
                kind == "BOM" && (approvedBom is null ||
                approvedBom.ParentItemId != owner.Value ||
                approvedBom.State != ManufacturingRevisionState.Approved))
                return Results.Conflict(new { message = "نسخه مقصد باید برای همان محصول، قبلاً تصویب شده باشد." });

            var orders = await db.ManufacturingOrders.Include(x => x.Operations)
                .Where(x => input.OrderIds.Contains(x.Id)).ToListAsync(ct);
            if (orders.Count != input.OrderIds.Length)
                return Results.Conflict(new { message = "حداقل یکی از دستورات انتخاب‌شده وجود ندارد؛ هیچ تغییری ثبت نشد." });

            // Use the untouched, approved counterpart revision for each order.
            var routeIds = orders.Select(x => x.RoutingRevisionId).Distinct().ToArray();
            var bomIds = orders.Select(x => x.BomRevisionId).Distinct().ToArray();
            var routesById = (await db.RoutingRevisions.AsNoTracking()
                .Include(x => x.Operations).Where(x => routeIds.Contains(x.Id))
                .ToListAsync(ct)).ToDictionary(x => x.Id);
            var bomsById = (await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x => x.Lines).Where(x => bomIds.Contains(x.Id))
                .ToListAsync(ct)).ToDictionary(x => x.Id);

            var assessed = orders.OrderBy(x => x.Number, StringComparer.Ordinal)
                .Select(order =>
                {
                    var routing = kind == "Routing" ? approvedRoute :
                        routesById.GetValueOrDefault(order.RoutingRevisionId);
                    var bom = kind == "BOM" ? approvedBom :
                        bomsById.GetValueOrDefault(order.BomRevisionId);
                    var issues = Assess(kind, input.BaselineId, input.CandidateId,
                        order, routing, bom);
                    return new FactoryDraftEngineeringRebindRow(order.Id, order.Number,
                        PersianPlanningDates.Format(order.PlannedStart), issues.Count == 0, issues);
                }).ToArray();
            if (assessed.Any(x => !x.CanRebind))
                return Results.Conflict(new
                {
                    message = "حداقل یک دستور پیش‌شرط تغییر نسخه را ندارد؛ انتقال گروهی انجام نشد.",
                    dryRun = input.DryRun, rows = assessed
                });

            if (input.DryRun)
                return Results.Ok(new
                {
                    dryRun = true, applied = 0, kind, input.BaselineId,
                    input.CandidateId, rows = assessed,
                    disclaimer = "پیش‌نمایش است؛ نتیجه هنگام ثبت با قفل و کنترل دوباره بررسی می‌شود."
                });

            string Sanitize(string value) =>
                value.Replace("\r", " ").Replace("\n", " ").Replace(";", "،")
                    .Replace("|", " ").Trim();
            var actor = Sanitize(context.User.Identity?.Name ?? "unknown");
            if (actor.Length > 100) actor = actor[..100];
            var reason = Sanitize(input.Reason);
            foreach (var order in orders)
            {
                if (kind == "BOM") order.BomRevisionId = input.CandidateId;
                else order.RoutingRevisionId = input.CandidateId;
                order.UpdatedUtc = DateTime.UtcNow;
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit
                {
                    OrderId = order.Id,
                    Action = AuditAction,
                    Details = $"Kind={kind};From={input.BaselineId:N};To={input.CandidateId:N};Actor={actor};Reason={reason}"
                });
            }
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "دستور هم‌زمان تغییر کرده؛ هیچ انتقالی نهایی نشد." });
            }
            return Results.Ok(new
            {
                dryRun = false, applied = orders.Count, kind, input.BaselineId,
                input.CandidateId, rows = assessed,
                disclaimer = "فقط نسخه دستورات پیش‌نویس تغییر کرده است؛ ECO تأییدشده/امضاشده یا دستورات آزادشده دست‌کاری نشده‌اند."
            });
        });
        return routes;
    }
}
