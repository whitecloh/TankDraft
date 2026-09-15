# Fusion: меню, повторные матчи и буфер отображения
Status: F3 menu / Human repeat / cancel / server Bot PASS; старый arrival buffer заменён в Fusion native path, см. Fusion_Native_Replication.md
Last reviewed: 2026-09-14

Текущее продолжение: **Fusion_Native_Replication.md**. Ниже сохранены результаты прежнего request/reply transport; они не описывают частоту новой native replica. Владелец подтвердил заметное улучшение в новой сборке, отдельные подёргивания остаются.

Владелец наблюдал ощутимые лаги в прежнем PC тесте. Старые результаты «полный матч завершился» не являются приёмкой плавности. Влияние VPN не установлено: замер PollMs включает игровой request/reply, relay, ожидание Unity callbacks и локальный authority. Состояние VPN в этих запусках программно не переключается.

## Флоу и зависимости

- Authored `FusionGameClient` содержит `FusionClientLifetimeScope`. Это постоянный родительский DI scope на время Photon runner. `FusionGameClientFlow` получает `FusionSessionContext` через injection, инициализирует его после готовности SDK и открывает существующий MainMenu через `LifetimeScope.EnqueueParent`, сохраняя parent override до завершения async load. Глобального session service locator не добавлено.
- `MainMenuLifetimeScope` выбирает `FusionMatchLauncher`, когда родитель содержит Fusion context. При прямом запуске старых диагностических сцен их существующий transport остаётся доступным.
- Кнопка «В бой» и существующее окно сообщений/отмены используют `NetworkQueueSettings`, `NetworkQueuePresentation` и контракт `IMatchLauncher`. UI не назначает победителя, соперника или бота. Config/каталоги/подготовленные view не копируются в сетевой слой.
- `FusionQueueClient` держит стабильный Join OperationId при неопределённом ответе. Confirmed Idle сбрасывает intent для нового поиска. Истёкший lobby обновляется один раз; повторный отказ возвращается пользователю, бесконечного retry нет. Cancel может вернуть Matched, если сервер уже назначил игру: клиент открывает этот матч.
- После завершения боя `IMatchSessionNavigation` помечает PendingLeave и загружает меню под тем же parent scope. Меню подтверждает Leave на сервере до следующего Join. Каждый матч получает отдельную папку diagnostics/intent journal; старый transport освобождается, runner сохраняется.
- Падение самого Photon runner и возвращение после перезапуска приложения остаются следующим этапом. Эти изменения не делают client-host миграцию и не обеспечивают durable восстановление после потери dedicated процесса.

## Отображение и измерения

При прежнем PresentationDelay200ms и median arrival≈383ms у клиента регулярно не было следующего снимка для интерполяции. Это конкретная причина ступенчатого движения в приложении, независимо от причины самого сетевого RTT.

`BattlePresentationClock` оценивает интервалы последних16 arrivals. Нижняя и верхняя задержки заданы в authored `ServerClientSettings` (200–800ms). Presentation cursor меняет скорость плавно и не идёт назад при изменении буфера. Смена фазы/раунда сбрасывает оценку. Новые юниты, попадания, урон и результат по-прежнему приходят с сервера; клиент не предсказывает исход боя. Драфт сохраняет обычную короткую задержку и серверный deadline.

Цена буфера — дополнительная задержка наблюдаемого боя. Он не сокращает RTT, не устраняет потери пакетов и не обещает отсутствие рывков при любом VPN/маршруте. `HeldFrames/InterpolatedFrames` измеряют наличие следующего снимка для отображения, а не субъективную плавность. `MaxRenderFrameSeconds` отдельно фиксирует длинные Unity кадры. Скриншоты включаются только явным `TD_FUSION_CAPTURE_SCREENSHOTS`, поскольку захват тоже может задерживать кадр.

## Проверки и запуск

- ServerClient **83/83**, включая реальный source queue/channel: uncertain Join/retry, Cancel→Idle→новый Join, гонку Cancel→Matched, чужой InstanceId, bounded lobby refresh. Детерминированный trace с arrivals380/480ms проверяет уменьшение starvation против фиксированных200ms, отсутствие rewind и верхнюю границу delay. Это тест алгоритма, не сравнение двух сетевых маршрутов.
- `Logs/FusionMigration/Checks/fusion-menu-buffer.trx`; foundation23projects/35refs PASS.
- `Tools/Fusion/test-gameplay-pair.ps1 -Matches 2 -MaximumSeconds 600`: два PC Player, два Human матча подряд через MainMenu. Script проверяет distinct MatchIds, одинаковые итоги у обеих сторон и exit0.
- `-SingleClient -CancelFirstSearch -MaximumSeconds 420`: подтверждённая отмена первого поиска, затем новый поиск и серверный Bot после configured timeout10s. Этот случай не засчитывается как Human PvP.
- `-CaptureScreenshots` включает визуальное evidence. Без этого флага проверяем performance без screenshot overhead. `summarize-gameplay-pair.ps1` принимает новый layout c несколькими `match-*` каталогами.

Первый запуск `Logs/FusionServer/game-3d8242a0015f46939d734b7af39afa9f` не принят: synchronous scene load завершил временную регистрацию до Awake, бой выбрал local credentials и не стартовал. Клиенты и сервер остановлены. Исправлено persistent parent scope + async load; только последующие Player результаты могут закрывать эту регрессию.

Следующий запуск `game-d4f0a18a74144ab9bc749494531ff6bd` не принят: VContainer `Editor/ScriptTemplateModifier.cs` переписал новый `*LifetimeScope.cs` пустым шаблоном при создании meta. После восстановления исходника MCP проверил `contextResolved=True`, `contextInjected=True`; meta/GUID сохранены. При добавлении новых scope-файлов обязательно проверять содержимое после первого Unity refresh. Настройки пакета не изменялись.

`game-08ef16780646471ab4e810fa966b97fe`: один Human матч4:1, 27shared battle ticks/0mismatch, по1connection/authgeneration3. Повтор не прошёл: автотест вызывал Quit на следующем кадре, пока async return ещё грузил меню. Исправлено `_returningToMenu` guard. Владелец наблюдал этот бой и сообщил: «Рывки остались примерно такими же». **Плавность НЕ принята.** Poll median366–367ms, starvation7.4–7.7% render frames, max hold0.67–0.70s, max Unity frame32–45ms. Сервер в одном status snapshot потреблял0.8% логических CPU машины; это не CPU profiler всех процессов.

Следующий обязательный шаг по плавности: регулярный bounded server-push снимков, чтобы cadence не зависел от полного request/reply RTT. Нужны backpressure/ACK, предел буфера, приоритет команд и авторизации, event cursor без потерь/дублей и замеры по звеньям gateway/relay/client. После этого повторить визуальную приёмку на том же VPN; одного увеличения interpolation delay недостаточно. F4 reconnect/fault не заменяет эту performance-задачу.

Финальный Human repeat run `Logs/FusionServer/game-2c1d70eb6fd845a3a2d3045103480275` — **PASS**. Два PC Player в одном dedicated instance: сначала4:2, затем4:1, оба итоговых счёта совпали, IDs матчей разные; подтверждены Leave→Idle→Join и смена сторон между матчами. Exit0, по1logical connection на матч, access generation до4; sampled battle ticks25/0mismatch. `result.json` и `timing-summary.json` сохранены. Съёмка screenshot в этом run отключена. Poll median364–368ms, p95 435–438ms, max655–686ms; starvation8.5–9.6%, max hold0.87–1.02s. Это положительная приёмка lifecycle, **не** плавности. После неё server drain→Stopped, новый bot-тест запущен отдельно.

Финальный cancel/bot run `Logs/FusionServer/game-c0f118587e5844a9be9d36d27d756950` — **PASS**. Cancel→Idle подтверждён13:57:46UTC, новый Join→Searching13:57:49UTC, Bot assignment13:58:00UTC (окно ожидания10s + polling). Dedicated бой завершился4:0, exit0,1connection/access generation4; median366ms, p95 411ms. Просмотрен `client-0/match-4c72e6195bdf48a0944dca6eb1160630/battle-0-1.png`: authored HUD явно показывает «Бот». После теста drain выполнен, manager **Stopped**, instanceId пуст; игровых Player/server процессов не осталось. Панель доступна на loopback18878.

Граница приёмки F3: автоматизация вызывает тот же IMatchLauncher/Cancel/Return lifecycle, который привязан к существующим UI-кнопкам; отдельного ручного прогона на телефоне не было. Состав армии остаётся серверным QA preset. Прежний отрицательный отзыв владельца о плавности сохраняется; последняя сборка исправляла переходы и bot-label, не частоту доставки снимков.

## Пределы текущего этапа

QA разрешён без шифрования; PlayFab Custom Auth, allowlist, server authority и выключенная экономика сохраняются. Запуск по-прежнему требует подготовленные приватные QA credentials и работающий dedicated процесс через Server Manager. Состав армии пока задаёт серверный QA preset: перенос выбранной в мете колоды, прогрессии и экономики не выполнен этим menu cutover. Android, независимые сети, SDK reconnect, drain во время боя, restart/loss и нагрузка проверяются отдельно.
