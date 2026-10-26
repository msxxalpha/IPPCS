(()=>{
"use strict";
const form=document.querySelector("#login-form"),error=document.querySelector("#login-error");
form.addEventListener("submit",async e=>{
 e.preventDefault();if(!form.reportValidity())return;
 const submit=form.querySelector("button[type=submit]");submit.disabled=true;error.hidden=true;
 const data=new FormData(form);
 try{
  const response=await fetch("/auth/login",{method:"POST",credentials:"same-origin",
   headers:{"Content-Type":"application/json"},
   body:JSON.stringify({username:data.get("username"),password:data.get("password")})});
  if(!response.ok)throw new Error(response.status===423?"حساب موقتاً قفل شده است.":"نام کاربری یا رمز عبور صحیح نیست.");
  const requested=new URLSearchParams(location.search).get("returnUrl");
  const allowed=["/app.html","/planning.html","/execution.html"];
  location.replace(allowed.includes(requested)?requested:"/app.html");
 }catch(ex){error.hidden=false;error.textContent=ex.message;submit.disabled=false;}
});
})();