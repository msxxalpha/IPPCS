(()=>{
"use strict";
const $=id=>document.getElementById(id);
function node(tag,text,className){
 const element=document.createElement(tag);
 if(text!==undefined)element.textContent=String(text);
 if(className)element.className=className;
 return element;
}
function link(url,label){
 const element=node("a",label,"planning-link");
 // The service returns fixed application-owned routes, not user input.
 const allowed=new Set(["/app.html","/planning.html","/execution.html",
   "/control-plan.html","/factory-recall.html","/production-deviations.html",
   "/factory-master-data.html","/planning-advanced.html"]);
 element.href=allowed.has(url)?url:"/app.html";
 return element;
}
function notice(text,failed=false){
 const element=$("readiness-notice");element.hidden=false;
 element.className="notice"+(failed?" error":"");
 element.textContent=text;
}
function setupCard(gate){
 const card=node("article",undefined,"factory-setup-card");
 const heading=node("div",undefined,"factory-setup-heading");
 heading.append(node("strong",gate.title),
   node("span",gate.configured?"داده اولیه موجود است":"نیازمند تنظیم",gate.configured?"factory-setup-ok":"factory-setup-pending"));
 card.append(heading,
   node("p","تعداد رکوردهای مرتبط: "+Number(gate.records).toLocaleString("fa-IR")),
   node("p",gate.advice),
   link(gate.page,"ورود به صفحه تنظیمات"));
 return card;
}
function moduleCard(module){
 const card=node("article",undefined,"factory-uat-card");
 const title=node("h4",String(module.number).padStart(2,"0")+" — "+module.title);
 card.append(title,node("p",module.implementedScope));
 const lab=node("strong","گیت بازماندهٔ کارخانه:");
 card.append(lab,node("p",module.outstandingFactoryAcceptance));
 const footer=node("div",undefined,"factory-uat-footer");
 footer.append(node("span","قابلیت آزمایشی؛ پذیرش کارخانه ثبت نشده است","factory-uat-status"),
   link(module.page,"آزمایش در سامانه"));
 card.append(footer);return card;
}
function display(data){
 if(data.kind!=="FactoryUatPreparationOnly"||
   data.productionReleaseAuthorized!==false||data.factoryAcceptedModules!==0||
   !Array.isArray(data.modules)||data.modules.length!==18||
   !Array.isArray(data.setup)||data.declaredModules!==18)
  throw Error("اطلاعات وضعیت انتشار نامعتبر است؛ از این گزارش برای تأیید کارخانه استفاده نکنید.");
 $("readiness-total").textContent=data.declaredModules.toLocaleString("fa-IR");
 $("readiness-accepted").textContent="۰";
 $("readiness-configured").textContent=
   data.configuredSetupGates.toLocaleString("fa-IR")+" / "+
   data.totalSetupGates.toLocaleString("fa-IR");
 const setup=$("readiness-setup");setup.replaceChildren();
 data.setup.forEach(x=>setup.append(setupCard(x)));
 const modules=$("readiness-modules");modules.replaceChildren();
 data.modules.forEach(x=>modules.append(moduleCard(x)));
 notice(data.warning);
}
async function reload(){
 const button=$("readiness-refresh");
 button.disabled=true;
 try{
  const result=await IPPCSSecurity.secureFetch("/api/v1/factory-release-readiness");
  if(!result.ok)throw Error("سرویس وضعیت آماده‌سازی پاسخ معتبر نداد.");
  display(await result.json());
 }catch(error){notice(error.message,true);}
 finally{button.disabled=false;}
}
const priorityPages=new Set([
 "/engineering-rebind.html","/control-plan.html",
 "/production-deviations.html","/planning-advanced.html"
]);
function priorityCard(item){
 const card=node("article",undefined,"factory-setup-card");
 const heading=node("div",undefined,"factory-setup-heading");
 const severity=item.severity==="Critical"?"بحرانی":item.severity==="High"?
  "اولویت بالا":"نیازمند بررسی";
 heading.append(node("strong",item.area+" — "+item.identifier),
  node("span",severity,"factory-setup-pending"));
 card.append(heading,node("p",item.description));
 const anchor=node("a","ورود به ماژول مرتبط","planning-link");
 anchor.href=priorityPages.has(item.page)?item.page:"/app.html";
 card.append(anchor);return card;
}
async function reloadPriority(){
 const button=$("priority-refresh");
 button.disabled=true;
 try{
  const response=await IPPCSSecurity.secureFetch(
   "/api/v1/factory-release-readiness/priority-queue");
  if(!response.ok){
   let detail;try{detail=await response.json();}catch{}
   throw Error(detail?.message||"دریافت صف اقدامات ممکن نیست.");
  }
  const data=await response.json();
  if(data.kind!=="FactoryPriorityExceptionsOnly"||
   data.productionReleaseAuthorized!==false||data.factoryAcceptedModules!==0||
   !Array.isArray(data.rows)||data.total!==data.rows.length)
   throw Error("ساختار صف اقدامات معتبر نیست؛ وضعیت پذیرش کارخانه قابل استنباط نیست.");
  const rows=$("priority-list");rows.replaceChildren();
  $("priority-total").textContent=Number(data.total).toLocaleString("fa-IR");
  $("priority-critical").textContent=Number(data.critical).toLocaleString("fa-IR");
  $("priority-high").textContent=Number(data.high).toLocaleString("fa-IR");
  if(!data.rows.length)
   rows.append(node("p","در محدوده داده قابل محاسبه موردی دیده نشد؛ پذیرش و آزمون کارخانه همچنان باز است."));
  for(const entry of data.rows)rows.append(priorityCard(entry));
  $("priority-disclaimer").textContent=data.disclaimer;
 }catch(error){notice(error.message,true);}
 finally{button.disabled=false;}
}
function start(){
 $("readiness-refresh").addEventListener("click",reload);
 $("priority-refresh").addEventListener("click",reloadPriority);
 reload();
 reloadPriority();
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",start);
else start();
})();
