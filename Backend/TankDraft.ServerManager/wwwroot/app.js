const token = document.querySelector('meta[name="control-token"]').content;
const $ = id => document.getElementById(id);
const states = {Stopped:'Выключен',Starting:'Запускается',Connecting:'Fusion подключается',Ready:'Готов к подключениям',AuthorityOnly:'Работает только локальное ядро',Draining:'Завершаем матчи',Failed:'Требуется проверка'};
let busy=false;
async function request(path,method='GET') {
  const response=await fetch('/api/'+path,{method,headers:{'X-TankDraft-Control':token},signal:AbortSignal.timeout(15000)});
  const value=await response.json();
  if(!response.ok) throw new Error(({already_running:'Сервер уже запущен.',gateway_build_missing:'Сначала соберите Fusion Server.',fusion_auth_not_configured:'Ещё не настроена авторизация серверного процесса Fusion.',operation_failed:'Операция не выполнена. Проверьте локальную конфигурацию.'})[value.error]||'Панель недоступна.');
  return value;
}
async function refresh(){
 try {
  const s=await request('status'); $('state').textContent=states[s.state]||s.state;
  $('mode').textContent=s.plaintextQa?'Закрытый игровой QA: канал без шифрования, только разрешённые аккаунты. Экономика выключена.':'Режим диагностики подключения: игровые команды недоступны.';
  $('detail').textContent=s.instanceId?'Экземпляр '+s.instanceId:'Запустите сервер или локальную диагностику.';
  $('matches').textContent=s.authority?.activeMatches??'—'; $('queued').textContent=s.authority?.queued??'—';
  $('pump').textContent=s.authority?.pumpAgeMilliseconds!=null?Math.round(s.authority.pumpAgeMilliseconds)+' мс':'—';
  $('memory').textContent=s.managerMemoryMb+' / '+s.gatewayMemoryMb+' МБ';
  $('uptime').textContent=s.instanceId&&s.started?'Время работы: '+Math.floor((Date.now()-Date.parse(s.started))/1000)+' с':'Сервер не запущен.';
  $('exits').textContent='CPU: '+s.cpuPercent+'% · Запусков: '+s.starts+' · Завершений вне команды оператора: '+s.unexpectedExits;
  $('build').textContent=s.gatewayBuildExists?'Сборка Fusion Server найдена.':'Сборка Fusion Server ещё не найдена.';
  $('logs').textContent=s.logs.map(x=>new Date(x.at).toLocaleTimeString()+'  '+x.message).join('\n')||'Событий пока нет.';
  const running=!!s.instanceId; $('start').disabled=busy||running; $('local').disabled=busy||running;
  $('drain').disabled=busy||!running; $('stop').disabled=busy||!running;
 }catch(e){$('state').textContent='Нет связи с панелью';$('error').textContent=e.message;}
}
for(const [id,path] of [['start','start'],['local','check-local'],['drain','drain'],['stop','stop']]) $(id).onclick=async()=>{
 if(busy)return;
 if(id==='stop'&&!confirm('Прервать незавершённые матчи и выключить сервер?'))return;
 busy=true;$('error').textContent='';
 try{await request(path,'POST');}catch(e){$('error').textContent=e.message;}
 finally{busy=false;await refresh();}
};
async function poll(){await refresh();setTimeout(poll,1000);}poll();
