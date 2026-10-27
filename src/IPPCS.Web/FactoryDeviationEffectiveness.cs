using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Stage 23: second-person verification of CAPA effectiveness within IPPCS.
// Uses the existing append-only ProductionDeviationEvents table and the
// current action/closure lifecycle; no new HR/workflow/ERP integration.
public sealed record FactoryDeviationEffectivenessInput(
    bool Effective, string EvidenceReference, string Observation);

public sealed record FactoryDeviationEffectivenessProof(
    bool Effective, string EvidenceReference, string Observation,
    string ReviewedBy, DateTime ReviewedUtc);

public sealed record FactoryDeviationEffectivenessState(
    Guid DeviationId, string State, bool IndependentlyVerified,
    bool AwaitingReview, bool ReopenedForCorrection,
    string? EvidenceReference, string? Observation, string? ReviewedBy,
    DateTime? ReviewedUtc, string Disclaimer);

public static class FactoryDeviationEffectiveness
{
    public const string EventPrefix = "EFFECTIVENESS_V1:";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Encode(FactoryDeviationEffectivenessProof proof)
        => EventPrefix + JsonSerializer.Serialize(proof, JsonOptions);

    public static FactoryDeviationEffectivenessProof? Decode(string? details)
    {
        if (string.IsNullOrWhiteSpace(details) ||
            !details.StartsWith(EventPrefix, StringComparison.Ordinal)) return null;
        try
        {
            var proof = JsonSerializer.Deserialize<FactoryDeviationEffectivenessProof>(
                details[EventPrefix.Length..], JsonOptions);
            if (proof is null || string.IsNullOrWhiteSpace(proof.ReviewedBy) ||
                string.IsNullOrWhiteSpace(proof.EvidenceReference) ||
                proof.ReviewedUtc == default) return null;
            return proof;
        }
        catch (JsonException) { return null; }
    }

    public static FactoryDeviationEffectivenessState Evaluate(ProductionDeviation row)
    {
        var evidence = row.Events.Where(x =>
            x.Details.StartsWith(EventPrefix, StringComparison.Ordinal))
            .OrderBy(x=>x.AtUtc).ThenBy(x=>x.Id).ToArray();
        var latest = evidence.LastOrDefault();
        var proof = latest is null ? null : Decode(latest.Details);
        var broken = evidence.Any(x=>Decode(x.Details) is null);
        // Never certify an incomplete or contradictory history.
        var verified = !broken && row.Status==ProductionDeviationStatus.Closed &&
            proof is { Effective:true } &&
            evidence.Count(x=>Decode(x.Details)?.Effective==true)==1;
        // A new closure after a rejection still requires fresh evidence.
        var reopened = row.Status==ProductionDeviationStatus.ActionAssigned &&
            latest is not null && proof is { Effective:false };
        var state = broken?"InvalidEvidence":
            verified?"Verified":
            row.Status==ProductionDeviationStatus.Closed?"AwaitingIndependentReview":
            reopened?"ReopenedForCorrection":"NotClosed";
        return new(row.Id,state,verified,
            row.Status==ProductionDeviationStatus.Closed&&!verified,
            reopened,proof?.EvidenceReference,proof?.Observation,
            proof?.ReviewedBy,proof?.ReviewedUtc,
            "تأیید اثربخشی توسط کاربر دیگری با نقش کیفیت انجام می‌شود؛ این شواهد داخلی IPPCS جایگزین ممیزی میدانی، آزمون کیفیت، MSA یا تأیید کارخانه نیست.");
    }

    public static bool CanReview(ProductionDeviation row,string actor,
        out string reason)
    {
        reason="";
        if(row.Status!=ProductionDeviationStatus.Closed)
        {
            reason="ابتدا باید اقدام اصلاحی انجام و پرونده به وضعیت مختومه برسد.";
            return false;
        }
        if(string.IsNullOrWhiteSpace(actor) ||
            actor.Equals(row.CreatedBy,StringComparison.OrdinalIgnoreCase) ||
            actor.Equals(row.Responsible,StringComparison.OrdinalIgnoreCase) ||
            row.Events.Any(x=>x.Username.Equals(actor,StringComparison.OrdinalIgnoreCase) &&
                (x.Status is ProductionDeviationStatus.ActionAssigned or
                    ProductionDeviationStatus.Closed)))
        {
            reason="بازبین اثربخشی باید از ایجادکننده، اقدام‌کننده و مختومه‌کننده پرونده مستقل باشد.";
            return false;
        }
        var snapshot=Evaluate(row);
        if(snapshot.State=="InvalidEvidence")
        {
            reason="سابقه تأیید اثربخشی مخدوش است و نیازمند بررسی مدیر سامانه است.";
            return false;
        }
        if(snapshot.IndependentlyVerified ||
           row.Events.Any(x=>x.Details.StartsWith(EventPrefix,StringComparison.Ordinal) &&
             Decode(x.Details) is { Effective:true }))
        {
            reason="تأیید اثربخشی این پرونده قبلاً ثبت شده است.";
            return false;
        }
        // After an ineffective trial, a second closure is not enough by
        // itself: a governed corrective-action revision must be recorded
        // between the failed review and the latest closure.
        var ordered=row.Events.OrderBy(x=>x.AtUtc).ThenBy(x=>x.Id).ToArray();
        var lastRejection=ordered.LastOrDefault(x=>
            Decode(x.Details) is { Effective:false });
        if(lastRejection is not null)
        {
            var latestClosure=ordered.LastOrDefault(x=>
                x.Status==ProductionDeviationStatus.Closed &&
                !x.Details.StartsWith(EventPrefix,StringComparison.Ordinal));
            if(latestClosure is null || latestClosure.AtUtc<=lastRejection.AtUtc ||
               !ordered.Any(x=>
                   x.Details.StartsWith("REVISION:Updated:",StringComparison.Ordinal) &&
                   x.AtUtc>lastRejection.AtUtc&&x.AtUtc<latestClosure.AtUtc))
            {
                reason="پس از رد اثربخشی باید بازنگری کنترل‌شده اقدام و اختتام مجدد با مدرک ثبت شود.";
                return false;
            }
        }
        if(!row.Events.Any(x=>x.Status==ProductionDeviationStatus.Closed &&
            !x.Details.StartsWith(EventPrefix,StringComparison.Ordinal)))
        {
            reason="رویداد مستند اختتام پرونده موجود نیست.";
            return false;
        }
        return true;
    }

    public static IEndpointRouteBuilder MapDeviationEffectiveness(
        this IEndpointRouteBuilder routes)
    {
        var read=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        read.MapGet("/production-deviations/{id:guid}/effectiveness",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var row=await db.ProductionDeviations.AsNoTracking().Include(x=>x.Events)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                return row is null?Results.NotFound():Results.Ok(Evaluate(row));
            });
        read.MapGet("/production-deviations/effectiveness-pending",
            async(ProductionDb db,CancellationToken ct)=>{
                var closed=await db.ProductionDeviations.AsNoTracking()
                    .Include(x=>x.Events).Where(x=>x.Status==ProductionDeviationStatus.Closed)
                    .OrderByDescending(x=>x.ClosedUtc).Take(501).ToListAsync(ct);
                if(closed.Count>500)
                    return Results.Conflict(new{message="پرونده‌های مختومه بیش از سقف ۵۰۰ است؛ فهرست ناقص ارائه نمی‌شود."});
                var pending=closed.Select(x=>new{deviation=x,proof=Evaluate(x)})
                    .Where(x=>!x.proof.IndependentlyVerified)
                    .Select(x=>new{
                        x.deviation.Id,x.deviation.MpsLineId,x.deviation.Description,
                        x.deviation.Responsible,x.deviation.ClosedUtc,
                        x.proof.State
                    }).ToArray();
                return Results.Ok(new{count=pending.Length,rows=pending,
                    disclaimer="این فهرست پرونده‌های بسته‌شده بدون تأیید مستقل اثربخشی است؛ بسته‌شدن به معنی پذیرش اثربخشی نیست."});
            });
        read.MapPost("/production-deviations/{id:guid}/effectiveness/review",
            async(Guid id,FactoryDeviationEffectivenessInput input,
                ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
                if(string.IsNullOrWhiteSpace(input.EvidenceReference) ||
                    input.EvidenceReference.Trim().Length is <4 or >100 ||
                    string.IsNullOrWhiteSpace(input.Observation) ||
                    input.Observation.Trim().Length is <10 or >400)
                    return Results.BadRequest(new{
                        message="شناسه مدرک (۴ تا ۱۰۰ حرف) و شرح شواهد واقعی (۱۰ تا ۴۰۰ حرف) اجباری است."});
                var actor=ctx.User.Identity?.Name;
                if(string.IsNullOrWhiteSpace(actor) || actor.Length>256)
                    return Results.Forbid();
                var proof=new FactoryDeviationEffectivenessProof(input.Effective,
                    input.EvidenceReference.Trim(),input.Observation.Trim(),
                    actor,DateTime.UtcNow);
                var encoded=Encode(proof);
                if(encoded.Length>1000)
                    return Results.BadRequest(new{message="طول مدرک اثربخشی بیش از ظرفیت ایمن تاریخچه است."});
                await using var tx=await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,ct);
                try{await FactoryDeviationGovernance.LockDeviation(db,id,ct);}
                catch(SqlException){
                    return Results.Conflict(new{message="قفل پرونده برای بررسی اثربخشی در دسترس نیست."});}
                var row=await db.ProductionDeviations.Include(x=>x.Events)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(row is null)return Results.NotFound();
                if(!CanReview(row,actor,out var reason))
                    return Results.Conflict(new{message=reason});
                if(!input.Effective)
                {
                    // Reopen the same CAPA for correction, keeping its closure
                    // event and rejection evidence. Only a new closure can
                    // subsequently be approved by an independent reviewer.
                    row.Status=ProductionDeviationStatus.ActionAssigned;
                    row.ClosedUtc=null;
                }
                db.ProductionDeviationEvents.Add(new ProductionDeviationEvent{
                    DeviationId=id,
                    Status=row.Status,Username=actor,Details=encoded
                });
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{
                    row.Id,row.Status,
                    independentlyVerified=input.Effective,
                    reopenedForCorrection=!input.Effective,
                    proof.EvidenceReference,proof.ReviewedBy,proof.ReviewedUtc,
                    message=input.Effective?
                        "اثربخشی با مدرک و تأیید مستقل ثبت شد.":
                        "اثربخشی رد و پرونده با حفظ شواهد برای اصلاح مجدد بازگشایی شد."
                });
            }).RequireAuthorization("IpQualityApprove");
        return routes;
    }
}
