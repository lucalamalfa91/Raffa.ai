
class Component extends DCLogic {
  state = this.initial();
  initial(){ return { screen:'signin', sign:0, wsName:'Helvetia Foods AG', docs:[], docView:'list', reviewId:null, review:{}, selField:0, correcting:false, correctVal:'', confTipSeen:false, showAll:false, justValidated:null, chat:[], convs:[], activeConv:null, docFilter:'attention', asking:false, q:'', cq:'', cid:1, tab:'Overview', hl:null, back:'ask', citedOpened:false, rsel:null, racts:{}, moreCols:false, quote:false, inviteEmail:'', inviteRole:'procurement', inviteSent:false, inviteError:false, dragging:false }; }
  timers=[]; fileRef=React.createRef();
  later(fn,ms){ this.timers.push(setTimeout(fn,ms)); }
  onKey=(e)=>{ if((e.metaKey||e.ctrlKey)&&e.key.toLowerCase()==='k'&&this.state.screen!=='signin'){ e.preventDefault(); this.go('ask'); } };
  componentDidMount(){ window.addEventListener('keydown',this.onKey); }
  componentWillUnmount(){ this.timers.forEach(clearTimeout); window.removeEventListener('keydown',this.onKey); }
  go(screen,extra){ const fresh=screen==='ask'&&this.state.screen!=='ask'?{activeConv:null,q:''}:{}; this.setState(Object.assign({screen},fresh,extra||{})); }
  chf(n){ return n>=1e6?'CHF '+(n/1e6).toFixed(1)+'M':'CHF '+Math.round(n/1000)+'k'; }
  confTag(c){ return c>0.95?'tag-neutral':c>=0.80?'tag-accent':'tag-outline'; }
  confLabel(c){ const p=Math.round(c*100)+'%'; return c>0.95?'Accepted · '+p:c>=0.80?'Flagged · '+p:'Review · '+p; }

  get CONTRACTS(){ return {
    1:{id:1,supplier:'Salesforce',name:'Sales Cloud Enterprise — MSA + Order Form',type:'MSA',file:'Salesforce_MSA_2024.pdf',spend:640000,start:'01 Mar 2024',end:'15 Jan 2027',days:129,cancel:'18 Oct 2026',cancelDays:40,notice:90,auto:'Yes',risk:'High',score:86,uplift:'7%',saving:'CHF 80–120k / yr',lever:'You pay CHF 156 per user against a market median of 132, and a 7% uplift clause kicks in at renewal.',action:'Start negotiation now',rationale:'The notice window closes in 40 days and the 7% uplift clause compounds at renewal. Both facts are validated and cited.',docCount:2},
    2:{id:2,supplier:'Microsoft',name:'Enterprise Agreement · M365 E5',type:'MSA',file:'Microsoft_EA_Enrollment.pdf',spend:1200000,start:'01 Jul 2024',end:'30 Jun 2027',days:295,cancel:'31 Mar 2027',cancelDays:204,notice:90,auto:'Yes',risk:'Medium',score:63,uplift:'5%',saving:'CHF 120–180k / yr',lever:'E5 utilisation is 61%. Moving the idle seats to E3 before the true-up is the lever.',action:'Prepare licence mix review',rationale:'E5 utilisation is 61%; a partial E3 downgrade is the largest lever before the true-up.',docCount:3},
    3:{id:3,supplier:'AWS',name:'Enterprise Discount Program',type:'Amendment',file:'AWS_EDP_Amendment_3.pdf',spend:2400000,start:'01 Sep 2024',end:'31 Aug 2027',days:357,cancel:'01 Jun 2027',cancelDays:266,notice:90,auto:'No',risk:'Medium',score:53,uplift:'0%',saving:'No saving yet',lever:'Commit is 94% used. The saving comes from a higher discount tier at renewal, not from acting now.',action:'Monitor commit utilisation',rationale:'Commit tracking at 94%; savings come from a higher tier at renewal, not from acting now.',docCount:4},
    4:{id:4,supplier:'DocuSign',name:'eSignature Business Pro',type:'Order Form',file:'DocuSign_OrderForm.pdf',spend:58000,start:'01 Nov 2023',end:'31 Oct 2026',days:53,cancel:'02 Oct 2026',cancelDays:24,notice:30,auto:'Yes',risk:'Low',score:48,uplift:'8%',saving:'CHF 8–12k / yr',lever:'Envelope usage is 38% of plan and the price rises 8% at renewal. Drop a tier.',action:'Send non-renewal notice',rationale:'Envelope usage is 38% of plan; move to a lower tier at renewal.',docCount:1}
  }; }
  get EVIDENCE(){ return {
    sf_term:{doc:'Salesforce_MSA_2024.pdf',page:2,sec:'2.1',before:'Subscription Term. The Initial Subscription Term commences on 1 March 2024 and ',after:'. Thereafter this Agreement renews as set out in Section 8.4.',quote:'expires on 15 January 2027'},
    sf_renew:{doc:'Salesforce_MSA_2024.pdf',page:12,sec:'8.4',before:'This Order Form shall ',after:' unless either party gives written notice of non-renewal at least ninety (90) days before the end of the then-current term.',quote:'automatically renew for successive twelve (12) month periods'},
    sf_liab:{doc:'Salesforce_MSA_2024.pdf',page:27,sec:'17.2',before:'Limitation of Liability. In no event shall either party\u2019s ',after:', except for breach of confidentiality obligations or infringement of intellectual property rights.',quote:'aggregate liability exceed the fees paid or payable in the twelve (12) months preceding the claim'},
    sf_uplift:{doc:'Salesforce_MSA_2024.pdf',page:9,sec:'6.2',before:'Upon each renewal, ',after:'; any such increase shall be notified no later than sixty (60) days before renewal.',quote:'Supplier may increase the fees by the greater of seven percent (7%) or the CPI'},
    sf_renewterm:{doc:'Salesforce_MSA_2024.pdf',page:14,sec:'9.1',before:'Unless terminated, this Order Form shall ',after:' \u2014 the Initial Subscription Term is defined in Schedule 1.',quote:'renew for successive periods equal in length to the initial Subscription Term'},
    ms_liab:{doc:'Microsoft_EA_Enrollment.pdf',page:22,sec:'14',before:'Each party\u2019s ',after:'.',quote:'liability is limited to the amounts paid under the affected Enrollment in the preceding 12 months'},
    aws_liab:{doc:'AWS_EDP_Amendment_3.pdf',page:19,sec:'11.2',before:'AWS\u2019s aggregate liability shall not exceed ',after:', with carve-outs for breach of confidentiality and IP indemnity.',quote:'the greater of USD 500,000 or the fees paid in the preceding twelve months'}
  }; }
  weakFields(){ return [
    {label:'Renewal term',value:'12 months (inferred)',src:'p.14 §9.1',ev:'sf_renewterm',conf:0.71,critical:'Critical field'},
    {label:'Price uplift at renewal',value:'7% or CPI, whichever is higher',src:'p.9 §6.2',ev:'sf_uplift',conf:0.64,critical:'Critical field'}
  ]; }
  allFields(){ return this.weakFields().concat([
    {label:'Annual fee',value:'CHF 640,000',src:'p.3 table',ev:'sf_term',conf:0.98,critical:'Critical field'},
    {label:'Subscription end',value:'15 Jan 2027',src:'p.2 §2.1',ev:'sf_term',conf:0.97,critical:'Critical field'},
    {label:'Cancellation notice',value:'90 days before term end',src:'p.12 §8.4',ev:'sf_renew',conf:0.93,critical:'Critical field'},
    {label:'Liability cap',value:'12 months fees',src:'p.27 §17.2',ev:'sf_liab',conf:0.88,critical:''},
    {label:'Payment terms',value:'Net 45, annual in advance',src:'p.8 §6.1',ev:'sf_term',conf:0.99,critical:''},
    {label:'Governing law',value:'Switzerland, Zürich',src:'p.31 §21',ev:'sf_term',conf:0.96,critical:''}
  ]); }

  pickFiles(files){ const f=files&&files[0]; if(!f) return; this.setState({dragging:false}); this.startUpload(f.name,(f.size/1048576).toFixed(1)+' MB'); }
  startUpload(name,size){
    const id=Date.now(); const outcome=this.props.uploadOutcome||'needs_review';
    const doc={id,cid:1,file:name||'Salesforce_MSA_2024.pdf',size:size||'2.4 MB · 38 pages',status:'processing',stage:0,when:'just now'};
    this.setState(s=>({docs:[doc,...s.docs],docView:'list'}));
    for(let i=1;i<=6;i++) this.later(()=>this.setState(s=>({docs:s.docs.map(d=>d.id===id?Object.assign({},d,i<6?{stage:i}:{status:outcome,stage:6}):d)})),700*i);
  }

  renderVals(){
    const s=this.state, role=this.props.role||'admin', seeded=(this.props.fixtures||'none')==='seeded';
    const isAdmin=role==='admin', C=this.CONTRACTS, EV=this.EVIDENCE;
    const seededDocs=seeded?[2,3,4].map(id=>({id:'seed'+id,cid:id,file:C[id].file,size:'',status:'completed',stage:6,when:'02 Sep',seed:true})):[];
    const docs=[...s.docs,...seededDocs];
    const completedCids=[...new Set(docs.filter(d=>d.status==='completed').map(d=>d.cid))];
    const kbReady=completedCids.length>0, kbOff=!kbReady;
    const order=['documents','ask','portfolio','renewals','home','quote','workspace','c360'];
    const scr={}; order.forEach(k=>scr[k]=s.screen===k);
    const isSignin=s.screen==='signin';

    // pilot acts chrome
    const live=s.docs[0]; const validated=s.docs.some(d=>d.status==='completed'); const answered=s.chat.some(m=>m.who==='Raffa');
    const actDef=[['Sign in',!isSignin],['Upload',!!live],['Review weak facts',validated],['Ask',answered],['Follow a citation',s.citedOpened]];
    const acts=actDef.map(([label,done],i)=>{const prev=i===0||actDef[i-1][1]; return {label,dot:done?'var(--color-accent)':prev?'var(--color-neutral-400)':'var(--color-neutral-700)',fg:done?'var(--color-neutral-100)':prev?'var(--color-neutral-300)':'var(--color-neutral-500)'};});

    const needReview=docs.filter(d=>d.status==='needs_review').length;
    const primaryNav=[{k:'ask',label:'Ask Raffa',badge:'⌘K',badgeFg:'var(--color-neutral-600)',isAsk:true},{k:'documents',label:'Documents',badge:needReview?needReview+' to review':(docs.length?docs.length+' docs':''),badgeFg:needReview?'var(--color-accent-700)':'var(--color-neutral-600)'}].map(n=>Object.assign(n,{go:()=>this.go(n.k,n.k==='documents'?{docView:'list'}:{}),fg:s.screen===n.k?'var(--color-accent)':'inherit',bar:s.screen===n.k?'var(--color-accent)':'transparent'}));
    const kbNav=[['portfolio','Portfolio',kbReady?completedCids.length:''],['renewals','Renewals',kbReady?completedCids.length:''],['quote','Quote check','optional']].map(([k,label,badge])=>({label,badge:String(badge),go:()=>this.go(k),fg:s.screen===k?'var(--color-accent)':kbReady?'inherit':'var(--color-neutral-500)',w:s.screen===k?600:400,bar:s.screen===k?'var(--color-accent)':'transparent'}));

    const cur=Object.assign({},C[s.cid]||C[1]);
    cur.spendFmt=this.chf(cur.spend); cur.cancelFg=cur.cancelDays<=45?'var(--color-accent-700)':'inherit'; cur.autoText=cur.auto==='Yes'?' and auto-renews for 12 months':'';
    const curDoc=docs.find(d=>d.cid===cur.id); cur.status=curDoc?curDoc.status.replace('_',' '):'not uploaded'; cur.statusTag=cur.status==='completed'?'tag-neutral':cur.status==='needs review'?'tag-outline':'tag-accent';

    // Documents rows
    const stageLabels=['Uploading','Classifying','OCR / text','Sections & tables','Extracting facts','Validating schema'];
    const attnDocs=docs.filter(d=>d.status!=='completed'); const showAttn=s.docFilter==='attention';
    const docRows=(showAttn?attnDocs:docs).map(d=>{const c=C[d.cid]; const st=d.status; const map={processing:{tag:'tag-neutral',bar:'var(--color-neutral-400)'},needs_review:{tag:'tag-outline',bar:'var(--color-accent)'},completed:{tag:'tag-neutral',bar:'transparent'},failed:{tag:'tag-accent',bar:'var(--color-accent)'}}[st];
      const open=()=>{ if(st==='needs_review') this.setState({docView:'review',reviewId:d.id,selField:0,correcting:false}); else if(st==='completed') this.go('c360',{cid:d.cid,tab:'Overview',hl:null,back:'documents'}); };
      const action=st==='needs_review'?{label:'Review '+this.weakFields().length+' fields',cls:'btn-primary',fn:open}:st==='completed'?{label:'Ask about it',cls:'btn-secondary',fn:()=>{this.go('ask'); this.ask('When does '+c.supplier+' expire?','global');}}:st==='failed'?{label:'Retry upload',cls:'btn-secondary',fn:()=>this.startUpload(d.file,d.size)}:null;
      return {file:d.file,meta:(d.size?d.size+' · ':'')+d.when,supplier:c.supplier,type:c.type,status:st.replace('_',' '),tag:map.tag,bar:st==='needs_review'?map.bar:'transparent',bg:st==='needs_review'?'var(--color-accent-100)':'transparent',processing:st==='processing',pct:Math.round(d.stage/6*100)+'%',stageLabel:stageLabels[Math.min(d.stage,5)]+'…',hasAction:!!action,actionLabel:action?action.label:'',actionCls:action?action.cls:'',action:action?(e)=>{e.stopPropagation();action.fn();}:null,open};});
    const askable=completedCids.length; const kbSummary=docs.length+' document'+(docs.length===1?'':'s')+' · '+askable+' askable'+(needReview?' · '+needReview+' waiting for your review':'');

    // Review
    const rdocRaw=docs.find(d=>d.id===s.reviewId)||{file:''}; const rdoc={file:rdocRaw.file};
    const fields=(s.showAll?this.allFields():this.weakFields());
    const reviewFields=fields.map((f,i)=>{const r=s.review[f.label]; const auto=f.conf>=0.80; const done=auto||!!r; return Object.assign({},f,{confLabel:this.confLabel(f.conf),tag:this.confTag(f.conf),pending:!done,done,doneLabel:auto?(f.conf>0.95?'Auto-accepted':'Flagged · usable'):r==='accepted'?'Accepted by you':'Corrected → '+r,bg:s.selField===i?'var(--color-neutral-200)':'transparent',bar:s.selField===i?'var(--color-accent)':'transparent',select:()=>this.setState({selField:i,correcting:false}),accept:(e)=>{e.stopPropagation();this.setState({review:Object.assign({},s.review,{[f.label]:'accepted'})});},correct:(e)=>{e.stopPropagation();this.setState({selField:i,correcting:true,correctVal:f.value});}});});
    const selF=reviewFields[Math.min(s.selField,reviewFields.length-1)]||reviewFields[0]||{}; const sel=Object.assign({label:selF.label,src:selF.src},EV[selF.ev]||{});
    const openWeak=this.weakFields().filter(f=>!s.review[f.label]).length;
    const finishReview=()=>{ const d=rdocRaw; this.setState(st=>({docs:st.docs.map(x=>x.id===d.id?Object.assign({},x,{status:'completed'}):x),docView:'list',justValidated:d.cid})); };

    // Ask engine
    const kbNames=completedCids.map(id=>C[id].supplier);
    const chipsFor={documents:['Which documents are not askable yet?','Which fields still lack confidence?'],portfolio:['Which of these have uncapped liability?','Which contracts renew in the next 120 days?'],renewals:['Why is this at the top?','Which should we start first?'],home:['Where is the largest saving still in review?','Which contracts renew in the next 120 days?'],quote:['How does this compare with our Snowflake contract?','Which contracts renew in the next 120 days?'],ask:['When does Salesforce expire?','What liabilities do we have?'],workspace:['When does Salesforce expire?','What liabilities do we have?']};
    const c360Chips=['When must we give notice to '+cur.supplier+'?','What is our liability cap with '+cur.supplier+'?']; const askChips=(s.screen==='c360'?c360Chips:(chipsFor[s.screen]||chipsFor.ask)).map(t=>({text:t,ask:()=>{this.go('ask');this.ask(t,'global');}}));
    const mkMsg=(m,scoped)=>Object.assign({},m,{fg:m.who==='You'?'var(--color-neutral-600)':'var(--color-accent)',size:m.who==='You'?'17px':'14px',w:m.who==='You'?800:400,font:m.who==='You'?'var(--font-heading)':'inherit',hasActions:!!(m.actions&&m.actions.length),actions:(m.actions||[]).map(a=>({label:a.label,go:()=>this.go(a.screen,a.extra||{})})),hasCites:!!(m.cites&&m.cites.length),cites:(m.cites||[]).map((c,i)=>Object.assign({n:i+1},EV[c.ev]||{},{open:()=>this.go('c360',{cid:c.cid,tab:c.tab||'Documents',hl:c.ev,back:scoped?'c360':'ask',citedOpened:true})}))});
    const gchat=s.chat.filter(m=>m.scope==='global'&&m.conv===s.activeConv).map(m=>mkMsg(m,false));
    const convs=[...s.convs].reverse().slice(0,5).map(cv=>{const n=s.chat.filter(m=>m.conv===cv.id&&m.who==='You').length; const on=cv.id===s.activeConv&&s.screen==='ask'; return {title:cv.title,meta:String(n),fg:on?'var(--color-accent)':'inherit',w:on?600:400,resume:()=>this.setState({screen:'ask',activeConv:cv.id,q:''})};});
    const activeCv=s.convs.find(c=>c.id===s.activeConv);

    // Portfolio / renewals / home
    const kbContracts=completedCids.map(id=>Object.assign({},C[id])).sort((a,b)=>a.cancelDays-b.cancelDays).map(c=>Object.assign(c,{spendFmt:this.chf(c.spend),status:'completed',statusTag:'tag-neutral',cancelFg:c.cancelDays<=45?'var(--color-accent-700)':'inherit',cancelW:c.cancelDays<=45?600:400,rowBg:c.cancelDays<=45?'var(--color-accent-100)':'transparent',bar:c.cancelDays<=45?'var(--color-accent)':'transparent',open:()=>this.go('c360',{cid:c.id,tab:'Overview',hl:null,back:'portfolio'})}));
    const urgent=kbContracts.filter(c=>c.cancelDays<=45).length;
    const pfSummary=kbReady?kbContracts.length+' validated contract'+(kbContracts.length===1?'':'s')+' · '+this.chf(kbContracts.reduce((a,c)=>a+c.spend,0))+' annual'+(urgent?' · '+urgent+' notice deadline'+(urgent>1?'s':'')+' within 45 days':''):'Lights up from validated contracts';
    const renewals=completedCids.map(id=>C[id]).sort((a,b)=>b.score-a.score).map(c=>{const act=s.racts[c.id]; const sel_=(s.rsel||(completedCids.length?[...completedCids].map(i=>C[i]).sort((a,b)=>b.score-a.score)[0].id:null))===c.id; return Object.assign({},c,{select:()=>this.setState({rsel:c.id}),bg:sel_?'var(--color-neutral-200)':'transparent',bar:sel_?'var(--color-accent)':'transparent',scoreFg:c.score>=80?'var(--color-accent-700)':'inherit',cancelFg:c.cancelDays<=45?'var(--color-accent-700)':'inherit',cancelW:c.cancelDays<=45?600:400,st:act||'Open',stTag:act?'tag-accent':'tag-neutral'});});
    const rsel=renewals.find(r=>r.id===s.rsel)||renewals[0]||{};
    const rnSummary=kbReady?renewals.length+' contract'+(renewals.length===1?'':'s')+' with validated dates · sorted by priority':'Computed from validated end dates and notice periods';
    const kpis=[{label:'Contracts analyzed',value:String(askable),meta:docs.length-askable?(docs.length-askable)+' still processing or in review':'all documents validated'},{label:'Upcoming renewals',value:String(kbContracts.filter(c=>c.days<=180).length),meta:'within 180 days · '+urgent+' notice deadline'+(urgent===1?'':'s')+' < 45 d'},{label:'Savings identified',value:seeded?'CHF 300–430k':'CHF 80–120k',meta:seeded?'4 opportunities':'1 opportunity · Salesforce renewal'}];
    const opps=completedCids.filter(id=>C[id].uplift!=='0%').map(id=>({supplier:C[id].supplier,name:C[id].action,est:id===1?'CHF 80–120k':id===2?'CHF 120–180k':'CHF 8–12k',status:s.racts[id]||'Identified',stTag:s.racts[id]?'tag-accent':'tag-neutral',open:()=>this.go('c360',{cid:id,tab:'Overview',hl:null,back:'home'})}));

    // Contract 360
    const baseTabs=['Overview','Clauses','Documents']; const moreTabNames=['Commercials','Products','Obligations','Risks','Benchmark','Renewal','Activity'];
    const isOther=moreTabNames.includes(s.tab);
    const tabs=baseTabs.map(n=>({name:n,go:()=>this.setState({tab:n}),fg:s.tab===n?'var(--color-text)':'var(--color-neutral-600)',w:s.tab===n?600:400,bar:s.tab===n?'var(--color-accent)':'transparent'}));
    if(s.moreTabs||isOther) moreTabNames.forEach(n=>tabs.push({name:n,go:()=>this.setState({tab:n}),fg:s.tab===n?'var(--color-text)':'var(--color-neutral-600)',w:s.tab===n?600:400,bar:s.tab===n?'var(--color-accent)':'transparent'}));
    else tabs.push({name:'More ▾',go:()=>this.setState({moreTabs:true}),fg:'var(--color-neutral-600)',w:400,bar:'transparent'});
    const tab={Overview:s.tab==='Overview',Clauses:s.tab==='Clauses',Documents:s.tab==='Documents',Other:isOther};
    const kt=(label,value,src,c)=>({label,value,src,conf:this.confLabel(c),tag:this.confTag(c)});
    const attnTerms=cur.id===1?this.weakFields().filter(f=>!s.review[f.label]).map(f=>kt(f.label,f.value,f.src,f.conf)):[];
    const topRisks=[{level:'High',tag:'tag-accent',text:'Auto-renewal with '+cur.notice+'-day notice — deadline '+cur.cancel},{level:cur.uplift==='0%'?'Low':'High',tag:cur.uplift==='0%'?'tag-neutral':'tag-accent',text:cur.uplift==='0%'?'No uplift clause':'Uplift clause: '+cur.uplift+(cur.id===1?' or CPI, whichever is higher':'')}];
    const cl=(type,normalized,src,ev,risk,c)=>({type,normalized,src,ev,risk,riskTag:risk==='High'?'tag-accent':'tag-neutral',conf:this.confLabel(c),tag:this.confTag(c),bg:s.hl===ev?'var(--color-accent-100)':'transparent',show:()=>this.setState({hl:ev})});
    const clauses=cur.id===1?[cl('Auto-renewal','Renews for successive 12-month terms unless notice ≥90 days before term end.','p.12 §8.4','sf_renew','High',0.97),cl('Price increase','Up to 7% or CPI at each renewal.','p.9 §6.2','sf_uplift','High',s.review['Price uplift at renewal']?0.99:0.64),cl('Limitation of liability','Capped at 12 months of fees; carve-outs for confidentiality and IP.','p.27 §17.2','sf_liab','Medium',0.88),cl('Termination for convenience','Not permitted during the term.','p.26 §16.1','sf_term','Medium',0.94),cl('Data processing','DPA incorporated; EU SCCs.','p.30 §20','sf_term','Low',0.96)]
      :cur.id===3?[cl('Limitation of liability','Greater of USD 500k or 12 months fees; carve-outs for confidentiality and IP.','p.19 §11.2','aws_liab','Medium',0.95),cl('Commit shortfall','Unused commit is invoiced at term end.','p.6 §4','aws_liab','High',0.93)]
      :cur.id===2?[cl('Limitation of liability','Amounts paid under the affected Enrollment in the preceding 12 months.','p.22 §14','ms_liab','Medium',0.96),cl('Price protection','Prices fixed for the Enrollment term; true-up annually.','p.9 §5','ms_liab','Low',0.97)]
      :[cl('Auto-renewal','Renews for 12 months unless 30-day notice.','p.2 §4','sf_renew','High',0.96),cl('Price increase','Up to 8% at renewal.','p.2 §5','sf_renew','High',0.91)];
    const family=cur.id===1?[{type:'MSA',file:'Salesforce_MSA_2024.pdf',status:cur.status,tag:cur.statusTag},{type:'Order Form',file:'Salesforce_OrderForm_2024.pdf',status:'completed',tag:'tag-neutral'}]:[{type:cur.type,file:cur.file,status:'completed',tag:'tag-neutral'}];
    const hl=s.hl?EV[s.hl]:null;
    const done360=(s.steps360||{})[cur.id]||[]; const stepDefs=[['Notify '+cur.supplier+' of intent to renegotiate','this week'],['Request revised pricing and licence mix','+10 days'],['Counter with the market benchmark','+20 days'],['Sign, or send non-renewal notice','by '+cur.cancel]];
    const steps360=stepDefs.map(([label,due],i)=>{const on=!!done360[i]; return {label,due,box:on?'var(--color-text)':'transparent',deco:on?'line-through':'none',fg:on?'var(--color-neutral-600)':'inherit',toggle:()=>{const arr=[...done360]; arr[i]=!on; this.setState({steps360:Object.assign({},s.steps360||{},{[cur.id]:arr})});}};});
    const otherRows=[kt('Annual spend',cur.spendFmt,'p.3 table',0.98),kt('Start → end',cur.start+' → '+cur.end,'p.2 §2.1',0.97),kt('Notice period',cur.notice+' days','p.12 §8.4',0.93),kt('Uplift at renewal',cur.uplift,'p.9 §6.2',cur.id===1&&!s.review['Price uplift at renewal']?0.64:0.9),kt('Auto-renewal',cur.auto,'p.12 §8.4',0.97)];
    const backLabels={ask:'Ask Raffa',documents:'Documents',portfolio:'Portfolio',renewals:'Renewals',c360:'Ask Raffa'};

    const members=[{name:'Luca Lamalfa',email:'luca.lamalfa@helvetiafoods.ch',role:isAdmin?'Workspace Admin':'Procurement',status:'Active',tag:'tag-neutral'},{name:'Marta Keller',email:'marta.keller@helvetiafoods.ch',role:'Workspace Admin',status:'Active',tag:'tag-neutral'}];
    if(s.inviteSent) members.push({name:s.inviteEmail.split('@')[0],email:s.inviteEmail,role:s.inviteRole==='admin'?'Workspace Admin':'Procurement',status:'Invited',tag:'tag-accent'});

    return {
      acts, resetDemo:()=>{this.timers.forEach(clearTimeout); this.setState(this.initial());},
      isSignin, isApp:!isSignin, sign0:s.sign===0, sign1:s.sign===1, signCreate:s.sign===2&&!seeded, signPick:s.sign===2&&seeded, seededCount:3,
      signIn:()=>{this.setState({sign:1}); this.later(()=>this.setState({sign:2}),900);}, enterWs:()=>this.go('ask'),
      wsName:s.wsName, setWsName:e=>this.setState({wsName:e.target.value}), roleLabel:isAdmin?'Workspace Admin':'Procurement', isAdmin, notAdmin:!isAdmin,
      scr, primaryNav, kbNav, kbDot:kbReady?'var(--color-accent)':'var(--color-neutral-400)', kbReady, kbOff,
      goWorkspace:()=>this.go('workspace'), goAsk:()=>this.go('ask'), goDocuments:()=>this.go('documents',{docView:'list'}),
      showNewChat:scr.ask&&!!activeCv, askHello:'What do you want to know?', askBarDot:kbReady?'var(--color-accent)':'var(--color-neutral-400)', askChips:kbReady?askChips:[], askPlaceholder:kbReady?'Ask Raffa — spend, dates, clauses, liability…':'Ask Raffa switches on after your first validated contract',
      docsEmpty:scr.documents&&s.docView==='list'&&docs.length===0, docsList:s.docView==='list'&&docs.length>0, docsReview:s.docView==='review',
      fileRef:this.fileRef, browse:()=>this.fileRef.current&&this.fileRef.current.click(), filesChosen:e=>this.pickFiles(e.target.files), useSample:()=>this.startUpload(),
      dragOver:e=>{e.preventDefault(); if(!s.dragging) this.setState({dragging:true});}, dragLeave:()=>this.setState({dragging:false}), dropFiles:e=>{e.preventDefault(); this.pickFiles(e.dataTransfer.files);},
      dropBg:s.dragging?'var(--color-accent-100)':'var(--color-surface)', dropBorder:s.dragging?'var(--color-accent)':'var(--color-divider)',
      kbSummary, docRows, attnCount:attnDocs.length, allCount:docs.length, attnEmpty:showAttn&&attnDocs.length===0, rowsVisible:docRows.length>0,
      filterAttn:()=>this.setState({docFilter:'attention'}), filterAll:()=>this.setState({docFilter:'all'}),
      attnBg:showAttn?'var(--color-text)':'transparent', attnFg:showAttn?'var(--color-bg)':'inherit', allBg:showAttn?'transparent':'var(--color-text)', allFg:showAttn?'inherit':'var(--color-bg)',
      filterHint:showAttn?'Completed documents are hidden — they are already askable.':'Everything, including validated documents.',
      convs, noConvs:!s.convs.length, hasActiveConv:!!activeCv, convTitle:activeCv?activeCv.title:'Ask Raffa · new chat',
      newChat:()=>this.setState({activeConv:null,q:''}), closeChat:()=>this.setState({activeConv:null,q:''}),
      justValidated:!!s.justValidated, justValidatedName:s.justValidated?C[s.justValidated].supplier:'', askValidated:()=>{const n=C[s.justValidated].supplier; this.go('ask'); this.ask('When does '+n+' expire?','global');},
      rdoc, reviewTitle:openWeak?openWeak+' fact'+(openWeak>1?'s':'')+' below 80% — you decide':'All weak facts decided', reviewSub:openWeak?'Raffa will not use these for renewals or Ask until you accept or correct them. Everything else was extracted above threshold.':'Mark the contract as validated to make it askable.',
      backToDocs:()=>this.setState({docView:'list'}), finishReview, reviewBlocked:openWeak>0, confTip:!s.confTipSeen, dismissTip:()=>this.setState({confTipSeen:true}),
      reviewFields, sel, toggleAllFields:()=>this.setState({showAll:!s.showAll}), allFieldsLabel:s.showAll?'Show only weak facts':'Show all 41 extracted facts',
      correcting:s.correcting, correctVal:s.correctVal, setCorrectVal:e=>this.setState({correctVal:e.target.value}), saveCorrection:()=>this.setState({review:Object.assign({},s.review,{[selF.label]:s.correctVal}),correcting:false}), cancelCorrection:()=>this.setState({correcting:false}),
      askOffReason:docs.length?'Your document is still processing or waiting for review. Ask only answers from facts that passed validation — so it never guesses.':'Upload a contract first. Raffa extracts the facts, you sign off the weak ones, and Ask switches on.', askOffCta:docs.length?'Go to Documents':'Upload a contract',
      askScope:'Answers only from '+askable+' validated contract'+(askable===1?'':'s')+' ('+kbNames.join(', ')+') · cites or abstains',
      q:s.q, setQ:e=>this.setState({q:e.target.value}), qKey:e=>{if(e.key==='Enter'&&s.q.trim()){ if(s.screen!=='ask') this.go('ask'); this.ask(s.q,'global'); }}, send:()=>this.ask(s.q,'global'), gchat, chatEmpty:!gchat.length, asking:s.asking,
      cur, otherRows, showDetails:!!s.more360, toggleDetails:()=>this.setState({more360:!s.more360}), detailsLabel:s.more360?'Hide details':'All terms, documents and open facts ▾',
      c360Open:!s.racts[cur.id], c360Acted:!!s.racts[cur.id], c360Status:s.racts[cur.id]||'', act360:()=>this.setState({racts:Object.assign({},s.racts,{[cur.id]:'In negotiation'})}), undo360:()=>{const r=Object.assign({},s.racts); delete r[cur.id]; this.setState({racts:r});}, goRenewals360:()=>this.go('renewals',{rsel:cur.id}), steps360, assign360:()=>this.setState({racts:Object.assign({},s.racts,{[cur.id]:'Assigned to Marta Keller'})}), attnTerms, noAttn:!attnTerms.length, topRisks, clauses, family, hl:hl||{}, hasHl:!!hl,
      backFrom360:()=>this.go(s.back==='c360'?'ask':s.back), backLabel:backLabels[s.back]||'Back',
      kbContracts, pfSummary, moreCols:s.moreCols, toggleCols:()=>this.setState({moreCols:!s.moreCols}), colsLabel:s.moreCols?'Fewer columns':'More columns',
      renewals, rsel, rnSummary, rselOpen:!s.racts[rsel.id], rselActed:!!s.racts[rsel.id], rAct:{negotiate:()=>this.setState({racts:Object.assign({},s.racts,{[rsel.id]:'In negotiation'})}),assign:()=>this.setState({racts:Object.assign({},s.racts,{[rsel.id]:'Assigned'})})}, open360R:()=>this.go('c360',{cid:rsel.id,tab:'Overview',hl:null,back:'renewals'}),
      kpis, opps, hasOpps:opps.length>0, noOpps:!opps.length,
      noQuote:!s.quote, hasQuote:s.quote, loadQuote:()=>this.setState({quote:true}),
      members, inviteEmail:s.inviteEmail, setInviteEmail:e=>this.setState({inviteEmail:e.target.value,inviteSent:false,inviteError:false}), inviteIsAdmin:s.inviteRole==='admin', inviteIsProc:s.inviteRole==='procurement', pickAdmin:()=>this.setState({inviteRole:'admin'}), pickProc:()=>this.setState({inviteRole:'procurement'}),
      sendInvite:()=>{const ok=/^[^@\s]+@helvetiafoods\.ch$/i.test(s.inviteEmail); this.setState({inviteSent:ok,inviteError:!ok});}, inviteSent:s.inviteSent, inviteError:s.inviteError
    };
  }

  ask(text,scope){
    if(!text||!text.trim()) return;
    const s=this.state, C=this.CONTRACTS, seeded=(this.props.fixtures||'none')==='seeded';
    const seededDocs=seeded?[2,3,4].map(id=>({cid:id,status:'completed'})):[];
    const kb=[...new Set([...s.docs,...seededDocs].filter(d=>d.status==='completed').map(d=>d.cid))];
    const has=id=>kb.includes(id); const lc=text.toLowerCase(); const any=(...k)=>k.some(w=>lc.includes(w));
    const scoped=scope!=='global'; const named=Object.values(C).find(c=>lc.includes(c.supplier.toLowerCase())); const sc=scoped?scope:(named?named.id:null);
    const word=(...k)=>k.some(w=>new RegExp('\\b'+w+'\\b','i').test(lc));
    const unknownSupplier=!named&&['databricks','snowflake','sap','oracle','google','workday','servicenow','zoom','slack','adobe'].find(x=>lc.includes(x));
    let a;
    if(any('benchmark','compar','confront','competitor','concorrent','in linea','allineat','market','mercato','fair price','prezzo giusto','too much','troppo')){
      const who=named?named.supplier:unknownSupplier?unknownSupplier[0].toUpperCase()+unknownSupplier.slice(1):'that supplier';
      a=named?{text:'Benchmarking is what Quote check does. Your '+named.supplier+' contract is validated, so I can compare its unit prices against the market and your own history there.',route:'Intent: benchmark → Quote check · supplier in validated contracts',actions:[{label:'Benchmark '+named.supplier+' in Quote check →',screen:'quote'}]}
        :{text:'That is a job for Quote check: it compares a quote or contract with market benchmarks and with what you already pay. '+who+' is not among your validated contracts yet — upload the quote (or the signed contract) and Quote check benchmarks it in minutes.',route:'Intent: benchmark → Quote check · supplier not in validated contracts',actions:[{label:'Open Quote check →',screen:'quote'},{label:'Upload the '+who+' contract',screen:'documents',extra:{docView:'list'}}]};
    } else if(any('what can you','cosa puoi','cosa sai fare','help','aiuto','how do i','come faccio')){
      a={text:'I answer from your validated contracts and route you to the right part of Raffa:\n• Documents — upload contracts, review weak facts.\n• Portfolio — every contract, spend, liability and risk in one table.\n• Renewals — deadlines and the action for each.\n• Quote check — benchmark a new quote against the market and your history.\nAsk me about dates, spend, notice periods, clauses — or which of these to open.',route:'Intent: capabilities',actions:[{label:'Renewals →',screen:'renewals'},{label:'Portfolio →',screen:'portfolio'},{label:'Quote check →',screen:'quote'}]};
    } else if(unknownSupplier&&!named){
      const who=unknownSupplier[0].toUpperCase()+unknownSupplier.slice(1);
      a={abstain:true,text:'',reason:'No '+who+' contract has been uploaded and validated, so I have nothing reliable to answer from. Upload it in Documents, or use Quote check if you only hold a quote.',route:'Intent: contract fact · supplier not found',actions:[{label:'Upload the '+who+' contract',screen:'documents',extra:{docView:'list'}},{label:'Quote check →',screen:'quote'}]};
    } else if(any('expire','scad','end','when does')&&(any('salesforce')||sc===1)){
      a=has(1)?{text:'Salesforce — Sales Cloud Enterprise ends on 15 January 2027. It auto-renews for 12 months unless you give written notice by 18 October 2026 (90 days) — that is in 40 days.',route:'Structured query on validated fields · subscription_end, notice_days',cites:[{ev:'sf_term',cid:1,tab:'Documents'},{ev:'sf_renew',cid:1,tab:'Clauses'}]}:{abstain:true,text:'',reason:'Salesforce is not validated yet. Finish its review in Documents and ask again.'};
    } else if(any('notice','preavviso','disdett')){
      const id=sc||1; a=has(id)?{text:'Give written notice to '+C[id].supplier+' by '+C[id].cancel+' — '+C[id].notice+' days before the '+C[id].end+' term end. That is in '+C[id].cancelDays+' days.',route:'Structured query · notice_days, subscription_end',cites:[{ev:id===1?'sf_renew':'sf_renew',cid:id,tab:'Clauses'}]}:{abstain:true,text:'',reason:'No validated notice period for this supplier yet.'};
    } else if(any('liabil','responsabil')||word('cap','capped','massimale')){
      const ids=(sc?[sc]:(any('aws')?[3]:any('microsoft')?[2]:kb)).filter(has);
      const lines={1:'Salesforce: each party\u2019s aggregate liability is capped at the fees paid in the 12 months before the claim; carve-outs for confidentiality and IP.',2:'Microsoft: liability limited to amounts paid under the affected Enrollment in the preceding 12 months.',3:'AWS: capped at the greater of USD 500,000 or 12 months of fees; carve-outs for confidentiality and IP indemnity.',4:'DocuSign: capped at 12 months of fees.'};
      const evs={1:'sf_liab',2:'ms_liab',3:'aws_liab',4:'sf_liab'};
      a=ids.length?{text:ids.map(i=>lines[i]).join('\n'),route:'Clause retrieval (RAG) on validated documents · limitation_of_liability · '+ids.length+' document'+(ids.length>1?'s':''),cites:ids.map(i=>({ev:evs[i],cid:i,tab:'Clauses'})),abstainNote:ids.length<kb.length?null:null}:{abstain:true,text:'',reason:'No validated liability clause in scope.'};
      if(!sc&&kb.length&&ids.length&&any('uncapped','unlimited','illimitat')) a.text='No validated contract contains an uncapped liability clause.\n'+a.text;
    } else if(any('legal','legale','lawyer','avvocat')&&any('paid','pagat','cost','spes','fee')){
      a={abstain:true,text:'',reason:'Legal fees are not a contract fact — no validated contract states what was paid to counsel. I would rather say so than estimate.',route:'Intent: structured · no matching validated field · 0 evidence passages above threshold'};
    } else if(any('120','days','giorni','renew','rinnov','scadono')){
      const list=kb.map(i=>C[i]).filter(c=>c.days<=120).sort((a,b)=>a.days-b.days);
      a=list.length?{text:list.length+' contract'+(list.length>1?'s':'')+' renew'+(list.length>1?'':'s')+' in the next 120 days:\n'+list.map(c=>'• '+c.supplier+' — '+c.end+' ('+c.days+' d) · notice by '+c.cancel).join('\n'),route:'Structured query on validated renewal fields · '+kb.length+' contracts in scope',cites:list.map(c=>({ev:c.id===1?'sf_term':'sf_renew',cid:c.id,tab:'Documents'}))}:{text:'No validated contract renews in the next 120 days. Earliest: '+(kb.length?C[kb.sort((x,y)=>C[x].days-C[y].days)[0]].supplier+' on '+C[kb[0]].end:'—')+'.',route:'Structured query on validated renewal fields'};
    } else if(any('askable','not yet','confidence','fiducia','fields','campi','missing')){
      const pend=s.docs.filter(d=>d.status!=='completed');
      a={text:pend.length?pend.length+' document'+(pend.length>1?'s are':' is')+' not askable yet: '+pend.map(d=>d.file+' ('+d.status.replace('_',' ')+')').join(', ')+'. Fields below 80%: renewal term (71%), price uplift (64%).':'Every uploaded document is validated and askable.',route:'Document status · document_status, field_confidence'};
    } else if(any('top','first','why','perch','start')){
      const t=kb.map(i=>C[i]).sort((a,b)=>b.score-a.score)[0];
      a=t?{text:t.supplier+' is first: priority '+t.score+'/100 — notice deadline in '+t.cancelDays+' days, '+t.uplift+' uplift at renewal, '+this.chf(t.spend)+' annual spend. '+t.rationale,route:'Deterministic priority score · components stored per contract',cites:[{ev:t.id===1?'sf_uplift':'sf_renew',cid:t.id,tab:'Clauses'}]}:{abstain:true,text:'',reason:'No validated contract to rank yet.'};
    } else if(any('saving','risparm','largest')){
      a=has(1)?{text:'The largest identified saving is the Salesforce renewal: CHF 80–120k per year (you pay CHF 156/user vs a market median of 132). The price-uplift clause behind it was extracted at 64% confidence and needs your sign-off before Raffa counts it.',route:'Benchmark match on validated unit price · adapter A, n = 214',cites:[{ev:'sf_uplift',cid:1,tab:'Clauses'}]}:{abstain:true,text:'',reason:'No validated contract has matched a benchmark yet.'};
    } else {
      a={abstain:true,text:'',reason:'Nothing in the '+kb.length+' validated contract'+(kb.length===1?'':'s')+' supports a reliable answer. Try a question about dates, spend, notice periods or clauses.',route:'Intent: unknown · 0 evidence passages above threshold'};
    }
    let conv=s.screen==='ask'?s.activeConv:null, convs=s.convs;
    if(scope==='global'&&!conv){ conv='cv'+Date.now(); const title=text.trim().replace(/\s+/g,' '); convs=[...convs,{id:conv,title:title.length>48?title.slice(0,46)+'…':title,when:'Today'}]; }
    this.setState({chat:[...s.chat,{who:'You',text,scope,conv}],convs,activeConv:scope==='global'?conv:s.activeConv,q:'',cq:'',asking:true});
    this.later(()=>this.setState(st=>({asking:false,chat:[...st.chat,Object.assign({who:'Raffa',scope,conv},a)]})),1000);
  }
}
