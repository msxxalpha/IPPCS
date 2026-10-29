(function(root){
 "use strict";

 function parseIsoDate(value){
  if(typeof value!=="string"||!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value))
   throw new Error("تاریخ میلادی نمودار گانت معتبر نیست.");
  const date=new Date(value+"T12:00:00.000Z");
  if(Number.isNaN(date.getTime())||date.toISOString().slice(0,10)!==value)
   throw new Error("تاریخ میلادی نمودار گانت معتبر نیست.");
  return date;
 }
 function clockMinutes(value){
  if(typeof value!=="string"||!/^([01][0-9]|2[0-3]):[0-5][0-9]$/.test(value))
   throw new Error("ساعت شروع یا پایان عملیات APS معتبر نیست.");
  const [hour,minute]=value.split(":").map(Number);
  return hour*60+minute;
 }
 function jalaliForIso(value){
  const parts=new Intl.DateTimeFormat("fa-IR-u-ca-persian-nu-latn",{
   timeZone:"UTC",year:"numeric",month:"2-digit",day:"2-digit"
  }).formatToParts(parseIsoDate(value));
  const result={};
  for(const p of parts)if(p.type==="year"||p.type==="month"||p.type==="day")result[p.type]=p.value;
  if(!result.year||!result.month||!result.day)
   throw new Error("تبدیل تاریخ گانت به تقویم شمسی ناموفق بود.");
  return result.year+"/"+result.month+"/"+result.day;
 }

 // Splits a real operation interval over calendar-day tracks. A shift crossing
 // midnight must never be represented as a negative-width bar on the start day.
 function buildSegments(allocations){
  if(!Array.isArray(allocations))throw new Error("فهرست عملیات گانت معتبر نیست.");
  const segments=[];
  for(const row of allocations){
   if(!row||!row.workCenterId||!row.startDateIso||!row.finishDateIso)
    throw new Error("تاریخ یا مرکز کاری عملیات گانت ناقص است.");
   const firstDate=parseIsoDate(row.startDateIso);
   const lastDate=parseIsoDate(row.finishDateIso);
   if(lastDate<firstDate)throw new Error("تاریخ پایان عملیات قبل از تاریخ شروع آن است.");
   const firstMinutes=clockMinutes(row.startTime);
   const lastMinutes=clockMinutes(row.finishTime);
   if(firstDate.getTime()===lastDate.getTime()&&lastMinutes<=firstMinutes)
    throw new Error("زمان پایان عملیات در همان روز باید بعد از زمان شروع باشد.");

   const cursor=new Date(firstDate.getTime());
   let dayCount=0;
   while(cursor<=lastDate){
    if(dayCount++>=91)throw new Error("بازه عملیات گانت از سقف افق پشتیبانی‌شده بیشتر است.");
    const dayIso=cursor.toISOString().slice(0,10);
    const isFirst=dayIso===row.startDateIso;
    const isLast=dayIso===row.finishDateIso;
    const startMinute=isFirst?firstMinutes:0;
    const finishMinute=isLast?lastMinutes:1440;
    if(finishMinute<startMinute)
     throw new Error("بازه روزانه عملیات گانت نامعتبر است.");
    // Operations that finish exactly at midnight have no positive-duration
    // segment on the next day; omit that zero-width segment.
    if(finishMinute>startMinute){
     segments.push({
      ...row,
      dayIso,
      dayJalali:isFirst?(row.startJalali||jalaliForIso(dayIso)):
       isLast?(row.finishJalali||jalaliForIso(dayIso)):jalaliForIso(dayIso),
      segmentStartMinutes:startMinute,
      segmentFinishMinutes:finishMinute,
      continuesFromPrevious:!isFirst,
      continuesNext:!isLast
     });
    }
    cursor.setUTCDate(cursor.getUTCDate()+1);
   }
  }
  return segments;
 }

 const api={buildSegments,jalaliForIso};
 if(root)root.IPPCSApsGantt=api;
 if(typeof module==="object"&&module&&module.exports)module.exports=api;
})(typeof window!=="undefined"?window:globalThis);
