using Microsoft.EntityFrameworkCore;

// Stage 40: diagnostic SPC evidence only. Does NOT grant quality release,
// prescribe factory sampling frequencies, or claim a calibrated Gage R&R.
public sealed record FactorySpcSignal(string Rule,int StartIndex,int EndIndex,
    string Description);
public sealed record FactorySpcEvidenceReview(
    Guid OrderId,Guid RequirementId,int SampleCount,int DistinctSampleDays,
    bool HasSpecificationLimits,int SpecViolations,int InvalidHistoricalPassFlags,
    int DuplicateTimestamps,decimal? MovingRangeMean,
    bool ProvisionalLimitAvailable,bool ReferenceHistoryLongEnough,
    bool MeasurementSystemVerified,bool CapabilityApproved,string Assessment,
    IReadOnlyList<FactorySpcSignal> Signals,IReadOnlyList<string> Findings,
    string Disclaimer);

public static class FactorySpcEvidence
{
    private static readonly TimeZoneInfo Tehran =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    private const int MinimumReferenceSamples=25;

    public static FactorySpcEvidenceReview Review(Guid orderId,
        ManufacturingInspectionRequirement req,
        IReadOnlyList<ManufacturingInspectionResult> entries)
    {
        var rows=entries.OrderBy(x=>x.RecordedUtc).ThenBy(x=>x.Id).ToArray();
        var findings=new List<string>();
        var signals=new List<FactorySpcSignal>();
        var bounded=req.Minimum.HasValue||req.Maximum.HasValue;
        var specFail=rows.Count(x=>bounded&&
            ((req.Minimum.HasValue&&x.Value<req.Minimum.Value)||
             (req.Maximum.HasValue&&x.Value>req.Maximum.Value)));
        var mismatch=rows.Count(x=>bounded&&
            x.Passed!=ManufacturingInspectionRules.Passed(req,x.Value));
        var repeated=rows.GroupBy(x=>x.RecordedUtc).Sum(x=>Math.Max(0,x.Count()-1));
        var days=rows.Select(x=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(x.RecordedUtc,DateTimeKind.Utc),Tehran)))
            .Distinct().Count();

        decimal? movingRange=rows.Length>1?
            rows.Skip(1).Select((x,i)=>Math.Abs(x.Value-rows[i].Value)).Average():null;
        if(rows.Length==0)findings.Add("برای این مشخصه هنوز اندازه‌گیری ثبت نشده است.");
        if(!bounded)findings.Add("حد مشخصات فنی برای این مشخصه ثبت نشده؛ انطباق محصول قابل استنباط نیست.");
        if(mismatch>0)findings.Add("برخی نتیجه‌های تاریخی قبول/مردود با حدود مشخصات منجمد دستور سازگار نیستند؛ ممیزی لازم است.");
        if(repeated>0)findings.Add("چند نمونه زمان ثبت یکسان دارند؛ ترتیب واقعی اندازه‌گیری‌ها را بررسی کنید.");
        if(rows.Length<MinimumReferenceSamples)
            findings.Add("کمتر از ۲۵ داده پیوسته موجود است؛ تاریخچه برای بازبینی اولیه حدود کنترل کوتاه است.");
        if(movingRange is 0m && rows.Length>1)
            findings.Add("تمام دامنه‌های متحرک صفرند؛ تفکیک‌پذیری ابزار اندازه‌گیری و روش ثبت را بررسی کنید.");

        // Exploratory run rules only when an initial reference has enough
        // chronologically sorted observations and nonzero dispersion. Limits
        // are estimated from the SAME data, never presented as approved limits.
        var referenceLongEnough=rows.Length>=MinimumReferenceSamples;
        var provisional=referenceLongEnough&&movingRange>0&&mismatch==0&&repeated==0;
        if(provisional)
        {
            var mean=rows.Average(x=>x.Value);
            var lower=mean-2.66m*movingRange!.Value;
            var upper=mean+2.66m*movingRange.Value;
            for(var i=0;i<rows.Length;i++)
            {
                if(rows[i].Value<lower||rows[i].Value>upper)
                    signals.Add(new("I3Sigma",i+1,i+1,
                        "نقطه بیرون از حدود کنترل تخمینی نمودار افراد؛ بررسی علت ویژه ضروری است."));
            }
            // Eight consecutive points strictly above/below center line.
            // Equality breaks a run; report each maximal segment once.
            var direction=0;var start=0;
            for(var i=0;i<=rows.Length;i++)
            {
                var side=i==rows.Length?0:Math.Sign(rows[i].Value-mean);
                if(side!=0&&side==direction)continue;
                if(direction!=0&&i-start>=8)
                    signals.Add(new("EightOnOneSide",start+1,i,
                        "حداقل هشت اندازه‌گیری متوالی در یک سوی میانگین؛ احتمال جابه‌جایی فرایند."));
                direction=side;start=i;
            }
            // Six strictly increasing/decreasing successive observations.
            var slope=0;start=0;
            for(var i=1;i<=rows.Length;i++)
            {
                var current=i==rows.Length?0:Math.Sign(rows[i].Value-rows[i-1].Value);
                if(current!=0&&current==slope)continue;
                if(slope!=0&&i-start>=6)
                    signals.Add(new("SixPointTrend",start+1,i,
                        "حداقل شش نقطه با روند یکنواخت؛ علت ویژه باید بررسی شود."));
                slope=current;start=i-1;
            }
        }
        if(specFail>0)findings.Add("اندازه‌گیری خارج از حدود مشخصات فنی ثبت شده است.");
        if(signals.Count>0)findings.Add("سیگنال‌های نمودار تخمینی مشاهده شدند؛ ثبات فرایند احراز نشده است.");
        findings.Add("مدرک معتبر Gage R&R، کالیبراسیون و روش نمونه‌برداری مصوب به این گزارش متصل نیست.");

        var assessment=rows.Length==0?"NoObservations":
            mismatch>0?"HistoricalInconsistency":
            !bounded?"SpecificationMissing":
            repeated>0?"TimestampCollision":
            !referenceLongEnough?"InsufficientReferenceHistory":
            movingRange==0?"NoObservedVariation":
            signals.Count>0?"ExploratorySignals":
            "PreliminaryReviewOnly";
        return new(orderId,req.Id,rows.Length,days,bounded,specFail,mismatch,
            repeated,movingRange,provisional,referenceLongEnough,false,false,
            assessment,signals,findings,
            "SPC صرفاً غربالگری اکتشافی است: حدود I-MR از همان سری داده تخمین زده شده، نه حدود مصوب کنترل. گذر از تعداد نمونه یا نبود سیگنال نشان‌دهنده قابلیت فرایند، MSA معتبر، کنترل پلن نمونه‌برداری مصوب یا اجازه ترخیص/آزادسازی محصول نیست.");
    }

    public static IEndpointRouteBuilder MapFactorySpcEvidence(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/manufacturing-orders/{id:guid}/spc/{requirementId:guid}/evidence",
            async(Guid id,Guid requirementId,ProductionDb db,CancellationToken ct)=>{
                var req=await db.ManufacturingInspectionRequirements.AsNoTracking()
                    .SingleOrDefaultAsync(x=>x.Id==requirementId,ct);
                if(req is null)return Results.NotFound();
                if(!await db.ManufacturingOrderOperations.AsNoTracking()
                    .AnyAsync(x=>x.Id==req.OrderOperationId&&x.OrderId==id,ct))
                    return Results.NotFound();
                var rows=await db.ManufacturingInspectionResults.AsNoTracking()
                    .Where(x=>x.OrderId==id&&x.RequirementId==requirementId)
                    .OrderBy(x=>x.RecordedUtc).ThenBy(x=>x.Id)
                    .Take(501).ToListAsync(ct);
                if(rows.Count>500)return Results.Conflict(new{
                    message="برای جلوگیری از نتیجه ناقص، گزارش SPC با بیش از ۵۰۰ اندازه‌گیری نیازمند فیلتر زمانی سروری است."});
                return Results.Ok(Review(id,req,rows));
            }).RequireAuthorization("IpProductionUser");
        return routes;
    }
}
