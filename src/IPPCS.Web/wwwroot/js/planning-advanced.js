(()=>{
"use strict";
const $=x=>document.querySelector(x),$$=x=>[...document.querySelectorAll(x)];
const state={items:[],mps:[],centers:[],orders:[],demand:[],sop:[],runs:[]};
const formval=(f,k)=>new FormData(f).get(k),number=(f,k)=>Number(IPPCSJalali.toLatin(formval(f,k)));
const format=n=>Number(n).toLocaleString("fa-IR",{maximumFractionDigits:3});
const item=id=>state.items.find(x=>x.id===id)?.name||String(id).slice(0,8);
const center=id=>state.centers.find(x=>x.id===id)?.name||String(id).slice(0,8);
function flash(msg,error=false){const el=$("#flash");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=msg;el.scrollIntoView({behavior:"smooth",block:"nearest"});}
async function api(path,method="GET",payload){
 const config={method,headers:{}};
 if(payload!==undefined){config.headers["Content-Type"]="application/json";config.body=JSON.stringify(payload);}
 const res=await IPPCSSecurity.secureFetch("/api/v1"+path,config);
 const text=await res.text();let data;
 try{data=text?JSON.parse(text):null;}catch{data=null;}
 if(!res.ok)throw new Error(data?.message||data?.title||data?.detail||text||"خطای سرور");
 return data;
}
function fill(select,rows,label,getId=x=>x.id){
 const previous=select.value;select.replaceChildren();
 const none=document.createElement("option");none.textContent="انتخاب کنید";none.value="";select.append(none);
 for(const row of rows){const o=document.createElement("option");o.value=getId(row);o.textContent=label(row);select.append(o);}
 if([...select.options].some(o=>o.value===previous))select.value=previous;
}
function table(id,columns,records,action=null){
 const host=$(id);host.replaceChildren();
 const wrap=document.createElement("div");wrap.className="table-wrap";
 const table=document.createElement("table"),head=document.createElement("thead"),top=document.createElement("tr");
 columns.forEach(([name])=>{const th=document.createElement("th");th.textContent=name;top.append(th);});
 if(action){const th=document.createElement("th");th.textContent="عملیات";top.append(th);}
 head.append(top);table.append(head);const body=document.createElement("tbody");
 if(!records.length){const tr=document.createElement("tr"),td=document.createElement("td");td.className="empty";td.colSpan=columns.length+(action?1:0);td.textContent="اطلاعاتی ثبت نشده است.";tr.append(td);body.append(tr);}
 for(const row of records){const tr=document.createElement("tr");
  columns.forEach(([,getter])=>{const td=document.createElement("td");let val=typeof getter==="function"?getter(row):row[getter];td.textContent=val??"—";tr.append(td);});
  if(action){const td=document.createElement("td");
   if(!action.disabled?.(row)){const btn=document.createElement("button");btn.className="btn-outline";btn.type="button";btn.textContent=action.label;btn.onclick=async()=>{
    btn.disabled=true;try{await action.run(row);}catch(e){flash(e.message,true);}finally{btn.disabled=false;}
   };td.append(btn);}tr.append(td);}
  body.append(tr);
 }
 table.append(body);wrap.append(table);host.append(wrap);
}
function showResources(){
 $$("[data-finished]").forEach(el=>fill(el,state.items.filter(x=>x.type===2),x=>x.code+" | "+x.name));
 $$("[data-mps]").forEach(el=>fill(el,state.mps.filter(x=>x.state===1),x=>x.code));
 $$("[data-all-mps]").forEach(el=>fill(el,state.mps,x=>x.code));
 fill($("#material-order"),state.orders.filter(x=>x.status===1||x.status===2),x=>x.number);
 fill($("#cost-order"),state.orders,x=>x.number);
 fill($("#cost-center"),state.centers,x=>x.code+" | "+x.name);
 fill($("#aps-blackout-center"),state.centers,x=>x.code+" | "+x.name);
 fill($("#maintenance-center"),state.centers,x=>x.code+" | "+x.name);
 fill($("#sop-bridge-sop"),state.sop,x=>x.code+" | "+x.fromJalali+" تا "+x.throughJalali);
 fill($("#demand-mps-target"),state.mps.filter(x=>x.state===0),x=>x.code);
 fill($("#demand-target"),state.demand,x=>item(x.itemId)+" • "+x.reference+" • "+x.dueOnJalali);
 table("#demand-table",[["محصول",x=>item(x.itemId)],["مرجع","reference"],
  ["نوع",x=>({Forecast:"پیش‌بینی",Confirmed:"نیاز قطعی داخلی",SafetyBuild:"ساخت اطمینان"})[x.category]||x.category],
  ["موعد","dueOnJalali"],["مقدار",x=>format(x.quantity)]],state.demand,{
  label:"حذف",run:async row=>{
   if(!window.confirm("آیا تقاضای برنامه‌ای حذف شود؟"))return;
   await api("/factory-demand/"+row.id,"DELETE");flash("تقاضا حذف شد.");await load();
  }
 });
 table("#sop-table",[["شناسه","code"],["از","fromJalali"],["تا","throughJalali"],
  ["نیاز",x=>format(x.demandQuantity)],["بار (دقیقه)",x=>format(x.roughCutMinutes)],
  ["ظرفیت (دقیقه)",x=>format(x.availableMinutes)],["اضافه‌بار",x=>format(x.overloadMinutes)]],state.sop);
 table("#aps-runs",[["تاریخ آغاز","startJalali"],["افق (روز)","horizonDays"],
  ["نسخه","algorithm"],["زمان محاسبه",x=>new Date(x.calculatedUtc).toLocaleString("fa-IR-u-ca-persian")]],state.runs);
}
async function load(){
 [state.items,state.mps,state.centers,state.orders,state.demand,state.sop,state.runs]=await Promise.all([
  api("/items"),api("/mps"),api("/work-centers"),api("/manufacturing-orders"),
  api("/factory-demand"),api("/factory-sop"),api("/factory-aps/runs")]);
 showResources();
}
function addForm(id,callback){
 const form=$(id);
 form.addEventListener("submit",async e=>{
  e.preventDefault();if(!form.reportValidity())return;
  const submit=form.querySelector('[type="submit"]');submit.disabled=true;
  try{await callback(form);flash("درخواست با موفقیت انجام شد.");form.reset();await load();}
  catch(error){flash(error.message,true);}finally{submit.disabled=false;}
 });
}
function renderGantt(allocations){
 const host=$("#aps-gantt");host.replaceChildren();
 if(!allocations.length){host.textContent="عملیاتی در این افق قابل زمان‌بندی نیست.";return;}
 const days=new Map();
 for(const row of allocations){
  const key=row.startJalali+"|"+row.workCenterId;
  if(!days.has(key))days.set(key,[]);
  days.get(key).push(row);
 }
 const wrap=document.createElement("div");wrap.style.cssText="display:grid;gap:12px;min-width:700px";
 [...days.entries()].sort((a,b)=>a[0].localeCompare(b[0])).forEach(([key,rows])=>{
  const container=document.createElement("div");container.style.cssText="display:grid;grid-template-columns:190px 1fr;gap:12px;align-items:center";
  const title=document.createElement("div");title.textContent=rows[0].startJalali+" • "+center(rows[0].workCenterId);
  title.style.fontSize="12px";
  const track=document.createElement("div");track.style.cssText="position:relative;height:43px;border:1px solid #cbd5e1;border-radius:8px;background:repeating-linear-gradient(to left,#f1f5f9 0,#f1f5f9 calc(100% / 24 - 1px),#d5e1ea calc(100% / 24 - 1px),#d5e1ea calc(100% / 24))";
  rows.forEach(row=>{
   const minutes=t=>{const [h,m]=t.split(":").map(Number);return h*60+m;};
   const start=minutes(row.startTime),finish=minutes(row.finishTime);
   const bar=document.createElement("div");bar.title=row.operationName+" — "+row.startTime+" تا "+row.finishTime;
   bar.textContent=row.sequence+"";
   bar.style.cssText="position:absolute;top:5px;height:31px;display:flex;align-items:center;justify-content:center;border-radius:5px;font-size:10px;overflow:hidden;white-space:nowrap;color:#fff;background:"+(row.late?"#b45309":"#1f567e")+";left:"+(start/1440*100)+"%;width:"+Math.max(0,(finish-start)/1440*100)+"%";
   track.append(bar);
  });
  container.append(title,track);wrap.append(container);
 });
 host.append(wrap);host.style.overflowX="auto";
}
function renderAps(data){
 renderGantt(data.allocations);
 table("#aps-table",[["کالا",x=>item(x.itemId)],["عملیات","sequence"],["شرح","operationName"],
  ["مرکز کاری",x=>center(x.workCenterId)],["شروع","startJalali"],["ساعت شروع","startTime"],
  ["پایان","finishJalali"],["ساعت پایان","finishTime"],["دقیقه",x=>format(x.durationMinutes)],
  ["دیرکرد",x=>x.late?"دارد":"ندارد"]],data.allocations);
 table("#aps-errors",[["محصول",x=>item(x.itemId)],["شماره مرحله","sequence"],["علت","reason"]],data.unscheduled);
}
async function reloadMaterial(){
 const id=$("#material-order").value;
 if(!id){
  table("#material-table",[["قطعه","componentItemId"],["مصرف","recordedConsumed"]],[]);
  table("#material-reconciliation",[["قطعه","componentItemId"]],[]);
  table("#material-evidence-ledger",[["قطعه","componentItemId"]],[]);
  table("#material-lot-balances",[["قطعه","componentItemId"]],[]);
  fill($("#material-lot"),[],x=>x.lotCode);
  fill($("#material-reversal-entry"),[],x=>x.entryId);
  return;
 }
 const data=await api("/factory-material/"+id);
 fill($("#material-component"),data.rows.filter(x=>x.plannedRequired>0),
  x=>item(x.componentItemId)+" | برنامه: "+format(x.plannedRequired),x=>x.componentItemId);
 table("#material-table",[["جزء",x=>item(x.componentItemId)],
  ["استاندارد",x=>format(x.plannedRequired)],["ثبت‌شده",x=>format(x.recordedConsumed)],
  ["انحراف",x=>format(x.variance)]],data.rows);
 const actual=await api("/factory-material/"+id+"/execution-reconciliation");
 const fmt=x=>x===null||x===undefined?"نامشخص":format(x);
 table("#material-reconciliation",[
  ["قطعه",x=>item(x.componentItemId)],
  ["مقدار برنامه",x=>fmt(x.standardForPlannedQuantity)],
  ["خروجی ثبت‌شده آخرین مرحله",x=>fmt(x.reportedFinalOperationQuantity)],
  ["استاندارد برای خروجی واقعی",x=>fmt(x.standardForReportedOutput)],
  ["مصرف ثبت‌شده",x=>fmt(x.actualLoggedConsumption)],
  ["اختلاف مصرف",x=>fmt(x.outputBasedVariance)],
  ["درصد اختلاف",x=>x.outputBasedVariancePercent===null?"نامشخص":fmt(x.outputBasedVariancePercent)+"٪"],
  ["دفعات ثبت مصرف","recordedEntries"]
 ],actual.rows);
 const lots=await api("/manufacturing-orders/"+id+"/confirmations");
 fill($("#material-lot"),lots.filter(x=>x.goodQuantity+x.scrapQuantity+x.reworkQuantity>0),
   x=>(x.lotCode||"بدون کد")+" | گزارش "+x.operationSequence+
     " | سالم "+format(x.goodQuantity));
 fill($("#material-lot-component"),data.rows.filter(x=>x.plannedRequired>0),
   x=>item(x.componentItemId)+" | استاندارد "+format(x.plannedRequired),
   x=>x.componentItemId);
 const evidence=await api("/factory-material/"+id+"/evidence-ledger");
 fill($("#material-reversal-entry"),evidence.rows.filter(x=>x.netQuantity>0),
   x=>item(x.componentItemId)+" | "+format(x.recordedQuantity)+
      (x.confirmationId?" / دارای بچ":" / قدیمی بدون بچ")+" | "+x.entryId.slice(0,8),
   x=>x.entryId);
 table("#material-evidence-ledger",[
  ["شناسه سند",x=>x.entryId.slice(0,8)],
  ["جزء",x=>item(x.componentItemId)],
  ["بچ",x=>x.confirmationId?x.confirmationId.slice(0,8):"نامشخص"],
  ["مقدار اولیه",x=>format(x.recordedQuantity)],
  ["مانده خالص",x=>format(x.netQuantity)],
  ["سند اصلاح",x=>x.reversedById?.slice(0,8)||"—"],
  ["مرجع","reference"],["ثبت‌کننده","recordedBy"]
 ],evidence.rows);
}
function register(){
 $$("nav [data-view]").forEach(btn=>btn.onclick=()=>{
  $$("section.page").forEach(section=>section.hidden=section.id!==btn.dataset.view);
  $$("nav [data-view]").forEach(b=>b.classList.toggle("nav-active",b===btn));
  $("#page-heading").textContent=btn.textContent;$("#flash").hidden=true;
  if(btn.dataset.view==="dashboard")updateOverview().catch(e=>flash(e.message,true));
 });
 addForm("#forecast-form",async f=>{
  const id=formval(f,"itemId");
  const query="/factory-demand/forecast/"+id+"?fromJalali="+encodeURIComponent(formval(f,"from"))+
   "&lookback="+encodeURIComponent(number(f,"lookback"))+
   "&months="+encodeURIComponent(number(f,"months"));
  const result=await api(query);
  table("#forecast-table",[["سال شمسی","jalaliYear"],["ماه شمسی","jalaliMonth"],
   ["مقدار پیشنهادی",x=>format(x.forecastQuantity)]],result.rows);
  flash("پیش‌بینی صرفاً بر اساس میانگین نیازهای قطعی ثبت‌شده است؛ تعداد سوابق: "+result.historySamples);
 });
 addForm("#forecast-backtest-form",async f=>{
  const result=await api("/factory-demand/backtest/"+formval(f,"itemId")+
    "?throughJalali="+encodeURIComponent(formval(f,"through"))+
    "&lookback="+encodeURIComponent(number(f,"lookback"))+
    "&months="+encodeURIComponent(number(f,"months")));
  const pct=x=>x===null||x===undefined?"نامشخص":format(x)+"٪";
  table("#backtest-summary",[["شاخص","name"],["نتیجه","value"]],[
    {name:"میانگین قدر مطلق خطا MAE",value:format(result.meanAbsoluteError)},
    {name:"خطای وزنی WAPE",value:pct(result.wapePercent)},
    {name:"سوگیری پیش‌بینی",value:format(result.bias)},
    {name:"ماه‌های دارای سابقه کافی",value:format(result.monthsWithHistory)+
        " / "+format(result.monthsEvaluated)}
  ]);
  table("#backtest-table",[["سال شمسی","jalaliYear"],["ماه شمسی","jalaliMonth"],
    ["پیش‌بینی",x=>format(x.predicted)],["تقاضای واقعی",x=>format(x.actual)],
    ["خطا",x=>format(x.error)],["خطای قدر مطلق",x=>format(x.absoluteError)],
    ["خطای درصدی",x=>pct(x.absolutePercentageError)],
    ["ماه‌های دارای تقاضای ثبت‌شده قبل",x=>format(x.priorConfirmedMonths)]],result.months);
  flash(result.warning||"بازآزمایی تاریخی فقط از تقاضای قطعی ثبت‌شده در IPPCS محاسبه شد.");
 });
 addForm("#demand-form",f=>api("/factory-demand","POST",{
  itemId:formval(f,"itemId"),reference:formval(f,"reference"),category:formval(f,"category"),
  dueOnJalali:formval(f,"due"),quantity:number(f,"quantity")
 }));
 $("#demand-transfer").onclick=async()=>{
  const id=$("#demand-target").value,version=$("#demand-mps-target").value;
  if(!id||!version){flash("تقاضا و MPS پیش‌نویس هدف را انتخاب کنید.",true);return;}
  const btn=$("#demand-transfer");btn.disabled=true;
  try{await api("/factory-demand/"+id+"/to-mps/"+version,"POST");
   flash("تقاضای انتخاب‌شده به MPS پیش‌نویس منتقل شد.");await load();
  }catch(e){flash(e.message,true);}finally{btn.disabled=false;}
 };
 addForm("#sop-form",async f=>{
  const data=await api("/factory-sop/calculate","POST",{
   code:formval(f,"code"),fromJalali:formval(f,"from"),throughJalali:formval(f,"through")
  });
  table("#sop-centers",[["مرکز کاری",x=>center(x.workCenterId)],
   ["بار (دقیقه)",x=>format(x.requiredMinutes)],
   ["ظرفیت (دقیقه)",x=>format(x.availableMinutes)],
   ["اضافه‌بار",x=>format(x.overloadMinutes)]],data.byCenter||[]);
  flash("S&OP: بار "+format(data.roughCutMinutes)+" دقیقه و ظرفیت "+
   format(data.availableMinutes)+" دقیقه. "+(data.warnings?.join("؛ ")||""));
 });
 const apsBlackouts=[];
 function renderBlackouts(){
  const holder=$("#aps-blackout-list");holder.replaceChildren();
  apsBlackouts.forEach((block,i)=>{
   const row=document.createElement("div");row.className="row";row.style.padding="6px 0";
   const text=document.createElement("span");text.textContent=block.dateJalali+" | "+block.reason;
   const button=document.createElement("button");button.className="btn-outline";button.type="button";button.textContent="حذف";
   button.onclick=()=>{apsBlackouts.splice(i,1);renderBlackouts();};
   row.append(text,button);holder.append(row);
  });
 }
 $("#aps-add-blackout").onclick=()=>{
  const id=$("#aps-blackout-center").value,date=$("#aps-blackout-date").value,reason=$("#aps-blackout-reason").value.trim();
  if(!id||!IPPCSJalali.parse(date)||!reason){flash("مرکز، تاریخ شمسی و علت روز عدم‌دسترسی اجباری‌اند.",true);return;}
  if(apsBlackouts.some(x=>x.workCenterId===id&&x.dateJalali===date)){
   flash("این روز برای مرکز کاری قبلاً ثبت شده است.",true);return;
  }
  apsBlackouts.push({workCenterId:id,dateJalali:date,reason});
  $("#aps-blackout-date").value="";$("#aps-blackout-reason").value="";
  renderBlackouts();
 };
 addForm("#aps-form",async f=>{
  const result=await api("/factory-aps/calculate","POST",{
   mpsVersionId:formval(f,"mpsId"),startJalali:formval(f,"start"),
   horizonDays:number(f,"horizon"),capacityMultiplier:number(f,"capacity"),unavailableDays:apsBlackouts
  });
  renderAps(result);
  flash("زمان‌بندی محدودیت‌دار ثبت شد. عملیات فاقد ظرفیت به‌صورت جداگانه نمایش داده می‌شوند.");
 });
 addForm("#sop-mps-bridge-form",async f=>{
  const x=await api("/factory-sop/"+formval(f,"sopId")+"/reconcile-mps/"+formval(f,"mpsId"));
  table("#sop-mps-bridge-table",[["شاخص","name"],["مقدار","value"]],[
   {name:"مجموع تقاضای Snapshot تاریخی S&OP",value:format(x.historicalSopDemand)},
   {name:"مجموع تقاضای فعلی در همان بازه",value:format(x.currentScopedDemand)},
   {name:"مقدار مصوب MPS در بازه",value:format(x.approvedMpsQuantity)},
   {name:"فاصله MPS تا S&OP",value:format(x.differenceToSop)},
   {name:"فاصله MPS تا تقاضای فعلی",value:format(x.differenceToCurrentDemand)},
   {name:"تعداد ردیف‌های MPS",value:x.inWindowMpsLines},
   {name:"تغییر مقدار تقاضا از تصویر تاریخی",value:x.demandSnapshotChanged?"بله":"خیر"}
  ]);
  flash(x.confidenceNote);
 });
 addForm("#whatif-form",async f=>{
  const data=await api("/factory-whatif/capacity","POST",{
   mpsVersionId:formval(f,"mpsId"),startJalali:formval(f,"start"),
   horizonDays:number(f,"horizon"),baselineCapacity:number(f,"baseline"),
   scenarioCapacity:number(f,"scenario")
  });
  table("#whatif-table",[["وضعیت","name"],["قابل زمان‌بندی",x=>format(x.scheduled)],
   ["خارج از ظرفیت",x=>format(x.unscheduled)],["عملیات دارای دیرکرد",x=>format(x.late)]],
   [{name:"پایه",...data.baseline},{name:"سناریو",...data.scenario}]);
 });
 $("#material-order").onchange=()=>reloadMaterial().catch(e=>flash(e.message,true));
 async function reviewMaterialLot(){
  const order=$("#material-order").value,lot=$("#material-lot").value;
  if(!order||!lot)throw Error("دستور و بچ دارای عملکرد را انتخاب کنید.");
  const data=await api("/factory-material/"+order+"/lot/"+lot);
  table("#material-lot-balances",[
   ["جزء",x=>item(x.componentItemId)],
   ["استاندارد برای بچ",x=>format(x.standardForReportedLot)],
   ["مصرف خالص",x=>format(x.recordedNetQuantity)],
   ["اختلاف",x=>format(x.variance)],
   ["سند فعال","activeEntries"]
  ],data.rows);
 }
 $("#material-lot-review").onclick=()=>reviewMaterialLot().catch(e=>flash(e.message,true));
 $("#material-lot").onchange=()=>reviewMaterialLot().catch(e=>flash(e.message,true));
 addForm("#material-lot-form",async f=>{
  const order=$("#material-order").value;
  if(!order)throw Error("دستور فعال مصرف را انتخاب کنید.");
  await api("/factory-material/"+order+"/lot-consumption","POST",{
   confirmationId:formval(f,"confirmationId"),
   componentItemId:formval(f,"componentItemId"),
   quantity:number(f,"quantity"),reference:formval(f,"reference")
  });
  await reloadMaterial();
 });
 addForm("#material-reversal-form",async f=>{
  const order=$("#material-order").value;
  if(!order)throw Error("دستور دارای مصرف را انتخاب کنید.");
  if(!window.confirm("این اصلاح سند قبلی را حذف نمی‌کند و مقدار آن را با سند معکوس صفر می‌کند. ادامه می‌دهید؟"))
    throw Error("اصلاح لغو شد.");
  await api("/factory-material/"+order+"/consumption/"+formval(f,"entryId")+"/reverse",
   "POST",{reason:formval(f,"reason")});
  await reloadMaterial();
 });
 addForm("#material-form",async f=>{
  await api("/factory-material/"+formval(f,"orderId")+"/consumption","POST",{
   componentItemId:formval(f,"componentItemId"),quantity:number(f,"quantity"),
   reference:formval(f,"reference")
  });await reloadMaterial();
 });
 addForm("#cost-form",f=>api("/factory-cost/rates","PUT",{
  workCenterId:formval(f,"center"),conversionRialsPerMinute:number(f,"rate"),
  scrapRialsPerUnit:number(f,"scrap"),effectiveFromJalali:formval(f,"from"),
  note:formval(f,"note")||null
 }));
 $("#cost-review").onclick=async()=>{
  const id=$("#cost-order").value;if(!id){flash("دستور تولید را انتخاب کنید.",true);return;}
  try{const data=await api("/factory-cost/orders/"+id);
   table("#cost-table",[["عملیات","name"],["دقیقه تخمینی",x=>format(x.estimateTime)],
    ["دقیقه استاندارد عملکرد",x=>format(x.observedTime)],
    ["نرخ جاری تعریف‌شده",x=>x.hasCostRate?"بله":"خیر"],
    ["هزینه تبدیل (ریال)",x=>format(x.conversionRials)],
    ["هزینه ضایعات (ریال)",x=>format(x.scrapRials)]],data.rows);
   const [measured,rateHistory]=await Promise.all([
     api("/factory-cost/orders/"+id+"/actual-variance"),
     api("/factory-cost/orders/"+id+"/rate-snapshot")
   ]);
   const money=x=>x===null||x===undefined?"نامشخص":format(x);
   table("#cost-rate-snapshots",[
      ["عملیات",x=>x.operationId.slice(0,8)],
      ["مرکز",x=>center(x.workCenterId)],
      ["نرخ منجمد (ریال/دقیقه)",x=>money(x.conversionRialsPerMinute)],
      ["ضایعات (ریال/واحد)",x=>money(x.scrapRialsPerUnit)],
      ["تاریخ مبنای نرخ",x=>x.effectiveFrom?
        new Date(x.effectiveFrom+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"}):"نامشخص"]
   ],rateHistory.rates);

   table("#cost-variance-summary",[["شاخص","name"],["مقدار","value"]],[
     {name:"هزینه استاندارد (ریال)",value:money(measured.totals.standardConversionRials)},
     {name:"هزینه زمان واقعی (ریال)",value:money(measured.totals.actualConversionRials)},
     {name:"انحراف تبدیل (ریال)",value:money(measured.totals.conversionVarianceRials)},
     {name:"مراکز بدون نرخ معتبر",value:format(measured.totals.countMissingRates)},
     {name:"عملیات بدون ساعت واقعی بسته‌شده",value:format(measured.totals.countMissingActualTime)}
   ]);
   table("#cost-variance-table",[["ردیف عملیات","sequence"],
     ["مرکز کاری",x=>center(x.workCenterId)],
     ["دقیقه استاندارد",x=>format(x.standardMinutes)],
     ["دقیقه واقعی",x=>money(x.actualMinutes)],
     ["انحراف دقیقه",x=>money(x.minutesVariance)],
     ["هزینه استاندارد (ریال)",x=>money(x.standardConversionRials)],
     ["هزینه واقعی (ریال)",x=>money(x.actualConversionRials)],
     ["انحراف هزینه (ریال)",x=>money(x.conversionVarianceRials)],
     ["ساعت باز",x=>x.hasOpenClock?"بله":"خیر"]],measured.rows);
   if(!rateHistory.isHistoricalSnapshot)
     flash("این دستور سند نرخ منجمد ندارد؛ نرخ جاری، هزینه تاریخی قطعی نیست.",true);
   else if(rateHistory.missingRates>0)
     flash("بعضی نرخ‌ها هنگام آزادسازی نامشخص بوده‌اند؛ نباید با نرخ جدید تکمیل شوند.",true);
   else flash("مبنای هزینه تبدیل هنگام آزادسازی دستور منجمد شده است.");
   // The first estimate table uses today's center standard rates. The
   // historical actual-variance table above uses frozen per-order evidence.
  }catch(e){flash(e.message,true);}
 };
 addForm("#production-alert-form",async f=>{
  const d=await api("/factory-kpis/production-alerts?asOfJalali="+
    encodeURIComponent(formval(f,"date"))+"&daysAhead="+number(f,"days"));
  table("#production-alert-totals",[["وضعیت","name"],["تعداد","count"]],[
    {name:"بحرانی؛ قرنطینه باز",count:format(d.criticalCount)},
    {name:"مهم؛ دیرکرد",count:format(d.highCount)},
    {name:"نزدیک به سررسید یا بدون اقدام",count:format(d.mediumCount)}
  ]);
  const kinds={QualityHold:"قرنطینه باز",OrderOverdue:"دستور معوق",
    OrderDueSoon:"سررسید نزدیک",DeviationAction:"اقدام اصلاحی"};
  const levels={Critical:"بحرانی",High:"مهم",Medium:"پیگیری"};
  table("#production-alert-table",[
    ["نوع",x=>kinds[x.type]||x.type],
    ["اهمیت",x=>levels[x.severity]||x.severity],
    ["شناسه","identifier"],
    ["تاریخ موعد",x=>x.dueDate?new Date(x.dueDate+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"}):"نامشخص"],
    ["روز دیرکرد",x=>x.daysOverdue===null?"—":format(x.daysOverdue)],
    ["شرح","message"]
  ],d.alerts,{
   label:"پیگیری پرونده",
   disabled:x=>x.type!=="DeviationAction",
   run:x=>{window.location.assign("/production-deviations.html?case="+
     encodeURIComponent(x.referenceId));}
  });
  flash(d.disclaimer);
 });
 addForm("#maintenance-whatif-form",async f=>{
   const centerId=formval(f,"center"),day=formval(f,"blackout");
   const reason=String(formval(f,"reason")||"").trim();
   if((centerId||day||reason)&&(!centerId||!day||!reason))
    throw Error("برای سناریوی تعمیرات، مرکز کاری، تاریخ شمسی و علت همگی لازم‌اند.");
   const unavailable=centerId?[{workCenterId:centerId,dateJalali:day,reason}]:[];
   const report=await api("/factory-whatif/capacity-maintenance","POST",{
    mpsVersionId:formval(f,"mpsId"),startJalali:formval(f,"start"),
    horizonDays:number(f,"horizon"),baselineCapacity:number(f,"baseline"),
    scenarioCapacity:number(f,"scenario"),scenarioUnavailableDays:unavailable
   });
   table("#maintenance-whatif-table",[
    ["وضعیت","name"],["تعداد عملیات",x=>format(x.plannedOperations)],
    ["زمان‌بندی‌شده",x=>format(x.scheduledOperations)],
    ["بدون ظرفیت",x=>format(x.unscheduledOperations)],
    ["عملیات دیرکرد",x=>format(x.lateOperations)],
    ["بار تخصیص‌یافته (دقیقه)",x=>format(x.allocatedMinutes)]
   ],[{name:"پایه",...report.baseline},{name:"تغییر ظرفیت و تعمیرات",...report.scenario}]);
   table("#maintenance-whatif-errors",[
    ["کالا",x=>item(x.itemId)],["عملیات","sequence"],["علت","reason"]
   ],report.scenarioUnscheduled);
   flash(report.disclaimer);
 });
 addForm("#mps-audit-form",async f=>{
  const r=await api("/mps/"+formval(f,"mpsId")+"/demand-audit");
  table("#mps-audit-summary",[["شاخص","name"],["مقدار","value"]],[
   {name:"نیاز قطعی اقلام در بازه MPS",value:format(r.confirmedDemandQuantity)},
   {name:"مقدار فاقد ارجاع کامل به تقاضا",value:format(r.confirmedUncoveredQuantity)},
   {name:"مقدار ردیف‌های دستی غیرمرجع‌دار",value:format(r.otherMpsQuantity)},
   {name:"تعداد ارجاع نامعتبر",value:r.unmatchedReferenceLines.length}
  ]);
  table("#mps-audit-lines",[
   ["کالا",x=>item(x.itemId)],["مرجع","reference"],
   ["موعد",x=>new Date(x.neededOn+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
   ["قطعی",x=>format(x.confirmedQuantity)],
   ["ارجاع صریح",x=>format(x.exactlyReferencedQuantity)],
   ["بدون ارجاع",x=>format(x.uncoveredQuantity)],
   ["ارجاع با مقدار یا تاریخ متفاوت",x=>x.referenceChanged?"بله":"خیر"]
  ],r.demandGaps);
  flash(r.disclaimer);
 });
 addForm("#mrp-risk-form",async f=>{
  const r=await api("/mrp/"+formval(f,"mpsId")+"/assumption-risks");
  table("#mrp-risk-summary",[["شاخص","name"],["مقدار","value"]],[
   {name:"بچ پیشنهادی ساخت",value:r.proposedManufacturingLots},
   {name:"شروع پیشنهاد قبل از تاریخ مبنا",value:r.releaseBeforeBaselineLots}
  ]);
  table("#mrp-risk-lines",[
   ["کالا",x=>item(x.itemId)],["نیاز ناخالص",x=>format(x.grossRequirement)],
   ["تخصیص فرضی موجودی مبنا",x=>format(x.assumedOpening)],
   ["دریافت برنامه‌ای",x=>format(x.assumedExpectedReceipt)],
   ["عرضه صرفاً پیشنهادی",x=>format(x.proposedReceipt)],
   ["مانده تخصیص‌نیافته پیشنهاد",x=>format(x.unallocatedProposalQuantity)],
   ["شروع قبل از مبنا",x=>x.pastReleaseLots],
   ["پوشش فرضی",x=>x.assumedCoveragePercent===null?"نامشخص":format(x.assumedCoveragePercent)+"٪"]
  ],r.rows);
  flash(r.disclaimer);
 });
 addForm("#crp-stress-form",async f=>{
  const r=await api("/crp/"+formval(f,"mpsId")+"/center-stress");
  table("#crp-stress-table",[
   ["مرکز",x=>center(x.workCenterId)],
   ["بار (دقیقه)",x=>format(x.requiredMinutes)],
   ["ظرفیت (دقیقه)",x=>format(x.availableMinutes)],
   ["اضافه‌بار (دقیقه)",x=>format(x.overloadMinutes)],
   ["نرخ اشباع",x=>x.utilizationPercent===null?"نامشخص":format(x.utilizationPercent)+"٪"],
   ["روزهای اضافه‌بار","overloadedDays"],
   ["روزهای بدون شیفت","daysWithoutCapacity"],
   ["اولین اضافه‌بار",x=>x.firstOverload?new Date(x.firstOverload+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"}):"—"]
  ],r.centers);
  flash(r.disclaimer);
 });
 addForm("#mps-peg-adherence-form",async f=>{
  const result=await api("/factory-kpis/peg-adherence/"+formval(f,"mpsId"));
  table("#peg-adherence-summary",[["شاخص","name"],["مقدار","value"]],[
    {name:"نیاز کل MPS",value:format(result.totals.required)},
    {name:"مقدار تخصیص‌یافته",value:format(result.totals.assigned)},
    {name:"تولید سالم تکمیل‌شده",value:format(result.totals.completed)},
    {name:"تولید سالم به‌موقع",value:format(result.totals.onTime)},
    {name:"تولید سالم دیرهنگام",value:format(result.totals.late)},
    {name:"کسری تحقق برنامه",value:format(result.totals.shortage)}
  ]);
  table("#peg-adherence-table",[
    ["کالا",x=>item(x.itemId)],["نیاز شمسی","neededOnJalali"],
    ["ردیف MPS",x=>x.lineId.slice(0,8)],
    ["نیاز",x=>format(x.required)],["تخصیص",x=>format(x.assigned)],
    ["سالم نهایی",x=>format(x.completedGood)],
    ["به‌موقع",x=>format(x.onTimeGood)],["دیرهنگام",x=>format(x.lateGood)],
    ["تاریخ پایان نامشخص",x=>format(x.undatedGood)],
    ["در انتظار تکمیل",x=>format(x.pendingAssigned)],
    ["تخصیص‌نیافته",x=>format(x.unassigned)],
    ["کسری",x=>format(x.shortage)],
    ["تحقق",x=>x.fulfilmentPercent===null?"نامشخص":format(x.fulfilmentPercent)+"٪"]
  ],result.rows);
  flash("گزارش براساس ارتباط صریح دستورهای تولید با ردیف‌های MPS مصوب محاسبه شد.");
 });
 addForm("#adherence-form",async f=>{
  const data=await api("/factory-kpis/adherence/"+formval(f,"mpsId")+
   "?fromJalali="+encodeURIComponent(formval(f,"from"))+
   "&throughJalali="+encodeURIComponent(formval(f,"through")));
  table("#adherence-table",[["محصول",x=>item(x.itemId)],
   ["مقدار MPS",x=>format(x.mpsRequired)],["تولید سالم تکمیل",x=>format(x.completedGood)],
   ["انحراف",x=>format(x.variance)],["تحقق",x=>x.fulfilmentPercent===null?"نامشخص":format(x.fulfilmentPercent)+"٪"]],data.rows);
 });
 load().catch(e=>flash("بارگذاری اطلاعات برنامه‌ریزی ممکن نیست: "+e.message,true));
}
async function updateOverview(){
 const d=await api("/factory-kpis/overview");
 table("#overview-table",[["شاخص","name"],["مقدار","value"]],[
  {name:"دستورات ثبت‌شده",value:format(d.orderCount)},
  {name:"دستورات تکمیل‌شده",value:format(d.completedOrders)},
  {name:"دستورات در حال اجرا",value:format(d.activeOrders)},
  {name:"دستورات معلق",value:format(d.heldOrders)},
  {name:"قرنطینه‌های تعیین‌تکلیف‌نشده",value:format(d.openQualityHolds)},
  {name:"دقایق توقف ثبت‌شده",value:format(d.totalLoggedDowntimeMinutes)},
  {name:"تولید سالم نهایی",value:format(d.finalGoodQuantity)},
  {name:"دستورات بازِ دارای دیرکرد",value:format(d.overdueOpenOrders)}
 ]);
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",register);else register();
})();