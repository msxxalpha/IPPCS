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
function start(){
 $("readiness-refresh").addEventListener("click",reload);
 reload();
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",start);
else start();
})();
