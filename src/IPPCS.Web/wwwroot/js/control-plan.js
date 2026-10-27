(()=>{
"use strict";
const $=x=>document.querySelector(x),$$=x=>[...document.querySelectorAll(x)];
const state={routes:[],plans:[],orders:[],requirements:[],confirmations:[]};
const val=(f,key)=>new FormData(f).get(key);
const num=x=>Number(IPPCSJalali.toLatin(x));
const label=x=>String(x??"—");
function alertTo(message,error=false){const el=$("#feedback");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=message;}
async function api(path,method="GET",data){const config={method,headers:{}};
 if(data!==undefined){config.headers["Content-Type"]="application/json";config.body=JSON.stringify(data);}
 const r=await window.IPPCSSecurity.secureFetch("/api/v1"+path,config),raw=await r.text();let body;
 try{body=raw?JSON.parse(raw):null;}catch{body=null;}
 if(!r.ok)throw new Error(body?.message||body?.title||raw||"خطای سرویس");
 return body;
}
function select(el,rows,labelFn){let old=el.value;el.replaceChildren();
 let p=document.createElement("option");p.value="";p.textContent="انتخاب کنید";el.append(p);
 rows.forEach(row=>{let op=document.createElement("option");op.value=row.id;op.textContent=labelFn(row);el.append(op);});
 if([...el.options].some(o=>o.value===old))el.value=old;
}
function table(host,columns,rows,action){const el=$(host);el.replaceChildren();
 const wrap=document.createElement("div");wrap.className="table-wrap";
 const t=document.createElement("table"),h=document.createElement("thead"),tr=document.createElement("tr");
 columns.forEach(([name])=>{let th=document.createElement("th");th.textContent=name;tr.append(th);});
 if(action){let th=document.createElement("th");th.textContent="عملیات";tr.append(th);}
 h.append(tr);t.append(h);const b=document.createElement("tbody");
 if(!rows.length){let tr=document.createElement("tr"),td=document.createElement("td");td.colSpan=columns.length+(action?1:0);td.className="empty";td.textContent="رکوردی برای نمایش موجود نیست.";tr.append(td);b.append(tr);}
 rows.forEach(row=>{let tr=document.createElement("tr");
 columns.forEach(([,get])=>{let td=document.createElement("td");td.textContent=label(typeof get==="function"?get(row):row[get]);tr.append(td);});
 if(action){let td=document.createElement("td");if(!action.disabled(row)){let button=document.createElement("button");button.className="btn-outline";button.type="button";button.textContent=action.label;button.onclick=async()=>{button.disabled=true;try{await action.run(row);}catch(e){alertTo(e.message,true);}finally{button.disabled=false;}};td.append(button);}tr.append(td);}
 b.append(tr);});t.append(b);wrap.append(t);el.append(wrap);
}
async function load(){[state.routes,state.plans,state.orders]=await Promise.all([
 api("/routings"),api("/control-plans"),api("/manufacturing-orders")]);
 select($("#plan-routing"),state.routes.filter(x=>x.state===1),x=>x.revisionCode+" | "+x.itemId.slice(0,8));
 select($("#draft-plan"),state.plans.filter(x=>x.state===0),x=>x.code);
 for(const el of [$("#inspection-order"),$("#link-order")])select(el,state.orders.filter(x=>x.status!==0&&x.status!==5),x=>x.number);
 select($("#assembly-order"),state.orders.filter(x=>x.kind===1&&x.status!==0&&x.status!==5),x=>x.number);
 select($("#semi-source-order"),state.orders.filter(x=>x.kind===0&&x.status===4),x=>x.number);
 select($("#readiness-order"),state.orders.filter(x=>x.status!==0&&x.status!==5),x=>x.number);
 select($("#spc-order"),state.orders.filter(x=>x.status!==0&&x.status!==5),x=>x.number);
 select($("#trace-order"),state.orders.filter(x=>x.status!==0&&x.status!==5),x=>x.number);

 table("#plan-table",[["کد","code"],["مسیر",x=>x.routingRevisionId.slice(0,8)],["شروع","validFromJalali"],
 ["پایان",x=>x.validUntilJalali||"بدون پایان"],["وضعیت",x=>x.state===1?"مصوب":"پیش‌نویس"],
 ["مشخصه‌ها",x=>x.characteristics.map(c=>c.operationSequence+"-"+c.code+" ("+label(c.minimum)+" تا "+label(c.maximum)+")").join("، ")]],
 state.plans,{label:"تأیید",disabled:x=>x.state!==0,run:async x=>{
 await api("/control-plans/"+x.id+"/approve","POST");alertTo("نسخه کنترل پلن تصویب شد.");await load();
 }});
}
async function inspections(){
 const orderId=$("#inspection-order").value;
 if(!orderId)return;
 const [data,history]=await Promise.all([api("/manufacturing-orders/"+orderId+"/inspection-requirements"),
 api("/manufacturing-orders/"+orderId+"/inspection-results")]);
 state.requirements=data.requirements;
 select($("#inspection-requirement"),state.requirements,x=>x.operationSequence+" • "+x.code+" • "+x.description);
 table("#requirement-table",[["عملیات","operation"],["کد","code"],["عنوان","description"],
 ["حداقل",x=>x.minimum??"—"],["حداکثر",x=>x.maximum??"—"],["واحد","unit"],
 ["اجباری",x=>x.mandatory?"بله":"خیر"],["وضعیت",x=>x.lastPassed===null?"ثبت‌نشده":x.lastPassed?"قبول":"مردود"]],state.requirements);
 table("#inspection-history",[["مشخصه","characteristic"],["اندازه",x=>x.value],
 ["نتیجه",x=>x.passed?"قبول":"مردود"],["مقدار قرنطینه","quarantinedQuantity"],
 ["کاربر","recordedBy"],["زمان",x=>new Date(x.recordedUtc).toLocaleString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})]],
 history);
}
async function confirmations(){
 const orderId=$("#link-order").value;if(!orderId)return;
 const order=await api("/manufacturing-orders/"+orderId);
 const hits=await Promise.all(order.operations.map(async op=>{
 const detail=await api("/manufacturing-orders/"+orderId+"/confirmations?operationId="+op.id);
 return detail;
 }));
 state.confirmations=hits.flat();
 for(const el of [$("#link-from"),$("#link-to")])select(el,state.confirmations,
 x=>x.operationSequence+" • "+x.lotCode+" / سالم: "+x.goodQuantity);
 const links=await api("/manufacturing-orders/"+orderId+"/lot-genealogy");
 const lookup=id=>state.confirmations.find(x=>x.id===id);
 table("#genealogy-table",[["مبدا",x=>lookup(x.sourceConfirmationId)?.lotCode||x.sourceConfirmationId.slice(0,8)],
 ["مقصد",x=>lookup(x.destinationConfirmationId)?.lotCode||x.destinationConfirmationId.slice(0,8)],
 ["مقدار",x=>x.quantity],["ثبت‌کننده","linkedBy"]],links);
}
async function assemblyOrigins(){
 const targetId=$("#assembly-order").value;
 const sourceId=$("#semi-source-order").value;
 if(!targetId)throw Error("دستور مونتاژ نهایی انتخاب نشده است.");
 const [targetOrder,details]=await Promise.all([
   api("/manufacturing-orders/"+targetId),
   api("/manufacturing-orders/"+targetId+"/assembly-genealogy")
 ]);
 const lastTarget=[...targetOrder.operations].sort((a,b)=>b.sequence-a.sequence)[0];
 const targets=(await api("/manufacturing-orders/"+targetId+"/confirmations"))
   .filter(x=>x.operationId===lastTarget?.id&&x.goodQuantity>0&&x.lotCode);
 select($("#assembly-destination-confirmation"),targets,
   x=>x.lotCode+" / سالم "+x.goodQuantity);
 if(sourceId){
  const sourceOrder=await api("/manufacturing-orders/"+sourceId);
  const lastSource=[...sourceOrder.operations].sort((a,b)=>b.sequence-a.sequence)[0];
  const sources=(await api("/manufacturing-orders/"+sourceId+"/confirmations"))
    .filter(x=>x.operationId===lastSource?.id&&x.goodQuantity>0&&x.lotCode);
  select($("#assembly-source-confirmation"),sources,
    x=>x.lotCode+" / سالم "+x.goodQuantity);
 }else select($("#assembly-source-confirmation"),[],x=>x.lotCode);
 table("#assembly-coverage",[
  ["کد نیمه‌ساخته",x=>x.componentItemId.slice(0,8)],
  ["نیاز محاسباتی BOM",x=>x.requiredForReportedOutput],
  ["انتساب قطعی شجره",x=>x.linkedProductionQuantity],
  ["مانده بدون شجره",x=>x.missingProductionQuantity],
  ["انتساب مازاد",x=>x.overlinkedProductionQuantity]
 ],details.coverage);
 table("#assembly-links",[
  ["دستور نیمه‌ساخته","sourceOrderNumber"],
  ["بچ نیمه‌ساخته","sourceLot"],["بچ مونتاژ","assemblyLot"],
  ["قطعه",x=>x.componentItemId.slice(0,8)],
  ["مقدار مستند","componentQuantity"],
  ["ثبت",x=>new Date(x.linkedUtc).toLocaleString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})]
 ],details.links);
}
async function readiness(){
 const id=$("#readiness-order").value;
 if(!id)throw Error("دستور تولید انتخاب نشده است.");
 const [report,quality]=await Promise.all([
   api("/manufacturing-orders/"+id+"/production-readiness"),
   api("/manufacturing-orders/"+id+"/inspection-summary")
 ]);
 table("#readiness-steps",[
  ["ترتیب","sequence"],["عملیات","name"],["سالم","good"],
  ["ضایعات","scrap"],["دوباره‌کاری","rework"],
  ["قابل انتقال به مرحله بعد","forwardAvailable"],
  ["قرنطینه","pendingQuality"],
  ["کنترل اجباری","mandatoryCharacteristics"],
  ["اندازه‌گیری ثبت‌نشده","missingMandatory"],
  ["کنترل مردود","failedMandatory"],
  ["تکمیل مرحله",x=>x.isComplete?"بله":"خیر"],
  ["شروع مرحله مجاز",x=>x.canProceed?"بله":"خیر"]
 ],report.rows);
 table("#readiness-inspections",[
  ["مشخصه","code"],["دفعات آزمون","sampleCount"],
  ["قبول","passedSamples"],["مردود","failedSamples"],
  ["آخرین نتیجه",x=>x.lastPassed===null?"ثبت نشده":x.lastPassed?"قبول":"مردود"],
  ["کنترل اجباری ناتمام",x=>x.awaitingMandatory?"بله":"خیر"]
 ],quality.characteristics);
 alertTo("موارد کیفیت باز: "+report.openQualityHolds+
  " | مجموع بازرسی‌ها: "+quality.resultsCount+
  " | نتیجه نهایی ثبت‌شده: "+(report.isFinalQualityReady?"بدون مانع در داده‌ها":"نیازمند بررسی"));
}
async function reloadSpcRequirements(){
 const order=$("#spc-order").value;
 if(!order){select($("#spc-requirement"),[],x=>x.code);return;}
 const data=await api("/manufacturing-orders/"+order+"/inspection-requirements");
 select($("#spc-requirement"),data.requirements,
  x=>x.operationSequence+" • "+x.code+" • "+x.description);
}
async function spcReport(){
 const order=$("#spc-order").value,req=$("#spc-requirement").value;
 if(!order||!req)throw Error("دستور و مشخصه بازرسی انتخاب نشده است.");
 const r=await api("/manufacturing-orders/"+order+"/spc/"+req);
 const fmt=x=>x===null||x===undefined?"نامشخص":x;
 table("#spc-summary",[["شاخص","name"],["مقدار","value"]],[
  {name:"تعداد نمونه",value:r.sampleCount},
  {name:"میانگین",value:fmt(r.mean)},
  {name:"میانگین دامنه متحرک",value:fmt(r.movingRangeMean)},
  {name:"حد کنترل پایین برآوردی",value:fmt(r.lowerControlLimit)},
  {name:"حد کنترل بالا برآوردی",value:fmt(r.upperControlLimit)},
  {name:"مردود مشخصات فنی",value:r.specificationFailures},
  {name:"خارج از حدود کنترل",value:r.controlLimitViolations}
 ]);
 table("#spc-samples",[
  ["زمان",x=>new Date(x.recordedUtc).toLocaleString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
  ["مقدار","value"],["نتیجه",x=>x.passed?"قبول":"مردود"]
 ],r.samples);
 alertTo(r.warning);
}
async function reloadTraceLots(){
 const order=$("#trace-order").value;
 if(!order){select($("#trace-confirmation"),[],x=>x.lotCode);return;}
 const records=await api("/manufacturing-orders/"+order+"/confirmations");
 select($("#trace-confirmation"),records.filter(x=>x.lotCode&&x.goodQuantity>0),
   x=>x.operationSequence+" • "+x.lotCode+" / سالم "+x.goodQuantity);
}
async function traceGraph(){
 const id=$("#trace-confirmation").value;
 if(!id)throw Error("ابتدا بچ ثبت‌شده را انتخاب کنید.");
 const dir=$("#trace-direction").value,depth=Number($("#trace-depth").value);
 if(!Number.isInteger(depth)||depth<1||depth>16)throw Error("عمق ردیابی معتبر نیست.");
 const r=await api("/manufacturing-genealogy/explore/"+id+
   "?direction="+encodeURIComponent(dir)+"&depth="+depth);
 const lookup=new Map(r.nodes.map(x=>[x.confirmationId,x]));
 table("#trace-summary",[["شاخص","name"],["مقدار","value"]],[
  {name:"بچ‌های ثبت‌شده",value:r.batchCount},
  {name:"دستورات مرتبط",value:r.orderCount},
  {name:"پیوندهای مستند",value:r.edges.length},
  {name:"عمق مشاهده‌شده",value:r.depthReached},
  {name:"روابط فراتر از عمق انتخابی",value:r.hasMoreBeyondDepth?"وجود دارد؛ عمق را افزایش دهید":"مشاهده نشد"}
 ]);
 table("#trace-nodes",[
  ["دستور","orderNumber"],["بچ","lotCode"],
  ["ترتیب","sequence"],["سالم","goodQuantity"],
  ["زمان",x=>new Date(x.occurredUtc).toLocaleString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})]
 ],r.nodes);
 table("#trace-edges",[
  ["بچ مبدأ",x=>lookup.get(x.sourceId)?.lotCode||x.sourceId.slice(0,8)],
  ["بچ مقصد",x=>lookup.get(x.targetId)?.lotCode||x.targetId.slice(0,8)],
  ["مقدار","quantity"],
  ["زمان پیوند",x=>new Date(x.linkedUtc).toLocaleString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})]
 ],r.edges);
 alertTo(r.disclaimer);
}
function register(id,fn){const f=$(id);f.addEventListener("submit",async e=>{
 e.preventDefault();if(!f.reportValidity())return;
 const b=f.querySelector('button[type="submit"]');b.disabled=true;
 try{await fn(f);alertTo("اطلاعات ثبت شد.");f.reset();await load();}
 catch(err){alertTo(err.message,true);}finally{b.disabled=false;}
});}
function boot(){
 $$("nav [data-section]").forEach(button=>button.addEventListener("click",()=>{
 $$("section.page").forEach(section=>section.hidden=section.id!==button.dataset.section);
 $$("nav [data-section]").forEach(b=>b.classList.toggle("nav-active",b===button));
 $("#page-title").textContent=button.textContent;$("#feedback").hidden=true;
 }));
 register("#plan-form",f=>api("/control-plans","POST",{
 routingRevisionId:val(f,"routing"),code:val(f,"code"),validFromJalali:val(f,"from"),
 validUntilJalali:val(f,"to")||null
 }));
 register("#characteristic-form",f=>api("/control-plans/"+val(f,"planId")+"/characteristics","POST",{
 operationSequence:num(val(f,"sequence")),code:val(f,"code"),description:val(f,"description"),
 unit:val(f,"unit"),minimum:val(f,"minimum")?num(val(f,"minimum")):null,
 maximum:val(f,"maximum")?num(val(f,"maximum")):null,mandatory:val(f,"mandatory")==="true"
 }));
 register("#inspection-form",async f=>{
 const id=$("#inspection-order").value;if(!id)throw Error("دستور را انتخاب کنید.");
 const result=await api("/manufacturing-orders/"+id+"/inspections/"+val(f,"requirement"),"POST",{
 value:num(val(f,"value")),nonconformingQuantity:num(val(f,"nonconforming")),note:val(f,"note")||null
 });await inspections();alertTo(result.passed?"اندازه‌گیری قبول شد.":"مقدار نامنطبق قرنطینه شد.");
 });
 register("#lot-link-form",async f=>{
 const id=$("#link-order").value;if(!id)throw Error("دستور را انتخاب کنید.");
 await api("/manufacturing-orders/"+id+"/lot-genealogy","POST",{
 sourceConfirmationId:val(f,"from"),destinationConfirmationId:val(f,"to"),quantity:num(val(f,"quantity"))
 });await confirmations();
 });
 register("#assembly-origin-form",async f=>{
  const id=$("#assembly-order").value;
  if(!id)throw Error("دستور مونتاژ انتخاب نشده است.");
  await api("/manufacturing-orders/"+id+"/assembly-genealogy","POST",{
    semiFinishedConfirmationId:val(f,"source"),
    assemblyConfirmationId:val(f,"target"),
    componentQuantity:num(val(f,"quantity"))
  });
  await assemblyOrigins();
 });
 $("#assembly-refresh").onclick=()=>assemblyOrigins().catch(e=>alertTo(e.message,true));
 $("#readiness-refresh").onclick=()=>readiness().catch(e=>alertTo(e.message,true));
 $("#spc-order").onchange=()=>reloadSpcRequirements().catch(e=>alertTo(e.message,true));
 $("#spc-refresh").onclick=()=>spcReport().catch(e=>alertTo(e.message,true));
 $("#trace-order").onchange=()=>reloadTraceLots().catch(e=>alertTo(e.message,true));
 $("#trace-refresh").onclick=()=>traceGraph().catch(e=>alertTo(e.message,true));
 $("#inspection-refresh").onclick=()=>inspections().catch(e=>alertTo(e.message,true));
 $("#link-refresh").onclick=()=>confirmations().catch(e=>alertTo(e.message,true));
 load().catch(e=>alertTo(e.message,true));
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
})();