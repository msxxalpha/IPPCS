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
public record BomLineInput(Guid ComponentItemId, decimal QuantityPer, decimal ScrapPercent, int Sequence);
public record MpsVersionInput(string Code);
public record MpsLineInput(Guid ItemId, string NeededOnJalali, decimal Quantity, string? Reference);
public record PlanningBalanceInput(Guid ItemId, decimal PhysicalQuantity, decimal ReservedQuantity, decimal QuarantinedQuantity,
    decimal SafetyQuantity, int LeadDays, decimal MinimumBatch, decimal BatchMultiple, string SnapshotDateJalali);
public record PlanningReceiptInput(Guid ItemId, string AvailableOnJalali, decimal Quantity, string? Reference);
public record MrpRow(Guid ItemId, DateOnly NeededOn, decimal GrossRequired, decimal OpeningAvailable,
    decimal NetShortage, decimal SuggestedQuantity, DateOnly? SuggestedRelease, decimal ClosingAvailable);
public record CrpRow(Guid WorkCenterId, DateOnly NeededOn, decimal RequiredMinutes,
    decimal CapacityMinutes, decimal OverloadMinutes, decimal? UtilizationPercent);

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
        var byBalance = balances.ToDictionary(x=>x.ItemId);
        var byReceipt = receipts.GroupBy(x=>x.ItemId)
            .ToDictionary(g=>g.Key,g=>g.OrderBy(x=>x.AvailableOn).ToList());
        var rows = new List<MrpRow>();

        foreach (var itemId in depth.OrderBy(x=>x.Value).ThenBy(x=>x.Key).Select(x=>x.Key))
        {
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
                foreach(var part in effective.Lines)
                {
                    decimal partNeed=planned*part.QuantityPer/(1-part.ScrapPercent/100m);
                    var key=(part.ComponentItemId,release!.Value);
                    demands[key]=demands.GetValueOrDefault(key)+partNeed;
                }
            }
        }
        return rows.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ToList();
    }

    public static List<CrpRow> RunRoughCutCrp(IReadOnlyList<MpsDemandLine> lines,
        IReadOnlyList<RoutingRevision> routings,IReadOnlyList<WorkShift> shifts)
    {
        var loads=new Dictionary<(Guid WorkCenterId,DateOnly NeededOn),decimal>();
        foreach(var line in lines)
        {
            var route=routings.Where(r=>r.ItemId==line.ItemId && r.State==RoutingState.Approved
                && r.EffectiveFrom<=line.NeededOn && (r.EffectiveTo is null||line.NeededOn<=r.EffectiveTo.Value)).ToArray();
            if(route.Length!=1)throw new InvalidOperationException("برای هر محصول و تاریخ برنامه باید دقیقاً یک مسیر ساخت مصوب معتبر وجود داشته باشد.");
            foreach(var op in route[0].Operations)
            {
                var key=(op.WorkCenterId,line.NeededOn);
                if(op.YieldPercent<=0)throw new InvalidOperationException("بازده عملیات ساخت نامعتبر است.");
                loads[key]=loads.GetValueOrDefault(key)+op.SetupMinutes+line.Quantity*op.RunMinutesPerUnit/(op.YieldPercent/100m);
            }
        }
        return loads.OrderBy(x=>x.Key.NeededOn).ThenBy(x=>x.Key.WorkCenterId).Select(x=>{
            var weekly=shifts.Where(s=>s.WorkCenterId==x.Key.WorkCenterId
                && s.DayOfWeek==(int)x.Key.NeededOn.DayOfWeek);
            var cap=weekly.Sum(s=>(decimal)(s.EndTime-s.StartTime).TotalMinutes*s.EffectiveCapacityPercent/100m);
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
        api.MapGet("/bom-revisions",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .OrderBy(x=>x.ParentItemId).ThenBy(x=>x.ValidFrom).Take(400).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.Id,x.ParentItemId,x.Revision,x.State,
                validFromJalali=PersianPlanningDates.Format(x.ValidFrom),
                validUntilJalali=x.ValidUntil.HasValue?PersianPlanningDates.Format(x.ValidUntil.Value):null,
                lines=x.Lines.OrderBy(l=>l.Sequence).Select(l=>new{l.Id,l.ComponentItemId,l.QuantityPer,l.ScrapPercent,l.Sequence})}));
        });
        api.MapPost("/bom-revisions",async(BomRevisionInput input,ProductionDb db,CancellationToken ct)=>{
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
        api.MapPost("/bom-revisions/{id:guid}/lines",async(Guid id,BomLineInput input,ProductionDb db,CancellationToken ct)=>{
            var rev=await db.ManufacturingBomRevisions.SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(rev is null)return Results.NotFound();
            if(rev.State!=ManufacturingRevisionState.Draft)return Results.Conflict(new{message="نسخه قفل است."});
            if(input.ComponentItemId==rev.ParentItemId||input.Sequence<=0||input.QuantityPer<=0
                ||input.ScrapPercent is <0 or >=100)
                return Results.BadRequest(new{message="مقادیر جزء معتبر نیست."});
            if(!await db.Items.AnyAsync(x=>x.Id==input.ComponentItemId,ct))
                return Results.BadRequest(new{message="جزء انتخاب‌شده وجود ندارد."});
            if(await db.ManufacturingBomLines.AnyAsync(x=>x.RevisionId==id
                && (x.ComponentItemId==input.ComponentItemId || x.Sequence==input.Sequence),ct))
                return Results.Conflict(new{message="جزء یا شماره ترتیب تکراری است."});
            var part=new ManufacturingBomLine{RevisionId=id,ComponentItemId=input.ComponentItemId,
                Sequence=input.Sequence,QuantityPer=input.QuantityPer,ScrapPercent=input.ScrapPercent};
            db.ManufacturingBomLines.Add(part);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/bom-revisions/{id}",new{part.Id});
        });
        api.MapPost("/bom-revisions/{id:guid}/approve",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var rev=await db.ManufacturingBomRevisions.Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(rev is null)return Results.NotFound();
            if(rev.State!=ManufacturingRevisionState.Draft)return Results.Conflict(new{message="نسخه پیش‌نویس نیست."});
            if(rev.Lines.Count==0)return Results.BadRequest(new{message="BOM بدون جزء قابل تأیید نیست."});
            var other=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            if(other.Any(x=>x.ParentItemId==rev.ParentItemId && x.ValidFrom<=(rev.ValidUntil??DateOnly.MaxValue)
                && rev.ValidFrom<=(x.ValidUntil??DateOnly.MaxValue)))
                return Results.Conflict(new{message="نسخه‌های تأییدشده دارای همپوشانی تاریخی هستند."});
            // Conservative cycle rejection across all approved revisions, even disjoint dates.
            var edges=other.Append(rev).SelectMany(x=>x.Lines.Select(l=>(Parent:x.ParentItemId,Child:l.ComponentItemId))).ToList();
            var visiting=new HashSet<Guid>();var done=new HashSet<Guid>();
            bool Cycle(Guid x){
                if(visiting.Contains(x))return true;
                if(!done.Add(x))return false;
                visiting.Add(x);
                foreach(var e in edges.Where(e=>e.Parent==x))if(Cycle(e.Child))return true;
                visiting.Remove(x);return false;
            }
            if(edges.Select(x=>x.Parent).Distinct().Any(Cycle))
                return Results.Conflict(new{message="ساختار مصوب موجب چرخه در BOM می‌شود."});
            rev.State=ManufacturingRevisionState.Approved;await db.SaveChangesAsync(ct);
            return Results.Ok(new{rev.Id,rev.State});
        });

        api.MapGet("/mps",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines).OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.Id,x.Code,x.State,x.CreatedUtc,
                lines=x.Lines.OrderBy(y=>y.NeededOn).Select(y=>new{y.Id,y.ItemId,y.Quantity,
                    neededOnJalali=PersianPlanningDates.Format(y.NeededOn),y.Reference})}));
        });
        api.MapPost("/mps",async(MpsVersionInput input,ProductionDb db,CancellationToken ct)=>{
            if(string.IsNullOrWhiteSpace(input.Code)||input.Code.Trim().Length>64)
                return Results.BadRequest(new{message="کد نسخه برنامه معتبر نیست."});
            var code=input.Code.Trim();
            if(await db.MpsVersions.AnyAsync(x=>x.Code==code,ct))
                return Results.Conflict(new{message="کد برنامه تکراری است."});
            var version=new MpsVersion{Code=code};db.MpsVersions.Add(version);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/mps/{version.Id}",new{version.Id,version.Code});
        });
        api.MapPost("/mps/{id:guid}/lines",async(Guid id,MpsLineInput input,ProductionDb db,CancellationToken ct)=>{
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
            return Results.Created($"/api/v1/mps/{id}",new{line.Id});
        });
        api.MapPost("/mps/{id:guid}/approve",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Draft)return Results.Conflict(new{message="برنامه قبلاً تأیید شده است."});
            if(version.Lines.Count==0)return Results.BadRequest(new{message="برنامه بدون ردیف قابل تأیید نیست."});
            version.State=PlanningApproval.Approved;await db.SaveChangesAsync(ct);
            return Results.Ok(new{version.Id,version.State});
        });

        api.MapGet("/planning-balances",async(ProductionDb db,CancellationToken ct)=>{
            var list=await db.PlanningBalances.AsNoTracking().OrderBy(x=>x.ItemId).ToListAsync(ct);
            return Results.Ok(list.Select(x=>new{x.ItemId,x.PhysicalQuantity,x.ReservedQuantity,x.QuarantinedQuantity,
                x.SafetyQuantity,x.LeadDays,x.MinimumBatch,x.BatchMultiple,
                snapshotDateJalali=PersianPlanningDates.Format(x.SnapshotDate)}));
        });
        api.MapPut("/planning-balances/{itemId:guid}",async(Guid itemId,PlanningBalanceInput input,ProductionDb db,CancellationToken ct)=>{
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
        api.MapPost("/planning-receipts",async(PlanningReceiptInput input,ProductionDb db,CancellationToken ct)=>{
            if(input.Quantity<=0||!PersianPlanningDates.TryParse(input.AvailableOnJalali,out var date)
                ||(input.Reference?.Length??0)>120)
                return Results.BadRequest(new{message="تاریخ و مقدار دریافت برنامه‌ای معتبر نیست."});
            if(!await db.Items.AnyAsync(x=>x.Id==input.ItemId,ct))return Results.BadRequest(new{message="کالا وجود ندارد."});
            var receipt=new PlanningExpectedReceipt{ItemId=input.ItemId,AvailableOn=date,
                Quantity=input.Quantity,Reference=input.Reference?.Trim()??""};
            db.PlanningExpectedReceipts.Add(receipt);await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/planning-receipts/{receipt.Id}",new{receipt.Id});
        });
        api.MapPost("/mrp/{id:guid}/calculate",async(Guid id,ProductionDb db,CancellationToken ct)=>{
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
        api.MapDelete("/planning-receipts/{id:guid}",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var receipt=await db.PlanningExpectedReceipts.FindAsync([id],ct);
            if(receipt is null)return Results.NotFound();
            db.PlanningExpectedReceipts.Remove(receipt);await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        api.MapDelete("/mps/{versionId:guid}/lines/{lineId:guid}",async(Guid versionId,Guid lineId,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.FindAsync([versionId],ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Draft)return Results.Conflict(new{message="فقط ردیف‌های برنامه پیش‌نویس قابل حذف هستند."});
            var line=await db.MpsDemandLines.SingleOrDefaultAsync(x=>x.Id==lineId&&x.MpsVersionId==versionId,ct);
            if(line is null)return Results.NotFound();
            db.MpsDemandLines.Remove(line);await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        api.MapDelete("/bom-revisions/{revisionId:guid}/lines/{lineId:guid}",async(Guid revisionId,Guid lineId,ProductionDb db,CancellationToken ct)=>{
            var revision=await db.ManufacturingBomRevisions.FindAsync([revisionId],ct);
            if(revision is null)return Results.NotFound();
            if(revision.State!=ManufacturingRevisionState.Draft)return Results.Conflict(new{message="فقط اجزای BOM پیش‌نویس قابل حذف هستند."});
            var line=await db.ManufacturingBomLines.SingleOrDefaultAsync(x=>x.Id==lineId&&x.RevisionId==revisionId,ct);
            if(line is null)return Results.NotFound();
            db.ManufacturingBomLines.Remove(line);await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/mrp/runs/{runId:guid}",async(Guid runId,ProductionDb db,CancellationToken ct)=>{
            var run=await db.PlanningCalculations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId,ct);
            return run is null?Results.NotFound():Results.Ok(new{run.Id,run.MpsVersionId,run.CalculatedUtc,
                run.AlgorithmVersion,results=JsonSerializer.Deserialize<JsonElement>(run.ResultSnapshotJson)});
        });
        api.MapGet("/crp/{id:guid}/preview",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            try{
                var rows=PlanningCalculators.RunRoughCutCrp(version.Lines,routes,shifts);
                return Results.Ok(new{mode="RoughCutSingleDay",rows=rows.Select(x=>new{
                    x.WorkCenterId,neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                    x.RequiredMinutes,x.CapacityMinutes,x.OverloadMinutes,x.UtilizationPercent})});
            }
            catch(InvalidOperationException ex){return Results.UnprocessableEntity(new{message=ex.Message});}
        });
        return routes;
    }
}
