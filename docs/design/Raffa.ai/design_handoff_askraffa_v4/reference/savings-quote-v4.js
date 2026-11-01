/* Raffa.ai 4.0 — Savings (Portfolio view) + Quote check (prototype). Pure data and functions: SQ.report / SQ.assess / SQ.fromCase.
   Data is fictional. No market benchmark exists in this release: a quote is compared only with the customer's own contract price,
   the target the user set, other offers actually received, and the company policy. */
window.SQ=(function(){
var MON=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'],TODAY='08 Sep 2026';
function pd(s){var m=/(\d+) (\w+) (\d+)/.exec(s);return new Date(m[2]+' '+m[1]+', '+m[3]);}
function k(n){return n>=1e6?'CHF '+(n/1e6).toFixed(1)+'M':'CHF '+Math.round(n/1000)+'k';}
function kr(a,b){return 'CHF '+Math.round(a/1000)+'–'+Math.round(b/1000)+'k';}
function num(n){return Math.round(n).toLocaleString('en-US');}
function up(n){return n>=1000?num(n):String(n);}
function parseSaved(s){var m=/CHF\s*([\d,\.]+)\s*(k)?/i.exec(s||'');if(!m)return 0;var n=parseFloat(m[1].replace(/,/g,''));return m[2]?n*1000:n;}
function B(en,it){return {en:en,it:it};}
function tx(x,l){return x==null?'':typeof x==='string'?x:(x[l]||x.en);}
/* Contractual results already recorded: signed price vs previous contract, same quantities, with baseline, period and method.
   kind: price | term | clause = negotiated result · volume = quantity reduction (NOT a negotiation result).
   realized: true only when invoices prove the saving; realizedAmount is what the invoices show so far. */
var HIST=[
{id:'h1',supplier:'Snowflake',contract:'Capacity agreement',cat:'Data & analytics',kind:'price',lever:'Unit price',owner:'Marta Keller',amount:42000,when:'14 Mar 2026',evidence:'Snowflake_Capacity_2026_signed.pdf',note:'CHF 2.60 → 2.34 per credit, same volume (160,000 credits)',baseline:'2024 contract price · CHF 2.60 / credit',period:'12 months from 01 Apr 2026',method:'(old − new unit price) × contracted volume',realized:true,realizedEv:'Invoices Apr–Aug 2026 · 5 of 12 months',realizedAmount:17500,quote:'q2'},
{id:'h2',supplier:'SAP Ariba',contract:'Procurement suite',cat:'ERP & finance',kind:'term',lever:'Term',owner:'Luca Lamalfa',amount:61000,when:'22 Jan 2026',evidence:'SAP_Ariba_Renewal_2026_signed.pdf',note:'36 months for −9%, uplift frozen',baseline:'2025 annual fee · CHF 678k',period:'12 months from 01 Feb 2026',method:'old annual fee − new annual fee, same modules',realized:true,realizedEv:'Invoices Feb–Aug 2026 · 7 of 12 months',realizedAmount:35600},
{id:'h3',supplier:'Workday',contract:'HCM subscription',cat:'HR',kind:'volume',lever:'Volume',owner:'Marta Keller',amount:28000,when:'05 Nov 2025',evidence:'Workday_OrderForm_2025_signed.pdf',note:'1,250 → 1,100 workers; unused seats removed — quantity reduction, unit price unchanged',baseline:'2024 order form · 1,250 workers',period:'12 months from 01 Dec 2025',method:'removed seats × unchanged unit price',realized:false},
{id:'h4',supplier:'Atlassian',contract:'Cloud Enterprise',cat:'Engineering tools',kind:'volume',lever:'Volume',owner:'Marta Keller',amount:14000,when:'19 May 2026',evidence:'Atlassian_Cloud_2026_signed.pdf',note:'Tier right-sized to 800 users — quantity reduction',baseline:'2025 tier · 1,000 users',period:'12 months from 01 Jun 2026',method:'old tier fee − new tier fee',realized:false},
{id:'h5',supplier:'Adobe',contract:'Creative Cloud for teams',cat:'Productivity',kind:'price',lever:'Unit price',owner:'Luca Lamalfa',amount:35000,when:'28 Aug 2026',evidence:'Adobe_VIP_2026_signed.pdf',note:'VIP Select level 13 pricing applied, same licences',baseline:'2025 price list · level 12',period:'12 months from 01 Sep 2026',method:'(old − new licence price) × licences',realized:false},
{id:'h6',supplier:'Zoom',contract:'Business licences',cat:'Productivity',kind:'clause',lever:'Clauses',owner:'Marta Keller',amount:9000,when:'11 Jun 2026',evidence:'Zoom_Renewal_2026_signed.pdf',note:'Auto-renewal uplift 10% → 0% — increase avoided, not a reduction of today’s fee',baseline:'Contractual uplift 10% on CHF 90k',period:'renewed term from 01 Jul 2026',method:'avoided uplift × annual fee',realized:false}];
/* Saved quote checks. A line carries `existing` only when the customer's current contract states a price for the same thing. */
var QUOTES=[
{id:'q1',ref:'QC-07',supplier:'Databricks',file:'Databricks_Proposal_Q-88213.pdf',kind:'new',cid:null,date:'02 Sep 2026',by:'Marta Keller',currency:'CHF',term:'12 months',valid:'30 Sep 2026',status:'checked',
 lines:[{name:'Premium DBU — committed',sku:'DBX-PREM-DBU',qty:600000,unit:'DBU',price:0.55},{name:'SQL Serverless DBU',sku:'DBX-SQL-SRVLESS',qty:200000,unit:'DBU',price:0.70},{name:'Enterprise Support',sku:'DBX-ENT-SUPPORT',qty:1,unit:'year',price:50000}],
 stated:{payment:'Annual in advance',uplift:null,renewal:'Auto-renews 12 months unless 60 days notice',liability:null,sla:'99.9% uptime, credits not stated'},
 gaps:[B('Annual uplift not stated','Aumento annuo non indicato'),B('Limitation of liability not stated','Limite di responsabilità non indicato'),B('Service credits for SLA breaches not stated','Crediti per violazioni SLA non indicati'),B('Data egress fees not stated','Costi di uscita dati non indicati')]},
{id:'q2',ref:'QC-06',supplier:'Snowflake',file:'Snowflake_Renewal_Offer_2026.pdf',kind:'renewal',cid:null,date:'20 Feb 2026',by:'Marta Keller',currency:'CHF',term:'24 months',valid:'15 Mar 2026',status:'signed',decidedAt:'14 Mar 2026',target:{kind:'p10',label:'−10% on unit price'},
 lines:[{name:'Compute credits — Enterprise',sku:'SNOW-ENT-CREDIT',qty:160000,unit:'credit',price:2.34,existing:2.60,existingLabel:'2024 contract price'}],
 stated:{payment:'Annual in advance',uplift:'0% for the term',renewal:'By written agreement',liability:'12 months of fees',sla:'99.9%'},gaps:[]}];
/* Offers that arrive inside a renewal chat (scripted supplier replies) — checked on demand, against the customer's own contract. */
var CASEQ={
4:{supplier:'DocuSign',file:'DocuSign reply · 08 Sep 2026',term:'24 months',valid:'02 Oct 2026',lines:[{name:'eSignature Business Pro — seats',sku:'DS-BP-SEAT',qty:120,unit:'seat / yr',price:459,existing:483,existingLabel:'Your current price'}],stated:{payment:'“May be flexible, subject to credit review”',uplift:'cap at 4%',renewal:null,liability:'Standard terms — not negotiable',sla:null},gaps:[B('Payment terms conditional on a credit review','Pagamenti condizionati a una verifica di credito'),B('SLA and credits not restated','SLA e crediti non ripetuti nell’offerta')]},
3:{supplier:'AWS',file:'AWS reply · 08 Sep 2026',term:'24 months',valid:'01 Jun 2027',lines:[{name:'EDP annual commit — effective rate',sku:'AWS-EDP-COMMIT',qty:1,unit:'year',price:2280000,existing:2400000,existingLabel:'Your current commit'}],stated:{payment:'Monthly, Net 30',uplift:null,renewal:null,liability:'Standard terms',sla:null},gaps:[B('Carry-forward of unused commit not addressed','Riporto dell’impegno non usato non trattato'),B('Support fee tier not stated','Scaglione del supporto non indicato')]},
2:{supplier:'Microsoft',file:'Microsoft reply · 08 Sep 2026',term:'36 months',valid:'31 Mar 2027',lines:[{name:'Microsoft 365 E5 — per user / month',sku:'M365-E5',qty:1500,unit:'user / mo',price:54.2,existing:57,existingLabel:'Your current price'}],stated:{payment:'Annual in advance',uplift:'price protection for the term',renewal:null,liability:'12 months',sla:'99.9%'},gaps:[B('Downward adjustment of counts at anniversary not addressed','Riduzione dei conteggi all’anniversario non trattata')]},
1:{supplier:'Salesforce',file:'Salesforce reply · 08 Sep 2026',term:'24 months',valid:'18 Oct 2026',lines:[{name:'Sales Cloud Enterprise — per user / month',sku:'SFDC-SC-ENT',qty:340,unit:'user / mo',price:148,existing:156,existingLabel:'Your current price'}],stated:{payment:'Annual in advance, Net 30',uplift:'cap at 4%',renewal:'Auto-renewal kept',liability:'Standard — 12 months',sla:'99.9%'},gaps:[B('Uplift cap above the 3% you asked for','Tetto all’aumento sopra il 3% richiesto'),B('Auto-renewal clause unchanged','Clausola di rinnovo automatico invariata')]}};
function fromCase(cid,by,ref){var t=CASEQ[cid];if(!t)return null;return Object.assign({id:'qc'+cid,ref:ref,kind:'renewal',cid:cid,date:TODAY,by:by,currency:'CHF',status:'checked'},JSON.parse(JSON.stringify(t)));}
function mult(l){return /\/ mo/.test(l.unit)?12:1;}
function pct(d){return (d>0?'+':'')+(Math.abs(d)<10?d.toFixed(1):Math.round(d))+'%';}
/* assess(q, o): o.lang, o.target {kind:'none'|'p5'|'p10'|'budget'|'custom', budget?, label?}, o.planTerm ('12'|'24'|'36'), o.others [{name,total,term}], o.noticeDate, o.policy {liabMin:24} */
function assess(q,o){
  o=o||{};var L=o.lang||'en',S=function(a,b){return L==='it'?b:a;};
  var lines=q.lines.map(function(l){var m=mult(l),a=l.price*l.qty*m,ex=l.existing!=null?l.existing:null,cur=ex!=null?ex*l.qty*m:null,d=ex!=null?(l.price/ex-1)*100:null;return Object.assign({},l,{annual:a,currentAnnual:cur,delta:d,pos:d==null?'none':d<-0.5?'below':d>0.5?'above':'same'});});
  var total=0,curTotal=0,cmpN=0;lines.forEach(function(l){total+=l.annual;if(l.currentAnnual!=null){curTotal+=l.currentAnnual;cmpN++;}});
  var hasCur=cmpN>0,partial=hasCur&&cmpN<lines.length;if(!hasCur)curTotal=null;
  var own=hasCur?(total/curTotal-1)*100:null;
  var tgt=o.target&&o.target.kind&&o.target.kind!=='none'?o.target:(q.target||null);
  var targetTotal=null,targetMet=null;
  if(tgt&&hasCur){if(tgt.kind==='p5')targetTotal=curTotal*0.95;else if(tgt.kind==='p10')targetTotal=curTotal*0.90;else if(tgt.kind==='budget'&&tgt.budget)targetTotal=tgt.budget;if(targetTotal!=null)targetMet=total<=targetTotal*1.001;}
  var others=o.others||[],gaps=(q.gaps||[]).map(function(g){return tx(g,L);});
  var missing=[];if(!hasCur)missing.push(S('your current contract price for these lines','il prezzo del tuo contratto attuale per queste righe'));if(!tgt)missing.push(S('a target set by you (collected in the renewal chat)','un obiettivo indicato da te (raccolto nella chat del rinnovo)'));if(!others.length)missing.push(S('other offers for the same need','altre offerte per la stessa esigenza'));
  var basis=hasCur?'current':others.length?'others':'none';
  var verdict=basis==='none'?'nocompare':(tgt&&targetMet===false)?'misses_target':(tgt&&targetMet)?'meets_target':own<-0.5?'below_current':own>0.5?'above_current':'same';
  var points=[];
  lines.forEach(function(l){if(l.delta!=null)points.push({kind:'price',tone:l.delta>0?'alert':'ok',text:S(l.name+': CHF '+up(l.price)+' per '+l.unit+' vs CHF '+up(l.existing)+' today ('+pct(l.delta)+')',l.name+': CHF '+up(l.price)+' per '+l.unit+' contro CHF '+up(l.existing)+' oggi ('+pct(l.delta)+')'),src:S(l.existingLabel||'current contract','contratto attuale')});});
  if(tgt&&targetTotal!=null)points.push({kind:'target',tone:targetMet?'ok':'alert',text:targetMet?S('Meets your target ('+(tgt.label||tgt.kind)+'): '+k(total)+' ≤ '+k(targetTotal)+' per year','Rispetta il tuo obiettivo ('+(tgt.label||tgt.kind)+'): '+k(total)+' ≤ '+k(targetTotal)+' l’anno'):S('Misses your target ('+(tgt.label||tgt.kind)+'): '+k(total)+' vs '+k(targetTotal)+' per year','Manca il tuo obiettivo ('+(tgt.label||tgt.kind)+'): '+k(total)+' contro '+k(targetTotal)+' l’anno'),src:S('your objective, not a benchmark','il tuo obiettivo, non un benchmark')});
  else if(tgt&&!hasCur)points.push({kind:'target',tone:'neutral',text:S('Your target ('+(tgt.label||tgt.kind)+') cannot be measured: no current price for these lines','Il tuo obiettivo ('+(tgt.label||tgt.kind)+') non è misurabile: nessun prezzo attuale per queste righe'),src:''});
  if(o.planTerm&&q.term&&String(parseInt(q.term,10))!==String(o.planTerm))points.push({kind:'term',tone:'alert',text:S('Term '+q.term+' — your plan asks for '+o.planTerm+' months','Durata '+q.term+' — il tuo piano chiede '+o.planTerm+' mesi'),src:S('renewal plan','piano di rinnovo')});
  if(o.policy&&q.stated&&/12/.test(q.stated.liability||''))points.push({kind:'policy',tone:'alert',text:S('Liability 12 months — company policy requires at least '+o.policy.liabMin,'Responsabilità 12 mesi — la policy aziendale richiede almeno '+o.policy.liabMin),src:'Procurement Policy v3.2'});
  if(gaps.length)points.push({kind:'gap',tone:'alert',text:S('Not stated: '+gaps.join('; '),'Non indicato: '+gaps.join('; ')),src:S('the quote itself','il preventivo stesso')});
  if(o.noticeDate&&q.valid&&pd(q.valid)>=pd(o.noticeDate))points.push({kind:'validity',tone:'alert',text:S('Offer valid until '+q.valid+' — on or after your notice deadline '+o.noticeDate,'Offerta valida fino al '+q.valid+' — alla pari o dopo la tua scadenza di disdetta '+o.noticeDate),src:S('contract notice clause','clausola di disdetta')});
  others.forEach(function(x){points.push({kind:'other',tone:x.total<total?'alert':'ok',text:S(x.name+': '+k(x.total)+' / yr for '+x.term+' months ('+pct((x.total/total-1)*100)+' vs this quote)',x.name+': '+k(x.total)+' / anno per '+x.term+' mesi ('+pct((x.total/total-1)*100)+' rispetto a questa offerta)'),src:S('offer received','offerta ricevuta')});});
  var evaluable=[S('lines, quantities and unit prices','righe, quantità e prezzi unitari'),S('term and validity','durata e validità'),S('clauses stated or missing','clausole indicate o mancanti')];
  return {lines:lines,total:total,curTotal:curTotal,partial:partial,own:own,target:tgt,targetTotal:targetTotal,targetMet:targetMet,missing:missing,basis:basis,verdict:verdict,points:points.slice(0,5),gaps:gaps,others:others,evaluable:evaluable};
}
function counterEmail(q,a,by){
  var asks=[];
  a.lines.forEach(function(l){if(a.target&&a.targetTotal!=null&&!a.targetMet){var tp=l.existing*(a.target.kind==='p5'?0.95:a.target.kind==='p10'?0.90:(a.targetTotal/a.curTotal));asks.push('• '+l.name+': you quoted CHF '+up(l.price)+' per '+l.unit+'; our target is CHF '+up(Math.round(tp*100)/100)+' or better.');}else if(l.delta!=null&&l.delta>0)asks.push('• '+l.name+': you quoted CHF '+up(l.price)+' per '+l.unit+', above our current CHF '+up(l.existing)+'. We ask you to hold the current price.');});
  a.gaps.forEach(function(g){asks.push('• Please state in writing: '+g.replace(/ not stated$/,'').replace(/ non indicato$/,'')+'.');});
  if(!asks.length)asks.push('• Please confirm the quoted prices, term and clauses in a revised Order Form.');
  return 'Dear '+q.supplier+' team,\n\nThank you for proposal '+q.file.replace(/\.(pdf|eml)$/,'')+'. Before we can proceed we need the following:\n'+asks.join('\n')+'\n\nWe can offer a '+q.term+' commitment in exchange. Please send a revised proposal.\n\nKind regards,\n'+by+'\nProcurement, Helvetia Foods AG';
}
function inPeriod(when,period,now){var d=pd(when),n=pd(now||TODAY);if(period==='fy')return d.getFullYear()===n.getFullYear();if(period==='q'){var qs=Math.floor(n.getMonth()/3)*3;return d.getFullYear()===n.getFullYear()&&d.getMonth()>=qs&&d.getMonth()<qs+3;}var from=new Date(n);from.setFullYear(from.getFullYear()-1);return d>from&&d<=n;}
function months(period,now){var n=pd(now||TODAY),out=[],y=n.getFullYear(),m=n.getMonth();if(period==='fy'){for(var i=0;i<12;i++)out.push({y:y,m:i,future:i>m});}else if(period==='q'){var qs=Math.floor(m/3)*3;for(var j=qs;j<qs+3;j++)out.push({y:y,m:j,future:j>m});}else{for(var o=11;o>=0;o--){var mm=m-o,yy=y;while(mm<0){mm+=12;yy--;}out.push({y:yy,m:mm,future:false});}}return out.map(function(b){return Object.assign(b,{key:b.y+'-'+b.m,label:MON[b.m]+(b.m===0||out.indexOf(b)===0?' '+String(b.y).slice(2):''),amount:0,count:0,items:[]});});}
/* report(): contractual results (negotiated, signed) · realized (invoices) · quantity reductions (shown apart) · open objectives (user targets, never estimates). */
function report(inp){
  var f=inp.f||{},T=inp.targets||{},hist=inp.hist||[],open=inp.open||[];
  var match=function(x){return (!f.sup||x.supplier===f.sup)&&(!f.cat||x.cat===f.cat)&&(!f.cats||f.cats.indexOf(x.cat)>-1)&&(!f.owner||x.owner===f.owner);};
  var fh=hist.filter(match).filter(function(h){return inPeriod(h.when,f.period);}).sort(function(a,b){return pd(b.when)-pd(a.when);});
  var neg=fh.filter(function(h){return h.kind!=='volume';}),vol=fh.filter(function(h){return h.kind==='volume';});
  var contractual=neg.reduce(function(s,h){return s+h.amount;},0),realized=neg.reduce(function(s,h){return s+(h.realized?(h.realizedAmount||0):0);},0),volumeAmt=vol.reduce(function(s,h){return s+h.amount;},0);
  var fo=open.filter(match),objectives=fo.reduce(function(s,o){return s+(o.targetAmount||0);},0),undefinedN=fo.filter(function(o){return o.targetAmount==null;}).length;
  var bk=months(f.period,inp.today);neg.forEach(function(h){var d=pd(h.when),b=bk.filter(function(x){return x.y===d.getFullYear()&&x.m===d.getMonth();})[0];if(b){b.amount+=h.amount;b.count++;b.items.push(h);}});
  var peak=bk.reduce(function(m,b){return Math.max(m,b.amount);},0);
  var groups={};function g(label){return groups[label]=groups[label]||{label:label,contractual:0,realized:0,objectives:0,n:0};}
  neg.forEach(function(h){var x=g(h.supplier);x.contractual+=h.amount;x.realized+=h.realized?(h.realizedAmount||0):0;x.n++;});fo.forEach(function(o){var x=g(o.supplier);x.objectives+=o.targetAmount||0;x.n++;});
  var by=Object.keys(groups).map(function(k_){var x=groups[k_];x.total=x.contractual+x.objectives;return x;}).sort(function(a,b){return b.total-a.total;}).slice(0,6);
  var maxBy=by.reduce(function(m,x){return Math.max(m,x.total);},0);
  var opts=function(key){var all={};hist.concat(open).forEach(function(x){if(x[key])all[x[key]]=1;});return Object.keys(all).sort();};
  return {contractual:contractual,realized:realized,volumeAmt:volumeAmt,count:neg.length,volumeN:vol.length,objectives:objectives,undefinedN:undefinedN,target:T.annual||0,pctTarget:T.annual?contractual/T.annual:0,pctSpend:inp.spend?contractual/inp.spend:0,months:bk,peak:peak,by:by,maxBy:maxBy,recent:neg,volumeItems:vol,
    open:fo.slice().sort(function(a,b){return (a.days==null?9e9:a.days)-(b.days==null?9e9:b.days);}),noticeSoon:fo.filter(function(o){return o.days!=null&&o.days>=0&&o.days<=(T.noticeDays||45);}),supplierOpts:opts('supplier'),catOpts:opts('cat'),ownerOpts:opts('owner')};
}
var CATS={1:'CRM',2:'Productivity',3:'Cloud infrastructure',4:'Productivity'};
return {HIST:HIST,QUOTES:QUOTES,CASEQ:CASEQ,CATS:CATS,fromCase:fromCase,assess:assess,counterEmail:counterEmail,report:report,inPeriod:inPeriod,k:k,kr:kr,num:num,up:up,pct:pct,parseSaved:parseSaved,pd:pd,tx:tx,TODAY:TODAY};
})();
