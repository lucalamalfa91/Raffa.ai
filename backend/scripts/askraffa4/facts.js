// Exports the prototype demo contracts (RD.CONTRACTS + RD.DEEP) as the C# tests' contracts.json.
const W=require('./load');const out={};
for(const id of [1,2,3,4]){const C=W.RD.CONTRACTS[id],D=W.RD.DEEP[id];
 out[id]={id,supplier:C.supplier,name:C.name,type:C.type,file:C.file,spend:C.spend,start:C.start,end:C.end,cancel:C.cancel,cancelDays:C.cancelDays,notice:C.notice,auto:C.auto==='Yes',uplift:C.uplift,
  products:D.products.map(p=>({name:p[0],sku:p[1],unit:p[2],qty:p[3],price:p[4]})),
  push:D.push.map(x=>({type:x[0],norm:x[1],ask:x[2],src:x[3]})),raise:D.raise.map(x=>({type:x[0],norm:x[1],ask:x[2],src:x[3]})),
  terms:D.terms.map(t=>({label:t[0],value:t[1]})),oblYou:D.oblYou.map(o=>({text:o[0],when:o[1],priority:o[2],cadence:o[3]})),oblSup:D.oblSup.map(o=>({text:o[0],when:o[1],priority:o[2],cadence:o[3]}))};}
console.log(JSON.stringify(Object.values(out),null,1));
