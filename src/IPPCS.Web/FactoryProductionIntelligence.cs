using System.Globalization;
using Microsoft.EntityFrameworkCore;

// Stage 10: broaden three manufacturing-only modules without adding ERP
// integrations, synthetic operational events, or new database tables.
public record FactoryBacktestMonth(int JalaliYear,int JalaliMonth,
    decimal Predicted,decimal Actual,decimal Error,decimal AbsoluteError,
    decimal? AbsolutePercentageError,int PriorConfirmedMonths);
public record FactoryBacktestReport(IReadOnlyList<FactoryBacktestMonth> Months,
    decimal MeanAbsoluteError,decimal? WapePercent,decimal Bias,
    int MonthsWithHistory,int MonthsEvaluated,string Warning);

public record FactoryClockCostRow(Guid OperationId,int Sequence,Guid WorkCenterId,
    decimal StandardMinutes,decimal? ActualMinutes,decimal? MinutesVariance,
    bool HasApplicableRate,decimal? StandardConversionRials,
    decimal? ActualConversionRials,decimal? ConversionVarianceRials,
    bool HasOpenClock,int ClosedSessions);
public record FactoryClockCostTotals(int CountMissingRates,int CountMissingActualTime,
    decimal? StandardConversionRials,decimal? ActualConversionRials,
    decimal? ConversionVarianceRials);
public record FactoryClockOee(decimal? AvailabilityPercent,decimal? PerformancePercent,
    decimal? QualityPercent,decimal? OeePercent,decimal ActualMinutes,
    bool HasOpenClock,string? Warning);

public record FactoryMpsLineAdherence(Guid LineId,Guid ItemId,DateOnly NeededOn,
    decimal Required,decimal Assigned,decimal CompletedGood,decimal OnTimeGood,
    decimal LateGood,decimal UndatedGood,decimal PendingAssigned,decimal Unassigned,
    decimal Shortage,decimal? FulfilmentPercent,int LateOrderCount);
public static class FactoryProductionIntelligence
{
    private static readonly PersianCalendar Calendar=new();
    private static readonly TimeZoneInfo Tehran=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static DateOnly JalaliMonth(int ordinal)
    {
        var year=ordinal/12;
        var month=ordinal%12+1;
        return DateOnly.FromDateTime(Calendar.ToDateTime(year,month,1,0,0,0,0));
    }
    public static int MonthOrdinal(DateOnly date)
    {
        var d=date.ToDateTime(TimeOnly.MinValue);
        return Calendar.GetYear(d)*12+Calendar.GetMonth(d)-1;
    }

    public static FactoryBacktestReport Backtest(
        IReadOnlyList<FactoryDemand> history,DateOnly through,int lookback,int months)
    {
        if(lookback is <2 or >12||months is <2 or >12)
            throw new ArgumentOutOfRangeException(nameof(months));
        var lastOrdinal=MonthOrdinal(through);
        var confirmed=history.Where(x=>x.Category=="Confirmed").ToArray();
        var rows=new List<FactoryBacktestMonth>();
        for(var month=lastOrdinal-months+1;month<=lastOrdinal;month++)
        {
            var target=JalaliMonth(month);
            var prediction=FactoryForecasting.MonthlyMovingAverage(confirmed,target,lookback);
            var actual=confirmed.Where(x=>MonthOrdinal(x.DueOn)==month).Sum(x=>x.Quantity);
            var priorMonths=confirmed.Where(x=>MonthOrdinal(x.DueOn)<month &&
                MonthOrdinal(x.DueOn)>=month-lookback)
                .Select(x=>MonthOrdinal(x.DueOn)).Distinct().Count();
            rows.Add(new(month/12,month%12+1,prediction,actual,
                prediction-actual,Math.Abs(prediction-actual),
                actual>0?Math.Round(100m*Math.Abs(prediction-actual)/actual,2):null,
                priorMonths));
        }
        var abs=rows.Sum(x=>x.AbsoluteError);
        var actualTotal=rows.Sum(x=>x.Actual);
        var eligible=rows.Count(x=>x.PriorConfirmedMonths>=2);
        var warning=eligible<rows.Count?
            "بعضی ماه‌های ارزیابی کمتر از دو ماه سابقه نیاز قطعی دارند؛ نتایج باید با احتیاط تفسیر شوند.": "";
        if(actualTotal==0)
            warning+=" مجموع نیاز واقعی صفر است؛ WAPE محاسبه نشده است.";
        return new(rows,Math.Round(abs/rows.Count,4),
            actualTotal>0?Math.Round(100m*abs/actualTotal,2):null,
            Math.Round(rows.Sum(x=>x.Error)/rows.Count,4),eligible,rows.Count,warning.Trim());
    }

    public static FactoryClockCostRow OperationCost(ManufacturingOrder order,
        ManufacturingOrderOperation operation,FactoryCenterCostRate? rate,
        IReadOnlyList<FactoryClockSession> sessions)
    {
        var std=operation.SetupMinutes+
            operation.RunMinutesPerUnit*order.PlannedQuantity;
        var opSessions=sessions.Where(x=>x.OrderId==order.Id &&
            x.OperationId==operation.Id).ToArray();
        var hasOpen=opSessions.Any(x=>x.StoppedUtc is null);
        decimal? actual=opSessions.Length>0&&!hasOpen
            ?opSessions.Sum(x=>(decimal)(x.StoppedUtc!.Value-x.StartedUtc).TotalMinutes)
            :null;
        var rateApplicable=rate is not null&&rate.EffectiveFrom<=order.PlannedStart;
        var standardRials=rateApplicable?std*rate!.ConversionRialsPerMinute:(decimal?)null;
        var actualRials=rateApplicable&&actual.HasValue
            ?actual.Value*rate!.ConversionRialsPerMinute:(decimal?)null;
        return new(operation.Id,operation.Sequence,operation.WorkCenterId,
            std,actual,actual.HasValue?actual.Value-std:null,rateApplicable,
            standardRials,actualRials,
            standardRials.HasValue&&actualRials.HasValue?
                actualRials.Value-standardRials.Value:null,
            hasOpen,opSessions.Count(x=>x.StoppedUtc.HasValue));
    }

    public static FactoryClockCostTotals TotalCosts(
        IReadOnlyList<FactoryClockCostRow> rows)
    {
        var ratesComplete=rows.All(x=>x.StandardConversionRials.HasValue);
        var actualComplete=rows.All(x=>x.ActualConversionRials.HasValue);
        return new(rows.Count(x=>!x.HasApplicableRate),
            rows.Count(x=>!x.ActualMinutes.HasValue),
            ratesComplete?rows.Sum(x=>x.StandardConversionRials!.Value):null,
            actualComplete?rows.Sum(x=>x.ActualConversionRials!.Value):null,
            ratesComplete&&actualComplete?rows.Sum(x=>x.ConversionVarianceRials!.Value):null);
    }

    public static FactoryClockOee EvaluateClockOee(
        Guid center,DateOnly day,decimal? planned,
        IReadOnlyList<FactoryClockSession> sessions,
        decimal idealMinutes,decimal good,decimal scrap,decimal rework)
    {
        var dayStart=day.ToDateTime(TimeOnly.MinValue);
        var dayEnd=day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        decimal measured=0;
        bool open=false;
        foreach(var s in sessions.Where(x=>x.WorkCenterId==center))
        {
            var start=TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(s.StartedUtc,DateTimeKind.Utc),Tehran);
            var finish=s.StoppedUtc.HasValue
                ?TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(s.StoppedUtc.Value,DateTimeKind.Utc),Tehran)
                :dayEnd;
            if(start>=dayEnd||finish<=dayStart)continue;
            if(s.StoppedUtc is null)open=true;
            if(s.StoppedUtc is not null)
                measured+=(decimal)((finish<dayEnd?finish:dayEnd)-(start>dayStart?start:dayStart)).TotalMinutes;
        }
        if(planned is null||planned<=0)
            return new(null,null,null,null,measured,open,"زمان برنامه مرکز برای این روز ثبت نشده است.");
        if(open)
            return new(null,null,null,null,measured,true,
                "ساعت عملیاتی باز وجود دارد؛ زمان نهایی کارکرد و OEE قابل تأیید نیست.");
        if(measured<=0)
            return new(null,null,null,null,measured,false,
                "زمان واقعی خاتمه‌یافته ثبت نشده است؛ OEE واقعی گزارش نمی‌شود.");
        if(measured>planned.Value)
            return new(null,null,null,null,measured,false,
                "زمان واقعی ثبت‌شده از زمان برنامه بیشتر است؛ شیفت و ثبت ساعت بررسی شود.");
        // Missing standard cycle and negative production observations must
        // never become a plausible numerical OEE (especially negative quality).
        if(good<0||scrap<0||rework<0||idealMinutes<0)
            return new(null,null,null,null,measured,false,
                "مقادیر تولید سالم، ضایعات، دوباره‌کاری یا زمان سیکل استاندارد نامعتبرند.");
        var total=good+scrap+rework;
        if(total<=0||idealMinutes==0)
            return new(null,null,null,null,measured,false,
                "برای OEE خروجی تولید و زمان سیکل استاندارد مثبت و واقعی لازم است.");
        var availability=100m*measured/planned.Value;
        var performance=100m*idealMinutes/measured;
        var quality=100m*good/total;
        var oee=availability*performance*quality/10000m;
        var warning=performance>100m?
            "شاخص عملکرد بالای ۱۰۰٪ است؛ زمان سیکل و ثبت قطعات بررسی شود.":null;
        return new(Math.Round(availability,2),Math.Round(performance,2),
            Math.Round(quality,2),Math.Round(oee,2),Math.Round(measured,2),false,warning);
    }

    public static IReadOnlyList<FactoryMpsLineAdherence> PeggedAdherence(
        IEnumerable<MpsDemandLine> lines,IEnumerable<ManufacturingOrderPeg> pegs,
        IReadOnlyDictionary<Guid,ManufacturingOrder> orders)
    {
        return lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.Id).Select(line=>{
            var linked=pegs.Where(p=>p.MpsLineId==line.Id &&
                orders.TryGetValue(p.OrderId,out var order) &&
                order.Status!=ManufacturingOrderStatus.Cancelled).ToArray();
            var assigned=linked.Sum(x=>x.LinkedQuantity);
            decimal completed=0,onTime=0,late=0,undated=0;
            var lateOrderIds=new HashSet<Guid>();
            foreach(var peg in linked)
            {
                var order=orders[peg.OrderId];
                if(order.Status!=ManufacturingOrderStatus.Completed)continue;
                var good=order.Operations.OrderBy(o=>o.Sequence)
                    .LastOrDefault()?.GoodQuantity??0m;
                var attributed=order.PlannedQuantity>0
                    ?Math.Min(peg.LinkedQuantity,Math.Max(0m,
                        good*peg.LinkedQuantity/order.PlannedQuantity)):0m;
                completed+=attributed;
                if(!order.CompletedUtc.HasValue)
                    undated+=attributed;
                else
                {
                    var finished=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(order.CompletedUtc.Value,DateTimeKind.Utc),Tehran));
                    if(finished<=line.NeededOn)onTime+=attributed;
                    else{late+=attributed;lateOrderIds.Add(order.Id);}
                }
            }
            var unassigned=Math.Max(0m,line.Quantity-assigned);
            return new FactoryMpsLineAdherence(line.Id,line.ItemId,line.NeededOn,
                line.Quantity,assigned,completed,onTime,late,undated,
                Math.Max(0m,assigned-completed),unassigned,
                Math.Max(0m,line.Quantity-completed),
                line.Quantity>0?Math.Round(100m*completed/line.Quantity,2):null,
                lateOrderIds.Count);
        }).ToArray();
    }

    public static IEndpointRouteBuilder MapFactoryProductionIntelligence(
        this IEndpointRouteBuilder endpoints)
    {
        var api=endpoints.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");

        api.MapGet("/factory-kpis/peg-adherence/{mpsId:guid}",
            async(Guid mpsId,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="گزارش تحقق تخصیص‌یافته فقط برای MPS مصوب محاسبه می‌شود."});
            var lineIds=version.Lines.Select(x=>x.Id).ToArray();
            var links=await db.ManufacturingOrderPegs.AsNoTracking()
                .Where(x=>lineIds.Contains(x.MpsLineId)).ToListAsync(ct);
            var orderIds=links.Select(x=>x.OrderId).Distinct().ToArray();
            var orders=await db.ManufacturingOrders.AsNoTracking()
                .Include(x=>x.Operations).Where(x=>orderIds.Contains(x.Id))
                .ToDictionaryAsync(x=>x.Id,ct);
            var rows=PeggedAdherence(version.Lines,links,orders);
            return Results.Ok(new{mpsId,version.Code,
                rows=rows.Select(x=>new{
                    x.LineId,x.ItemId,
                    neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                    x.Required,x.Assigned,x.CompletedGood,x.OnTimeGood,x.LateGood,
                    x.UndatedGood,x.PendingAssigned,x.Unassigned,x.Shortage,
                    x.FulfilmentPercent,x.LateOrderCount}),
                totals=new{
                    required=rows.Sum(x=>x.Required),
                    assigned=rows.Sum(x=>x.Assigned),
                    completed=rows.Sum(x=>x.CompletedGood),
                    onTime=rows.Sum(x=>x.OnTimeGood),
                    late=rows.Sum(x=>x.LateGood),
                    shortage=rows.Sum(x=>x.Shortage)},
                disclaimer="تحقق بر اساس تخصیص صریح دستور به ردیف MPS؛ خروجی سالم تکمیل‌شده با نسبت سهم دستور محاسبه می‌شود. اقلام دارای زمان پایان نامشخص به‌موقع فرض نمی‌شوند. ارتباطی با سیستم فروش وجود ندارد."});
        });

        api.MapGet("/factory-demand/backtest/{itemId:guid}",
            async(Guid itemId,string throughJalali,int lookback,int months,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(throughJalali,out var through)
                ||lookback is <2 or >12||months is <2 or >12)
                return Results.BadRequest(new{message="ماه مبنا، پنجره سابقه یا افق ارزیابی معتبر نیست."});
            if(!await db.Items.AsNoTracking().AnyAsync(x=>x.Id==itemId&&
                x.Type==PlanningItemType.Finished,ct))return Results.NotFound();
            var current=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,Tehran));
            if(MonthOrdinal(through)>=MonthOrdinal(current))
                return Results.BadRequest(new{message="ارزیابی فقط روی ماه‌های شمسی خاتمه‌یافته انجام می‌شود."});
            var earliest=JalaliMonth(MonthOrdinal(through)-months+1-lookback);
            var history=await db.FactoryDemands.AsNoTracking()
                .Where(x=>x.ItemId==itemId&&x.Category=="Confirmed"&&
                    x.DueOn>=earliest&&x.DueOn<=through.AddDays(32))
                .ToListAsync(ct);
            var result=Backtest(history,through,lookback,months);
            return Results.Ok(new{itemId,throughJalali,
                result.Months,result.MeanAbsoluteError,result.WapePercent,
                result.Bias,result.MonthsWithHistory,result.MonthsEvaluated,
                result.Warning,disclaimer="بازآزمایی میانگین متحرک تقاضای قطعی ثبت‌شده IPPCS؛ ماه‌های بدون ثبت صفر فرض شده‌اند، نه تأیید عدم تقاضا. هیچ نسخه پیش‌بینی یا MPS تغییر نمی‌کند."});
        });

        api.MapGet("/factory-cost/orders/{orderId:guid}/actual-variance",
            async(Guid orderId,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking()
                .Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            var rates=await db.FactoryCenterCostRates.AsNoTracking().ToListAsync(ct);
            var frozenEvents=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.OrderId==orderId&&x.Action==FactoryCostRateHistory.Action).ToListAsync(ct);
            IReadOnlyList<FactoryCostRateSnapshot> frozen;
            try{frozen=FactoryCostRateHistory.Read(order.Operations,frozenEvents);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var ratesAreFrozen=frozenEvents.Count>0;
            var frozenByOperation=frozen.ToDictionary(x=>x.OperationId);
            var events=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.OrderId==orderId&&
                    (x.Action==FactoryOperationClock.Started||
                     x.Action==FactoryOperationClock.Stopped)).ToListAsync(ct);
            IReadOnlyList<FactoryClockSession> clocks;
            try{clocks=FactoryOperationClock.Read(events);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var rows=order.Operations.OrderBy(x=>x.Sequence).Select(op=>{
                FactoryCenterCostRate? selected;
                if(ratesAreFrozen){
                    var snapshot=frozenByOperation[op.Id];
                    selected=snapshot.ConversionRialsPerMinute is null?null:
                        new FactoryCenterCostRate{
                            WorkCenterId=op.WorkCenterId,
                            ConversionRialsPerMinute=snapshot.ConversionRialsPerMinute.Value,
                            ScrapRialsPerUnit=snapshot.ScrapRialsPerUnit!.Value,
                            EffectiveFrom=snapshot.EffectiveFrom!.Value};
                }else selected=rates.SingleOrDefault(x=>x.WorkCenterId==op.WorkCenterId);
                return OperationCost(order,op,selected,clocks);
            }).ToArray();
            return Results.Ok(new{orderId,rows,totals=TotalCosts(rows),
                hasFrozenCostRates=ratesAreFrozen,
                disclaimer=ratesAreFrozen?
                  "نرخ‌های هزینه تبدیل هنگام آزادسازی دستور منجمد شده‌اند؛ هزینه واقعی فقط بر ساعت عملیات بسته‌شده و همان نرخ داخلی ثابت استوار است. نه حسابداری، سربار یا هزینه انبار.":
                  "این دستور قدیمی سند نرخ منجمد ندارد؛ نرخ جاری فقط برآورد آزمایشی است و نرخ تاریخی قطعی قابل اثبات نیست."});
        });

        api.MapGet("/oee/clock-verified",async(string dateJalali,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(dateJalali,out var date))
                return Results.BadRequest(new{message="تاریخ شمسی معتبر نیست."});
            var startUtc=TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue),Tehran);
            var endUtc=TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue),Tehran);
            var centers=await db.WorkCenters.AsNoTracking().ToListAsync(ct);
            var plans=await db.ManufacturingOeePlans.AsNoTracking()
                .Where(x=>x.ProductionDate==date).ToListAsync(ct);
            var audits=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==FactoryOperationClock.Started||
                    x.Action==FactoryOperationClock.Stopped).ToListAsync(ct);
            IReadOnlyList<FactoryClockSession> sessions;
            try{sessions=FactoryOperationClock.Read(audits);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var confirmations=await db.ManufacturingConfirmations.AsNoTracking()
                .Where(x=>x.OccurredUtc>=startUtc&&x.OccurredUtc<endUtc)
                .Join(db.ManufacturingOrderOperations.AsNoTracking(),
                    x=>x.OperationId,op=>op.Id,(x,op)=>new{
                    op.WorkCenterId,op.RunMinutesPerUnit,x.GoodQuantity,
                    x.ScrapQuantity,x.ReworkQuantity}).ToListAsync(ct);
            return Results.Ok(new{dateJalali,
                timeZone=Tehran.Id,
                rows=centers.Select(center=>{
                    var entries=confirmations.Where(x=>x.WorkCenterId==center.Id).ToArray();
                    var plan=plans.SingleOrDefault(x=>x.WorkCenterId==center.Id);
                    var good=entries.Sum(x=>x.GoodQuantity);
                    var scrap=entries.Sum(x=>x.ScrapQuantity);
                    var rework=entries.Sum(x=>x.ReworkQuantity);
                    var ideal=entries.Sum(x=>(x.GoodQuantity+x.ScrapQuantity+
                        x.ReworkQuantity)*x.RunMinutesPerUnit);
                    var quality=EvaluateClockOee(center.Id,date,plan?.PlannedMinutes,
                        sessions,ideal,good,scrap,rework);
                    return new{centerId=center.Id,centerName=center.Name,
                        plannedMinutes=plan?.PlannedMinutes,quality.ActualMinutes,
                        good,scrap,rework,idealMinutes=ideal,
                        quality.HasOpenClock,quality.AvailabilityPercent,
                        quality.PerformancePercent,quality.QualityPercent,
                        quality.OeePercent,quality.Warning};
                }).ToArray(),disclaimer="OEE مبتنی بر ساعت ثبت‌شده دستی است، نه PLC. توقف دستی دوباره از زمان کارکرد کسر نمی‌شود. فقط ساعت‌های بسته‌شده و داده تولید معتبر مبنا هستند؛ ساعت باز یا داده ناقص شاخص را نامشخص می‌کند."});
        });
        return endpoints;
    }
}
