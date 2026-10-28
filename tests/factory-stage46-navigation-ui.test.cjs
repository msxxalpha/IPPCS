const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const read=p=>fs.readFileSync(path.join(root,p),"utf8");
test("all primary and secondary navigation links resolve to existing pages",()=>{
 const files=fs.readdirSync(root).filter(x=>x.endsWith(".html"));
 for(const file of files){
  const html=read(file);
  for(const match of html.matchAll(/(?:href|data-href)="(\/[^"#?]+\.html)(?:#[^"]*)?"/g)){
   assert.ok(fs.existsSync(path.join(root,match[1].slice(1))),file+" has dead navigation target "+match[1]);
  }
 }
 const main=read("app.html");
 const menu=[...main.matchAll(/<a[^>]*class="planning-link"[^>]*href="([^"]+)"/g)].map(x=>x[1]);
 assert.ok(menu.length>=12,"main navigation should expose the dedicated module pages");
 for(const href of menu)assert.ok(fs.existsSync(path.join(root,href.replace(/^\//,"").split("#")[0])),href);
});
test("client navigation shows runtime errors and marks active page accessibly",()=>{
 const security=read("js/security-client.js");
 assert.match(security,/aria-current","page"/);
 assert.match(security,/classList\.remove\("open"\)/);
 assert.match(security,/unhandledrejection/);
 assert.match(security,/data-ip-client-error/);
});
test("responsive application CSS includes keyboard focus, mobile layout and reduced-motion support",()=>{
 const css=read("css/production.css");
 assert.match(css,/@media\(max-width:900px\)/);
 assert.match(css,/@media\(max-width:560px\)/);
 assert.match(css,/focus-visible/);
 assert.match(css,/prefers-reduced-motion/);
});

test("shift capacity action passes a selector to the table renderer",()=>{
 const script=read("js/production.js");
 assert.match(script,/table\("#shift-capacity-table",\[/);
 assert.doesNotMatch(script,/table\(\$\("#shift-capacity-table"\),\[/);
});
