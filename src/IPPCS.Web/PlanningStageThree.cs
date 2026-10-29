using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public enum PlanningApproval { Draft, Approved }
public enum ManufacturingRevisionState { Draft, Approved }

public sealed class ManufacturingBomRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentItemId { get; set; }
    public string Revision { get; set; } = "";
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public ManufacturingRevisionState State { get; set; }
    public List<ManufacturingBomLine> Lines { get; set; } = new();
}
public sealed class ManufacturingBomLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public ManufacturingBomRevision Revision { get; set; } = null!;
    public Guid ComponentItemId { get; set; }
    public decimal QuantityPer { get; set; }
    public decimal ScrapPercent { get; set; }
    public int Sequence { get; set; }
    // Null means a mandatory BOM component. Lines with the same non-empty group
    // code are mutually substitutable options for one requirement, not additive demand.
    public string? AlternativeGroupCode { get; set; }
    public bool IsDefaultAlternative { get; set; }
    public int AlternativePriority { get; set; } = 1;
}
public sealed class MpsVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public PlanningApproval State { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<MpsDemandLine> Lines { get; set; } = new();
}
public sealed class MpsDemandLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MpsVersionId { get; set; }
    public MpsVersion Version { get; set; } = null!;
    public Guid ItemId { get; set; }
    public DateOnly NeededOn { get; set; }
    public decimal Quantity { get; set; }
    public string Reference { get; set; } = ""; // Internal planning reference only.
}
public sealed class PlanningBalance
{
    public Guid ItemId { get; set; } // Primary key
    public decimal PhysicalQuantity { get; set; } // Manually provided planning balance; NOT warehouse inventory
    public decimal ReservedQuantity { get; set; }
    public decimal QuarantinedQuantity { get; set; }
    public decimal SafetyQuantity { get; set; }
    public int LeadDays { get; set; }
    public decimal MinimumBatch { get; set; } = 1m;
    public decimal BatchMultiple { get; set; } = 1m;
    public DateOnly SnapshotDate { get; set; }
}
public sealed class PlanningExpectedReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public DateOnly AvailableOn { get; set; }
    public decimal Quantity { get; set; }
    public string Reference { get; set; } = ""; // Manually entered planning assumption, not purchasing module
}
public sealed class PlanningCalculation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MpsVersionId { get; set; }
    public DateTime CalculatedUtc { get; set; } = DateTime.UtcNow;
    public string AlgorithmVersion { get; set; } = "MRP-PREVIEW-1";
    public string InputSnapshotJson { get; set; } = "";
    public string ResultSnapshotJson { get; set; } = "";
}

public record BomRevisionInput(Guid ParentItemId, string Revision, string ValidFromJalali, string? ValidUntilJalali);
public record BomLineInput(Guid ComponentItemId, decimal QuantityPer, decimal ScrapPercent, int Sequence,
    string? AlternativeGroupCode = null, bool IsDefaultAlternative = false, int AlternativePriority = 1);
public record MpsVersionInput(string Code);
public record MpsLineInput(Guid ItemId, string NeededOnJalali, decimal Quantity, string? Reference);
public record PlanningBalanceInput(Guid ItemId, decimal PhysicalQuantity, decimal ReservedQuantity, decimal QuarantinedQuantity,
    decimal SafetyQuantity, int LeadDays, decimal MinimumBatch, decimal BatchMultiple, string SnapshotDateJalali);
public record PlanningReceiptInput(Guid ItemId, string AvailableOnJalali, decimal Quantity, string? Reference);
public record MrpRow(Guid ItemId, DateOnly NeededOn, decimal GrossRequired, decimal OpeningAvailable,
    decimal NetShortage, decimal SuggestedQuantity, DateOnly? SuggestedRelease, decimal ClosingAvailable);
public record CrpRow(Guid WorkCenterId, DateOnly NeededOn, decimal RequiredMinutes,
    decimal CapacityMinutes, decimal OverloadMinutes, decimal? UtilizationPercent);

// Product-dependent capacity is expressed as output units for a specific item/operation,
// not as a universal "units per work center" number. This is a rough-cut view: each
// row's standalone output assumes no competing product consumes that center's minutes.
public record CrpProductCapacityRow(Guid MpsLineId, Guid ItemId, DateOnly NeededOn,
    decimal DemandQuantity, Guid WorkCenterId, int OperationSequence, string OperationName,
    decimal SetupMinutes, decimal RunMinutesPerUnit, decimal YieldPercent,
    decimal? ProductCapacityUnitsPerHour, bool HasProductCapacityOverride,
    decimal RequiredMinutes, decimal AvailableMinutes, decimal StandaloneCapacityUnits,
    decimal? UtilizationPercent, decimal OverloadMinutes);



public static class PlanningCalculators
{
    public static List<MrpRow> RunMrp(
        IReadOnlyList<MpsDemandLine> lines, IReadOnlyList<ManufacturingBomRevision> revisions,
        IReadOnlyList<PlanningBalance> balances, IReadOnlyList<PlanningExpectedReceipt> receipts,
        IReadOnlyDictionary<Guid, PlanningItemType> types)
    {
        var graph = revisions.Where(x => x.State == ManufacturingRevisionState.Approved)
            .GroupBy(x => x.ParentItemId).ToDictionary(x => x.Key, x => x.ToList());
        var depth = new Dictionary<Guid,int>();
        var visiting = new HashSet<Guid>();
        int AssignDepth(Guid id, int layer)
        {
            if (!types.ContainsKey(id)) throw new InvalidOperationException("کالای موردنیاز در اطلاعات پایه تعریف نشده است.");
            if (!visiting.Add(id)) throw new InvalidOperationException("در نسخه‌های تأییدشده BOM چرخه شناسایی شد.");
            depth[id] = Math.Max(depth.GetValueOrDefault(id),layer);
            if (graph.TryGetValue(id,out var r))
                foreach(var part in r.SelectMany(x=>x.Lines)) AssignDepth(part.ComponentItemId, layer+1);
            visiting.Remove(id);
            return depth[id];
        }
        foreach(var root in lines.Select(x=>x.ItemId).Distinct()) AssignDepth(root,0);

        var demands = lines.GroupBy(x=> (x.ItemId,x.NeededOn))
            .ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Quantity));
        var demandedItems = demands.Keys.Select(x=>x.ItemId).ToHashSet();
        var byBalance = balances.ToDictionary(x=>x.ItemId);
        var byReceipt = receipts.GroupBy(x=>x.ItemId)
            .ToDictionary(g=>g.Key,g=>g.OrderBy(x=>x.AvailableOn).ToList());
        var rows = new List<MrpRow>();

        foreach (var itemId in depth.OrderBy(x=>x.Value).ThenBy(x=>x.Key).Select(x=>x.Key))
        {
            // Approved historical revisions can reference components that are not needed by any selected release date.
            // Do not require planning balances for graph-only items with no dated demand.
            if (!demandedItems.Contains(itemId)) continue;
            var balance = byBalance.GetValueOrDefault(itemId)
                ?? throw new InvalidOperationException("موجودی مبنا یا قواعد زمان تأمین کالا ثبت نشده است.");
            decimal available = Math.Max(0m,
                balance.PhysicalQuantity-balance.ReservedQuantity-balance.QuarantinedQuantity-balance.SafetyQuantity);
            var arrivals = byReceipt.GetValueOrDefault(itemId) ?? new List<PlanningExpectedReceipt>();
            var idx=0;
            var needs = demands.Where(x=>x.Key.ItemId==itemId).OrderBy(x=>x.Key.NeededOn).ToArray();
            foreach(var n in needs)
            {
                if(n.Value<=0)continue;
                while(idx<arrivals.Count && arrivals[idx].AvailableOn<=n.Key.NeededOn)
                    available += arrivals[idx++].Quantity;
                decimal shortage=Math.Max(0,n.Value-available),planned=0;
                if(shortage>0)
                {
                    var min=balance.MinimumBatch;
                    var multiple=balance.BatchMultiple;
                    if(min<=0||multiple<=0)throw new InvalidOperationException("حداقل بچ یا مضرب بچ نامعتبر است.");
                    var minimumRequired=Math.Max(shortage,min);
                    planned=decimal.Ceiling(minimumRequired/multiple)*multiple;
                }
                var release=planned>0?n.Key.NeededOn.AddDays(-balance.LeadDays):(DateOnly?)null;
                var before=available;
                available += planned-n.Value;
                rows.Add(new MrpRow(itemId,n.Key.NeededOn,n.Value,before,shortage,planned,release,available));
                if(planned==0)continue;
                // BOM version is selected at proposed production RELEASE date, not finish date.
                var selected=graph.GetValueOrDefault(itemId)?.Where(x=>x.ValidFrom<=release!.Value
                    && (x.ValidUntil is null||release.Value<=x.ValidUntil.Value)).ToList()
                    ?? new List<ManufacturingBomRevision>();
                if(selected.Count>1)throw new InvalidOperationException("بیش از یک نسخه BOM معتبر برای تاریخ شروع ساخت یافت شد.");
                if(selected.Count==0)
                {
                    if(types[itemId] is PlanningItemType.Finished or PlanningItemType.SemiFinished)
                        throw new InvalidOperationException("BOM مصوب معتبر برای یک کالای ساختنی موجود نیست.");
                    continue;
                }
                var effective=selected[0];
                // Mandatory lines are additive. Each alternative group contributes exactly
                // one default option to baseline MRP; order-specific mixes are handled separately.
                var effectiveLines=effective.Lines.Where(x=>string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
                    .Concat(effective.Lines.Where(x=>!string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
                        .GroupBy(x=>x.AlternativeGroupCode!,StringComparer.OrdinalIgnoreCase)
                        .Select(group=>group.SingleOrDefault(x=>x.IsDefaultAlternative)
                            ??throw new InvalidOperationException("گروه آلترناتیو BOM باید دقیقاً یک قطعه پیش‌فرض داشته باشد.")))
                    .ToArray();
                foreach(var part in effectiveLines)
                {
                    decimal partNeed=planned*part.QuantityPer/(1-part.ScrapPercent/100m);
                    var key=(part.ComponentItemId,release!.Value);
                    demands[key]=demands.GetValueOrDefault(key)+partNeed;
                    demandedItems.Add(part.ComponentItemId);
                }
            }
        }
        return rows.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ToList();
    }

    public static List<CrpProductCapacityRow> RunProductCapacityCrp(
        IReadOnlyList<MpsDemandLine> lines,
        IReadOnlyList<RoutingRevision> routings,
        IReadOnlyList<WorkShift> shifts,
        IReadOnlyDictionary<Guid, decimal>? productCapacityRates = null)
    {
        var rows=new List<CrpProductCapacityRow>();
        foreach(var line in lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ThenBy(x=>x.Id))
        {
            if(line.Quantity<=0)throw new InvalidOperationException("مقدار تقاضای MPS باید مثبت باشد.");
            var matches=routings.Where(r=>r.ItemId==line.ItemId&&r.State==RoutingState.Approved
                &&r.EffectiveFrom<=line.NeededOn&&(r.EffectiveTo is null||line.NeededOn<=r.EffectiveTo.Value)).ToArray();
            if(matches.Length!=1)
                throw new InvalidOperationException("برای هر محصول و تاریخ برنامه باید دقیقاً یک مسیر ساخت مصوب معتبر وجود داشته باشد.");
            var route=matches[0];
            if(route.Operations.Count==0)throw new InvalidOperationException("مسیر ساخت محصول فاقد عملیات است.");
            var operations=route.Operations.OrderBy(x=>x.Sequence).ToArray();
            if(operations.Select(x=>x.Sequence).Distinct().Count()!=operations.Length)
                throw new InvalidOperationException("شماره عملیات در مسیر ساخت محصول باید یکتا باشد.");
            for(var i=0;i<operations.Length;i++)
            {
                var op=operations[i];
                if(op.WorkCenterId==Guid.Empty||op.Sequence<=0||op.SetupMinutes<0||
                    op.RunMinutesPerUnit<=0||op.YieldPercent is <=0 or >100)
                    throw new InvalidOperationException("زمان استاندارد، بازده یا مرکز عملیات محصول نامعتبر است.");
                // An optional work-center/product/operation rate overrides routing throughput.
                // The rate is processed units/hour; setup time and cumulative yield remain in Routing.
                decimal? configuredUnitsPerHour=null;
                if(productCapacityRates is not null &&
                    productCapacityRates.TryGetValue(op.Id,out var configuredRate))
                {
                    if(configuredRate<=0)
                        throw new InvalidOperationException("ظرفیت محصول‌محور ذخیره‌شده باید مثبت باشد.");
                    configuredUnitsPerHour=configuredRate;
                }
                var hasRate=configuredUnitsPerHour.HasValue;
                var effectiveRunMinutes=hasRate?60m/configuredUnitsPerHour!.Value:op.RunMinutesPerUnit;
                decimal cumulativeYield=1m;
                for(var j=i;j<operations.Length;j++)
                {
                    if(operations[j].YieldPercent is <=0 or >100)
                        throw new InvalidOperationException("بازده عملیات ساخت نامعتبر است.");
                    cumulativeYield*=operations[j].YieldPercent/100m;
                }
                var runPerFinishedUnit=effectiveRunMinutes/cumulativeYield;
                var required=op.SetupMinutes+line.Quantity*runPerFinishedUnit;
                var available=FactoryShiftCalendar.AvailableMinutes(op.WorkCenterId,line.NeededOn,shifts);
                var standalone=available<=op.SetupMinutes?0m:
                    decimal.Floor((available-op.SetupMinutes)/runPerFinishedUnit);
                rows.Add(new CrpProductCapacityRow(line.Id,line.ItemId,line.NeededOn,line.Quantity,
                    op.WorkCenterId,op.Sequence,op.Name,op.SetupMinutes,effectiveRunMinutes,
                    op.YieldPercent,configuredUnitsPerHour,hasRate,required,available,standalone,
                    available==0?null:Math.Round(required/available*100m,2),
                    Math.Max(0m,required-available)));
            }
        }
        return rows;
    }

    public static List<CrpRow> RunRoughCutCrp(IReadOnlyList<MpsDemandLine> lines,
        IReadOnlyList<RoutingRevision> routings,IReadOnlyList<WorkShift> shifts,
        IReadOnlyDictionary<Guid, decimal>? productCapacityRates = null)
    {
        var loads=new Dictionary<(Guid WorkCenterId,DateOnly NeededOn),decimal>();
        foreach(var line in lines)
        {
            var route=routings.Where(r=>r.ItemId==line.ItemId && r.State==RoutingState.Approved
                && r.EffectiveFrom<=line.NeededOn && (r.EffectiveTo is null||line.NeededOn<=r.EffectiveTo.Value)).ToArray();
            if(route.Length!=1)throw new InvalidOperationException("برای هر محصول و تاریخ برنامه باید دقیقاً یک مسیر ساخت مصوب معتبر وجود داشته باشد.");
            var operations=route[0].Operations.OrderBy(x=>x.Sequence).ToArray();
            if(operations.Length==0||operations.Select(x=>x.Sequence).Distinct().Count()!=operations.Length)
                throw new InvalidOperationException("مسیر ساخت محصول باید عملیات دارای شماره یکتا داشته باشد.");
            for(var i=0;i<operations.Length;i++)
            {
                var op=operations[i];
                if(op.WorkCenterId==Guid.Empty||op.Sequence<=0||op.SetupMinutes<0||
                    op.RunMinutesPerUnit<=0||op.YieldPercent is <=0 or >100)
                    throw new InvalidOperationException("زمان استاندارد، بازده یا مرکز عملیات محصول نامعتبر است.");
                decimal cumulativeYield=1m;
                for(var j=i;j<operations.Length;j++)
                {
                    if(operations[j].YieldPercent is <=0 or >100)
                        throw new InvalidOperationException("بازده عملیات ساخت نامعتبر است.");
                    cumulativeYield*=operations[j].YieldPercent/100m;
                }
                var key=(op.WorkCenterId,line.NeededOn);
                var effectiveRunMinutes=op.RunMinutesPerUnit;
                if(productCapacityRates is not null && productCapacityRates.TryGetValue(op.Id,out var unitsPerHour))
                {
                    if(unitsPerHour<=0)
                        throw new InvalidOperationException("ظرفیت محصول‌محور ذخیره‌شده باید مثبت باشد.");
                    effectiveRunMinutes=60m/unitsPerHour;
                }
                loads[key]=loads.GetValueOrDefault(key)+op.SetupMinutes+line.Quantity*effectiveRunMinutes/cumulativeYield;
            }
        }
        return loads.OrderBy(x=>x.Key.NeededOn).ThenBy(x=>x.Key.WorkCenterId).Select(x=>{
            // Wall-clock calendar day capacity includes night-shift minutes
            // carried from the previous weekday; no negative TimeOnly delta.
            var cap=FactoryShiftCalendar.AvailableMinutes(
                x.Key.WorkCenterId,x.Key.NeededOn,shifts);
            return new CrpRow(x.Key.WorkCenterId,x.Key.NeededOn,x.Value,cap,
                Math.Max(0,x.Value-cap),cap==0?null:Math.Round(x.Value/cap*100m,2));
        }).ToList();
    }
}

public static class PlanningStageThreeEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingBomRevision>().HasIndex(x=>new{x.ParentItemId,x.Revision}).IsUnique();
        model.Entity<ManufacturingBomRevision>().Property(x=>x.Revision).HasMaxLength(32);
        model.Entity<ManufacturingBomRevision>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ParentItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingBomLine>().HasOne(x=>x.Revision).WithMany(x=>x.Lines).HasForeignKey(x=>x.RevisionId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ManufacturingBomLine>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ComponentItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingBomLine>().HasIndex(x=>new{x.RevisionId,x.ComponentItemId}).IsUnique();
        model.Entity<ManufacturingBomLine>().Property(x=>x.AlternativeGroupCode).HasMaxLength(32);
        model.Entity<ManufacturingBomLine>().Property(x=>x.QuantityPer).HasPrecision(18,6);
        model.Entity<ManufacturingBomLine>().Property(x=>x.ScrapPercent).HasPrecision(8,4);
        model.Entity<MpsVersion>().HasIndex(x=>x.Code).IsUnique();
        model.Entity<MpsVersion>().Property(x=>x.Code).HasMaxLength(64);
        model.Entity<MpsDemandLine>().HasOne(x=>x.Version).WithMany(x=>x.Lines).HasForeignKey(x=>x.MpsVersionId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<MpsDemandLine>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MpsDemandLine>().Property(x=>x.Quantity).HasPrecision(18,4);
        model.Entity<MpsDemandLine>().Property(x=>x.Reference).HasMaxLength(120);
        model.Entity<PlanningBalance>().HasKey(x=>x.ItemId);
        model.Entity<PlanningBalance>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);
        foreach(var name in new[]{nameof(PlanningBalance.PhysicalQuantity),nameof(PlanningBalance.ReservedQuantity),
            nameof(PlanningBalance.QuarantinedQuantity),nameof(PlanningBalance.SafetyQuantity),
            nameof(PlanningBalance.MinimumBatch),nameof(PlanningBalance.BatchMultiple)})
            model.Entity<PlanningBalance>().Property<decimal>(name).HasPrecision(18,4);
        model.Entity<PlanningExpectedReceipt>().HasOne<PlanningItem>().WithMany().HasForeignKey(x=>x.ItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PlanningExpectedReceipt>().Property(x=>x.Quantity).HasPrecision(18,4);
        model.Entity<PlanningExpectedReceipt>().Property(x=>x.Reference).HasMaxLength(120);
        model.Entity<PlanningCalculation>().Property(x=>x.AlgorithmVersion).HasMaxLength(40);
    }

    public static IEndpointRouteBuilder MapPlanningStageThree(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        var planningApi = routes.MapGroup("/api/v1").RequireAuthorization("IpPlanningWrite");
        api.MapGet("/bom-revisions",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .OrderBy(x=>x.ParentItemId).ThenBy(x=>x.ValidFrom).Take(400).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.Id,x.ParentItemId,x.Revision,x.State,
                validFromJalali=PersianPlanningDates.Format(x.ValidFrom),
                validUntilJalali=x.ValidUntil.HasValue?PersianPlanningDates.Format(x.ValidUntil.Value):null,
                lines=x.Lines.OrderBy(l=>l.Sequence).ThenBy(l=>l.AlternativePriority).Select(l=>new{
                    l.Id,l.ComponentItemId,l.QuantityPer,l.ScrapPercent,l.Sequence,
                    l.AlternativeGroupCode,l.IsDefaultAlternative,l.AlternativePriority})}));
        });
        planningApi.MapPost("/bom-revisions",async(BomRevisionInput input,ProductionDb db,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Revision)||input.Revision.Trim().Length>32
                ||!PersianPlanningDates.TryParse(input.ValidFromJalali,out var from))
                return Results.BadRequest(new{message="نسخه و تاریخ شروع معتبر لازم است."});
            DateOnly? until=null;
            if(!string.IsNullOrWhiteSpace(input.ValidUntilJalali)){
                if(!PersianPlanningDates.TryParse(input.ValidUntilJalali,out var d)||d<from)
                    return Results.BadRequest(new{message="تاریخ پایان نامعتبر است."});
                until=d;
            }
            if(!await db.Items.AnyAsync(x=>x.Id==input.ParentItemId,ct))
                return Results.BadRequest(new{message="کالای والد وجود ندارد."});
            var code=input.Revision.Trim();
            if(await db.ManufacturingBomRevisions.AnyAsync(x=>x.ParentItemId==input.ParentItemId && x.Revision==code,ct))
                return Results.Conflict(new{message="این نسخه قبلاً ثبت شده است."});
            var rev=new ManufacturingBomRevision{ParentItemId=input.ParentItemId,Revision=code,ValidFrom=from,ValidUntil=until};
            db.ManufacturingBomRevisions.Add(rev);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/bom-revisions/{rev.Id}",new{rev.Id,rev.Revision});
        });
        planningApi.MapPost("/bom-revisions/{id:guid}/lines",async(Guid id,BomLineInput input,ProductionDb db,CancellationToken ct)=>{
            var owner=await db.ManufacturingBomRevisions.AsNoTracking().Where(x=>x.Id==id)
                .Select(x=>(Guid?)x.ParentItemId).SingleOrDefaultAsync(ct);
            if(owner is null)return Results.NotFound();
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryRevisionGovernance.LockRevisionItem(db,"BOM",owner.Value,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل اصلاح BOM در دسترس نیست."});}
            var rev=await db.ManufacturingBomRevisions.SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(rev is null)return Results.NotFound();
            if(rev.State!=ManufacturingRevisionState.Draft)return Results.Conflict(new{message="نسخه قفل است."});
            var groupCode=string.IsNullOrWhiteSpace(input.AlternativeGroupCode)
                ?null:input.AlternativeGroupCode.Trim().ToUpperInvariant();
            if(input.ComponentItemId==rev.ParentItemId||input.Sequence<=0||input.QuantityPer<=0
                ||input.ScrapPercent is <0 or >=100||input.AlternativePriority<=0
                ||(groupCode is not null&&groupCode.Length>32)
                ||(groupCode is null&&input.IsDefaultAlternative))
                return Results.BadRequest(new{message="مقادیر جزء یا گروه آلترناتیو معتبر نیست."});
            if(!await db.Items.AnyAsync(x=>x.Id==input.ComponentItemId,ct))
                return Results.BadRequest(new{message="جزء انتخاب‌شده وجود ندارد."});
            var existingLines=await db.ManufacturingBomLines.AsNoTracking()
                .Where(x=>x.RevisionId==id).ToListAsync(ct);
            if(existingLines.Any(x=>x.ComponentItemId==input.ComponentItemId))
                return Results.Conflict(new{message="یک قطعه نمی‌تواند دوبار در یک نسخه BOM تعریف شود."});
            if(groupCode is null&&existingLines.Any(x=>x.Sequence==input.Sequence))
                return Results.Conflict(new{message="شماره ترتیب جزء اجباری تکراری است."});
            if(groupCode is not null&&existingLines.Any(x=>
                (x.AlternativeGroupCode==groupCode&&x.Sequence!=input.Sequence)||
                (x.Sequence==input.Sequence&&
                 !string.Equals(x.AlternativeGroupCode,groupCode,StringComparison.OrdinalIgnoreCase))))
                return Results.Conflict(new{message="اعضای یک گروه آلترناتیو باید ترتیب یکسان داشته باشند و با جزء اجباری هم‌ردیف نشوند."});
            if(groupCode is not null&&input.IsDefaultAlternative&&existingLines.Any(x=>
                x.AlternativeGroupCode==groupCode&&x.IsDefaultAlternative))
                return Results.Conflict(new{message="برای این گروه قبلاً آلترناتیو پیش‌فرض تعیین شده است."});
            var part=new ManufacturingBomLine{RevisionId=id,ComponentItemId=input.ComponentItemId,
                Sequence=input.Sequence,QuantityPer=input.QuantityPer,ScrapPercent=input.ScrapPercent,
                AlternativeGroupCode=groupCode,IsDefaultAlternative=input.IsDefaultAlternative,
                AlternativePriority=input.AlternativePriority};
            db.ManufacturingBomLines.Add(part);await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/bom-revisions/{id}",new{part.Id});
        });
        planningApi.MapPost("/bom-revisions/{id:guid}/approve",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var owner=await db.ManufacturingBomRevisions.AsNoTracking()
                .Where(x=>x.Id==id).Select(x=>(Guid?)x.ParentItemId).SingleOrDefaultAsync(ct);
            if(owner is null)return Results.NotFound();
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            // Serialize by item BEFORE acquiring repeatable-read row locks.
            try{await FactoryRevisionGovernance.LockRevisionItem(db,"BOM",owner.Value,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل تصویب BOM در دسترس نیست."});}
            var rev=await db.ManufacturingBomRevisions.Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(rev is null)return Results.NotFound();
            if(rev.State!=ManufacturingRevisionState.Draft)return Results.Conflict(new{message="نسخه پیش‌نویس نیست."});
            if(rev.Lines.Count==0)return Results.BadRequest(new{message="BOM بدون جزء قابل تأیید نیست."});
            var other=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            var issues=FactoryRevisionGovernance.BomIssues(rev,other);
            if(issues.Count>0)return Results.Conflict(new{message="BOM برای تصویب آماده نیست.",issues});
            if(other.Any(x=>x.ParentItemId==rev.ParentItemId && x.ValidFrom<=(rev.ValidUntil??DateOnly.MaxValue)
                && rev.ValidFrom<=(x.ValidUntil??DateOnly.MaxValue)))
                return Results.Conflict(new{message="نسخه‌های تأییدشده دارای همپوشانی تاریخی هستند."});
            if(FactoryRevisionGovernance.HasEffectiveBomCycle(other.Where(x=>x.Id!=rev.Id).Append(rev)))
                return Results.Conflict(new{message="ساختار BOM در یک بازه تاریخی مشترک موجب چرخه می‌شود."});
            rev.State=ManufacturingRevisionState.Approved;await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new{rev.Id,rev.State});
        });

        api.MapGet("/mps",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines).OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.Id,x.Code,x.State,x.CreatedUtc,
                lines=x.Lines.OrderBy(y=>y.NeededOn).Select(y=>new{y.Id,y.ItemId,y.Quantity,
                    neededOnJalali=PersianPlanningDates.Format(y.NeededOn),y.Reference})}));
        });
        planningApi.MapPost("/mps",async(MpsVersionInput input,ProductionDb db,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Code)||input.Code.Trim().Length>64)
                return Results.BadRequest(new{message="کد نسخه برنامه معتبر نیست."});
            var code=input.Code.Trim();
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryMpsChangeControl.LockMps(db,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل تغییر نسخه MPS در دسترس نیست."});}
            if(await db.MpsVersions.AnyAsync(x=>x.Code==code,ct))
                return Results.Conflict(new{message="کد برنامه تکراری است."});
            var version=new MpsVersion{Code=code};db.MpsVersions.Add(version);await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/mps/{version.Id}",new{version.Id,version.Code});
        });
        planningApi.MapPost("/mps/{id:guid}/lines",async(Guid id,MpsLineInput input,ProductionDb db,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryMpsChangeControl.LockMps(db,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل تغییر ردیف MPS در دسترس نیست."});}
            var version=await db.MpsVersions.FindAsync([id],ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Draft)return Results.Conflict(new{message="برنامه تأییدشده قابل تغییر نیست."});
            if(input.Quantity<=0||!PersianPlanningDates.TryParse(input.NeededOnJalali,out var date)
                ||(input.Reference?.Length??0)>120)
                return Results.BadRequest(new{message="مقدار، مرجع یا تاریخ شمسی نامعتبر است."});
            var type=await db.Items.Where(x=>x.Id==input.ItemId).Select(x=>(PlanningItemType?)x.Type).SingleOrDefaultAsync(ct);
            if(type is null||type!=PlanningItemType.Finished)
                return Results.BadRequest(new{message="فقط محصول نهایی در MPS ثبت شود."});
            var line=new MpsDemandLine{MpsVersionId=id,ItemId=input.ItemId,
                NeededOn=date,Quantity=input.Quantity,Reference=input.Reference?.Trim()??""};
            db.MpsDemandLines.Add(line);await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/mps/{id}",new{line.Id});
        });
        planningApi.MapPost("/mps/{id:guid}/approve",async(Guid id,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryMpsChangeControl.LockMps(db,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل تصویب MPS در دسترس نیست."});}
            var version=await db.MpsVersions.Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Draft)return Results.Conflict(new{message="برنامه قبلاً تأیید شده است."});
            if(version.Lines.Count==0)return Results.BadRequest(new{message="برنامه بدون ردیف قابل تأیید نیست."});
            if(version.Lines.Any(x=>x.Quantity<=0)||version.Lines.Count>5000)
                return Results.Conflict(new{message="نسخه دارای ردیف نامعتبر یا بیش از سقف کنترل‌شده است."});
            var actor=ctx.User.Identity?.Name??"unknown";
            // A controlled revision is not a normal new plan: before publishing
            // its changed demand we must recalculate and freeze actual MRP/CRP
            // implications and the parent source under the *same* MPS SQL lock.
            var originEvidence=await db.PlanningCalculations.AsNoTracking()
                .Where(x=>x.MpsVersionId==id&&
                    x.AlgorithmVersion==FactoryMpsChangeControl.RevisionAlgorithm)
                .ToListAsync(ct);
            if(originEvidence.Count>1)
                return Results.Conflict(new{message="اسناد منشأ بازنگری MPS تکراری یا ناسازگار هستند."});
            if(originEvidence.Count==1)
            {
                var origin=FactoryMpsChangeControl.ParseOrigin(originEvidence[0].InputSnapshotJson);
                if(origin is null||origin.DraftVersionId!=version.Id)
                    return Results.Conflict(new{message="سند منشأ بازنگری MPS معتبر نیست."});
                var basePlan=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                    .SingleOrDefaultAsync(x=>x.Id==origin.SourceVersionId,ct);
                if(basePlan is null||basePlan.State!=PlanningApproval.Approved||
                    version.Lines.Count>5000)
                    return Results.Conflict(new{message="نسخه مبنای مصوب بازنگری موجود یا معتبر نیست."});
                FactoryMpsChangeImpact impact;
                try{impact=await FactoryMpsChangeControl.CalculateActualImpact(db,basePlan,version,ct);}
                catch(Exception ex)when(ex is InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
                    {return Results.UnprocessableEntity(new{message="بررسی اثر برنامه بر ظرفیت یا فرضیات MRP کامل نیست: "+ex.Message});}
                db.PlanningCalculations.Add(new PlanningCalculation{
                    MpsVersionId=id,
                    AlgorithmVersion=FactoryMpsChangeControl.ImpactApprovalAlgorithm,
                    InputSnapshotJson=JsonSerializer.Serialize(new{
                        origin.SourceVersionId,version.Id,origin.Reason,approvedBy=actor,
                        approvedUtc=DateTime.UtcNow,
                        lines=version.Lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId)
                            .Select(x=>new{x.Id,x.ItemId,x.NeededOn,x.Quantity,x.Reference})}),
                    ResultSnapshotJson=JsonSerializer.Serialize(impact,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})});
            }
            db.PlanningCalculations.Add(new PlanningCalculation{
                MpsVersionId=version.Id,AlgorithmVersion=FactoryMpsChangeControl.ApprovalAlgorithm,
                InputSnapshotJson=JsonSerializer.Serialize(new{
                    version.Id,version.Code,approvedBy=actor,approvedUtc=DateTime.UtcNow,
                    lines=version.Lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId)
                        .Select(x=>new{x.Id,x.ItemId,x.NeededOn,x.Quantity,x.Reference})}),
                ResultSnapshotJson=JsonSerializer.Serialize(new{status="Approved",
                    lineCount=version.Lines.Count,quantity=version.Lines.Sum(x=>x.Quantity)})});
            version.State=PlanningApproval.Approved;await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new{version.Id,version.State});
        });

        api.MapGet("/planning-balances",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.PlanningBalances.AsNoTracking().OrderBy(x=>x.ItemId).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.ItemId,x.PhysicalQuantity,x.ReservedQuantity,x.QuarantinedQuantity,
                x.SafetyQuantity,x.LeadDays,x.MinimumBatch,x.BatchMultiple,
                snapshotDateJalali=PersianPlanningDates.Format(x.SnapshotDate)}));
        });
        planningApi.MapPut("/planning-balances/{itemId:guid}",async(Guid itemId,PlanningBalanceInput input,ProductionDb db,CancellationToken ct)=>{
            if(itemId!=input.ItemId||!PersianPlanningDates.TryParse(input.SnapshotDateJalali,out var on)
                ||input.PhysicalQuantity<0||input.ReservedQuantity<0||input.QuarantinedQuantity<0
                ||input.SafetyQuantity<0||input.LeadDays is <0 or >3650
                ||input.MinimumBatch<=0||input.BatchMultiple<=0
                ||input.ReservedQuantity+input.QuarantinedQuantity>input.PhysicalQuantity)
                return Results.BadRequest(new{message="موجودی فرضی، دسته‌بندی، یا تاریخ معتبر نیست."});
            if(!await db.Items.AnyAsync(x=>x.Id==itemId,ct))return Results.NotFound();
            var balance=await db.PlanningBalances.FindAsync([itemId],ct);
            if(balance is null){balance=new PlanningBalance{ItemId=itemId};db.PlanningBalances.Add(balance);}
            balance.PhysicalQuantity=input.PhysicalQuantity;balance.ReservedQuantity=input.ReservedQuantity;
            balance.QuarantinedQuantity=input.QuarantinedQuantity;balance.SafetyQuantity=input.SafetyQuantity;
            balance.LeadDays=input.LeadDays;balance.MinimumBatch=input.MinimumBatch;
            balance.BatchMultiple=input.BatchMultiple;balance.SnapshotDate=on;
            await db.SaveChangesAsync(ct);return Results.Ok(new{balance.ItemId});
        });
        api.MapGet("/planning-receipts",async(ProductionDb db,CancellationToken ct)=>{
            var data=await db.PlanningExpectedReceipts.AsNoTracking().OrderBy(x=>x.AvailableOn).Take(500).ToListAsync(ct);
            return Results.Ok(data.Select(x=>new{x.Id,x.ItemId,x.Quantity,x.Reference,
                availableOnJalali=PersianPlanningDates.Format(x.AvailableOn)}));
        });
        planningApi.MapPost("/planning-receipts",async(PlanningReceiptInput input,ProductionDb db,CancellationToken ct)=>{
            if(input.Quantity<=0||!PersianPlanningDates.TryParse(input.AvailableOnJalali,out var date)
                ||(input.Reference?.Length??0)>120)
                return Results.BadRequest(new{message="تاریخ و مقدار دریافت برنامه‌ای معتبر نیست."});
            if(!await db.Items.AnyAsync(x=>x.Id==input.ItemId,ct))return Results.BadRequest(new{message="کالا وجود ندارد."});
            var receipt=new PlanningExpectedReceipt{ItemId=input.ItemId,AvailableOn=date,
                Quantity=input.Quantity,Reference=input.Reference?.Trim()??""};
            db.PlanningExpectedReceipts.Add(receipt);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/planning-receipts/{receipt.Id}",new{receipt.Id});
        });
        planningApi.MapPost("/mrp/{id:guid}/calculate",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Approved)return Results.Conflict(new{message="ابتدا نسخه MPS را تأیید کنید."});
            var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines).ToListAsync(ct);
            var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            var receipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            var types=await db.Items.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Type,ct);
            if(balances.Any(x=>x.SnapshotDate!=balances[0].SnapshotDate))
                return Results.Conflict(new{message="تاریخ مبنای موجودی‌های برنامه‌ریزی یکسان نیست."});
            if(balances.Count!=0 && version.Lines.Any(x=>x.NeededOn<balances[0].SnapshotDate))
                return Results.Conflict(new{message="موعد برنامه قبل از تاریخ مبنای موجودی است."});
            if(balances.Count!=0 && receipts.Any(x=>x.AvailableOn<=balances[0].SnapshotDate))
                return Results.Conflict(new{message="دریافت پیش از تاریخ مبنای موجودی باعث احتساب دوباره می‌شود؛ آن را حذف یا در مانده مبنا لحاظ کنید."});
            List<MrpRow> result;
            try{result=PlanningCalculators.RunMrp(version.Lines,bom,balances,receipts,types);}
            catch(Exception ex)when(ex is InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
                {return Results.UnprocessableEntity(new{message=ex.Message});}
            IReadOnlyList<MrpSuggestionExplanation> explanations;
            try{explanations=NetMrpSuggestionExplainer.Explain(result,balances);}
            catch(InvalidOperationException ex){return Results.UnprocessableEntity(new{message=ex.Message});}
            var snapshot=new PlanningCalculation{
                MpsVersionId=id,InputSnapshotJson=JsonSerializer.Serialize(new{
                    mps=new{version.Id,version.Code,version.State,lines=version.Lines.Select(l=>new{
                        l.Id,l.ItemId,l.NeededOn,l.Quantity,l.Reference})},
                    bom=bom.Select(b=>new{b.Id,b.ParentItemId,b.Revision,b.State,b.ValidFrom,b.ValidUntil,
                        lines=b.Lines.Select(l=>new{l.ComponentItemId,l.QuantityPer,l.ScrapPercent,l.Sequence})}),
                    balances,receipts,types}),
                ResultSnapshotJson=JsonSerializer.Serialize(result)};
            db.PlanningCalculations.Add(snapshot);await db.SaveChangesAsync(ct);
            return Results.Ok(new{runId=snapshot.Id,algorithm=snapshot.AlgorithmVersion,explanations=explanations.Select(x=>new{x.ItemId,neededOnJalali=PersianPlanningDates.Format(x.NeedDate),x.NetShortage,x.SuggestedReceipt,x.LotSizingSurplus,releaseJalali=x.ProposedReleaseDate.HasValue?PersianPlanningDates.Format(x.ProposedReleaseDate.Value):null,x.ReleaseBeforePlanningBaseline,x.Warning}),
                rows=result.Select(x=>new{x.ItemId,neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                    x.GrossRequired,x.OpeningAvailable,x.NetShortage,x.SuggestedQuantity,
                    suggestedReleaseJalali=x.SuggestedRelease.HasValue?PersianPlanningDates.Format(x.SuggestedRelease.Value):null,
                    x.ClosingAvailable})});
        });
        // Correct mistaken independent planning assumptions without changing saved MRP snapshots.
        planningApi.MapDelete("/planning-receipts/{id:guid}",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var receipt=await db.PlanningExpectedReceipts.FindAsync([id],ct);
            if(receipt is null)return Results.NotFound();
            db.PlanningExpectedReceipts.Remove(receipt);await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        planningApi.MapDelete("/mps/{versionId:guid}/lines/{lineId:guid}",async(Guid versionId,Guid lineId,ProductionDb db,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryMpsChangeControl.LockMps(db,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل تغییر ردیف MPS در دسترس نیست."});}
            var version=await db.MpsVersions.FindAsync([versionId],ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Draft)return Results.Conflict(new{message="فقط ردیف‌های برنامه پیش‌نویس قابل حذف هستند."});
            var line=await db.MpsDemandLines.SingleOrDefaultAsync(x=>x.Id==lineId&&x.MpsVersionId==versionId,ct);
            if(line is null)return Results.NotFound();
            db.MpsDemandLines.Remove(line);await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });
        planningApi.MapDelete("/bom-revisions/{revisionId:guid}/lines/{lineId:guid}",async(Guid revisionId,Guid lineId,ProductionDb db,CancellationToken ct)=>{
            var owner=await db.ManufacturingBomRevisions.AsNoTracking().Where(x=>x.Id==revisionId)
                .Select(x=>(Guid?)x.ParentItemId).SingleOrDefaultAsync(ct);
            if(owner is null)return Results.NotFound();
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            try{await FactoryRevisionGovernance.LockRevisionItem(db,"BOM",owner.Value,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){return Results.Conflict(new{message="قفل اصلاح BOM در دسترس نیست."});}
            var revision=await db.ManufacturingBomRevisions.FindAsync([revisionId],ct);
            if(revision is null)return Results.NotFound();
            if(revision.State!=ManufacturingRevisionState.Draft)return Results.Conflict(new{message="فقط اجزای BOM پیش‌نویس قابل حذف هستند."});
            var line=await db.ManufacturingBomLines.SingleOrDefaultAsync(x=>x.Id==lineId&&x.RevisionId==revisionId,ct);
            if(line is null)return Results.NotFound();
            db.ManufacturingBomLines.Remove(line);await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/mrp/runs/{runId:guid}",async(Guid runId,ProductionDb db,CancellationToken ct)=>{
            var run=await db.PlanningCalculations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId,ct);
            return run is null?Results.NotFound():Results.Ok(new{run.Id,run.MpsVersionId,run.CalculatedUtc,
                run.AlgorithmVersion,results=JsonSerializer.Deserialize<JsonElement>(run.ResultSnapshotJson)});
        });
        api.MapGet("/crp/{id:guid}/product-capacity-preview",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="نسخه MPS باید مصوب باشد."});
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var productCapacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
            try{
                var rows=PlanningCalculators.RunProductCapacityCrp(version.Lines,routes,shifts,productCapacityRates);
                return Results.Ok(new{
                    mode="ProductDependentRoughCutCapacity",
                    disclaimer="ظرفیت خروجی به محصول، مسیر، زمان آماده‌سازی، زمان واحد و بازده عملیات وابسته است. ظرفیت مستقل هر ردیف فرض می‌کند محصول دیگری از دقایق همان مرکز استفاده نمی‌کند؛ این عدد ظرفیت ترکیبی قابل‌تعهد نیست. تداخل زمانی و توالی واقعی را در APS بررسی کنید.",
                    rows=rows.Select(x=>new{x.MpsLineId,x.ItemId,demandOnJalali=PersianPlanningDates.Format(x.NeededOn),
                        x.DemandQuantity,x.WorkCenterId,x.OperationSequence,x.OperationName,
                        x.SetupMinutes,x.RunMinutesPerUnit,x.YieldPercent,
                        x.ProductCapacityUnitsPerHour,x.HasProductCapacityOverride,x.RequiredMinutes,
                        x.AvailableMinutes,x.StandaloneCapacityUnits,x.UtilizationPercent,x.OverloadMinutes})
                });
            }catch(InvalidOperationException ex){
                return Results.UnprocessableEntity(new{message=ex.Message});
            }
        });
        api.MapGet("/crp/{id:guid}/preview",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var productCapacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
            try{
                var rows=PlanningCalculators.RunRoughCutCrp(version.Lines,routes,shifts,productCapacityRates);
                return Results.Ok(new{mode="RoughCutSingleDay",rows=rows.Select(x=>new{
                    x.WorkCenterId,neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                    x.RequiredMinutes,x.CapacityMinutes,x.OverloadMinutes,x.UtilizationPercent})});
            }
            catch(InvalidOperationException ex){return Results.UnprocessableEntity(new{message=ex.Message});}
        });
        return routes;
    }
}
