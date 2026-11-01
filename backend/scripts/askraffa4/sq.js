// Exports SQ demo data and the prototype's own outputs for a set of assess/report/counterEmail cases.
const W=require('./load');const SQ=W.SQ;
const en=x=>x==null?null:typeof x==='string'?x:x.en;
const quotes=SQ.QUOTES.concat([4,3,2,1].map(cid=>SQ.fromCase(cid,'Marta Keller','QC-0'+(7+cid))));
const exportQuote=q=>Object.assign({},q,{gaps:(q.gaps||[])});
const cases=[];
function add(name,q,o){for(const lang of ['en','it']){const a=SQ.assess(q,Object.assign({lang},o));cases.push({name,lang,quote:q.id,opts:o,verdict:a.verdict,basis:a.basis,total:a.total,curTotal:a.curTotal,own:a.own,partial:a.partial,targetTotal:a.targetTotal,targetMet:a.targetMet,
  points:a.points.map(p=>({kind:p.kind,tone:p.tone,text:p.text,src:p.src})),missing:a.missing,gaps:a.gaps,evaluable:a.evaluable,lines:a.lines.map(l=>({annual:l.annual,currentAnnual:l.currentAnnual,delta:l.delta,pos:l.pos})),
  counter:lang==='en'?SQ.counterEmail(q,a,'Marta Keller'):null});}}
const byId=id=>quotes.find(q=>q.id===id);
add('databricks_nocompare',byId('q1'),{});
add('databricks_with_others',byId('q1'),{others:[{name:'Snowflake offer',total:420000,term:12}],noticeDate:'02 Oct 2026'});
add('snowflake_meets_target',byId('q2'),{});
add('snowflake_misses_budget',byId('q2'),{target:{kind:'budget',budget:300000,label:'Budget CHF 300k'}});
add('docusign_below',byId('qc4'),{planTerm:'12',noticeDate:'02 Oct 2026',policy:{liabMin:24}});
add('docusign_misses_p10',byId('qc4'),{target:{kind:'p10',label:'−10% on unit price'},planTerm:'24'});
add('docusign_meets_p5',byId('qc4'),{target:{kind:'p5',label:'−5% on unit price'}});
add('aws_below_policy',byId('qc3'),{policy:{liabMin:24},noticeDate:'01 Jun 2027'});
add('microsoft_policy',byId('qc2'),{policy:{liabMin:24},planTerm:'36',others:[{name:'Google Workspace',total:900000,term:36},{name:'Zoho',total:1100000,term:24}]});
add('salesforce_p10',byId('qc1'),{target:{kind:'p10',label:'−10% on unit price'},noticeDate:'18 Oct 2026',planTerm:'24'});
const above=JSON.parse(JSON.stringify(byId('qc1')));above.id='above';above.lines[0].price=163;
add('salesforce_above',above,{});
const same=JSON.parse(JSON.stringify(byId('qc1')));same.id='same';same.lines[0].price=156.5;
add('salesforce_same',same,{});
const partial=JSON.parse(JSON.stringify(byId('q1')));partial.id='partial';partial.lines[0].existing=0.6;partial.lines[0].existingLabel='2025 order form';
add('databricks_partial',partial,{});
quotes.push(above,same,partial);
const open=[{supplier:'Salesforce',cat:'CRM',owner:'Marta Keller',targetAmount:44800,days:40},{supplier:'DocuSign',cat:'Productivity',owner:'Marta Keller',targetAmount:null,days:24},{supplier:'Microsoft',cat:'Productivity',owner:'Luca Lamalfa',targetAmount:60000,days:204},{supplier:'AWS',cat:'Cloud infrastructure',owner:'Luca Lamalfa',targetAmount:120000,days:266}];
const reports=[];
for(const [name,f,targets,spend] of [['fy',{period:'fy'},{annual:500000,noticeDays:45},4298000],['q',{period:'q'},{},0],['12m',{period:'12m'},{annual:100000},4298000],['marta_fy',{period:'fy',owner:'Marta Keller'},{},0],['productivity',{period:'12m',cats:['Productivity','HR']},{noticeDays:30},0],['sup',{period:'fy',sup:'Snowflake'},{},0]]){
  const r=SQ.report({f,targets,hist:SQ.HIST,open,spend,today:'08 Sep 2026'});
  reports.push({name,f,targets,spend,out:{contractual:r.contractual,realized:r.realized,volumeAmt:r.volumeAmt,count:r.count,volumeN:r.volumeN,objectives:r.objectives,undefinedN:r.undefinedN,target:r.target,pctTarget:r.pctTarget,pctSpend:r.pctSpend,
    months:r.months.map(b=>({label:b.label,amount:b.amount,count:b.count,future:b.future})),peak:r.peak,by:r.by.map(x=>({label:x.label,contractual:x.contractual,realized:x.realized,objectives:x.objectives,n:x.n,total:x.total})),maxBy:r.maxBy,
    recent:r.recent.map(h=>h.id),volumeItems:r.volumeItems.map(h=>h.id),open:r.open.map(o=>o.supplier),noticeSoon:r.noticeSoon.map(o=>o.supplier),supplierOpts:r.supplierOpts,catOpts:r.catOpts,ownerOpts:r.ownerOpts}});
}
const fmt=[0,999,1499,1500,156000,155499,999999,1e6,2.4e6,2449999].map(n=>({n,k:SQ.k(n),up:SQ.up(n)})).concat([[-4.97],[3.2],[-12.6],[0],[25]].map(([d])=>({d,pct:SQ.pct(d)})));
console.log(JSON.stringify({hist:SQ.HIST,quotes:quotes.map(exportQuote),open,cases,reports,fmt,kr:SQ.kr(120000,180000)},null,1));
