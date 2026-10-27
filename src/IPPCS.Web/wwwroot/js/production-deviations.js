(()=>{"use strict";
const $=s=>document.querySelector(s);let mps=[],orders=[],deviations=[],lines=[];
const notice=(s,b=false)=>{let e=$("#notice");e.hidden=false;e.textContent=s;e.className="notice"+(b?" error":"");};
async function api(p,m="GET",data){let r=await IPPCSSecurity.secureFetch("/api/v1"+p,{method:m,headers:data===undefined?{}:{"Content-Type":"application/json"},body:data===undefined?undefined:JSON.stringify(data)});let t=await r.text(),v;try{v=t?JSON.parse(t):null;}catch{v=null;}if(!r.ok)throw Error(v?.message||t||"خطای سرویس");return v;}
function fill(el,arr,view){let old=el.value;el.replaceChildren();let empty=document.createElement("option");empty.value="";empty.textContent="انتخاب کنید";el.append(empty);arr.forEach(x=>{let o=document.createElement("option");o.value=x.id;o.textContent=view(x);el.append(o);});if([...el.options].some(x=>x.value===old))el.value=old;}
function refreshLine(){const v=mps.find(x=>x.id===$("#version").value);lines=v?.lines||[];fill($("#line"),lines,x=>x.itemId.slice(0,8)+" • "+x.neededOnJalali+" • "+x.quantity);refreshOrders();}
function refreshOrders(){const l=lines.find(x=>x.id===$("#line").value);fill($("#order"),orders.filter(x=>!l||x.itemId===l.itemId).filter(x=>x.status!==5),x=>x.number);}
async function render(){deviations=await api("/production-deviations");fill($("#action-case"),deviations.filter(x=>x.status!==2),x=>x.description.slice(0,65));fill($("#close-case"),deviations.filter(x=>x.status===1),x=>x.description.slice(0,65));fill($("#revise-case"),deviations.filter(x=>x.status===1),x=>x.description.slice(0,65));fill($("#escalate-case"),deviations.filter(x=>x.status!==2),x=>x.description.slice(0,65));fill($("#effectiveness-case"),deviations.filter(x=>x.status===2&&!x.effectiveness?.independentlyVerified),x=>x.description.slice(0,65));let host=$("#deviation-table");host.replaceChildren();const div=document.createElement("div");div.className="table-wrap";const t=document.createElement("table");let th=document.createElement("tr");["نوع","شرح","وضعیت","اثربخشی","علت","اقدام","مسئول","موعد شمسی","تشدیدها","سوابق"].forEach(n=>{let c=document.createElement("th");c.textContent=n;th.append(c);});let head=document.createElement("thead");head.append(th);t.append(head);let body=document.createElement("tbody");deviations.forEach(x=>{let tr=document.createElement("tr");[x.category,x.description,["باز","دارای اقدام","مختومه"][x.status],(x.effectiveness?.state==="Verified"?"تأیید مستقل":x.effectiveness?.state==="AwaitingIndependentReview"?"منتظر بازبینی":x.effectiveness?.state==="ReopenedForCorrection"?"رد اثربخشی؛ اصلاح مجدد":x.effectiveness?.state==="InvalidEvidence"?"شواهد نامعتبر":"—"),x.rootCause||"—",x.correctiveAction||"—",x.responsible||"—",x.actionDueJalali||"—",x.events.filter(e=>e.details.startsWith("ESCALATED:")).length,x.events.length].forEach(v=>{let td=document.createElement("td");td.textContent=String(v);tr.append(td);});body.append(tr);});t.append(body);div.append(t);host.append(div);}
async function boot(){[mps,orders]=await Promise.all([api("/mps"),api("/manufacturing-orders")]);fill($("#version"),mps.filter(x=>x.state===1),x=>x.code);$("#version").onchange=refreshLine;$("#line").onchange=refreshOrders;
$("#create").onsubmit=async ev=>{ev.preventDefault();try{await api("/production-deviations","POST",{mpsLineId:$("#line").value,orderId:$("#order").value||null,category:Number($("#category").value),description:$("#description").value});notice("انحراف ثبت شد.");await render();}catch(e){notice(e.message,true);}};
$("#action").onsubmit=async ev=>{ev.preventDefault();try{await api("/production-deviations/"+$("#action-case").value+"/action","POST",{rootCause:$("#cause").value,correctiveAction:$("#corrective").value,responsible:$("#responsible").value,actionDueJalali:$("#action-date").value});notice("اقدام اصلاحی ثبت شد.");await render();}catch(e){notice(e.message,true);}};
function prefillRevision(){
 const d=deviations.find(x=>x.id===$("#revise-case").value);
 if(!d)return;
 $("#revise-cause").value=d.rootCause||"";
 $("#revise-corrective").value=d.correctiveAction||"";
 $("#revise-responsible").value=d.responsible||"";
 $("#revise-date").value=d.actionDueJalali||"";
}
$("#revise-case").onchange=prefillRevision;
$("#revise").onsubmit=async ev=>{
 ev.preventDefault();
 try{
  await api("/production-deviations/"+$("#revise-case").value+"/revise-action","POST",{
   rootCause:$("#revise-cause").value,correctiveAction:$("#revise-corrective").value,
   responsible:$("#revise-responsible").value,actionDueJalali:$("#revise-date").value,
   changeReason:$("#revise-reason").value
  });
  notice("اطلاعات پیشین در تاریخچه ثبت شد و بازنگری جدید انجام شد.");
  await render();
 }catch(e){notice(e.message,true);}
};
$("#escalate").onsubmit=async ev=>{
 ev.preventDefault();
 try{
  await api("/production-deviations/"+$("#escalate-case").value+"/escalate","POST",{
   reason:$("#escalate-reason").value,reviewByJalali:$("#escalate-date").value||null
  });
  notice("رویداد تشدید پیگیری ثبت شد. اعلان خارجی ارسال نشد.");
  await render();
 }catch(e){notice(e.message,true);}
};
$("#followup-review").onclick=async ()=>{
 try{
  const id=$("#escalate-case").value;
  if(!id)throw Error("پرونده موردنظر را انتخاب کنید.");
  const parts=new Intl.DateTimeFormat("en-US-u-ca-persian-nu-latn",{
   timeZone:"Asia/Tehran",year:"numeric",month:"2-digit",day:"2-digit"
  }).formatToParts(new Date());
  const day=["year","month","day"].map(k=>parts.find(x=>x.type===k).value).join("/");
  const report=await api("/production-deviations/"+id+"/follow-up?asOfJalali="+encodeURIComponent(day));
  const host=$("#followup-summary");
  host.replaceChildren();
  const list=document.createElement("p");
  list.textContent="تعداد تشدیدها: "+report.escalationCount+
   " | موعد اقدام گذشته: "+(report.isOverdue?"بله":"خیر")+
   " | آخرین دلیل: "+(report.lastEscalationReason||"—")+
   " | تعداد رویدادهای تاریخچه: "+report.eventCount;
  host.append(list);
  notice(report.disclaimer);
 }catch(e){notice(e.message,true);}
};
async function refreshEffectivenessPending(){
 const response=await api("/production-deviations/effectiveness-pending");
 const host=$("#effectiveness-pending");host.replaceChildren();
 const p=document.createElement("p");
 p.textContent="پرونده‌های مختومه بدون تأیید مستقل: "+response.count;
 host.append(p);
 if(response.rows.length){
  const wrapper=document.createElement("div");wrapper.className="table-wrap";
  const table=document.createElement("table"),header=document.createElement("tr");
  ["پرونده","وضعیت","مختومه در"].forEach(title=>{
   const th=document.createElement("th");th.textContent=title;header.append(th);
  });table.append(header);
  for(const row of response.rows){
   const tr=document.createElement("tr");
   for(const txt of [row.description,row.state,
     row.closedUtc?new Date(row.closedUtc).toLocaleString("fa-IR"):"—"]){
    const td=document.createElement("td");td.textContent=txt;tr.append(td);
   }table.append(tr);
  }wrapper.append(table);host.append(wrapper);
 }
}
$("#effectiveness-load").onclick=async()=>{
 try{await refreshEffectivenessPending();}
 catch(e){notice(e.message,true);}
};
$("#effectiveness-form").onsubmit=async ev=>{
 ev.preventDefault();
 try{
  const id=$("#effectiveness-case").value;
  if(!id)throw Error("پرونده مختومه را انتخاب کنید.");
  const response=await api("/production-deviations/"+id+"/effectiveness/review","POST",{
   effective:$("#effectiveness-result").value==="true",
   evidenceReference:$("#effectiveness-ref").value.trim(),
   observation:$("#effectiveness-observation").value.trim()
  });
  const host=$("#effectiveness-summary");host.replaceChildren();
  const p=document.createElement("p");p.textContent=response.message+" | مدرک: "+response.evidenceReference;
  host.append(p);
  notice(response.message);
  await render();await refreshEffectivenessPending();
 }catch(e){notice(e.message,true);}
};

$("#close").onsubmit=async ev=>{ev.preventDefault();try{await api("/production-deviations/"+$("#close-case").value+"/close","POST",{verification:$("#verification").value});notice("اختتام اولیه ثبت شد؛ بازبینی مستقل اثربخشی باقی است.");await render();await refreshEffectivenessPending();}catch(e){notice(e.message,true);}};
await render();await refreshEffectivenessPending();
const selected=new URLSearchParams(window.location.search).get("case");
if(selected&&deviations.some(x=>x.id===selected&&x.status!==2)){
 $("#escalate-case").value=selected;
 if(deviations.some(x=>x.id===selected&&x.status===1)){
  $("#revise-case").value=selected;
  prefillRevision();
 }
 $("#followup-review").click();
}
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",()=>boot().catch(e=>notice(e.message,true)));else boot().catch(e=>notice(e.message,true));
})();