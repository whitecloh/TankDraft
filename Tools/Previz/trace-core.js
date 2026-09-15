(function(root){
  'use strict';
  const groups=typeof module!=='undefined'?require('./trace-groups.js'):root.TraceGroups;
  const kinds=['panel','button','label','card','vehicle'];
  const screens=['arena','collection','card','draft','battle','result','shop','commander','pass','rating','profile'];
  const states=['normal','locked','loading','error','empty','upgrade-ready','comeback','waiting'];
  function validate(o){
    const v=o?.viewport,t=o?.theme;
    if(![1,2].includes(o?.schemaVersion)||!v||!t||typeof o.screen!=='string'||typeof o.state!=='string')return 'Ожидается schemaVersion 1 или 2, экран и viewport.';
    if(!screens.includes(o.screen)||!states.includes(o.state))return 'Неизвестный экран или состояние.';
    if(o.reference){const r=o.reference,f=r.fitRect;if(!f||![f.x,f.y,f.width,f.height,r.sourceWidth,r.sourceHeight].every(Number.isFinite)||f.x<0||f.y<0||f.width<=0||f.height<=0||r.sourceWidth<=0||r.sourceHeight<=0||f.x+f.width>v.width+.01||f.y+f.height>v.height+.01)return 'Некорректная геометрия подложки.';}
    if(![v.width,v.height,v.safeTop,v.safeBottom].every(Number.isFinite)||v.width<240||v.height<240||v.width>4096||v.height>4096||v.safeTop<0||v.safeBottom<0||v.safeTop+v.safeBottom>=v.height)return 'Некорректный viewport.';
    if(!['background','accent','text'].every(k=>/^#[0-9a-f]{6}$/i.test(t[k])))return 'Цвет должен иметь формат #RRGGBB.';
    if(!Array.isArray(o.elements)||!o.elements.length||o.elements.length>200)return 'Допустимо 1–200 элементов.';
    const ids=new Set();
    for(const e of o.elements){
      if(!e||typeof e.id!=='string'||!e.id.trim()||e.id.length>100||ids.has(e.id)||!kinds.includes(e.kind)||typeof e.label!=='string'||e.label.length>500)return 'Некорректный ID, вид или подпись.';
      ids.add(e.id);
      if(![e.x,e.y,e.width,e.height].every(Number.isFinite)||e.x<0||e.y<0||e.width<=0||e.height<=0||e.x+e.width>v.width+.01||e.y+e.height>v.height+.01)return `Границы элемента ${e.id} выходят за viewport.`;
      if(e.row!==undefined&&(!Number.isInteger(e.row)||e.row<0||e.row>3))return 'Некорректный ряд.';
    }
    return groups.validate(o);
  }
  function makeLayout(ref,width=576,height=1280){
    const scale=Math.min(width/ref.width,height/ref.height),ox=(width-ref.width*scale)/2,oy=(height-ref.height*scale)/2;
    return groups.resolve({schemaVersion:ref.groups?.length?2:1,screen:ref.screen,state:ref.state,viewport:{width,height,safeTop:0,safeBottom:0},theme:{background:'#17212c',accent:'#82e1ce',text:'#e5edf5'},reference:{id:ref.id,file:ref.image,video:'video_2026-09-12_14-26-11.mp4',seconds:ref.seconds,sourceWidth:ref.width,sourceHeight:ref.height,fitRect:{x:ox,y:oy,width:ref.width*scale,height:ref.height*scale}},...(ref.groups?{groups:groups.scaled(ref.groups,scale,ox,oy)}:{}),elements:ref.elements.map(e=>({...e,x:e.x*scale+ox,y:e.y*scale+oy,width:e.width*scale,height:e.height*scale}))});
  }
  function hitTest(elements,x,y,visible=()=>true){return elements.filter(e=>visible(e)&&x>=e.x&&y>=e.y&&x<=e.x+e.width&&y<=e.y+e.height).sort((a,b)=>a.width*a.height-b.width*b.height)[0]||null;}
  function moved(element,dx,dy,viewport){return {...element,x:Math.max(0,Math.min(viewport.width-element.width,element.x+dx)),y:Math.max(0,Math.min(viewport.height-element.height,element.y+dy))};}
  root.TraceCore={kinds,validate,makeLayout,hitTest,moved,groups};if(typeof module!=='undefined')module.exports=root.TraceCore;
})(typeof globalThis!=='undefined'?globalThis:window);
