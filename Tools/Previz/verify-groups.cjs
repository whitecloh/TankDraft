'use strict';
const fs=require('fs'),path=require('path'),assert=require('assert/strict'),core=require('./trace-core.js'),refs=require('./reference-layouts.js');
const args=process.argv.slice(2),outIndex=args.indexOf('--export-dir'),out=outIndex<0?null:path.resolve(args[outIndex+1]);if(out)fs.mkdirSync(out,{recursive:true});
const base=()=>core.makeLayout(refs.find(r=>r.id==='arena'));
let count=0;
for(const type of ['horizontal','vertical','grid'])for(const alignment of core.groups.alignments){
  const l=base(),g=l.groups[0];Object.assign(g,{type,alignment,x:20,y:150,width:536,height:630,cellWidth:82,cellHeight:92,spacingX:13,spacingY:17,paddingLeft:7,paddingRight:11,paddingTop:5,paddingBottom:9,columns:3});
  core.groups.resolve(l);
  const first=l.elements.find(e=>e.id===g.children[0]);l.elements.push({id:'wallet.icon',kind:'vehicle',label:'Icon',parentId:first.id,x:first.x+5,y:first.y+5,width:18,height:18});
  assert.equal(core.validate(l),'');
  const before=l.elements.filter(e=>g.children.includes(e.id)||e.id==='wallet.icon').map(e=>({...e}));
  g.y+=7;core.groups.resolve(l);before.forEach(e=>assert.ok(Math.abs(l.elements.find(x=>x.id===e.id).y-e.y-7)<1e-8));
  assert.equal(core.validate(l),'');assert.equal(core.groups.owner(l,'wallet.icon').id,g.id);
  if(out)fs.writeFileSync(path.join(out,`${type}-${alignment}.json`),JSON.stringify(l,null,2));count++;
}
// Alignment is anchored by independently calculated examples, not only by round trips.
const horizontal=base();let expected=[33,163,293,423];horizontal.groups[0].children.forEach((id,i)=>assert.equal(horizontal.elements.find(e=>e.id===id).x,expected[i]));
const rewards=horizontal.groups[1];expected=[111,202,293,384];rewards.children.forEach((id,i)=>assert.equal(horizontal.elements.find(e=>e.id===id).x,expected[i]));
const reject=mutate=>{const l=base();mutate(l);assert.notEqual(core.validate(l),'');};
reject(l=>l.schemaVersion=1);reject(l=>l.groups[0].children.push('missing'));reject(l=>l.groups[1].children[0]='wallet.0');
reject(l=>l.groups[0].id=l.elements[0].id);reject(l=>l.groups[0].spacingX=-1);reject(l=>l.groups[0].paddingLeft=.1);
reject(l=>l.groups[0].cellWidth=600);reject(l=>l.groups[0].alignment='unknown');reject(l=>l.groups[0].columns=0);
reject(l=>l.groups[0].children=[]);reject(l=>l.elements[0].parentId=l.elements[1].id);
reject(l=>{l.elements.at(-1).parentId=l.elements.at(-2).id;l.elements.at(-2).parentId=l.elements.at(-1).id;});
reject(l=>l.elements[0].x+=1);reject(l=>l.groups[0].children[0]=null);
assert.equal(core.validate(core.makeLayout(refs.find(r=>r.id==='draft'))),'');
console.log(`PASS: ${count} group layouts with nested content, 9 alignments, 3 types, ownership/overflow/cycle validation, v1 compatibility.`);
