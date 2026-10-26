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
 const response=await fetch("/api/v1"+path,init);
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
 render();
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
