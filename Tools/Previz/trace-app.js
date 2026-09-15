/* Local reference tracing workspace. All imported text is treated as data. */
(() => {
  'use strict';
  const $=id=>document.getElementById(id),core=TraceCore,refs=REFERENCE_LAYOUTS;
  const canvas=$('canvas'),ctx=canvas.getContext('2d');
  const colors={panel:'#76d9ce',button:'#ffd269',label:'#91b8ff',card:'#ffac70',vehicle:'#ee99d6'};
  const names={panel:'Панели',button:'Кнопки',label:'Текст',card:'Карточки',vehicle:'Объекты'};
  const visible=new Set(core.kinds),drafts=new Map(),cache=new Map();
  let current=refs[0],layout=core.makeLayout(current),selected=null,drag=null,image=null,downloadUrl=null,dirty=false;
  const clone=o=>JSON.parse(JSON.stringify(o));
  const report=message=>{$('loadStatus').textContent=message;};
  refs.forEach(r=>$('screen').append(new Option(`${r.title} · ${r.timecode}`,r.id)));
  core.kinds.forEach(kind=>{
    const label=document.createElement('label'),input=document.createElement('input');input.type='checkbox';input.checked=true;
    input.style.accentColor=colors[kind];input.onchange=()=>{if(input.checked)visible.add(kind);else visible.delete(kind);draw();renderList();};
    label.append(input,document.createTextNode(names[kind]));label.style.color=colors[kind];$('layers').append(label);
  });
  function sourceInfo(){
    $('source').textContent=current?`${current.title} · Видео 2 / ${current.timecode} · ${current.width} × ${current.height}`:'Импортированный макет';
    $('notes').textContent=current?.notes||'Разметка по видимым границам кадра. Точность отдельных контуров можно скорректировать в пикселях; это не утверждённый финальный UI.';
  }
  function loadImage(ref){
    image=null;draw();if(!ref){report('Подложка не привязана');return;}
    report('Загрузка кадра…');
    if(cache.has(ref.id)){image=cache.get(ref.id);report('Кадр загружен');draw();return;}
    const img=new Image();
    img.onload=()=>{cache.set(ref.id,img);if(current?.id!==ref.id)return;image=img;report('Кадр загружен');draw();};
    img.onerror=()=>{if(current?.id===ref.id)report('Кадр недоступен: '+ref.image);};
    img.src=ref.image;
  }
  function fit(){
    const host=$('canvasHost'),mainTop=document.querySelector('main').getBoundingClientRect().top;
    const maxHeight=Math.max(300,innerHeight-mainTop-95),maxWidth=Math.max(240,host.clientWidth-24);
    const factor=$('zoom').value==='1'?1:Math.min(maxHeight/layout.viewport.height,maxWidth/layout.viewport.width);
    canvas.style.width=Math.round(layout.viewport.width*factor)+'px';canvas.style.height=Math.round(layout.viewport.height*factor)+'px';
    host.style.height=Math.min(maxHeight,layout.viewport.height*factor+20)+'px';
  }
  function draw(showSelection=true){
    const v=layout.viewport;canvas.width=v.width;canvas.height=v.height;
    ctx.fillStyle=layout.theme.background;ctx.fillRect(0,0,v.width,v.height);
    const mode=$('mode').value,f=Math.min(v.width/576,v.height/1280),opacity=+$('opacity').value/100;
    if(image&&mode!=='layout'){
      const r=layout.reference?.fitRect||{x:0,y:0,width:v.width,height:v.height};
      ctx.save();ctx.globalAlpha=mode==='original'?1:opacity;ctx.drawImage(image,r.x,r.y,r.width,r.height);ctx.restore();
    }
    if($('grid').checked&&mode!=='original'){
      ctx.save();ctx.strokeStyle='#ffffff22';ctx.lineWidth=f;const step=16*f;
      for(let x=0;x<v.width;x+=step){ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x,v.height);ctx.stroke();}
      for(let y=0;y<v.height;y+=step){ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(v.width,y);ctx.stroke();}ctx.restore();
    }
    if(mode!=='original')for(const e of layout.elements){
      if(!visible.has(e.kind))continue;const sel=showSelection&&selected===e.id;
      ctx.save();ctx.strokeStyle=sel?'#ffffff':colors[e.kind];ctx.lineWidth=(sel?3:1.5)*f;
      if(e.kind==='vehicle')ctx.setLineDash([6*f,4*f]);
      ctx.fillStyle=sel?'#ffffff16':mode==='layout'?colors[e.kind]+'12':colors[e.kind]+'05';
      ctx.fillRect(e.x,e.y,e.width,e.height);ctx.strokeRect(e.x,e.y,e.width,e.height);ctx.setLineDash([]);
      if($('labels').checked||sel||mode==='layout'){
        const font=Math.max(10,Math.round(12*f));ctx.font=`${font}px system-ui`;
        const text=sel?`${e.label} · ${e.width.toFixed(0)}×${e.height.toFixed(0)}`:e.label;
        const w=Math.min(v.width-e.x,ctx.measureText(text).width+10*f),y=Math.max(0,e.y-18*f);
        ctx.fillStyle='#101923ee';ctx.fillRect(e.x,y,w,18*f);ctx.fillStyle=sel?'#fff':colors[e.kind];
        ctx.fillText(text,e.x+4*f,y+13*f,Math.max(1,w-8*f));
      }ctx.restore();
    }
    if(mode!=='original')for(const g of layout.groups||[]){ctx.save();ctx.strokeStyle=selected===g.id?'#ffffff':'#8ce0cd';ctx.setLineDash([9*f,5*f]);ctx.strokeRect(g.x,g.y,g.width,g.height);ctx.restore();}
    $('opacityValue').textContent=Math.round(opacity*100)+'%';
    $('count').textContent=`${layout.elements.length} элементов${dirty?' · изменено':''}`;
    $('jsonPreview').textContent=JSON.stringify(exportLayout(),null,2);fit();
  }
  function exportLayout(){const output=clone(layout);if(output.reference)output.reference.opacity=+$('opacity').value/100;return output;}
  function select(id){selected=id;renderFields();renderList();draw();}
  function renderFields(){
    const group=(layout.groups||[]).find(g=>g.id===selected);
    if(group){renderGroupFields(group);return;}
    const e=layout.elements.find(e=>e.id===selected);$('fields').replaceChildren();
    $('selection').textContent=e?e.id:'Выберите элемент на кадре или в списке.';if(!e)return;
    const owner=core.groups.owner(layout,e.id);
    if(owner){const button=document.createElement('button');button.textContent='Параметры группы: '+owner.id;button.className='wide';button.onclick=()=>select(owner.id);$('fields').append(button);}
    for(const key of ['label','kind','x','y','width','height']){
      const label=document.createElement('label'),input=document.createElement(key==='kind'?'select':'input');
      if(key==='label'||key==='kind')label.className='wide';
      label.append(document.createTextNode({label:'Подпись',kind:'Тип',x:'X',y:'Y',width:'Ширина',height:'Высота'}[key]));
      if(key==='kind')core.kinds.forEach(k=>input.append(new Option(names[k],k)));
      else{input.type=key==='label'?'text':'number';if(key!=='label')input.step='any';}
      input.value=e[key];
      if(owner&&key!=='label')input.disabled=true;
      input.addEventListener(key==='kind'?'change':'input',()=>{
        const candidate={...e,[key]:key==='label'||key==='kind'?input.value:Number(input.value)};
        const error=core.validate({...layout,elements:layout.elements.map(x=>x.id===e.id?candidate:x)});
        if(error||input.value===''){input.setCustomValidity?.(error||'Введите значение');return;}
        input.setCustomValidity?.('');Object.assign(e,candidate);dirty=true;draw();renderList();
      });
      input.addEventListener('blur',()=>{input.value=e[key];input.setCustomValidity?.('');});label.append(input);$('fields').append(label);
    }
  }
  function renderGroupFields(group){
    $('fields').replaceChildren();$('selection').textContent=group.id+' · '+group.children.length+' элементов';
    const labels={x:'X группы',y:'Y группы',width:'Ширина группы',height:'Высота группы',cellWidth:'Ширина ячейки',cellHeight:'Высота ячейки',spacingX:'Интервал X',spacingY:'Интервал Y',paddingLeft:'Отступ слева',paddingRight:'Отступ справа',paddingTop:'Отступ сверху',paddingBottom:'Отступ снизу',columns:'Столбцы',alignment:'Выравнивание',type:'LayoutGroup'};
    for(const key of ['type','alignment',...core.groups.geometry,...core.groups.paddings,'columns']){
      const label=document.createElement('label'),choice=key==='type'||key==='alignment',input=document.createElement(choice?'select':'input');label.append(document.createTextNode(labels[key]));
      if(choice){label.className='wide';(key==='type'?['horizontal','vertical','grid']:core.groups.alignments).forEach(v=>input.append(new Option(v,v)));}else{input.type='number';input.step=key==='columns'||core.groups.paddings.includes(key)?'1':'any';}
      input.value=group[key];input.addEventListener(choice?'change':'input',()=>{
        if(input.value==='')return;const candidate=clone(layout),target=candidate.groups.find(g=>g.id===group.id);target[key]=choice?input.value:Number(input.value);
        if(target.columns<1||target.cellWidth<=0||target.cellHeight<=0){input.setCustomValidity('Размер ячейки и число столбцов должны быть положительными');return;}
        core.groups.resolve(candidate);const error=core.validate(candidate);input.setCustomValidity(error);if(error)return;
        layout=candidate;group=target;dirty=true;draw();
      });
      input.addEventListener('blur',()=>{input.value=group[key];input.setCustomValidity('');});label.append(input);$('fields').append(label);
    }
  }
  function renderList(){
    const q=$('search').value.toLowerCase();$('elementList').replaceChildren();
    for(const g of layout.groups||[]){if(q&&!g.id.toLowerCase().includes(q))continue;const button=document.createElement('button');button.className='element-row'+(selected===g.id?' selected':'');button.textContent='▦ '+g.id+' · '+g.type;button.onclick=()=>select(g.id);$('elementList').append(button);}
    for(const e of layout.elements){
      if(!visible.has(e.kind)||!(e.id+' '+e.label).toLowerCase().includes(q))continue;
      const button=document.createElement('button'),dot=document.createElement('span'),text=document.createElement('span');
      button.className='element-row'+(selected===e.id?' selected':'');button.setAttribute('role','option');button.setAttribute('aria-selected',String(selected===e.id));
      dot.style.background=colors[e.kind];text.textContent=e.label;button.title=e.id;button.append(dot,text);button.onclick=()=>select(e.id);$('elementList').append(button);
    }
  }
  function switchScreen(){
    if(current)drafts.set(current.id,clone(layout));current=refs.find(r=>r.id===$('screen').value);
    const [w,h]=$('viewport').value.split('x').map(Number);
    layout=drafts.get(current.id)||core.makeLayout(current,w,h);dirty=drafts.has(current.id);selected=null;
    ensureViewportOption();sourceInfo();renderFields();renderList();loadImage(current);
  }
  function ensureViewportOption(){
    const value=`${layout.viewport.width}x${layout.viewport.height}`;
    if(![...$('viewport').options].some(o=>o.value===value))$('viewport').append(new Option(value+' · импорт',value));$('viewport').value=value;
  }
  function resizeLayout(){
    const [w,h]=$('viewport').value.split('x').map(Number),old=layout.reference?.fitRect||{x:0,y:0,width:layout.viewport.width,height:layout.viewport.height};
    const sourceW=layout.reference?.sourceWidth||layout.viewport.width,sourceH=layout.reference?.sourceHeight||layout.viewport.height;
    const s=Math.min(w/sourceW,h/sourceH),next={x:(w-sourceW*s)/2,y:(h-sourceH*s)/2,width:sourceW*s,height:sourceH*s},factor=next.width/old.width;
    const elements=layout.elements.map(e=>({...e,x:next.x+(e.x-old.x)*factor,y:next.y+(e.y-old.y)*factor,width:e.width*factor,height:e.height*factor}));
    const candidate={...layout,elements,viewport:{width:w,height:h,safeTop:0,safeBottom:0},...(layout.reference?{reference:{...layout.reference,fitRect:next}}:{}),...(layout.groups?{groups:core.groups.scaled(layout.groups,factor,next.x-old.x*factor,next.y-old.y*factor)}:{})};
    core.groups.resolve(candidate);
    const error=core.validate(candidate);if(error){ensureViewportOption();report('Размер не изменён: '+error);return;}
    layout=candidate;
    dirty=true;renderFields();draw();
  }
  const coords=event=>{const r=canvas.getBoundingClientRect();return {x:(event.clientX-r.left)*canvas.width/r.width,y:(event.clientY-r.top)*canvas.height/r.height};};
  canvas.addEventListener('pointerdown',event=>{
    if($('mode').value==='original')return;const point=coords(event),e=core.hitTest(layout.elements,point.x,point.y,e=>visible.has(e.kind));
    const target=e?(core.groups.owner(layout,e.id)||e):null;select(target?.id||null);canvas.focus();if(target){drag={id:target.id,point,original:{...target}};canvas.setPointerCapture(event.pointerId);}
  });
  canvas.addEventListener('pointermove',event=>{
    if(!drag)return;const point=coords(event),e=layout.elements.find(e=>e.id===drag.id)||(layout.groups||[]).find(g=>g.id===drag.id);
    Object.assign(e,core.moved(drag.original,Math.round(point.x-drag.point.x),Math.round(point.y-drag.point.y),layout.viewport));
    core.groups.resolve(layout);
    if(e.x!==drag.original.x||e.y!==drag.original.y)dirty=true;draw();
  });
  const endDrag=()=>{drag=null;renderFields();};canvas.addEventListener('pointerup',endDrag);canvas.addEventListener('pointercancel',endDrag);
  canvas.addEventListener('keydown',event=>{
    const item=layout.elements.find(e=>e.id===selected),e=(layout.groups||[]).find(g=>g.id===selected)||(item?(core.groups.owner(layout,item.id)||item):null);const moves={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]};
    if(!e||!moves[event.key])return;event.preventDefault();const [x,y]=moves[event.key],step=event.shiftKey?10:1;
    Object.assign(e,core.moved(e,x*step,y*step,layout.viewport));core.groups.resolve(layout);dirty=true;renderFields();draw();
  });
  function addElement(duplicate){
    if(duplicate&&((layout.groups||[]).some(g=>g.id===selected)||core.groups.owner(layout,selected))){report('Копирование группы выполняется в authored-конфиге; размеры меняйте в параметрах группы.');return;}
    if(layout.elements.length>=200){report('Достигнут лимит 200 элементов');return;}
    const existing=layout.elements.find(e=>e.id===selected);
    const e=duplicate&&existing?core.moved({...existing},10,10,layout.viewport):{kind:'panel',label:'Новый элемент',x:30,y:120,width:180,height:70};
    e.id='custom.'+(crypto.randomUUID?.()||Date.now().toString(36)+Math.random().toString(36).slice(2));delete e.parentId;layout.elements.push(e);dirty=true;select(e.id);
  }
  function download(name,blob){
    if(!blob){report('Не удалось создать файл');return;}if(downloadUrl)URL.revokeObjectURL(downloadUrl);downloadUrl=URL.createObjectURL(blob);
    const a=document.createElement('a');a.href=downloadUrl;a.download=name;a.textContent='Скачать '+name;$('downloads').replaceChildren(a);a.click();
  }
  $('screen').onchange=switchScreen;$('viewport').onchange=resizeLayout;$('zoom').onchange=fit;$('mode').onchange=()=>draw();
  $('opacity').oninput=()=>draw();$('labels').onchange=()=>draw();$('grid').onchange=()=>draw();$('search').oninput=renderList;
  $('add').onclick=()=>addElement(false);$('duplicate').onclick=()=>addElement(true);
  $('remove').onclick=()=>{if(!selected||layout.elements.length<2)return;if((layout.groups||[]).some(g=>g.id===selected)||core.groups.owner(layout,selected)||layout.elements.some(e=>e.parentId===selected)){report('Состав группы и иерархии изменяется в authored-конфиге.');return;}layout.elements=layout.elements.filter(e=>e.id!==selected);dirty=true;select(null);};
  $('reset').onclick=()=>{if(!current)return;if(dirty&&!confirm('Сбросить изменения этого окна к исходной разметке?'))return;const [w,h]=$('viewport').value.split('x').map(Number);layout=core.makeLayout(current,w,h);drafts.delete(current.id);dirty=false;select(null);loadImage(current);};
  $('exportJson').onclick=()=>{download(`tankdraft-${current?.id||'layout'}.json`,new Blob([JSON.stringify(exportLayout(),null,2)],{type:'application/json'}));dirty=false;draw();};
  $('exportPng').onclick=()=>{try{draw(false);canvas.toBlob(blob=>{download(`tankdraft-${current?.id||'layout'}-${$('mode').value}.png`,blob);draw();},'image/png');}catch(error){report('PNG: откройте инструмент через localhost или загрузите кадр кнопкой «Свой кадр».');draw();}};
  $('exportMd').onclick=()=>{const output=exportLayout(),clean=s=>String(s).replaceAll('|','\\|').replaceAll('\n',' ');const text=`# ${current?.title||'Layout'}\n\nИсточник: ${output.reference?.video||'импорт'} / ${current?.timecode||'—'}\nViewport: ${output.viewport.width} × ${output.viewport.height}\n\n| ID | Тип | Элемент | X | Y | W | H |\n|---|---|---|---:|---:|---:|---:|\n`+output.elements.map(e=>`| ${clean(e.id)} | ${e.kind} | ${clean(e.label)} | ${e.x} | ${e.y} | ${e.width} | ${e.height} |`).join('\n');download(`tankdraft-${current?.id||'layout'}.md`,new Blob([text+(output.groups?.length?'\n\n## LayoutGroup\n\n'+JSON.stringify(output.groups,null,2):'')],{type:'text/markdown;charset=utf-8'}));};
  $('importJson').onclick=()=>$('jsonFile').click();
  $('jsonFile').onchange=async event=>{
    const file=event.target.files[0];if(!file)return;
    try{if(file.size>500000)throw Error('JSON слишком большой');const imported=JSON.parse(await file.text()),error=core.validate(imported);if(error)throw Error(error);
      if(current)drafts.set(current.id,clone(layout));layout=imported;current=refs.find(r=>r.id===imported.reference?.id)||null;
      $('screen').value=current?.id||'';selected=null;dirty=false;ensureViewportOption();sourceInfo();renderFields();renderList();loadImage(current);
    }catch(error){report('Импорт отклонён: '+error.message);}event.target.value='';
  };
  $('customReference').onclick=()=>$('imageFile').click();
  $('imageFile').onchange=event=>{
    const file=event.target.files[0];if(!file)return;const reader=new FileReader();
    reader.onload=()=>{const img=new Image();img.onload=()=>{if(current)drafts.set(current.id,clone(layout));current=null;$('screen').value='';image=img;const v=layout.viewport,s=Math.min(v.width/img.width,v.height/img.height);layout.reference={id:'custom',file:file.name,sourceWidth:img.width,sourceHeight:img.height,fitRect:{x:(v.width-img.width*s)/2,y:(v.height-img.height*s)/2,width:img.width*s,height:img.height*s}};sourceInfo();report('Свой кадр: '+file.name);dirty=true;draw();};img.onerror=()=>report('Не удалось прочитать изображение');img.src=reader.result;};reader.readAsDataURL(file);event.target.value='';
  };
  $('showPlan').onclick=()=>$('planDialog').showModal();$('closePlan').onclick=()=>$('planDialog').close();
  window.addEventListener('resize',fit);window.addEventListener('beforeunload',()=>{if(downloadUrl)URL.revokeObjectURL(downloadUrl);});
  sourceInfo();renderList();loadImage(current);
})();
