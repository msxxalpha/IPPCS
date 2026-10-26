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
 $("#inspection-refresh").onclick=()=>inspections().catch(e=>alertTo(e.message,true));
 $("#link-refresh").onclick=()=>confirmations().catch(e=>alertTo(e.message,true));
 load().catch(e=>alertTo(e.message,true));
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
})();