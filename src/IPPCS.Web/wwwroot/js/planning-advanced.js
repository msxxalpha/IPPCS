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
 if(!id){table("#material-table",[["قطعه","componentItemId"],["مصرف","recordedConsumed"]],[]);return;}
 const data=await api("/factory-material/"+id);
 fill($("#material-component"),data.rows.filter(x=>x.plannedRequired>0),
  x=>item(x.componentItemId)+" | برنامه: "+format(x.plannedRequired),x=>x.componentItemId);
 table("#material-table",[["جزء",x=>item(x.componentItemId)],
  ["استاندارد",x=>format(x.plannedRequired)],["ثبت‌شده",x=>format(x.recordedConsumed)],
  ["انحراف",x=>format(x.variance)]],data.rows);
}
function register(){
 $$("nav [data-view]").forEach(btn=>btn.onclick=()=>{
  $$("section.page").forEach(section=>section.hidden=section.id!==btn.dataset.view);
  $$("nav [data-view]").forEach(b=>b.classList.toggle("nav-active",b===btn));
  $("#page-heading").textContent=btn.textContent;$("#flash").hidden=true;
  if(btn.dataset.view==="dashboard")updateOverview().catch(e=>flash(e.message,true));
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
 addForm("#aps-form",async f=>{
  const result=await api("/factory-aps/calculate","POST",{
   mpsVersionId:formval(f,"mpsId"),startJalali:formval(f,"start"),
   horizonDays:number(f,"horizon"),capacityMultiplier:number(f,"capacity")
  });
  renderAps(result);
  flash("زمان‌بندی محدودیت‌دار ثبت شد. عملیات فاقد ظرفیت به‌صورت جداگانه نمایش داده می‌شوند.");
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
    ["نرخ تعریف‌شده",x=>x.hasCostRate?"بله":"خیر"],
    ["هزینه تبدیل (ریال)",x=>format(x.conversionRials)],
    ["هزینه ضایعات (ریال)",x=>format(x.scrapRials)]],data.rows);
   flash("برآورد تبدیل: "+format(data.totalConversionRials)+" ریال، برآورد ضایعات: "+
    format(data.totalScrapRials)+" ریال. مراکز بدون نرخ: "+data.missingRates);
  }catch(e){flash(e.message,true);}
 };
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