# Стабилизация удалённого прототипа с VPN
Status: VPN ON human match with parallel renewal complete; repeat-match and fault acceptance pending
Last reviewed: 2026-09-14

Продолжение на ПК: [PC_Remote_Stability.md](PC_Remote_Stability.md). Исправлены журнал повторного remote матча и привязка timing к snapshot. Cloud 76f119710450: два Human матча подряд и обе стороны offline40s с возвратом PASS, 420 общих ticks без расхождений. Редкие паузы до683ms остаются: по metrics v2 ожидание ответа, сервер/маршрут пока не разделены. Полная плавность ещё не принята.

Уточнение владельца 14.09.2026: отдельный ход/выбор драфта сокращён с 20 до 5 секунд. Источник — `Backend/Config/local-host.json` и `Backend/TankDraft.RemoteHost/Config/remote-host.json`; server runtime считает deadline, клиент показывает его. APK/content hash для такого изменения не меняются. Поиск соперника остаётся 10 секунд. RemoteHost с новой policy: 30/30 PASS (`Logs/VpnQa/five-second-turn.trx`). Владелец подтвердил готовность к следующему bounded VPN тесту.

Владелец утвердил VPN ON как условие текущих тестов. Стек PlayFab + Edgegap без Azure сохраняется. Доступность без VPN из России остаётся ограничением будущего релиза, не целью этого патча. VPN включает владелец; приложение не настраивает VPN, не ослабляет TLS/auth и не подменяет серверную игру локальным ботом.

## Основание

В предыдущем A/B/A один VPN-сеанс сохранял соединение 132 секунды. Единственная same-round Battle пауза более 300 ms: 1176.399 ms, round 3 / tick 136, AuthGeneration 4 → 5, FrameSeconds около 22 ms. Это связь с renewal, не отдельное измерение HTTP задержки. Серверный ExchangeMatchAsync повторно проверял тот же PlayFab ticket в admission, добавляя внешний запрос. HTTP prefetch нового access небезопасен: выдача сразу отзывает прежний token.

## Порядок работ

1. Ограничить ожидание очереди независимо от исполнения сетевой отмены; исключить блокировку Unity thread, применение позднего ответа и накопление зависших запросов. Сохранить operation IDs при неопределённом результате.
2. Убрать повторный PlayFab verification в одном запросе renewal с сохранением allowlist, account, assignment, expiry, generation, replay и revocation проверок.
3. Включить длительность WSS обмена в заданный интервал Poll, без burst после задержки. Исключить автоматическое PNG-кодирование из remote performance runs; запись QA-журнала сбрасывать раз в секунду и при Dispose. При принудительном kill хвост журнала до одной секунды может быть потерян.
4. Собрать актуальные Windows/Android clients и server container, выполнить VPN ON human match; измерить Poll/Renewal отдельно от времени Unity frame. Проверить возврат после background и разрыва без повторного применения команды.
5. По измерениям решить, нужна ли доработка presentation buffer/протокола renewal. Не маскировать длинные разрывы увеличением буфера без оценки задержки отображения.

## Приёмка

Очередь покидает ожидание в заданный deadline даже с некооперативным HTTP handler; поздние ответы не меняют context. В реальном матче клиент показывает серверный результат, при общем tick совпадают полные entity hashes. VPN остаётся включённым; краткий background проверяется отдельно от непрерывной плавности. Автоматические тесты не доказывают плавность Android. Не обещаем отсутствие любых пауз при произвольном качестве VPN.

Cloud: только подтверждённый Free Tier, один ограниченный deployment, без экономики/покупок; после теста остановка и удаление private bootstrap. Секреты не попадают в сборки и evidence.

## Выполнено в текущем патче

- Очередь: deadline 10 s отделён от завершения HTTP handler, сетевой вызов вынесен с Unity thread. Wire permit живёт до реального завершения I/O; отменённые попытки не остаются в очереди на последующую отправку. Late response не применяет ready/lobby/conflict к context. Diagnostics разделяют stage и phase.
- `/v1/session`: одна свежая PlayFab verification вместо двух; общий admission API сохранён. Trusted `ExchangeVerified` вызывается только сервером после проверки identity, allowlist и назначения. TTL и немедленный отзыв старого access не изменены.
- WSS polling: период включает длительность обмена; missed polls не догоняются burst. В журнал добавлены `ReceivedAt`, `PollMs`, `RenewalMs` (последнее успешное renewal). Это диагностические наблюдения, не client authority.
- Remote PNG screenshots opt-in через `TD_REMOTE_CAPTURE_SCREENSHOTS=1`; локальные visual QA сохраняют старое поведение. Presentation delay остаётся 0.2 s до новых измерений.

Проверки 14.09.2026: ServerClient **72/72 PASS**, включая настоящий TLS/socket с задержкой snapshot 300 ms и периодом 200 ms; median cadence менее 450 ms без burst. RemoteHost **26/26 PASS**, AdmissionChecks **19/19 PASS**. Evidence: `Logs/VpnQa/vpn-client.trx`, `vpn-cadence.trx`.

Первый optimized baseline: Windows QueueMenu `Succeeded; errors=0; warnings=1`; Android `Succeeded; errors=0; warnings=4`, active target возвращён на StandaloneWindows64. Установленный APK SHA256 `1a24d3e6c70652c04b3f3a903346273cb8443e181b3a9646170b2f3d5ea16839`. OCI `Logs/RemoteContainer/03a372b40d32460e920136db493841cd`, tag `remote-fc830da62e78`, registry digest `sha256:86e72d15f50ce6d50965ccd103103d67b25ab8b5afff8a1c332ebdf25d421033`.

## VPN ON device run 644b29b074b9

Stockholm, Free 0.5 vCPU / 1 GiB, economy off. Создан 08:44:31 UTC, остановка запрошена 08:56:52 UTC, `Terminated` подтверждён через UI. Bootstrap удалён с телефона и из приватного PC-файла. Evidence: `Logs/RemoteAndroid/cloud-644b29b074b9`, PC `Logs/RemoteClient/f3283ff2d240438facc09c6fe0f8de2a`.

Клиенты получили **разные server-Bot matches**, оба завершились 4:2 за 6 раундов. Это не приёмка human PvP и не сравнение entity hashes. Причина несовпадения очереди: холодный старт PC assembly load 12.392 s превысил 10 s поиска на уже запущенном телефоне. Новый `Tools/Backend/run-remote-device-pair.ps1` проверяет VPN на телефоне, дожидается загрузки PC (`UnloadTime:` в player.log), затем запускает телефон; PowerShell parser PASS, runtime нового launcher ещё не проверен; проверить обе remote-assignment на одинаковый MatchId и Human до заявления об общем матче.

| Метрика | Android | PC |
|---|---:|---:|
| Battle presentation frames | 610 | 464 |
| Соседние Battle интервалы внутри одного раунда | 604 | 458 |
| Median presentation cadence | 109.059 ms | 100.203 ms |
| P95 presentation cadence | 112.490 ms | 200.258 ms |
| Максимальный интервал | 887.108 ms | 800.637 ms |
| Connections | 1 | 1 |

Интервалы вычислены по ClientTime, это не RTT. Максимальные паузы совпадают со сменой AuthGeneration: Android 2→3, PC 16→17. В первом промежуточном Android sample типичный FrameSeconds около 22.2 ms (~45 fps), PollMs 47–70 ms; вывод о 60 fps не подтверждён. Обычная cadence улучшилась относительно прежних ~156 ms, но пауза обновления доступа остаётся. Отсутствие transport-diagnostic файла обозначает отсутствие записанных ошибок, не отдельную гарантию сети.

## Доработка после измерений: parallel access refresh

Welcome опционально объявляет `ParallelAccessRefresh=true`; новый клиент включает режим через `EnableParallelRefresh` и строгий `ParallelRefreshEnabled`. Без opt-in сохраняется прежний последовательный протокол. HTTP verification выполняется параллельно с Poll; одновременно существует одна попытка renewal на соединение. Pending Command остаётся в journal до переавторизации, чтобы не попасть в гонку отзыва токена.

После немедленного отзыва access сервер при первом Poll отвечает только `AccessRefreshRequired`, без состояния боя. Следующий frame обязан быть Reauthenticate; повторный Poll/иная команда закрывает socket 4401. Новый token проходит прежние проверки account/match/side/stream/generation. TTL, fencing и отзыв не ослаблены. Клиент обрабатывает также неожиданный hint через новую проверку credentials, не превращая его в бесконечный Poll или постоянную ошибку. Suspend отменяет connection token; HTTP provider с задержанной отменой ограничен deadline, поздний fault наблюдается.

ServerClient **76/76 PASS**, включая реальные TLS/socket тесты с 500 ms verification, ответом после revocation с задержкой 200 ms, неожиданным hint и suspend во время renewal. RemoteHost **30/30 PASS**: успешный handoff, повторный Poll, invalid/expired token и legacy denial. Evidence: `Logs/VpnQa/vpn-client-parallel.trx`. Эти тесты не доказывают плавность на телефоне.

Новый OCI `Logs/RemoteContainer/ec1f7a60bd1f4dcc9443a24ebcad0f2b`, tag `remote-5a9c2eb2f079`, registry digest `sha256:9ab8eeb4be43419a6936c454854d9847a1448d83d3a13989a09e46fa3ed83db5` проверен. Архив не выполнялся на Linux локально; новый cloud deployment ещё не создан. Windows пересобран: `Succeeded; errors=0; warnings=0`. Android пересобран и установлен: `Succeeded; errors=0; warnings=3; originalTargetRestored=StandaloneWindows64`, APK SHA256 `04411c7f19c790bb821f3985485a6e6682bd1a72a5f4b4844e8b7465ca31b97d`. Установка не является runtime-приёмкой нового protocol. Следующий этап: bounded VPN ON human match, cadence/renewal gaps, затем background/reconnect и отсутствие повторных команд.

## Human VPN ON с пятисекундным ходом: fe5f3b821b37

14.09.2026, та же Stockholm площадка. Сервер пересобран после изменения timer policy: tag `remote-f13754c37640`, archive `Logs/RemoteContainer/a403643318a0420989215fe03f57fc12`, registry digest `sha256:0db8ccdbd7a36aee6e2ecd9346cfa0a12efbaf9c9b3bdcdcfe8fceee124ee36b`. Published Linux `Config/remote-host.json` проверен: DraftChoiceSeconds=5. APK и authored content version сохранились. Один Free 0.5 vCPU / 1 GiB deployment, экономика выключена; создание 09:15:49 UTC. Остановка запрошена 09:19:21 UTC после завершения матча; `Terminated` подтверждён через UI и записан в receipt.

`run-remote-device-pair.ps1` успешно запустил телефон после загрузки PC. Оба получили Human assignment `6f964ff4f9954afe975f3ed20af283e7-36b001f58d954ed580d6e07013e14fa9`: PC Side0, Android Side1. Матч завершён 0:4 за 4 раунда. Итоговый entity hash одинаков; **88 уникальных общих Battle (Round,Tick), 0 расхождений**. Evidence: `Logs/RemoteAndroid/cloud-fe5f3b821b37`, `Logs/RemoteClient/10a6c708a8d546b6bb98da4df7461d79`.

| Метрика | Android | PC |
|---|---:|---:|
| Median presentation cadence | 109.336 ms | 100.033 ms |
| P95 presentation cadence | 114.289 ms | 133.454 ms |
| Максимальный интервал | 154.911 ms | 433.481 ms |
| Connections | 1 | 1 |
| Последняя AuthGeneration | 3 | 3 |

Оба максимума без смены generation. Android max снизился с 887 до 155 ms; это сравнение разных матчей, не строгий benchmark одинаковой симуляции. Последний renewal занял Android655/PC668 ms, но не остановил поток на эту длительность. Владелец подтвердил: «стало намного плавнее, восстановление связи не замечал».

PC max433 ms совпадает с ReceivedAt gap428 ms, FrameSeconds соседних записей около16.67 ms. Точная причина не установлена; нельзя считать проблему всех пауз полностью закрытой или приписывать её renewal. Диагностика PollMs записывает последнее значение транспорта во время presentation и не является точной RTT-меткой конкретного кадра. Следующий bounded этап: PC jitter/receive profiling, повторный матч, background/разрыв и восстановление с сохранением команд. Независимые сети, долговременная устойчивость и нагрузка этим одним матчем не приняты. Приватные bootstraps после теста удалены, процессы QA остановлены.
