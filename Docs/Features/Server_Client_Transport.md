# Unity-клиент локального server match
Status: local Windows WSS, access renewal and TCP fault slice verified; production auth and device QA pending
Last reviewed: 2026-09-13

Обновление 14.09.2026: remote VPN QA использует cadence с учётом времени WSS exchange и согласованный opt-in parallel access refresh. Локальный сервер без capability сохраняет последовательный renewal. Контракт, проверки revocation/suspend и ограничения device evidence: [VPN_QA_Stabilization.md](VPN_QA_Stabilization.md).

## Объём S4

Этот этап подключает существующие `UIMatchScreen` и `BattleWorldView` к локальному authoritative backend. Он не переносит правила матча в Unity: клиент формирует намерение игрока, получает подтверждённое сервером состояние и показывает его через authored views.

Поток:

`ServerClientSettings` → client intent → loopback WebSocket → durable backend → snapshot/events → authored `UIMatchScreen` и `BattleWorldView`.

`ServerClientSettings` хранит `MatchSettingsAsset`, content version из `Backend/Content/local-match.sha256`, версию протокола, polling/retry/timeout, лимит ответа, presentation buffer и тексты UI. Несовпадение content version, чужой match/round или устаревший snapshot не должны применяться к представлению.

## Сцена и сборка

`TankDraftServerClientAuthoring.Run` выполняется через Unity MCP после компиляции runtime. Он создаёт или обновляет `ServerClientSettings.asset`, один раз копирует `Match.unity` в `ServerMatch.unity`, сохраняет существующие camera/EventSystem/`UIMatchScreen`/`BattleWorldView`, удаляет только local `MatchLifetimeScope` в новой сцене и добавляет `ServerClientLifetimeScope`. Authoring восстанавливает исходный scene setup и проверяет, что в целевой сцене нет local scope и Photon-компонентов.

`Tools/Backend/QueueServerClientBuild.cs` через MCP вызывает постоянный editor bridge `ServerClientBuild.Queue`. Очередь хранится в SessionState и исполняется EditorApplication.update. В Windows Development build явно передаются `ServerMatch.unity`, `MainMenu.unity` и локальная `Match.unity` для существующей навигации меню; глобальные Build Settings не меняются. Статус — `Logs/BackendClient/build-status.txt`, Player — `Logs/BackendClient/Build/TankDraftServerClient.exe`.

## Реализация и запуск

Сборка `TankDraft.Match.ServerClient` не ссылается на Photon, PlayFab или боевую Simulation. Папки `Core`, `Transport`, `Content`, `Bootstrap` разделяют wire projection/journal, соединение, authored настройки и VContainer session. Domain используется только для типов offers/army/rules отображения. Клиент не создаёт MatchService/BattleSimulation и не меняет профиль или экономику.

Из корня проекта:

```powershell
./Tools/Backend/run-server-client.ps1
# Автоматическая локальная проверка с двумя настоящими Unity Player:
./Tools/Backend/run-server-client.ps1 -Automated
./Tools/Backend/verify-server-client-run.ps1 -RunDirectory 'Logs/BackendClient/tls-<run id>' -Tls
./Tools/Backend/test-tls-session.ps1
```

Launcher теперь использует WSS v2, короткоживущий access и grant через HTTPS. Сроки, TLS trust policy, стабильный command stream, обновление доступа и запуск описаны в [Local_TLS_Auth.md](Local_TLS_Auth.md). Legacy WS v1 host остаётся только для самостоятельной регрессионной проверки.

## Протокол и восстановление

- Адрес только `wss://127.0.0.1:18783/v1/socket`. Bearer проверяется до upgrade, после Hello и на каждом сообщении; Origin/query запрещены, одновременно допускается один socket на stream. Сверяются `tankdraft-server-v2` и content SHA-256.
- `Hello → Welcome`, `Poll(AfterEventSequence) → Snapshot`, `Command(CommandEnvelope) → Ack(OperationId, Reply)`. Один последовательный запрос/ответ, без неограниченного server send queue. Poll обычно каждые 100 ms; серверный Pump независимо работает каждые 20 ms. Клиент, который не читает ответы, не владеет матчевым scheduler.
- Ограничены соединения, headers, суммарный размер фрагментированного сообщения (8 KiB), ответ (1 MiB), количество сообщений и время receive/send. Глобальный limiter применяется до auth. Ошибочные/повторные JSON поля отвергаются; таймаут действует на всё сообщение, а не обновляется каждым фрагментом.
- UI отправляет только Choose/Order с текущими round/token. Сервер определяет side, проверяет выбор и публикует ACK после durable commit. Кнопки блокируются при pending, reconnect, CatchingUp и несовпадении показанного/current token. Continue и победитель с клиента не отправляются.
- `ClientIntentJournal` атомарно сохраняет весь envelope перед отправкой. При потере ACK или перезапуске процесса повторяется тот же OperationId/Sequence/payload. CatchingUp не завершает intent. ACK другого operation или отказ в протоколе не очищает journal. В нём нет bearer tokens.
- После reconnect запрашивается полный snapshot без cursor. Старые анимации пропускаются; единицы, снаряды и зоны восстанавливаются из текущих entities. UI показывает текущий раунд/счёт/выбор. Транспортный и presentation buffers ограничены; overflow ведёт к сбросу визуального backlog. Interpolation только между кадрами одного раунда/боевой фазы, без предсказания исхода и экстраполяции симуляции.
- Задержка отображения 0.2 s, локальный countdown вычисляется из server deadline/ServerNow и монотонного времени после получения. Он приблизительный; решение о своевременности всегда принимает сервер.

Ограниченный request/response поверх WebSocket — текущий adapter для измерений. Частота/формат snapshots, server-time alignment и трафик ещё требуют сетевого профилирования. Правила lifetime, закрытия и ограничений сверены с [ASP.NET Core WebSockets](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0) и [ClientWebSocket](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket).

## Проверка

Текущий прогон с задержками, jitter, reset и stall зашифрованного потока также прошёл: [Network_Fault_Matrix.md](Network_Fault_Matrix.md). Это отдельный слой воздействия на TCP соединения, не имитация процента потерь IP-пакетов.

Текущий WSS v2 с обновлением доступа: семь раундов, 3:4, 196 общих battle ticks без несовпадений; оба финальных Player exit code 0. Клиентский набор: 36/36. Полный актуальный evidence и известные ограничения — [Local_TLS_Auth.md](Local_TLS_Auth.md). Ниже сохранены предыдущие WS v1 результаты для трассировки перехода.

`Backend/TankDraft.ServerClient.Tests` компилирует именно Unity pure source через Compile Link: тесты journal/ACK/restart/CatchingUp, malformed JSON и преобразования настоящего ServerMatchJson (unit/projectile/zone/глобальные event sequences). DOTNET_ROOT должен указывать на `Logs/BackendSdk`; установка системного runtime или major roll-forward не требуется.

Первый реальный Windows run: `Logs/BackendClient/socket-b4ce62efc9154635a6f84dea3226e771`. Два Unity Player завершили семь раундов со счётом 3:4, revision 2522. Side 0 принудительно завершён launcher после отправки команды до чтения ACK и восстановился с прежним journal/token; side 1 прервал socket на 5 секунд в бою и переподключился. Pending intent у обоих пуст в финале. 89 общих battle ticks: несовпадений позиций/HP/ID/side нет. Наблюдались до 36 projectile views, 19 zone views, 48 effect views. Это не FPS/пиковая вместимость и не гарантия одинакового кадра при произвольной задержке сети.

Финальный повторный run после исправления момента QA screenshot, полного entity hash и проверки границ socket-host: `Logs/BackendClient/socket-df3cd48828ad4a759bb73ab3ea5ceb24`. `verify-server-client-run.ps1` — PASS: семь раундов, 3:4, revision 2522, **236 общих battle ticks без расхождения полного BattleEntityState**. Оба клиента получили свой корректный финальный экран (3:4 проигрыш / 4:3 победа), снимки просмотрены. Launcher завершился с кодом 0; оба клиентских процесса остановлены, listener 18782 отсутствует. Коды завершения отдельных Player не фиксировались. Финальная Windows сборка: 0 errors / 0 warnings. Разница текущих visual effects при разных кадрах допускается; победитель/HP/позиции определены общей authority.

Регрессия `test-local-backend.ps1` после изменения host entrypoint: Security 21/21, Match 22/22, Persistence 18/18, HTTP 46 checks, domain 13 checks и два одинаковых полных headless матча — PASS. Socket verifier повторно проверен после устранения ложноположительных ACK/stale/session assertions; проверяется именно `Reply.Accepted=true`, отсутствие повторной мутации, код `stale-token` и HTTP 409 второй активной сессии.

Проверка socket-host использует настоящий Kestrel/ClientWebSocket и отдельный QA clock: auth/version/Origin/JSON/size, durable retry, stale choice, reconnect/resync, offline-матч до финала, expiry/revoke после Welcome и отсутствие блокировки scheduler нечитающим клиентом. Продвижение через несколько раундов ускоряется QA clock; реальные Unity прогоны используют обычное серверное время.

## Границы и непроверенное

Текущий transport предназначен для локального loopback backend. TLS и локальная смена access session реализованы отдельно: [Local_TLS_Auth.md](Local_TLS_Auth.md). Не проверены PlayFab login/production identity, мобильные background/kill/restart, loss/jitter/reordering через сетевой эмулятор, заполнение TCP-буферов slow consumer, 100 CCU, Linux/MPS и distributed failover. Реальные покупки, matchmaking, выдача наград и облачные ресурсы не включались. S4 целиком не закрыт; далее измеряемая сетевая fault matrix.
