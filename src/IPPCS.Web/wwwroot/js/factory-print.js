(()=>{
"use strict";
const el=id=>document.getElementById(id);
const kinds=new Set(["order","mps","plan","bom","routing","control-plan","report","eol","aps"]);
const fmt=v=>v===null||v===undefined||v===""?"—":
 typeof v==="number"?v.toLocaleString("fa-IR",{maximumFractionDigits:4}):String(v);
const tz="Asia/Tehran";
function stamped(utc){
 if(!utc)return "—";
 const d=new Date(utc);
 return Number.isNaN(d.getTime())?"—":new Intl.DateTimeFormat("fa-IR-u-ca-persian",{
  timeZone:tz,year:"numeric",month:"2-digit",day:"2-digit",
  hour:"2-digit",minute:"2-digit",hour12:false
 }).format(d);
}
const node=(tag,content,klass)=>{
 const e=document.createElement(tag);
 if(content!==undefined&&content!==null)e.textContent=fmt(content);
 if(klass)e.className=klass;
 return e;
};
async function get(path){
 const r=await IPPCSSecurity.secureFetch("/api/v1"+path);
 let result=null;
 const text=await r.text();
 try{result=JSON.parse(text);}catch{}
 if(!r.ok)throw Error(result?.message||"دریافت سند چاپی ناموفق بود (HTTP "+r.status+").");
 return result;
}
const field=(label,value)=>({label,value:fmt(value)});
function printUrl(kind,id){
 if(!kinds.has(kind))throw Error("نوع سند چاپی معتبر نیست.");
 const qp=new URLSearchParams({kind});
 if(id)qp.set("id",id);
 return "/factory-print.html?"+qp.toString();
}
function fromReport(data,params){
 const total=(rows,key)=>(rows||[]).reduce((sum,x)=>sum+Number(x[key]||0),0);
 const rows=(objects,fields)=>objects.map(item=>fields.map(key=>fmt(item[key])));
 const definitions=data.definitions||{};
 return {
  kind:"report",title:"گزارش جامع عملکرد و کنترل تولید",
  reference:data.fromJalali+" تا "+data.throughJalali,status:"گزارش تحلیلی با داده‌های وضعیت فعلی",
  officiallyApproved:false,extractedUtc:data.generatedUtc,
  sourceId:"IPPCS / "+data.fromJalali+" — "+data.throughJalali,
  fields:[
   field("شروع بازه",data.fromJalali),field("پایان بازه",data.throughJalali),
   field("فیلتر محصول",params.get("itemId")||"تمام اقلام"),
   field("فیلتر مرکز",params.get("workCenterId")||"تمام مراکز"),
   field("تعداد دستورات",total(data.orders,"orderCount")),
   field("دستورات تکمیل‌شده",total(data.orders,"completedOrders")),
   field("دفعات عبور عملیات",total(data.centers,"confirmationCount")),
   field("قرنطینه باز فعلی",data.quality?.unresolvedEventsNow),
   field("CAPA سررسیدگذشته",data.capa?.actionOverdueNow)
  ],
  tables:[
   {title:"۱. وضعیت سررسید و تحقق دستورات بر حسب محصول",columns:[
    "کد","شرح محصول","دستورات","مقدار برنامه","تکمیل‌شده",
    "خروجی سالم مستند","فاقد عملیات نهایی","به‌موقع","بدون زمان پایان",
    "درصد به‌موقع","باز با سررسیدگذشته"],
    rows:rows(data.orders||[],["itemCode","itemName","orderCount","plannedQuantity",
     "completedOrders","finishedGoodWithEvidence","missingFinalOperation",
     "onTimeCompletedOrders","missingCompletionTimestamp","onTimePercent","overdueOpenNow"])},
   {title:"۲. عبور عملیات مراکز کاری",columns:[
    "مرکز","شرح","تعداد ثبت","سالم","ضایعات","بازکاری","سهم سالم ٪"],
    rows:rows(data.centers||[],["centerCode","centerName","confirmationCount",
     "good","scrap","rework","firstPassSharePercent"])},
   {title:"۳. روند روزانه ثبت تولید",columns:[
    "تاریخ شمسی","ثبت عملیات","سالم","ضایعات","بازکاری"],
    rows:rows(data.daily||[],["dateJalali","confirmationCount","good","scrap","rework"])},
   {title:"۴. پرونده‌های انحراف و اقدامات اصلاحی",columns:[
    "نوع","کد علت","جدید","باز اکنون","سررسید گذشته"],
    rows:rows(data.capa?.categories||[],["category","rootCauseCode","opened",
     "unresolvedNow","actionOverdueNow"])},
   {title:"۵. شاخص‌های قرنطینه کیفیت در بازه",columns:[
    "رخدادها","مقدار زمان ثبت","باز فعلی","مقدار رخداد باز فعلی"],
    rows:[[fmt(data.quality?.raisedEvents),fmt(data.quality?.heldQuantityAtEvent),
     fmt(data.quality?.unresolvedEventsNow),fmt(data.quality?.unresolvedQuantityFromPeriodNow)]]},
   {title:"۶. تعاریف و حدود قابلیت اتکای شاخص‌ها",columns:["شاخص","تعریف"],
    rows:Object.entries(definitions).map(([k,v])=>[k,fmt(v)])}
  ],
  warning:"خروجی عبور عملیات را نباید معادل موجودی یا خروجی نهایی دانست. "+
  "وضعیت قرنطینه، تأخیر، تکمیل دستور و CAPA ممکن است مربوط به زمان جاری باشد، "+
  "نه بازسازی تاریخی نقطه‌ای. گزارش فقط خواندنی و فاقد مجوز تولید/ارسال است."
 };
}
function fromEol(order,check){
 if(!check.alreadyApproved ||
    (check.issues||[]).some(x=>x.code!=="AlreadyApproved"))
  throw Error("گواهی داخلی EOL معتبر و ثبت‌شده موجود نیست؛ چاپ گواهی تأیید ممنوع است.");
 return {
  kind:"eol",title:"گواهی کنترل داخلی پایان خط (EOL)",
  reference:order.number,status:"دارای تأیید داخلی کنترل کیفیت",
  officiallyApproved:true,extractedUtc:new Date().toISOString(),sourceId:order.id,
  fields:[
   field("شماره دستور",order.number),field("نوع تولید","مونتاژ محصول نهایی"),
   field("تاریخ تأیید داخلی",stamped(check.approvedUtc)),
   field("تأییدکننده ثبت‌شده",check.approvedBy),
   field("بچ‌های نهایی",check.finalLotCount),
   field("مقدار سالم گواهی‌شده",check.finalGoodQuantity),
   field("مشخصه‌های کنترل اجباری",check.mandatoryFinalChecks)
  ],
  tables:[
   {title:"شواهد منشأ نیمه‌ساخته برای بچ‌های مونتاژ",
    columns:["کد بچ","شناسه جزء","نیاز مبنا","پیوند مستند","کسری"],
    rows:(check.coverage||[]).map(x=>[fmt(x.lotCode),fmt(x.componentItemId),
      fmt(x.requiredQuantity),fmt(x.linkedQuantity),fmt(x.missingQuantity)])},
   {title:"وضعیت پرونده گواهی",
    columns:["آیتم","وضعیت"],
    rows:[["گواهی ثبت‌شده","بله"],["اعتبار امضای داخلی ثبت‌شده","بررسی موفق"],
    ["مانده موارد بررسی‌نشده EOL",fmt(check.issues?.filter(x=>x.code!=="AlreadyApproved").length)]],
    note:"صرفاً گواهی کنترل داخلی است. امضای الکترونیکی PKI و اجازه تحویل مشتری نیست."}
  ],
  warning:check.disclaimer
 };
}
function jalaliDate(iso){
 if(!iso)return "—";
 if(!/^\\d{4}-\\d{2}-\\d{2}/.test(String(iso)))return fmt(iso);
 const date=new Date(String(iso).slice(0,10)+"T12:00:00Z");
 return Number.isNaN(date.getTime())?fmt(iso):new Intl.DateTimeFormat(
  "fa-IR-u-ca-persian",{timeZone:"UTC",year:"numeric",month:"2-digit",day:"2-digit"}).format(date);
}
function fromAps(run,detail,items=[],centers=[]){
 const data=detail?.result||{};
 const item=id=>items.find(x=>x.id===id)?.code||fmt(id);
 const center=id=>centers.find(x=>x.id===id)?.code||fmt(id);
 const allocations=data.allocations||data.Allocations||[];
 const unscheduled=data.unscheduled||data.Unscheduled||[];
 return {
  kind:"aps",title:"برنامه زمان‌بندی ظرفیت محدود (APS)",
  reference:run.id,status:"نتیجه سناریوی برنامه‌ریزی / فاقد تصویب اجرایی",
  officiallyApproved:false,extractedUtc:run.calculatedUtc,sourceId:run.id,
  fields:[field("الگوریتم",run.algorithm),field("تاریخ شروع",run.startJalali),
   field("افق برنامه (روز)",run.horizonDays),field("ردیف تخصیص",allocations.length),
   field("عملیات خارج از ظرفیت",unscheduled.length)],
  tables:[
   {title:"برنامه تخصیص عملیات و مراکز",columns:[
    "شناسه محصول","مرکز","مرحله","عملیات","مقدار","موعد",
    "شروع شمسی","ساعت شروع","پایان شمسی","ساعت پایان","دیرکرد"],
    rows:allocations.map(x=>[
     item(x.itemId),center(x.workCenterId),fmt(x.sequence),fmt(x.operationName),
     fmt(x.quantity),jalaliDate(x.dueOn||x.dueJalali),
     jalaliDate(x.startDate||x.startJalali),fmt(x.startsAt),
     jalaliDate(x.finishDate||x.finishJalali),fmt(x.finishesAt),x.late?"بله":"خیر"])},
   {title:"عملیات زمان‌بندی‌نشده",columns:["محصول","مرحله","علت"],
    rows:unscheduled.map(x=>[item(x.itemId),fmt(x.sequence),fmt(x.reason)])}
  ],
  warning:"نتیجه APS سناریویی است، نه دستور ساخت آزادشده یا رزرو قطعی ماشین. "+
   "اطلاعات خام برنامه در زمان چاپ از Snapshot ذخیره‌شده خوانده شده‌اند."
 };
}
function makeCells(doc){
 const root=el("print-document");root.dataset.kind=doc.kind;
 el("print-title").textContent=fmt(doc.title);
 el("print-reference").textContent=fmt(doc.reference);
 el("print-status").textContent=fmt(doc.status);
 el("print-created").textContent=stamped(doc.extractedUtc);
 el("print-source").textContent=fmt(doc.sourceId);
 el("print-disclaimer").textContent=fmt(doc.warning);
 el("print-footer-code").textContent=fmt(doc.reference);
 const watermark=el("print-watermark");
 watermark.textContent=doc.officiallyApproved?"":"پیش‌نویس / فاقد تصویب اجرایی";
 const facts=el("print-meta");facts.replaceChildren();
 for(const f of doc.fields||[]){
  const wrapper=node("div",null,"print-field");
  wrapper.append(node("label",f.label),node("strong",f.value));facts.append(wrapper);
 }
 const body=el("print-tables");body.replaceChildren();
 for(const group of doc.tables||[]){
  const sec=node("section",null,"print-section");
  sec.append(node("h2",group.title));
  const table=node("table",null,"print-table");
  const thead=node("thead"),head=node("tr");
  for(const col of group.columns||[])head.append(node("th",col));
  thead.append(head);table.append(thead);
  const tbody=node("tbody");
  if(!(group.rows||[]).length){
   const tr=node("tr");const empty=node("td","رکوردی برای این بخش ثبت نشده است.","empty");
   empty.colSpan=group.columns.length;tr.append(empty);tbody.append(tr);
  }
  for(const row of group.rows||[]){
   const tr=node("tr");
   for(const val of row)tr.append(node("td",val));
   tbody.append(tr);
  }
  table.append(tbody);sec.append(table);
  if(group.note)sec.append(node("p",group.note,"print-section-note"));
  body.append(sec);
 }
 root.hidden=false;
}
async function boot(){
 try{
  const params=new URLSearchParams(location.search);
  const kind=params.get("kind"),id=params.get("id");
  if(!kinds.has(kind))throw Error("نوع سند چاپی معتبر نیست.");
  const status=await IPPCSSecurity.secureFetch("/auth/me");
  if(!status.ok)throw Error("برای دریافت سند چاپی ابتدا وارد سامانه شوید.");
  const user=await status.json();
  el("print-user").textContent=fmt(user.username);
  let documentData;
  if(kind==="report"){
   const query=new URLSearchParams();
   for(const field of ["fromJalali","throughJalali","itemId","workCenterId"]){
    const value=params.get(field);
    if(value)query.set(field,value);
   }
   if(!query.get("fromJalali")||!query.get("throughJalali"))
    throw Error("برای گزارش، بازه تاریخ شمسی لازم است.");
   documentData=fromReport(await get("/factory-reports/overview?"+query),query);
  }else{
   if(!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(id||""))
    throw Error("شناسه معتبر سند برای چاپ لازم است.");
   if(kind==="eol"){
    const [order,check]=await Promise.all([
     get("/manufacturing-orders/"+id),
     get("/manufacturing-orders/"+id+"/end-of-line-check")]);
    documentData=fromEol(order,check);
   }else if(kind==="aps"){
    const [detail,items,centers]=await Promise.all([
     get("/factory-aps/runs/"+id),get("/items"),get("/work-centers")]);
    const result=detail.result||{};
    if((result.allocations||[]).length>800||(result.unscheduled||[]).length>800)
     throw Error("سناریوی APS بیش از سقف چاپ ۸۰۰ ردیف است؛ چاپ ناقص ممنوع.");
    documentData=fromAps(detail,detail,items,centers);
   }else{
    documentData=await get("/factory-print/"+kind+"/"+id);
   }
  }
  makeCells(documentData);
  el("print-action").disabled=false;
  el("print-action").onclick=()=>window.print();
  document.title="IPPCS | "+documentData.title+" | "+documentData.reference;
 }catch(error){
  el("print-error").hidden=false;
  el("print-error").textContent=error.message||"تهیه سند چاپی ممکن نیست.";
  el("print-action").disabled=true;
 }
}
function back(){
 const p=new URLSearchParams(location.search),returnTo=p.get("back");
 const allowed=new Set(["/app.html","/execution.html","/planning.html",
  "/planning-advanced.html","/control-plan.html","/factory-reports.html"]);
 location.assign(allowed.has(returnTo)?returnTo:"/app.html");
}
el("print-back").addEventListener("click",back);
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);
else boot();
window.IPPCSFactoryPrint={printUrl};
})();
