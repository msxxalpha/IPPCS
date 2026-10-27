/* Accessible progressive enhancement for high-volume factory select lists.
   An ordinary native select remains keyboard/screen-reader operable. */
(()=>{
"use strict";
function fill(select,rows,{value="id",label="label",placeholder="انتخاب کنید",search=true}={}){
 if(!select)return;
 const old=select.value;
 const values=rows.map(r=>({
  value:String(typeof value==="function"?value(r):r[value]),
  label:String(typeof label==="function"?label(r):r[label])
 }));
 select._factoryRows=values;
 select._factoryPlaceholder=placeholder;
 // Stable search input: do not duplicate controls when forms refresh.
 if(search&&!select._factorySearch){
  const field=document.createElement("input");
  field.type="search";
  field.className="factory-choice-search";
  field.placeholder="جست‌وجو در فهرست…";
  field.autocomplete="off";
  field.setAttribute("aria-label","جست‌وجوی "+(select.getAttribute("aria-label")||select.id||"گزینه‌ها"));
  select.parentElement.insertBefore(field,select);
  field.addEventListener("input",()=>render(select,select.value));
  field.addEventListener("keydown",e=>{
   if(e.key==="ArrowDown"){e.preventDefault();select.focus();}
   if(e.key==="Escape"){field.value="";render(select,select.value);}
  });
  select._factorySearch=field;
 }
 render(select,old);
}
function render(select,chosen){
 const query=(select._factorySearch?.value||"").trim().toLocaleLowerCase("fa");
 const rows=select._factoryRows||[];
 const displayed=query?rows.filter(r=>
  (r.label+" "+r.value).toLocaleLowerCase("fa").includes(query)):rows;
 const unique=new Map(displayed.map(x=>[x.value,x]));
 const selected=rows.find(x=>x.value===chosen);
 if(selected&&!unique.has(selected.value))unique.set(selected.value,selected);
 const fragment=document.createDocumentFragment();
 const blank=document.createElement("option");
 blank.value="";blank.textContent=select._factoryPlaceholder||"انتخاب کنید";fragment.append(blank);
 for(const row of unique.values()){
  const opt=document.createElement("option");
  opt.value=row.value;opt.textContent=row.label;fragment.append(opt);
 }
 select.replaceChildren(fragment);
 if(selected)select.value=selected.value;
 // Filtering is only a visual convenience. Never silently select a new value.
 if(!selected)select.value="";
 select.setAttribute("aria-label",select.getAttribute("aria-label")||"انتخاب از فهرست");
 select.dataset.matchCount=String(displayed.length);
}
window.IPPCSChoices={fill};
})();
