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
function multiple(container,rows,{
 value="code",label="label",onChange=()=>{}
}={}){
 const old=new Set(container._factoryMulti?.values()||[]);
 const entries=rows.map(row=>({
  value:String(typeof value==="function"?value(row):row[value]),
  label:String(typeof label==="function"?label(row):row[label])
 }));
 const known=new Set(entries.map(x=>x.value));
 const selected=new Set([...old].filter(x=>known.has(x)));
 container.replaceChildren();
 const search=document.createElement("input");search.type="search";
 search.className="factory-choice-search";search.placeholder="جست‌وجوی گزینه‌ها…";
 search.setAttribute("aria-label","جست‌وجوی گزینه‌های چندانتخابی");
 const count=document.createElement("p");count.className="factory-choice-count";
 const list=document.createElement("div");list.className="factory-checkbox-list";
 container.append(search,count,list);
 function draw(){
  const q=search.value.trim().toLocaleLowerCase("fa");
  list.replaceChildren();
  for(const entry of entries.filter(x=>
   !q||(x.label+" "+x.value).toLocaleLowerCase("fa").includes(q))){
   const labelEl=document.createElement("label");
   labelEl.className="factory-inline-check";
   const box=document.createElement("input");box.type="checkbox";box.value=entry.value;
   box.checked=selected.has(entry.value);
   box.addEventListener("change",()=>{
    if(box.checked)selected.add(entry.value);else selected.delete(entry.value);
    count.textContent=selected.size?
     selected.size.toLocaleString("fa-IR")+" گزینه انتخاب شده است.":"همه گزینه‌ها";
    onChange([...selected]);
   });
   const caption=document.createElement("span");caption.textContent=entry.label;
   labelEl.append(box,caption);list.append(labelEl);
  }
  count.textContent=selected.size?
   selected.size.toLocaleString("fa-IR")+" گزینه انتخاب شده است.":"همه گزینه‌ها";
 }
 search.addEventListener("input",draw);draw();
 const instance={values:()=>[...selected],clear:()=>{
  selected.clear();search.value="";draw();onChange([]);
 }};
 container._factoryMulti=instance;
 return instance;
}
window.IPPCSChoices={fill,multiple};
})();
