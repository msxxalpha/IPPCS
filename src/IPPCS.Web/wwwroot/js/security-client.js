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
   "/factory-reports.html"
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
 window.IPPCSSecurity={secureFetch,goLogin};
})();