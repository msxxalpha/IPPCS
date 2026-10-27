(()=>{
"use strict";
const $=s=>document.querySelector(s), $$=s=>Array.from(document.querySelectorAll(s));
const S={items:[],orders:[]};
const fval=(f,k)=>new FormData(f).get(k);
const numeric=(f,k)=>Number(window.IPPCSJalali.toLatin(fval(f,k)));
const num=v=>Number(v).toLocaleString("fa-IR",{maximumFractionDigits:4});
const statusNames=["پیش‌نویس","آزادشده","در حال اجرا","تعلیق","تکمیل‌شده","لغوشده"];
const status=x=>statusNames[x]||"نامشخص";
const itemName=id=>S.items.find(x=>x.id===id)?.name||id;
function flash(msg,error=false){const el=$("#flash");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=msg;el.scrollIntoView({behavior:"smooth",block:"nearest"});}
async function request(path,method="GET",data){
 const init={method,headers:{}};
 if(data!==undefined){init.headers["Content-Type"]="application/json";init.body=JSON.stringify(data);}
 const response=await window.IPPCSSecurity.secureFetch("/api/v1"+path,init);
 const text=await response.text();let value;
 try{value=text?JSON.parse(text):null;}catch{value=null;}
 if(!response.ok)throw new Error(value?.message||value?.title||text||"خطای سرویس");
 return value;
}
function select(el,values,display){
 const old=el.value;el.replaceChildren();
 const zero=document.createElement("option");zero.value="";zero.textContent="انتخاب کنید";el.append(zero);
 values.forEach(v=>{const opt=document.createElement("option");opt.value=v.id;opt.textContent=display(v);el.append(opt);});
 if([...el.options].some(x=>x.value===old))el.value=old;
}
function table(target,cols,rows,actions=[]){
 const root=$(target);root.replaceChildren();
 const wrap=document.createElement("div");wrap.className="table-wrap";
 const t=document.createElement("table"),thead=document.createElement("thead"),heads=document.createElement("tr");
 cols.forEach(([title])=>{const th=document.createElement("th");th.textContent=title;heads.append(th);});
 if(actions.length){const th=document.createElement("th");th.textContent="عملیات";heads.append(th);}
 thead.append(heads);t.append(thead);
 const body=document.createElement("tbody");
 if(!rows.length){const tr=document.createElement("tr"),td=document.createElement("td");td.className="empty";td.colSpan=cols.length+(actions.length?1:0);td.textContent="داده‌ای ثبت نشده است.";tr.append(td);body.append(tr);}
 for(const row of rows){const tr=document.createElement("tr");
  for(const [,get] of cols){const td=document.createElement("td");const v=typeof get==="function"?get(row):row[get];td.textContent=v??"—";tr.append(td);}
  if(actions.length){const td=document.createElement("td");td.style.whiteSpace="nowrap";
   for(const action of actions){if(action.show&&!action.show(row))continue;
    const btn=document.createElement("button");btn.type="button";btn.className="btn-outline";btn.textContent=action.label;
    btn.onclick=async()=>{btn.disabled=true;try{await action.run(row);}catch(e){flash(e.message,true);}finally{btn.disabled=false;}};
    td.append(btn,document.createTextNode(" "));
   }tr.append(td);}
  body.append(tr);
 }
 t.append(body);wrap.append(t);root.append(wrap);
}
async function reload(){
 [S.items,S.orders]=await Promise.all([request("/items"),request("/manufacturing-orders")]);
 select($("#order-item"),S.items.filter(x=>x.type===1||x.type===2),x=>x.code+" • "+x.name);
 $$("[data-order-active]").forEach(el=>select(el,S.orders.filter(x=>x.status===1||x.status===2),x=>x.number+" • "+status(x.status)));
 select($("#history-order"),S.orders,x=>x.number+" • "+status(x.status));
 select($("#draft-replan-order"),S.orders.filter(x=>x.status===0),x=>x.number+" • "+itemName(x.itemId));
 select($("#slot-order"),S.orders.filter(x=>[1,2].includes(x.status)),
  x=>x.number+" • "+status(x.status));
 select($("#timed-stop-order"),S.orders.filter(x=>[1,2,3].includes(x.status)),
  x=>x.number+" • "+status(x.status));
 select($("#clock-order"),S.orders.filter(x=>[1,2,3].includes(x.status)),
  x=>x.number+" • "+status(x.status));
 render();
 const selected=S.orders.find(x=>x.id===$("#timed-stop-order").value);
 select($("#timed-stop-operation"),selected?.operations||[],x=>x.sequence+" • "+x.name);
}
function render(){
 table("#orders-table",[
  ["شماره","number"],["محصول",x=>itemName(x.itemId)],
  ["نوع",x=>x.kind===0?"نیمه‌ساخته":"مونتاژ نهایی"],
  ["برنامه",x=>num(x.plannedQuantity)],["شروع","plannedStartJalali"],["موعد","dueOnJalali"],
  ["وضعیت",x=>status(x.status)],["تولید سالم",x=>num(x.operations.at(-1)?.goodQuantity||0)]
 ],S.orders,[
  {label:"آزادسازی",show:x=>x.status===0,run:async x=>{await request("/manufacturing-orders/"+x.id+"/release","POST");flash("دستور آزاد شد و عملیات مسیر ساخت ثبت شدند.");await reload();}},
  {label:"تعلیق",show:x=>x.status===1||x.status===2,run:async x=>{
   const reason=window.prompt("علت توقف یا قرنطینه دستور را وارد کنید:");if(reason===null)return;
   if(!reason.trim()){flash("علت تعلیق اجباری است.",true);return;}
   await request("/manufacturing-orders/"+x.id+"/hold","POST",{reason:reason.trim()});
   flash("دستور معلق شد.");await reload();
  }},
  {label:"ادامه",show:x=>x.status===3,run:async x=>{await request("/manufacturing-orders/"+x.id+"/resume","POST");flash("دستور فعال شد.");await reload();}},
  {label:"لغو",show:x=>x.status===0||x.status===1,run:async x=>{
   if(!window.confirm("آیا از لغو این دستور اطمینان دارید؟"))return;
   await request("/manufacturing-orders/"+x.id+"/cancel","POST");flash("دستور لغو شد.");await reload();
  }}
 ]);
 const operations=S.orders.flatMap(x=>x.operations.map(op=>({...op,order:x})));
 table("#operation-table",[
  ["شماره دستور",x=>x.order.number],["ترتیب",x=>x.sequence],["عنوان عملیات","name"],
  ["مرکز کاری",x=>x.workCenterId.slice(0,8)],["سالم",x=>num(x.goodQuantity)],
  ["ضایعات",x=>num(x.scrapQuantity)],["دوباره‌کاری",x=>num(x.reworkQuantity)],
  ["وضعیت",x=>x.isComplete?"تکمیل‌شده":"باز"]
 ],operations);
 refreshOperations("confirm-form","confirm-operation");
 refreshOperations("stop-form","stop-operation");
 refreshOperations("clock-form","clock-operation");
 refreshOperations("slot-form","slot-operation");
}
function refreshOperations(form,field){
 const host=$("#"+form),id=fval(host,"orderId");
 const order=S.orders.find(x=>x.id===id);
 const selectElement=$("#"+field);
 select(selectElement,order?.operations||[],x=>x.sequence+" - "+x.name+(x.isComplete?" (تکمیل)":""));
 if(field==="confirm-operation")for(const o of [...selectElement.options]) {
  const op=order?.operations.find(x=>x.id===o.value);
  if(op?.isComplete)o.disabled=true;
 }
}
function register(formId,run){const form=$(formId);
 form.addEventListener("submit",async e=>{
  e.preventDefault();if(!form.reportValidity())return;
  const btn=form.querySelector('button[type="submit"]');btn.disabled=true;
  try{await run(form);flash("عملیات با موفقیت ثبت شد.");form.reset();await reload();}
  catch(ex){flash(ex.message,true);}
  finally{btn.disabled=false;}
 });
}
function boot(){
 $$("nav [data-page]").forEach(btn=>btn.onclick=()=>{
  $$("section.page").forEach(p=>p.hidden=p.id!==btn.dataset.page);
  $$("nav [data-page]").forEach(b=>b.classList.toggle("nav-active",btn===b));
  $("#title").textContent=btn.textContent;
  $("#flash").hidden=true;
 });
 register("#order-form",f=>request("/manufacturing-orders","POST",{
  number:fval(f,"number"),itemId:fval(f,"itemId"),plannedQuantity:numeric(f,"quantity"),
  plannedStartJalali:fval(f,"start"),dueOnJalali:fval(f,"due")
 }));
 for(const [form,target] of [["confirm-form","confirm-operation"],["stop-form","stop-operation"]])
  $("#"+form+' [name="orderId"]').addEventListener("change",()=>refreshOperations(form,target));
 register("#confirm-form",f=>request("/manufacturing-orders/"+fval(f,"orderId")+"/operations/"+fval(f,"operationId")+"/confirm","POST",{
  goodQuantity:numeric(f,"good"),scrapQuantity:numeric(f,"scrap"),reworkQuantity:numeric(f,"rework"),
  lotCode:fval(f,"lot")||null,note:fval(f,"note")||null
 }));
 $("#draft-replan-order").addEventListener("change",()=>{
  const selected=S.orders.find(x=>x.id===$("#draft-replan-order").value);
  if(!selected)return;
  const form=$("#draft-replan-form");
  form.querySelector('[name="quantity"]').value=selected.plannedQuantity;
  form.querySelector('[name="start"]').value=selected.plannedStartJalali;
  form.querySelector('[name="due"]').value=selected.dueOnJalali;
  $("#release-issues").replaceChildren();
 });
 async function releaseCheck(){
  const id=$("#draft-replan-order").value;
  if(!id)throw Error("یک دستور پیش‌نویس انتخاب کنید.");
  const result=await request("/manufacturing-orders/"+id+"/release-check");
  table("#release-issues",[["کد","code"],["موضوع","description"]],
   result.issues.length?result.issues:[{code:"آماده",description:"شرایط ثبت‌شده برای آزادسازی مناسب است."}]);
  flash(result.canRelease?
   "مسیر ساخت با "+result.operations+" عملیات، BOM و ارتباط MPS بررسی شدند.":
   "مشکلات آزادسازی را رفع کنید؛ آزادسازی اکنون مجاز نیست.",!result.canRelease);
 }
 $("#release-check").onclick=()=>releaseCheck().catch(e=>flash(e.message,true));
 register("#draft-replan-form",async f=>{
  const id=fval(f,"orderId");
  const result=await request("/manufacturing-orders/"+id+"/replan-draft","POST",{
   plannedQuantity:numeric(f,"quantity"),plannedStartJalali:fval(f,"start"),
   dueOnJalali:fval(f,"due"),reason:fval(f,"reason")
  });
  flash("اصلاح برنامه و شناسه نسخه‌های معتبر ذخیره شد. دستور "+result.number+" هنوز پیش‌نویس است.");
  await reload();
  $("#draft-replan-order").value=id;
  await releaseCheck();
 });
 register("#dispatch-form",async f=>{
  const qs="?asOfJalali="+encodeURIComponent(fval(f,"from"))+
    "&horizonDays="+encodeURIComponent(numeric(f,"horizon"));
  const report=await request("/factory-dispatch/board"+qs);
  table("#dispatch-summary",[["شاخص","name"],["مقدار","count"]],[
   {name:"دستورات فعال داخل افق",count:report.ordersConsidered},
   {name:"مراحل بدون مانع ترتیب و کیفیت",count:report.runnableOperations},
   {name:"مراحل قابل شروع فاقد رزرو",count:report.missingReservationCount}
  ]);
  table("#dispatch-table",[
   ["دستور","orderNumber"],["مرکز",x=>x.workCenterId.slice(0,8)],
   ["مرحله",x=>x.sequence+" • "+x.operationName],
   ["موعد",x=>new Date(x.dueOn+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
   ["قابل شروع با گیت کیفیت",x=>x.canStartByRouteAndQuality?"بله":"خیر"],
   ["قرنطینه باز",x=>x.hasOpenQualityHold?"دارد":"ندارد"],
   ["رزرو در افق",x=>x.hasBookedMachineSlot?"دارد":"ندارد"],
   ["ساعت باز",x=>x.hasRunningClock?"دارد":"ندارد"],
   ["WIP",x=>num(x.workInProgress)],["قرنطینه مرحله",x=>num(x.qualityPending)],
   ["توضیح","guidance"]
  ],report.rows);
  flash(report.disclaimer);
 });
 const slotForm=$("#slot-form");
 $("#slot-order").addEventListener("change",()=>{
   refreshOperations("slot-form","slot-operation");
   $("#slot-table").replaceChildren();
 });
 async function reloadSlots(){
  const id=fval(slotForm,"orderId");
  if(!id){flash("ابتدا دستور ساخت را انتخاب کنید.",true);return;}
  const data=await request("/manufacturing-orders/"+id+"/capacity-reservations");
  const order=S.orders.find(x=>x.id===id);
  table("#slot-table",[
   ["مرحله",x=>order?.operations.find(y=>y.id===x.operationId)?.name||x.operationId.slice(0,8)],
   ["تاریخ رزرو","dayJalali"],
   ["شروع برنامه","startTime"],["پایان برنامه","endTime"],
   ["دقایق برنامه",x=>num(x.plannedMinutes)],
   ["دقایق واقعی",x=>x.actualMinutes==null?"—":num(x.actualMinutes)],
   ["اختلاف مدت (دقیقه)",x=>x.durationVarianceMinutes==null?"—":num(x.durationVarianceMinutes)],
   ["اختلاف شروع (دقیقه)",x=>x.startVarianceMinutes==null?"—":num(x.startVarianceMinutes)],
   ["شروع واقعی",x=>x.actualStartJalali?(x.actualStartJalali+" "+x.actualStartTime):"—"],
   ["پایان واقعی",x=>x.actualEndTime||"—"],
   ["توضیحات",x=>x.note||"—"]
  ],data.rows,[{label:"لغو رزرو",run:async x=>{
    if(!window.confirm("رزرو انتخاب‌شده لغو شود؟ سابقه حذف نخواهد شد."))return;
    await request("/manufacturing-orders/"+id+"/capacity-reservations/"+x.id+"/cancel","POST");
    flash("رزرو لغو شد و سابقه حفظ گردید.");await reloadSlots();
  }}]);
 }
 $("#slot-refresh").onclick=()=>reloadSlots().catch(ex=>flash(ex.message,true));
 slotForm.addEventListener("submit",async event=>{
   event.preventDefault();
   if(!slotForm.reportValidity())return;
   const id=fval(slotForm,"orderId"),button=slotForm.querySelector('button[type="submit"]');
   button.disabled=true;
   try{
     await request("/manufacturing-orders/"+id+"/capacity-reservations","POST",{
       operationId:fval(slotForm,"operationId"),dateJalali:fval(slotForm,"date"),
       startTime:fval(slotForm,"startTime"),endTime:fval(slotForm,"endTime"),
       note:fval(slotForm,"note")
     });
     flash("بازه زمانی مرکز کاری رزرو شد.");
     await reloadSlots();
   }catch(ex){flash(ex.message,true);}finally{button.disabled=false;}
 });
 const clockForm=$("#clock-form");
 $("#clock-order").addEventListener("change",()=>{
   refreshOperations("clock-form","clock-operation");
   $("#clock-history").replaceChildren();
 });
 const clockTime=x=>x?new Date(x).toLocaleString("fa-IR-u-ca-persian",{
   timeZone:"Asia/Tehran",year:"numeric",month:"2-digit",day:"2-digit",
   hour:"2-digit",minute:"2-digit",second:"2-digit",hour12:false
 }):"هنوز باز است";
 async function refreshClock(){
   const id=fval(clockForm,"orderId");
   if(!id){flash("ابتدا یک دستور تولید انتخاب کنید.",true);return;}
   const data=await request("/manufacturing-orders/"+id+"/operation-clock");
   const order=S.orders.find(x=>x.id===id);
   table("#clock-history",[
     ["عملیات",x=>order?.operations.find(op=>op.id===x.operationId)?.name||x.operationId.slice(0,8)],
     ["مرکز",x=>x.workCenterId.slice(0,8)],
     ["شروع واقعی",x=>clockTime(x.startedUtc)],
     ["پایان واقعی",x=>clockTime(x.stoppedUtc)],
     ["وضعیت",x=>x.isRunning?"در حال کار":"خاتمه‌یافته"]
   ],data.sessions);
 }
 async function clockAction(action){
   if(!clockForm.reportValidity())return;
   const id=fval(clockForm,"orderId"),op=fval(clockForm,"operationId");
   if(!id||!op){flash("دستور و عملیات را انتخاب کنید.",true);return;}
   const button=$("#clock-"+action);button.disabled=true;
   try{
     await request("/manufacturing-orders/"+id+"/operations/"+op+"/clock-"+action,"POST");
     flash(action==="start"?"زمان شروع عملیات ثبت شد.":"زمان پایان عملیات ثبت شد.");
     await reload();
     $("#clock-order").value=id;
     refreshOperations("clock-form","clock-operation");
     $("#clock-operation").value=op;
     await refreshClock();
   }catch(ex){flash(ex.message,true);}finally{button.disabled=false;}
 }
 $("#clock-start").onclick=()=>clockAction("start");
 $("#clock-stop").onclick=()=>clockAction("stop");
 $("#clock-refresh").onclick=()=>refreshClock().catch(ex=>flash(ex.message,true));
 function timedOperations(){
  const o=S.orders.find(x=>x.id===$("#timed-stop-order").value);
  select($("#timed-stop-operation"),o?.operations||[],x=>x.sequence+" • "+x.name);
 }
 async function timedHistory(){
  const id=$("#timed-stop-order").value;
  if(!id)throw Error("دستور تولید را انتخاب کنید.");
  const data=await request("/manufacturing-orders/"+id+"/timed-downtime");
  const order=S.orders.find(x=>x.id===id);
  table("#timed-stop-history",[
   ["عملیات",x=>order?.operations.find(o=>o.id===x.operationId)?.name||x.operationId.slice(0,8)],
   ["مرکز",x=>x.workCenterId.slice(0,8)],
   ["شروع",x=>clockTime(x.startedUtc)],
   ["پایان",x=>clockTime(x.stoppedUtc)],
   ["مدت (دقیقه)",x=>x.stoppedUtc?
     num((new Date(x.stoppedUtc)-new Date(x.startedUtc))/60000):"باز"],
   ["علت","reason"],["ثبت‌کننده","recordedBy"]
  ],data.rows);
  flash(data.warning);
 }
 async function timedAction(action){
  const id=$("#timed-stop-order").value,op=$("#timed-stop-operation").value;
  if(!id||!op)throw Error("دستور و عملیات تولید لازم است.");
  const start=action==="start";
  const reason=$("#timed-stop-reason").value.trim();
  if(start&&!reason)throw Error("علت توقف زمان‌دار اجباری است.");
  const path="/manufacturing-orders/"+id+"/timed-downtime"+
   (start?"/start":"/"+op+"/stop");
  await request(path,"POST",start?{operationId:op,reason}:undefined);
  await timedHistory();
  flash(start?"شروع توقف زمان‌دار ثبت شد؛ ساعت کار تا خاتمه توقف قابل شروع نیست.":
   "پایان توقف به صورت مستقل و تغییرناپذیر ثبت شد.");
 }
 $("#timed-stop-order").onchange=()=>{
  timedOperations();$("#timed-stop-history").replaceChildren();
 };
 for(const action of ["start","end"]){
  $("#timed-stop-"+action).onclick=()=>timedAction(action==="end"?"stop":"start")
    .catch(e=>flash(e.message,true));
 }
 $("#timed-stop-review").onclick=()=>timedHistory().catch(e=>flash(e.message,true));
 timedOperations();
 $("#oee-reconciliation-review").onclick=async()=>{
  try{
   const date=$("#oee-date").value;
   if(!date)throw Error("ابتدا تاریخ شمسی گزارش OEE را انتخاب کنید.");
   const report=await request("/oee/downtime-reconciliation?dateJalali="+encodeURIComponent(date));
   table("#oee-reconciliation-table",[
    ["مرکز",x=>S.orders.flatMap(o=>o.operations)
      .find(op=>op.workCenterId===x.workCenterId)?.name||x.workCenterId.slice(0,8)],
    ["برنامه (دقیقه)",x=>x.plannedMinutes===null?"نامشخص":num(x.plannedMinutes)],
    ["ساعت واقعی بسته‌شده",x=>num(x.closedRunningMinutes)],
    ["توقف دقیق",x=>x.timedStopMinutes===null?"نامشخص":num(x.timedStopMinutes)],
    ["توقف قدیمی بدون ساعت",x=>num(x.legacyUntimedMinutes)],
    ["باقی‌مانده بدون طبقه‌بندی",x=>x.unclassifiedMinutes===null?"نامشخص":num(x.unclassifiedMinutes)],
    ["وضعیت",x=>x.isTimeComparable?"قابل تطبیق زمانی":"نیازمند بررسی: "+x.status],
    ["توضیحات","warning"]
   ],report.rows);
   flash(report.disclaimer);
  }catch(e){flash(e.message,true);}
 };
 register("#stop-form",f=>request("/manufacturing-orders/"+fval(f,"orderId")+"/downtime","POST",{
  operationId:fval(f,"operationId"),reason:fval(f,"reason"),durationMinutes:numeric(f,"duration")
 }));
 $("#show-history").onclick=async()=>{
  const id=$("#history-order").value;if(!id){flash("یک دستور انتخاب کنید.",true);return;}
  try{
   const data=await request("/manufacturing-orders/"+id+"/history");
   table("#events-table",[
    ["زمان",x=>new Date(x.atUtc).toLocaleString("fa-IR-u-ca-persian")],
    ["رویداد","action"],["شرح",x=>x.details||"—"]
   ],data.events);
   table("#stops-table",[
    ["زمان",x=>new Date(x.occurredUtc).toLocaleString("fa-IR-u-ca-persian")],
    ["علت","reason"],["مدت (دقیقه)",x=>num(x.durationMinutes)]
   ],data.downtime);
  }catch(e){flash(e.message,true);}
 };
 reload().catch(e=>flash("داده‌های توسعه بارگذاری نشد: "+e.message,true));
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
})();
