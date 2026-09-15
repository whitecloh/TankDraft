# Штатная репликация состояния Fusion
Status: native replication / 2PC Human repeat PASS; владелец подтвердил заметное улучшение, отдельные подёргивания остаются
Last reviewed: 2026-09-14

Продолжение по решению владельца: Android отложен, проверки F4 и профилирование сначала на PC. См. [Fusion_PC_Recovery.md](Fusion_PC_Recovery.md); прежние строки ниже «затем Android» не блокируют текущий порядок.

## Решение

Владелец утвердил переход с request/reply Poll поверх ReliableData на штатные Networked properties и interpolation buffers Fusion. PlayFab, dedicated topology и .NET/LeoECS authority сохраняются. Это не Quantum migration и не предварительное проигрывание рассчитанного боя. QA пока без шифрования согласно Photon_Plaintext_QA.md; экономика выключена.

Базовая линия: Fusion_Menu_and_Presentation.md, Human repeat `game-2c1d70eb6fd845a3a2d3045103480275`: Poll median364–368ms, p95435–438ms, max hold0.87–1.02s. Владелец сообщил, что рывки остались. Завершение матча не является приёмкой плавности.

## Реализация

- На каждого Custom-Authenticated/allowlisted игрока создаётся authored FusionBattleReplica. NetworkBehaviour.ReplicateToAll(false) + ReplicateTo(recipient,true) ограничивают приватную проекцию одним получателем. Серверный gateway получает адресата из проверенного PlayerRef; клиент не задаёт чужой account/side.
- После Welcome/EnableParallelRefresh gateway самостоятельно опрашивает private loopback authority. Authored snapshotPeriod=50ms задаёт верхнюю частоту 20Hz без catch-up bursts. Одновременно допускается один producer; следующие снимки не запрашиваются, пока предыдущий не перенесён в network state.
- Типизированные NetworkArray содержат состояния сущностей и кольцо событий. Идентификаторы определений вынесены в компактный справочник. Метаданные текущего протокола (драфт, счёт, таймеры, результаты) пока сохраняют bounded JSON, упакованный в uint[]; это компромисс совместимости, а не JSON полного мира поверх reliable stream.
- Клиент использует TryGetSnapshotsBuffers/GetArrayReader и alpha Fusion. ForceRemoteRenderTimeframe включён: это серверные proxy-сущности, локального prediction боевых решений нет. IMatchNativePresentation отделяет SDK от ServerClientSession/View. Старый адаптивный arrival buffer не применяется поверх SDK interpolation.
- Серверные команды, ACK и обновление доступа остаются отдельными ReliableData request/reply. Idempotent intent journal и проверки .NET не заменяются клиентским состоянием. Pump и команды сериализуются в peer; это локальная очередь, не ожидание Internet Poll RTT.
- При успешном обновлении доступа gateway сам привязывает новую generation к private authority socket до ответа клиенту (NativeAccessBound). Последующий клиентский Reauthenticate идемпотентен: producer не ждёт дополнительного Internet RTT. AccessRefreshRequired несёт generation; mailbox отбрасывает позднее требование уже подтверждённого поколения. SocketOpen/Leave меняют epoch и уничтожают старую replica; поздний callback старого потока не может удовлетворить чтение нового матча.
- Native mailbox хранит только последнее состояние. Кольцо переносит события, переживающие пропуск промежуточных снимков; cursor убирает дубли. При выходе cursor за окно — явный ResyncRequired, восстановление актуального мира без проигрывания потерянной истории.

## Границы прототипа

Одна проекция ограничена 256 сущностями, 128 событиями в кольце, 64 определениями и 8192 байтами метаданных. Переполнение не обрезает авторитетный бой молча: оно требует отказа/диагностики. Перед расширением roster/count лимитов нужны сегментированные сетевые объекты и нагрузочные измерения. Короткие эффекты представлены событиями; отдельная параметрическая модель всех типов снарядов в этот шаг не включена.

Fusion snapshot times и тики внешней .NET симуляции различаются: producer частотой 20Hz не доказывает такую же доставку новых simulation ticks. Измерять нужно arrivals, изменения доменного tick, render stalls, задержку команд, расходы/allocations. PollMs после переключения означает ожидание локального mailbox, не RTT Photon, и напрямую не сравним с прежним сетевым PollMs.

SDK reconnect после реального Photon disconnect, восстановление после падения ПК и durable economy по-прежнему отдельные этапы. Новая replica позволяет получить текущее состояние после успешно выполненного нового входа; она сама не реализует авторизацию reconnect или хранение матча после потери процесса.

## Источники

- [Fusion Data Transfer](https://doc.photonengine.com/fusion/v2/manual/data-transfer/data-transfer)
- [Fusion Network Buffers](https://doc.photonengine.com/fusion/v2/manual/advanced/network-buffers)
- [Projectiles Essentials](https://doc.photonengine.com/fusion/v2/technical-samples/projectiles-essentials)
- [Disconnect & Reconnect](https://doc.photonengine.com/fusion/v2/technical-samples/fusion-disconnect-reconnect)

## Проверки

88/88 ServerClient и 65/65 RemoteHost прошли; отчёты Logs/FusionMigration/Checks/fusion-native*.trx. Проверены mailbox coalescing, event cursor/overflow, смена epoch, закрытие ожидающего reader, устаревший refresh и реальное обновление private socket до клиентского Reauthenticate. Foundation: 23 projects / 35 refs PASS. MCP compilation и Windows dedicated/client builds PASS (18:25 локального времени).

Два первых runtime прогона остановились до боя: большая replica (55341 words) не выделялась в Fusion Simulation.AllocateObject. После уплотнения — 7597 words. Эти прогоны не считаются gameplay acceptance. В первом работающем прототипе выявлен цикл устаревших refresh-сигналов; generation fencing устранил повторные обновления. Также исправлен диагностический MaxHold: ожидание в пятисекундном драфте ошибочно увеличивало величину боевой паузы.

Промежуточный Human repeat `game-fd49cd6d485e4078a07520dc6eb119f6` без screenshot capture: два матча 1:4 и 4:0, 240 shared ticks / 0 entity hash mismatch, по одному connection на матч, средние интервалы native state 69.55/69.22ms, максимальные 351.67/333.29ms. Этот run предшествует последней оптимизации private rebind и исправлению MaxHold; его старый MaxHold не использовать как длительность сетевого зависания.

Финальный Human repeat `Logs/FusionServer/game-edfe6afd2fe34915bfe5155bc1f70775`: **PASS**, два последовательных матча 3:4 и 0:4 через MainMenu/Leave/Join, одинаковые результаты у клиентов, exit0. **539 общих контрольных тиков, 0 расхождений entity hash**; по одному logical connection на матч, auth generation до4, disconnected samples0. Средние интервалы native state 72.84/74.33ms, максимальные 483.42/535.95ms; held render fraction 3.30/3.26%, max hold0.450/0.539s. Средние значения агрегированы по матчам, это не RTT и не строгий A/B benchmark со старым Poll median365ms.

В финальном run включён screenshot capture: max Unity frame118/128ms нельзя считать чистым performance benchmark. Просмотрен `client-0/match-449129b9e0a74da3ae412c09a8933fb1/battle-1-1.png`: HUD, поле и обе армии отображаются. Это проверка состава экрана, не доказательство плавности.

Отзыв владельца на финальный тест: «стало намного плавнее, не вижу постоянных лагов, но всё равно есть подергивания моментами». Улучшение подтверждено; полная приёмка плавности ещё открыта. Следующий performance шаг — раздельный profiler gateway/authority/render и замер gaps/allocations без screenshot capture, затем Android на том же VPN. Не маскировать остаточные паузы дальнейшим увеличением буфера без измерений. F4 SDK reconnect остаётся отдельным этапом.

Финальный cancel/server Bot `Logs/FusionServer/game-a1b025b3ff0d4d70882317bb06fbfeb5`: **PASS**, Cancel→Idle→новый поиск→Bot после configured10s, полный матч4:1, exit0,1connection, generation5, disconnected samples0. Без screenshot capture: native mean71.84ms/max449.87ms, held fraction3.18%, max hold0.533s, max Unity frame36.72ms. Следовательно, остаточные паузы нельзя целиком списать на захват изображений. Этот случай проверяет серверного бота, не Human PvP. После проверки manager drain→**Stopped**, instanceId=null; игровых Player/server процессов нет, панель loopback18878 оставлена доступной.
