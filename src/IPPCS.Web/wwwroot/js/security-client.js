(()=>{
 "use strict";
 let token=null;
 async function secureFetch(url,options={}){
  const path=typeof url==="string"?url:url.url;
  const method=(options.method||"GET").toUpperCase();
  const isApi=path.startsWith("/api/v1");
  const settings={...options,credentials:"same-origin",headers:new Headers(options.headers||{})};
  if(isApi&&!["GET","HEAD","OPTIONS"].includes(method)){
   if(!token){
    const response=await fetch("/auth/csrf",{credentials:"same-origin",cache:"no-store"});
    if(response.status===401){goLogin();throw new Error("نشست کاربر معتبر نیست.");}
    if(response.status===403)throw new Error("نقش شما اجازه دسترسی به عملیات تولید را ندارد.");
    if(!response.ok)throw new Error("دریافت شناسه امنیت درخواست ممکن نیست.");
    const data=await response.json();token=data.token;
   }
   settings.headers.set("X-IPPCS-CSRF",token);
  }
  const response=await fetch(url,settings);
  if(isApi&&response.status===401){goLogin();throw new Error("نشست شما منقضی شده یا معتبر نیست؛ دوباره وارد شوید.");}
  if(isApi&&response.status===403){
   throw new Error("کاربر وارد شده است، اما نقش شما مجوز انجام این عملیات را ندارد.");
  }
  if(response.status===400 && isApi && method!=="GET"){
   // A stale antiforgery token may require a new sign-in. Avoid automatic replay of an unsafe mutation.
   const clone=response.clone();
   try{const message=await clone.json();if(String(message.message||"").includes("شناسه امنیت"))token=null;}catch{}
  }
  return response;
 }
 function goLogin(){
  const allowed=new Set([
   "/app.html","/planning.html","/execution.html","/control-plan.html",
   "/planning-advanced.html","/engineering-rebind.html","/mps-pegging.html",
   "/production-deviations.html","/system-users.html","/factory-recall.html",
   "/factory-master-data.html","/factory-release-readiness.html","/factory-analytical-verification.html",
   "/factory-reports.html","/help.html","/factory-print.html"
  ]);
  const returnTo=allowed.has(location.pathname)?location.pathname:"/app.html";
  if(location.pathname!=="/login.html")location.assign("/login.html?returnUrl="+encodeURIComponent(returnTo));
 }
 document.addEventListener("click",async event=>{
  const btn=event.target.closest("[data-ip-logout]");
  if(!btn)return;
  btn.disabled=true;
  try{await secureFetch("/api/v1/auth/logout",{method:"POST"});location.assign("/login.html");}
  catch(err){btn.disabled=false;window.alert(err.message);}
 });
 // Universal, contextual Persian help on every authenticated application form.
 // Pure DOM links only: never modify submitted payloads or operational data.
 function injectHelpLinks(){
  if(location.pathname==="/login.html"||location.pathname==="/help.html")return;
  // Make navigation state explicit and close the mobile drawer after following a link.
  const nav=document.querySelector(".sidebar nav");
  if(nav){
   nav.querySelectorAll('a[href]').forEach(link=>{
    try{
     const target=new URL(link.href,location.origin);
     if(target.origin===location.origin&&target.pathname===location.pathname){
      link.setAttribute("aria-current","page");
      link.classList.add("nav-current");
     }
    }catch{}
    link.addEventListener("click",()=>document.querySelector(".sidebar")?.classList.remove("open"));
   });
  }
  if(nav&&!nav.querySelector("[data-ip-help-nav]")){
   const link=document.createElement("a");link.className="planning-link";
   link.setAttribute("data-ip-help-nav","");
   link.href="/help.html";link.textContent="راهنمای فرم‌ها و ترتیب استقرار";
   nav.append(link);
  }
  const forms=document.querySelectorAll("form[id]");
  for(const form of forms){
   if(form.querySelector("[data-ip-form-help]"))continue;
   const a=document.createElement("a");
   a.setAttribute("data-ip-form-help","");
   a.className="help-jump";
   a.href="/help.html#page="+encodeURIComponent(location.pathname)+
    "&form="+encodeURIComponent(form.id);
   a.textContent="؟ راهنمای این فرم";
   a.title="کاربرد، پیش‌نیاز و روش ورود اطلاعات این فرم";
   form.insertBefore(a,form.firstChild);
  }
 }
 // Report script/runtime failures visibly instead of leaving a form that appears inert.
 function reportClientFailure(message){
  const existing=document.querySelector("[data-ip-client-error]");
  if(existing)return;
  const host=document.querySelector(".content")||document.querySelector("main")||document.body;
  const banner=document.createElement("div");
  banner.dataset.ipClientError="";
  banner.className="notice error";
  banner.setAttribute("role","alert");
  banner.textContent="بخشی از فرم به‌درستی بارگذاری نشد. صفحه را تازه‌سازی کنید؛ اگر مشکل تکرار شد، متن خطا را برای پشتیبانی ارسال کنید. "+String(message||"").slice(0,220);
  host.prepend(banner);
 }
 window.addEventListener("error",event=>{
  if(event.filename&&event.filename.includes("/js/"))reportClientFailure(event.message);
 });
 window.addEventListener("unhandledrejection",event=>{
  const reason=event.reason;
  if(reason instanceof Error)reportClientFailure(reason.message);
 });
 if(document.readyState==="loading")
  document.addEventListener("DOMContentLoaded",injectHelpLinks);
 else injectHelpLinks();
 window.IPPCSSecurity={secureFetch,goLogin};
})();