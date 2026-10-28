(()=>{
"use strict";
const $=id=>document.getElementById(id);
let dataset=null;
const definitions={
 orders:[["کد محصول","itemCode"],["شرح محصول","itemName"],["دستور","orderCount"],["تعداد برنامه","plannedQuantity"],
  ["تکمیل‌شده","completedOrders"],["خروجی سالم نهایی مستند","finishedGoodWithEvidence"],
  ["فاقد شواهد عملیات آخر","missingFinalOperation"],["به‌موقع (دارای زمان پایان)","onTimeCompletedOrders"],
  ["زمان پایان ناموجود","missingCompletionTimestamp"],["درصد به‌موقع معتبر","onTimePercent"],["سررسیدگذشته باز اکنون","overdueOpenNow"]],
 centers:[["مرکز","centerCode"],["شرح","centerName"],["دفعات ثبت عملیات","confirmationCount"],
  ["سالم","good"],["ضایعات","scrap"],["دوباره‌کاری","rework"],["سهم سالم ٪","firstPassSharePercent"]],
 daily:[["تاریخ شمسی","dateJalali"],["ثبت عملیات","confirmationCount"],
  ["سالم","good"],["ضایعات","scrap"],["دوباره‌کاری","rework"]],
 capa:[["دسته انحراف","category"],["کد علت ریشه‌ای","rootCauseCode"],
  ["پرونده‌های جدید","opened"],["باز در حال حاضر","unresolvedNow"],
  ["سررسیدگذشته در حال حاضر","actionOverdueNow"]]
};
const categories={
 Quantity:"مقدار",DeliveryDate:"موعد",Quality:"کیفیت",Material:"مواد",
 Capacity:"ظرفیت",Downtime:"توقف",Other:"سایر"
};
function message(text,error=false){
 const host=$("report-message");
 host.hidden=false;host.className="notice"+(error?" error":"");host.textContent=text;
}
function number(value){
 if(value===null||value===undefined)return "—";
 return typeof value==="number"?value.toLocaleString("fa-IR",{maximumFractionDigits:4}):String(value);
}
function reportRows(key){
 return key==="capa"?(dataset?.capa?.categories||[]):(dataset?.[key]||[]);
}
function table(hostId,columns,rows){
 const target=$(hostId);target.replaceChildren();
 const table=document.createElement("table");
 table.className="factory-cases-table";
 const thead=document.createElement("thead");const header=document.createElement("tr");
 for(const [label] of columns){const th=document.createElement("th");th.textContent=label;header.append(th);}
 thead.append(header);table.append(thead);
 const tbody=document.createElement("tbody");
 if(!rows.length){
  const tr=document.createElement("tr");const td=document.createElement("td");
  td.colSpan=columns.length;td.textContent="در بازه انتخابی داده‌ای برای این شاخص ثبت نشده است.";
  tr.append(td);tbody.append(tr);
 }
 for(const row of rows){
  const tr=document.createElement("tr");
  for(const [,field] of columns){
   const td=document.createElement("td");td.dataset.label=columns.find(([,f])=>f===field)?.[0]||"";
   const value=field==="category"?(categories[row[field]]||row[field]):row[field];
   td.textContent=number(value);tr.append(td);
  }
  tbody.append(tr);
 }
 table.append(tbody);target.append(table);
}
function show(data){
 dataset=data;
 for(const key of ["orders","centers","daily","capa"]){
  table("rpt-"+key,definitions[key],reportRows(key));
 }
 const sum=(rows,key)=>rows.reduce((total,row)=>total+Number(row[key]||0),0);
 $("kpi-orders").textContent=number(sum(data.orders,"orderCount"));
 $("kpi-complete").textContent=number(sum(data.orders,"completedOrders"));
 $("kpi-events").textContent=number(sum(data.centers,"confirmationCount"));
 $("kpi-holds").textContent=number(data.quality.unresolvedEventsNow);
 $("kpi-capa").textContent=number(data.capa.actionOverdueNow);
 $("q-raised").textContent=number(data.quality.raisedEvents);
 $("q-open").textContent=number(data.quality.unresolvedEventsNow);
 $("q-quantity").textContent=number(data.quality.unresolvedQuantityFromPeriodNow);
 const list=$("report-definitions");list.replaceChildren();
 for(const [key,text] of Object.entries(data.definitions||{})){
  const line=document.createElement("li");line.textContent=text;list.append(line);
 }
 for(const button of document.querySelectorAll("[data-export]"))button.disabled=false;
 message("گزارش محاسبه شد؛ تاریخچه وضعیت دستور و اقدام اصلاحی بازسازی نشده است و وضعیت فعلی نمایش داده می‌شود.");
}
function safeCsv(value){
 let s=value===null||value===undefined?"":String(value);
 // Prevent spreadsheet formula injection, including formulas after whitespace.
 if(/^[\s\uFEFF]*[=+@-]/u.test(s)&&!/^\s*-?\d+(?:\.\d+)?\s*$/.test(s))s="'"+s;
 return '"'+s.replace(/"/g,'""')+'"';
}
function exportCsv(key){
 if(!dataset||!definitions[key])return;
 const columns=definitions[key],rows=reportRows(key);
 const contents=[columns.map(x=>safeCsv(x[0])).join(","),
  ...rows.map(row=>columns.map(([,field])=>safeCsv(
   field==="category"?(categories[row[field]]||row[field]):row[field])).join(","))];
 const blob=new Blob(["\uFEFF"+contents.join("\r\n")],{type:"text/csv;charset=utf-8"});
 const url=URL.createObjectURL(blob);
 const a=document.createElement("a");a.href=url;
 a.download="IPPCS-"+key+"-"+dataset.fromJalali.replaceAll("/","-")+
  "-"+dataset.throughJalali.replaceAll("/","-")+".csv";
 a.click();URL.revokeObjectURL(url);
}
function todayJalali(){
 const parts=new Intl.DateTimeFormat("en-US-u-ca-persian",{
  timeZone:"Asia/Tehran",year:"numeric",month:"2-digit",day:"2-digit"
 }).formatToParts(new Date());
 const take=key=>parts.find(x=>x.type===key)?.value||"";
 return take("year").padStart(4,"0")+"/"+take("month").padStart(2,"0")+
  "/"+take("day").padStart(2,"0");
}
function fill(id,rows,label,placeholder){
 const control=$(id);
 IPPCSChoices.fill(control,rows,{value:"id",label,placeholder});
}
async function get(path){
 const response=await IPPCSSecurity.secureFetch(path,{headers:{Accept:"application/json"}});
 const text=await response.text();
 let data;try{data=JSON.parse(text);}catch{data=null;}
 if(!response.ok)throw new Error(data?.message||"دریافت داده ناموفق بود ("+response.status+").");
 return data;
}
async function init(){
 const today=todayJalali();
 $("report-through").value=today;
 $("report-from").value=today.slice(0,8)+"01";
 document.querySelectorAll("[data-export]").forEach(btn=>
  btn.addEventListener("click",()=>exportCsv(btn.dataset.export)));
 $("report-filters").addEventListener("submit",async e=>{
  e.preventDefault();
  const from=$("report-from").value.trim(),through=$("report-through").value.trim();
  if(!IPPCSJalali.parse(from)||!IPPCSJalali.parse(through)){
   message("تاریخ‌های شمسی معتبر لازم است.",true);return;
  }
  const params=new URLSearchParams({fromJalali:from,throughJalali:through});
  if($("report-item").value)params.set("itemId",$("report-item").value);
  if($("report-center").value)params.set("workCenterId",$("report-center").value);
  const btn=$("report-run");btn.disabled=true;dataset=null;
  for(const button of document.querySelectorAll("[data-export]"))button.disabled=true;
  message("در حال دریافت اطلاعات ثبت‌شده…");
  try{show(await get("/api/v1/factory-reports/overview?"+params));}
  catch(err){message(err.message,true);}finally{btn.disabled=false;}
 });
 try{
  const dimensions=await get("/api/v1/factory-reports/dimensions");
  fill("report-item",dimensions.items,x=>x.code+" — "+x.name,"همه اقلام");
  fill("report-center",dimensions.centers,x=>x.code+" — "+x.name,"همه مراکز");
 }catch(err){message("خطای دریافت فیلترها: "+err.message,true);}
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",init);
else init();
})();
