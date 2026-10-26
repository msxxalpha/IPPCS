(()=>{"use strict";
const $=x=>document.querySelector(x);
function flash(s,error=false){const el=$("#ip-message");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=s;}
async function api(path,method="GET",body){
 const options={method,headers:{}};if(body!==undefined){options.headers["Content-Type"]="application/json";options.body=JSON.stringify(body);}
 const res=await IPPCSSecurity.secureFetch("/api/v1"+path,options);
 const raw=await res.text();let data;try{data=raw?JSON.parse(raw):null;}catch{data=null;}
 if(!res.ok){const e=new Error(data?.message||data?.title||raw||"خطای نامشخص");e.status=res.status;throw e;}
 return data;
}
function table(rows){
 const host=$("#users-table");host.replaceChildren();
 const box=document.createElement("div");box.className="table-wrap";
 const table=document.createElement("table"),head=document.createElement("thead");
 const h=document.createElement("tr");
 ["نام کاربری","نقش","وضعیت","عملیات"].forEach(x=>{const th=document.createElement("th");th.textContent=x;h.append(th);});
 head.append(h);table.append(head);const body=document.createElement("tbody");
 rows.forEach(user=>{
  const row=document.createElement("tr");
  for(const v of [user.userName,user.roles.join("، "),user.isLocked?"قفل‌شده":"فعال"]){
   const td=document.createElement("td");td.textContent=v;row.append(td);
  }
  const td=document.createElement("td");const button=document.createElement("button");
  button.type="button";button.className="btn-outline";button.textContent=user.isLocked?"بازکردن قفل":"قفل حساب";
  const isAdmin=user.roles.includes("Administrator");button.disabled=isAdmin;
  button.onclick=async()=>{if(!window.confirm("تغییر وضعیت حساب نرم‌افزار انجام شود؟"))return;
   button.disabled=true;try{await api("/system-users/"+user.id+(user.isLocked?"/unlock":"/lock"),"POST");
    flash("وضعیت حساب ذخیره شد.");await loadUsers();
   }catch(e){flash(e.message,true);}finally{button.disabled=false;}
  };
  td.append(button);row.append(td);body.append(row);
 });
 table.append(body);box.append(table);host.append(box);
}
async function loadUsers(){
 try{const data=await api("/system-users");$("#admin-panel").hidden=false;table(data);}
 catch(e){if(e.status===403)return;flash(e.message,true);}
}
function boot(){
 $("#password-form").onsubmit=async e=>{
  e.preventDefault();const form=e.currentTarget;if(!form.reportValidity())return;
  const submit=form.querySelector('button[type="submit"]');submit.disabled=true;
  const data=new FormData(form);
  try{await api("/auth/change-password","POST",{currentPassword:data.get("old"),newPassword:data.get("new")});
   form.reset();flash("رمز ورود تغییر کرد.");
  }catch(e){flash(e.message,true);}finally{submit.disabled=false;}
 };
 $("#user-form").onsubmit=async e=>{
  e.preventDefault();const form=e.currentTarget;if(!form.reportValidity())return;
  const submit=form.querySelector('button[type="submit"]');submit.disabled=true;
  const data=new FormData(form);
  try{await api("/system-users","POST",{username:data.get("username"),password:data.get("password"),role:data.get("role")});
   form.reset();flash("کاربر با نقش تعیین‌شده ساخته شد.");await loadUsers();
  }catch(e){flash(e.message,true);}finally{submit.disabled=false;}
 };
 loadUsers();
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
})();