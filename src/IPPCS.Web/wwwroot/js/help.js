(()=>{
"use strict";
const catalog=window.IPPCSHelpCatalog;
if(!catalog||!Array.isArray(catalog.pages)||!Array.isArray(catalog.deployment))return;
const $=id=>document.getElementById(id);
const create=(tag,text,className)=>{
 const e=document.createElement(tag);
 if(text!==undefined&&text!==null)e.textContent=String(text);
 if(className)e.className=className;
 return e;
};
function plainLink(href,text){
 const link=create("a",text,"planning-link");
 // Only application-owned pages from the static catalog can be opened.
 link.href=href;return link;
}
function fields(rows,host){
 const dl=create("dl");
 for(const [term,explanation] of rows){
  dl.append(create("dt",term),create("dd",explanation));
 }
 host.append(dl);
}
function show(which){
 const forms=which==="forms";
 $("help-forms").hidden=!forms;$("help-rollout").hidden=forms;
 $("help-show-forms").setAttribute("aria-pressed",String(forms));
 $("help-show-rollout").setAttribute("aria-pressed",String(!forms));
 $("help-show-forms").className=forms?"btn-main":"btn-outline";
 $("help-show-rollout").className=forms?"btn-outline":"btn-main";
}
function renderRollout(){
 const host=$("help-steps");host.replaceChildren();
 const checked=new Set();
 const progress=$("help-progress");
 const refresh=()=>{progress.textContent=checked.size.toLocaleString("fa-IR")+
  " از "+catalog.deployment.length.toLocaleString("fa-IR")+
  " گام برای مرور شخصی علامت‌گذاری شده؛ نتیجه ذخیره یا تأیید رسمی نشده است."};
 catalog.deployment.forEach((step,index)=>{
  const card=create("article",undefined,"help-step");
  const title=create("h3",step.title);
  const check=create("input");check.type="checkbox";check.id="help-step-"+index;
  const label=create("label");label.htmlFor=check.id;
  label.append(check,document.createTextNode(" بررسی شد (فقط در این صفحه)"));
  check.addEventListener("change",()=>{
   if(check.checked)checked.add(index);else checked.delete(index);refresh();
  });
  card.append(title);
  fields([["اطلاعات لازم",step.data],["روش اجرا",step.action],["معیار بررسی",step.done]],card);
  const footer=create("div",undefined,"help-step-footer");
  footer.append(label,plainLink(step.page,"رفتن به فرم مرتبط"));
  card.append(footer);host.append(card);
 });
 refresh();
}
function pageCard(page,query){
 const term=query.trim().toLocaleLowerCase("fa-IR");
 const text=[page.title,page.purpose,page.before,...page.workflows,
  ...page.forms.flat()].join(" ").toLocaleLowerCase("fa-IR");
 if(term&&!text.includes(term))return null;
 const card=create("article",undefined,"help-page");card.id="help-page-"+page.page.slice(1,-5);
 card.append(create("h3",page.title),create("p",page.purpose));
 fields([["پیش‌نیاز",page.before]],card);
 card.append(plainLink(page.page,"باز کردن این صفحه"));
 if(page.workflows.length){
  const heading=create("h4","بخش‌های عملیاتی و گزارش‌های صفحه");
  const ul=create("ul",undefined,"help-workflows");
  for(const line of page.workflows){const li=create("li",line);ul.append(li);}
  card.append(heading,ul);
 }
 for(const [id,title,usage] of page.forms){
  const block=create("div",undefined,"help-form");block.id="form-"+id;
  block.append(create("h4",title));
  fields([["شناسه فرم",id],["چگونه استفاده شود",usage]],block);
  block.append(plainLink(page.page,"رفتن به صفحه این فرم"));
  card.append(block);
 }
 return card;
}
function renderForms(){
 const search=$("help-search").value;
 const filter=$("help-page-filter").value;
 const host=$("help-pages");host.replaceChildren();
 let count=0;
 for(const page of catalog.pages){
  if(filter&&filter!==page.page)continue;
  const card=pageCard(page,search);
  if(!card)continue;
  count+=page.forms.length;host.append(card);
 }
 if(!host.children.length)host.append(create("p","موردی پیدا نشد؛ عبارت جستجو را کوتاه‌تر کنید."));
 $("help-result-count").textContent=host.children.length.toLocaleString("fa-IR")+
  " صفحه و "+count.toLocaleString("fa-IR")+" فرم در نتایج.";
}
function begin(){
 if(!catalog.pages.length)return;
 renderRollout();
 const select=$("help-page-filter");
 for(const page of catalog.pages){
  const option=create("option",page.title);option.value=page.page;select.append(option);
 }
 $("help-search").addEventListener("input",renderForms);
 select.addEventListener("change",renderForms);
 $("help-show-forms").addEventListener("click",()=>{
  show("forms");renderForms();
 });
 $("help-show-rollout").addEventListener("click",()=>show("rollout"));
 renderForms();
 const params=new URLSearchParams(location.hash.replace(/^#/,""));
 const page=params.get("page"),form=params.get("form");
 if(page&&catalog.pages.some(x=>x.page===page)){
  select.value=page;show("forms");renderForms();
  if(form){
   const target=[...$("help-pages").querySelectorAll(".help-form")].
    find(e=>e.id==="form-"+form);
   if(target){target.classList.add("help-target");target.scrollIntoView({block:"start"});}
  }
 }
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",begin);
else begin();
})();
