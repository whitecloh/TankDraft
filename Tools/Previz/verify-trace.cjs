'use strict';
const fs=require('fs'),path=require('path'),assert=require('assert/strict'),crypto=require('crypto'),vm=require('vm');
const refs=require('./reference-layouts.js'),core=require('./trace-core.js'),provenance=require('./References/provenance.json');
const args=process.argv.slice(2),outIndex=args.indexOf('--export-dir'),out=outIndex<0?null:path.resolve(args[outIndex+1]);
if(out)fs.mkdirSync(out,{recursive:true});
assert.equal(refs.length,24);assert.equal(new Set(refs.map(r=>r.id)).size,24);
let layouts=0,elements=0;
for(const ref of refs){
  const record=provenance.find(p=>p.id===ref.id);assert.ok(record,ref.id);
  assert.equal(crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname,ref.image))).digest('hex'),record.sha256);
  assert.equal(record.seconds,ref.seconds);
  for(const [w,h] of [[576,1280],[1080,1920],[1080,2400]]){
    const layout=core.makeLayout(ref,w,h);assert.equal(core.validate(layout),'',ref.id);
    const scale=layout.reference.fitRect.width/ref.width;
    layout.elements.forEach((e,i)=>{
      const src=core.groups.resolve(JSON.parse(JSON.stringify(ref))).elements[i],fit=layout.reference.fitRect;
      assert.ok(Math.abs((e.x-fit.x)/scale-src.x)<1e-8);
      assert.ok(Math.abs((e.y-fit.y)/scale-src.y)<1e-8);
      assert.ok(Math.abs(e.width/scale-src.width)<1e-8);
      assert.ok(Math.abs(e.height/scale-src.height)<1e-8);
    });
    layouts++;
    if(out)fs.writeFileSync(path.join(out,`${ref.id}-${w}x${h}.json`),JSON.stringify(layout,null,2));
  }
  elements+=ref.elements.length;
}
const base=()=>core.makeLayout(refs[0]),rejected=edit=>{const l=base();edit(l);assert.notEqual(core.validate(l),'');};
rejected(l=>l.schemaVersion=3);rejected(l=>l.screen='../scene');rejected(l=>l.state='unknown');
rejected(l=>l.viewport.safeTop=l.viewport.height);rejected(l=>l.theme.accent='red');
rejected(l=>l.elements.push({...l.elements[0]}));rejected(l=>l.elements[0].id=' ');
rejected(l=>l.elements[0].x=-1);rejected(l=>l.elements[0].width=Infinity);rejected(l=>l.elements[0].row=4);
rejected(l=>l.elements[0].y=1280);rejected(l=>l.reference.fitRect.width=-1);
rejected(l=>l.reference.fitRect.x=NaN);
const panel={id:'panel',x:0,y:0,width:200,height:200},button={id:'button',x:20,y:20,width:40,height:40};
assert.equal(core.hitTest([panel,button],30,30).id,'button');
assert.equal(core.hitTest([panel,button],30,30,e=>e.id==='panel').id,'panel');
assert.equal(core.hitTest([panel,button],230,230),null);
assert.deepEqual(core.moved(button,-100,-100,{width:240,height:240}),{...button,x:0,y:0});
assert.deepEqual(core.moved(button,1000,1000,{width:240,height:240}),{...button,x:200,y:200});

// Exercise the actual editor event handlers without a browser; this does not verify rendering.
class Element {
  constructor(id=''){this.id=id;this.value='';this.checked=false;this.style={};this.children=[];this.options=this.children;this.events={};this.clientWidth=700;this.textContent='';this.files=[];}
  append(...values){this.children.push(...values);}replaceChildren(...values){this.children=values;this.options=this.children;}
  addEventListener(k,f){(this.events[k]||=[]).push(f);}setAttribute(){}setCustomValidity(v){this.validationMessage=v;}
  getBoundingClientRect(){return {left:0,top:100,width:this.width,height:this.height};}
  getContext(){return context;}focus(){}setPointerCapture(){}click(){}showModal(){this.open=true;}close(){this.open=false;}
  toBlob(fn){fn(new Blob(['png-mock']));}
}
const context={fillRect(){},strokeRect(){},setLineDash(){},fillText(){},measureText(t){return {width:t.length*8};},save(){},restore(){},drawImage(){},beginPath(){},moveTo(){},lineTo(){},stroke(){}};
const dom={};const get=id=>dom[id]||(dom[id]=new Element(id));
for(const [id,value] of Object.entries({viewport:'576x1280',mode:'overlay',zoom:'fit',opacity:'55'}))get(id).value=value;
get('viewport').append({value:'576x1280'},{value:'1080x1920'},{value:'1080x2400'});
let lastBlob;
const sandbox={TraceCore:core,REFERENCE_LAYOUTS:refs,console,Blob,innerHeight:900,crypto:crypto.webcrypto,
  URL:{createObjectURL(blob){lastBlob=blob;return 'blob:test';},revokeObjectURL(){}},
  confirm:()=>true,addEventListener(){},Option:class {constructor(label,value){this.label=label;this.value=value;}},
  Image:class {set src(v){this.width=576;this.height=1280;this.onload();}},
  document:{getElementById:get,querySelector:()=>new Element(),createElement:()=>new Element(),createTextNode:text=>text}};
sandbox.window=sandbox;vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname,'trace-app.js'),'utf8'),sandbox);
const current=()=>JSON.parse(get('jsonPreview').textContent);
for(const ref of refs){get('screen').value=ref.id;get('screen').onchange();assert.equal(current().reference.id,ref.id);assert.equal(core.validate(current()),'');}
get('screen').value='draft';get('screen').onchange();
let count=current().elements.length;get('add').onclick();assert.equal(current().elements.length,count+1);
get('duplicate').onclick();assert.equal(current().elements.length,count+2);
get('remove').onclick();assert.equal(current().elements.length,count+1);
get('reset').onclick();assert.equal(current().elements.length,count);
get('viewport').value='1080x1920';get('viewport').onchange();assert.equal(core.validate(current()),'');
get('viewport').value='576x1280';get('viewport').onchange();assert.equal(core.validate(current()),'');
for(const mode of ['original','layout','overlay']){get('mode').value=mode;get('mode').onchange();}
(async()=>{
  get('exportJson').onclick();assert.equal(core.validate(JSON.parse(await lastBlob.text())),'');
  get('exportMd').onclick();const md=await lastBlob.text();assert.ok(md.includes('\n\n| ID |'));assert.ok(md.includes('05:45'));
  get('exportPng').onclick();assert.ok(lastBlob); // Canvas API mock, no claim of PNG visual validation.
  get('jsonFile').files=[{size:100,text:async()=>JSON.stringify(base())}];await get('jsonFile').onchange({target:get('jsonFile')});assert.equal(current().reference.id,'arena');
  const saved=current();get('jsonFile').files=[{size:100,text:async()=>'{"schemaVersion":2}'}];await get('jsonFile').onchange({target:get('jsonFile')});assert.deepEqual(current(),saved);
  console.log(`PASS: ${refs.length} reference frames, ${elements} authored elements, ${layouts} viewport layouts; provenance, geometry, validation and editor handlers.`);
})().catch(error=>{console.error(error);process.exitCode=1;});
