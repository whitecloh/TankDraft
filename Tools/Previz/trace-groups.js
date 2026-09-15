(function(root){
  'use strict';
  const alignments=['UpperLeft','UpperCenter','UpperRight','MiddleLeft','MiddleCenter','MiddleRight','LowerLeft','LowerCenter','LowerRight'];
  const geometry=['x','y','width','height','cellWidth','cellHeight','spacingX','spacingY'];
  const paddings=['paddingLeft','paddingRight','paddingTop','paddingBottom'];
  function rects(g){
    const n=g.children.length,cols=g.type==='horizontal'?n:g.type==='vertical'?1:Math.min(g.columns,n),rows=Math.ceil(n/cols);
    const cw=cols*g.cellWidth+(cols-1)*g.spacingX,ch=rows*g.cellHeight+(rows-1)*g.spacingY;
    const aw=g.width-g.paddingLeft-g.paddingRight,ah=g.height-g.paddingTop-g.paddingBottom;
    const a=alignments.indexOf(g.alignment),x=g.x+g.paddingLeft+(aw-cw)*(a%3)/2,y=g.y+g.paddingTop+(ah-ch)*Math.floor(a/3)/2;
    return {overflow:cw>aw+.01||ch>ah+.01,items:g.children.map((id,i)=>({id,x:x+(i%cols)*(g.cellWidth+g.spacingX),y:y+Math.floor(i/cols)*(g.cellHeight+g.spacingY),width:g.cellWidth,height:g.cellHeight}))};
  }
  function validate(layout){
    const groups=layout.groups||[],elements=layout.elements,byId=new Map(elements.map(e=>[e.id,e])),ids=new Set(byId.keys()),owners=new Set();
    if(!Array.isArray(groups)||groups.length>50||layout.schemaVersion===1&&groups.length)return 'Некорректные группы / версия формата.';
    for(const g of groups){
      if(!g||typeof g.id!=='string'||!/^[a-zA-Z0-9_.-]+$/.test(g.id)||g.id.length>100||ids.has(g.id))return 'Некорректный ID группы.';ids.add(g.id);
      if(!['horizontal','vertical','grid'].includes(g.type)||!alignments.includes(g.alignment)||!geometry.every(k=>Number.isFinite(g[k]))||!paddings.every(k=>Number.isInteger(g[k])&&g[k]>=0)||!Number.isInteger(g.columns)||g.columns<1||g.columns>200)return 'Некорректные параметры группы '+g.id;
      if(g.x<0||g.y<0||g.width<=0||g.height<=0||g.cellWidth<=0||g.cellHeight<=0||g.spacingX<0||g.spacingY<0||g.x+g.width>layout.viewport.width+.01||g.y+g.height>layout.viewport.height+.01)return 'Некорректные границы группы '+g.id;
      if(!Array.isArray(g.children)||!g.children.length||g.children.length>200)return 'Пустая или слишком большая группа.';
      let kind;
      for(const id of g.children){const e=byId.get(id);if(!e||owners.has(id)||e.parentId)return 'Некорректное владение элементом '+id;owners.add(id);if(kind&&kind!==e.kind)return 'В группе должны быть элементы одного вида.';kind=e.kind;}
      const resolved=rects(g);if(resolved.overflow)return 'Элементы не помещаются в группу '+g.id;
      for(const r of resolved.items){const e=byId.get(r.id);if(['x','y','width','height'].some(k=>Math.abs(e[k]-r[k])>.05))return 'Геометрия элемента не соответствует группе: '+r.id;}
    }
    for(const e of elements){
      if(e.parentId!==undefined&&e.parentId!==null&&typeof e.parentId!=='string')return 'Некорректный parentId: '+e.id;
      const seen=new Set([e.id]);let p=e.parentId;
      while(p){if(!byId.has(p)||seen.has(p))return 'Неизвестный родитель или цикл: '+e.id;seen.add(p);p=byId.get(p).parentId;}
    }
    return '';
  }
  function resolve(layout){
    const byId=new Map(layout.elements.map(e=>[e.id,e]));
    function update(e,r){const old={...e};Object.assign(e,r);for(const child of layout.elements.filter(c=>c.parentId===e.id))update(child,{x:e.x+(child.x-old.x)*e.width/old.width,y:e.y+(child.y-old.y)*e.height/old.height,width:child.width*e.width/old.width,height:child.height*e.height/old.height});}
    for(const g of layout.groups||[])for(const r of rects(g).items){const e=byId.get(r.id);if(e)update(e,r);}
    return layout;
  }
  function owner(layout,id){const e=layout.elements.find(e=>e.id===id);return (layout.groups||[]).find(g=>g.children.includes(id))||(e?.parentId?owner(layout,e.parentId):null);}
  function scaled(groups,scale,ox=0,oy=0){return (groups||[]).map(g=>({...g,x:g.x*scale+ox,y:g.y*scale+oy,width:g.width*scale,height:g.height*scale,cellWidth:g.cellWidth*scale,cellHeight:g.cellHeight*scale,spacingX:g.spacingX*scale,spacingY:g.spacingY*scale,...Object.fromEntries(paddings.map(k=>[k,Math.round(g[k]*scale)])),children:[...g.children]}));}
  root.TraceGroups={alignments,geometry,paddings,rects,validate,resolve,owner,scaled};if(typeof module!=='undefined')module.exports=root.TraceGroups;
})(typeof globalThis!=='undefined'?globalThis:window);
