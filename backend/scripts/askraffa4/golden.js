// Replays flows.json through the prototype's RC (lang en) and writes the English golden snapshots.
const W=require('./load');const fs=require('fs');
const lang=process.argv[2]||'en';
const FL=JSON.parse(fs.readFileSync(process.argv[3],'utf8'));
const RC=W.RC,RD=W.RD;
const EN={'Salta alla data di controllo (demo)':'Jump to the check date (demo)','Aggiungi il contratto ricevuto (demo)':'Add the contract received (demo)','Incolla la risposta del fornitore':'Paste the supplier reply','Prepara la disdetta':'Prepare the non-renewal notice','Valuta alternative':'Evaluate alternatives'};
const T0=x=>lang==='en'?EN[x]:x;
function ctx(cid,viewer){return {lang,C:RD.CONTRACTS[cid],D:RD.DEEP[cid],viewer};}
function compose(cc,v){
  const cards=v.cards.filter(x=>x.k!=='outlook');let ci=cards.map((x,i)=>x.current?i:-1).filter(i=>i>=0);const cur=ci.length?cards[ci[ci.length-1]]:cards.filter(x=>!x.plain).pop()||cards[cards.length-1];const curI=cards.indexOf(cur);
  const done=cards.slice(0,curI).filter(x=>!x.plain).map((x,i)=>[x.kicker,x.title,x.email?x.email.state:(i===0?'':(x.lines[0]||''))].join(' | '));
  const msgs=cards.slice(curI+1).filter(x=>x.plain).slice(-4).map(x=>(x.by==='Raffa.ai'?'Raffa.ai':x.by.split(' ')[0])+': '+x.text);
  const B=cur.buttons.map(b=>Object.assign({},b));const ph0=cc.phase,hasA=x=>B.some(b=>b.act===x);const bx=B.find(b=>b.act==='toEx'),br=B.find(b=>b.act==='requestAppr');if(bx&&br&&br.off){bx.primary=true;br.primary=false;}
  if(ph0==='waiting'&&v.demo.some(d=>d.act==='ff'))B.push({label:T0('Salta alla data di controllo (demo)'),act:'ff',p:{},primary:true});
  if(cc.agreed&&['waiting','waiting_due','doc_wait'].indexOf(ph0)>-1&&!hasA('addDoc')){if(ph0!=='waiting')B.forEach(b=>{b.primary=false;});B.push({label:T0('Aggiungi il contratto ricevuto (demo)'),act:'addDoc',p:{},primary:ph0!=='waiting'});}
  if(ph0==='waiting'&&!cc.agreed&&!hasA('openReply'))B.push({label:T0('Incolla la risposta del fornitore'),act:'openReply',p:{},primary:false});
  const ord=B.filter(x=>x.primary).concat(B.filter(x=>!x.primary));
  const vis=ord.slice(0,3).map(b=>(b.primary?'*':'')+b.label+' ['+b.act+']'+(b.off?' (off: '+(b.why||'')+')':''));
  const more=ord.slice(3).map(b=>b.label+' ['+b.act+']').concat(v.actions.map(a=>'+ '+a.label+' ['+a.act+']'),(!cc.notice?[T0('Prepara la disdetta')+' [startNotice]']:[]),(!cc.alt?[T0('Valuta alternative')+' [startAlt]']:[]),v.demo.map(d=>'Demo: '+d.label+' ['+d.act+']'));
  const cell=x=>x.v;
  return {k:cur.k,kicker:cur.kicker||(cur.by==='Raffa.ai'?'Raffa.ai':cur.by),title:cur.title||cur.text,banner:cur.banner||'',lines:cur.lines,rows:cur.rows.map(r=>r.k+': '+r.v),
    table:cur.table?[cur.table.cols.join(' | ')].concat(cur.table.rows.map(r=>r.map(cell).join(' | '))):[],steps:cur.steps.map(s=>s.label+(s.now?' (now)':s.on?' (on)':'')),
    email:cur.email?{to:cur.email.to,toMissing:!!cur.email.toMissing,state:cur.email.state,editing:!!cur.email.editing,attachNote:cur.email.attachNote||'',sentNote:cur.email.sentNote||''}:null,
    pre:cur.pre||'',fields:cur.fields.map(f=>f.name+': '+f.val+' '+f.conf+' '+f.state),chips:cur.chips.map(c=>(c.on?'[x] ':'')+c.label+' ['+c.act+']'),inputs:cur.inputs.map(i=>i.label+'='+i.value+' ('+i.ui+')'),
    buttons:vis,more,note:cur.note||'',bullets:cur.bullets||[],stats:(cur.stats||[]).map(s=>s.k+': '+s.v),done,msgs};
}
function snap(cc,cid,who,type,final){
  const x=ctx(cid,who),v=RC.view(cc,x);
  const s={type,who,phase:cc.phase,stage:RC.stage(cc),warn:cc.ui.warn||'',hdr:{title:v.hdr.title,status:v.hdr.status,next:v.hdr.next,noticeBy:v.hdr.noticeBy,noticeDays:v.hdr.noticeDays,end:v.hdr.end,tracks:v.hdr.tracks,people:v.hdr.people.map(p=>p.name+': '+p.role),youRole:v.hdr.youRole},
    page:compose(cc,v),
    summary:{goals:v.summary.goals.map(g=>g.k+': '+g.v),terms:v.summary.terms.map(t=>t.label+': '+t.val+' · '+t.state),decisions:v.summary.decisions.map(d=>d.by+' · '+d.at+' · '+d.text),refs:v.summary.refs},
    check:v.check.map(c=>(c.done?'[x] ':'[ ] ')+c.label+(c.who?' · '+c.who:'')),example:v.example,
    todo:RC.todo(cc,x).map(t=>t.kind+' · '+t.who+' · '+t.title+' · '+t.why+' · '+t.due+' ('+t.dueDays+')'),
    emailsList:v.emailsList.map(e=>e.label+': '+e.state),docsList:v.docsList.map(d=>d.label+': '+d.state),upcoming:v.upcoming.map(u=>u.date+' '+u.label+' '+u.when),
    termsCmp:v.termsCmp.map(t=>[t.label,t.current,t.from,t.proposed,t.propFrom,t.state].join(' | ')),
    stepsX:v.stepsX.map(s=>s.n+' '+s.label+(s.now?' (now)':s.done?' (done)':'')),
    since:v.since?v.since.lines:[],shortStatus:v.shortStatus,
    timeline:v.timeline.map(t=>t.at+' '+t.time+' '+t.by+' · '+t.label+(t.label==='Raffa.ai'?'':' · '+t.text)),
    threads:v.threads.map(t=>t.t+(t.on?' (on)':'')),reply:v.reply};
  if(lang==='it'){const k={};['type','who','warn','hdr','page','summary','check','example','todo','emailsList','docsList','upcoming','termsCmp','stepsX'].forEach(x=>k[x]=s[x]);return k;}
  if(final){s.cards=v.cards.map(c=>[c.k,c.kicker,c.title,c.dim?'dim':'',c.current?'current':''].join(' | '));s.emails=cc.emails.map(e=>({kind:e.kind,ver:e.ver,state:e.state,to:e.to,subject:e.subject,body:e.body,approvedBy:e.approvedBy,sent:e.sent}));s.outcome=cc.outcome;s.agreed=cc.agreed||null;s.limits=cc.limits;}
  return s;
}
const out={};
for(const [name,fl] of Object.entries(FL)){
  let c=RC.create(fl.cid,ctx(fl.cid,fl.creator),fl.branch||null,{owner:fl.owner});
  const snaps=[snap(c,fl.cid,fl.creator,'create',false)];
  fl.steps.forEach(([who,type,p],i)=>{try{c=RC.act(c,type,p||{},ctx(fl.cid,who));}catch(e){throw new Error(name+' step '+i+' '+type+': '+e.message);}snaps.push(snap(c,fl.cid,who,type,i===fl.steps.length-1));});
  out[name]=snaps;
}
console.log(JSON.stringify(out,null,1));
