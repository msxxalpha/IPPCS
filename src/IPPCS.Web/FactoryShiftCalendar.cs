// Uniform interpretation of weekly IPPCS shift windows across midnight.
// A shift starting on a weekday owns the following morning until EndTime.
// No connection to an HR attendance/leave system or external maintenance.
public static class FactoryShiftCalendar
{
    public static (DateTime Start,DateTime End) Window(WorkShift shift,DateOnly day)
    {
        var from=day.ToDateTime(shift.StartTime);
        var to=day.ToDateTime(shift.EndTime);
        if(shift.EndTime<shift.StartTime)to=to.AddDays(1);
        return (from,to);
    }
    public static bool Valid(WorkShift shift)=>
        shift.WorkCenterId!=Guid.Empty&&shift.DayOfWeek is >=0 and <=6&&
        shift.StartTime!=shift.EndTime&&
        shift.EffectiveCapacityPercent is >0 and <=100;

    public static DateTime EffectiveEnd(WorkShift shift,DateOnly day,
        decimal multiplier=1m)
    {
        var (from,to)=Window(shift,day);
        var effective=Math.Min((decimal)(to-from).TotalMinutes,
            Math.Floor((decimal)(to-from).TotalMinutes*
                shift.EffectiveCapacityPercent*multiplier/100m));
        return from.AddMinutes((double)Math.Max(0,effective));
    }
    public static decimal AvailableMinutes(Guid center,DateOnly date,
        IReadOnlyList<WorkShift> shifts)
    {
        var from=date.ToDateTime(TimeOnly.MinValue);
        var through=from.AddDays(1);
        decimal minutes=0;
        foreach(var shift in shifts.Where(x=>x.WorkCenterId==center&&Valid(x)))
        {
            // A late shift starting yesterday may carry capacity into today.
            foreach(var startDay in new[]{date.AddDays(-1),date})
            {
                if(shift.DayOfWeek!=(int)startDay.DayOfWeek)continue;
                var (a,_)=Window(shift,startDay);
                var b=EffectiveEnd(shift,startDay);
                var overlap=(b<through?b:through)-(a>from?a:from);
                if(overlap>TimeSpan.Zero)minutes+=(decimal)overlap.TotalMinutes;
            }
        }
        return minutes;
    }

    public static bool Fits(Guid center,DateTime start,DateTime end,
        IReadOnlyList<WorkShift> shifts)
    {
        if(end<=start||end-start>TimeSpan.FromDays(1))return false;
        foreach(var day in new[]{
            DateOnly.FromDateTime(start).AddDays(-1),DateOnly.FromDateTime(start)})
            foreach(var shift in shifts.Where(x=>x.WorkCenterId==center&&Valid(x)&&
                x.DayOfWeek==(int)day.DayOfWeek))
            {
                var (from,_)=Window(shift,day);
                if(start>=from&&end<=EffectiveEnd(shift,day))return true;
            }
        return false;
    }
    public static bool ConflictsWeekly(WorkShift proposed,IReadOnlyList<WorkShift> existing)
    {
        if(!Valid(proposed))return true;
        // 14-day witness covers Sunday->Monday and Saturday->Sunday boundaries
        // without assuming which Gregorian/Jalali weekday is the factory start.
        var week=new DateOnly(2026,10,25); // Sunday
        for(var i=0;i<14;i++)
        {
            var date=week.AddDays(i);
            if((int)date.DayOfWeek!=proposed.DayOfWeek)continue;
            var (from,to)=Window(proposed,date);
            foreach(var other in existing.Where(x=>x.WorkCenterId==proposed.WorkCenterId))
                for(var offset=-1;offset<=1;offset++)
                {
                    var otherDay=date.AddDays(offset);
                    if(other.DayOfWeek!=(int)otherDay.DayOfWeek||!Valid(other))continue;
                    var (a,b)=Window(other,otherDay);
                    if(from<b&&a<to)return true;
                }
        }
        return false;
    }
}
