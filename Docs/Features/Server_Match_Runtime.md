# Серверный цикл матча (S2)
Status: local scheduler/HTTP verified; SQLite recovery and Unity loopback client implemented in S3/S4; cloud pending
Last reviewed: 2026-09-13

## Объём

`Backend/TankDraft.Server.Match` владеет `MatchService`, LeoECS `BattleSimulation`, таймерами и валидацией решений. В HTTP-host добавлен `MatchPumpService`: матч продвигается даже при полном отсутствии запросов игроков. Это продолжение [Backend_Implementation_Plan.md](Backend_Implementation_Plan.md), режим по-прежнему **LocalOnly**. Настройки, SDK, ключи и ресурсы PlayFab/Azure на этом этапе не нужны и не создавались.

S2 обеспечивает возвращение в работающий процесс с новой проверенной сессией того же аккаунта. Поверх него реализован локальный [S3 persistence/replay](Server_Match_Persistence.md): host публикует изменения после SQLite commit и восстанавливает процесс при сохранном диске. Snapshot для клиента не является checkpoint сервера.

## Данные и ответственность

| Модуль | Роль |
|---|---|
| `TankDraft.Game.Core` | Общие правила драфта/боя из Unity linked source, без изменения существующего gameplay |
| `TankDraft.Server.Match` | Single-writer runtime, monotonic timeline, automatic choices, results, bounded events и private decision journal |
| `TankDraft.Server.Security` | Session/match identity, sequence, allowlist Choose/Order, receipt fingerprint и replay |
| `TankDraft.LocalHost` | Loopback HTTP, background polling, QA identity/clock; transport DTO conversion |
| `TankDraft.Server.Match.Tests` | Fake-clock проверки deadline/состояния/повторов на экспортированных боевых данных |

Authored SO → `Backend/Content/local-match.json` → immutable definitions → domain/simulation. Серверный lifecycle не создаёт Unity-объекты и не меняет prefab/view/content assets. Общий `ServerMatchJson` передаёт поля shared readonly structs через JSON и восстанавливает их конструкторами: позиции/HP/events не превращаются в значения по умолчанию при десериализации. Вычисляемые свойства вроде `BattleVec.Normalized` в wire-формат не входят.

## Время и переходы

- `TimeProvider.GetTimestamp()` задаёт монотонное время. `GetUtcNow()` один раз фиксирует начало timeline; UTC-поля snapshot вычисляются от этого начала. Перевод системных часов не сокращает и не продлевает окно выбора.
- Одно окно на текущий choice token. Первый выбор игрока сохраняется, но deadline не продлевается. Оба обычных выбора завершают окно раньше; единственный камбэк-выбор завершает своё окно сразу.
- Команда считается вовремя, если принята под lock серверного actor **строго до deadline**. При точном равенстве deadline сначала закрывается автоматически. Клиентские timestamps не участвуют; HTTP время загрузки тела/ожидание actor входят в фактическую задержку.
- На deadline фиксируются исходный token и стороны, которые ещё не сделали выбор. За каждую сервер один раз выбирает равномерный индекс среди трёх допустимых offers. Используется отдельный seeded XorShift32 с rejection sampling; состояние до/после и выбранный offer сохраняются в приватном in-memory journal.
- Приказы вручную проходят через `Order` и `MatchService.TryUseOrder`; автоматический выбор использует только offers и не тратит запасённые приказы. Камбэк получает только проигравший, затем идут три обычных выбора.
- После драфта создаётся настоящая `BattleSimulation`; fixed-step ticks назначаются сервером. Клиент не отправляет `Continue`, `SetWinner`, время или tick. После исхода score обновляется один раз, результат сохраняет terminal hits и общий tie-break trace.
- Между раундами действует короткое серверное окно показа результата, после него автоматически начинается новый draft. После четвёртой победы scheduler больше не меняет match state. У боевой фазы нет игрового timeout, сравнения HP или синтетического победителя.
- Просроченные переходы выполняются по исходным запланированным временам. Размер одной порции ограничен `MaxStepsPerPump`, поэтому пауза scheduler не создаёт бесконечный цикл в одном запросе и не сдвигает каждое окно к моменту catch-up.

Текущие значения в `Backend/Config/local-host.json` — **стартовые настройки локального теста, не восстановленный по видео баланс таймеров**:

| Поле | Значение | Назначение |
|---|---|---|
| DraftChoiceSeconds | 5 | Длительность отдельного выбора; решение владельца 14.09.2026 |
| RoundResultSeconds | 2 | Показ итога между раундами |
| SchedulerPollMilliseconds | 20 | Частота вызова Pump фоновым host |
| MaxStepsPerPump | 256 | Максимум просроченных transitions/ticks за один Pump |
| EventCapacity | 1024 | Ограниченный backlog визуальных событий |
| JournalCapacity | 256 | Число хранимых решений без вытеснения |
| MaxCommandReceipts | 512 | Число receipts без вытеснения |

`MaxSimulationTicksPerRound` применяется только к старой standalone QA-проверке с ошибкой при превышении. В серверном scheduler этот cutoff не используется. При переполнении journal или внутренней ошибке runtime останавливается с `Fault`, не назначает победителя и не забывает принятые решения ради освобождения места.

## Snapshot и reconnect

`GET /v1/match` аутентифицирует токен. Runtime дополнительно сверяет account/match/side и срок сессии. У snapshot есть revision, round/phase, свой choice/offer/army/charges, score и предыдущие результаты, server time/deadline, текущие entities (юниты, снаряды, зоны), simulation tick и event watermark. Подтверждённый собственный выбор виден как `CommittedChoice`. Offers, подтверждённый выбор и RNG соперника не раскрываются.

`Capture` не продвигает матч: это чтение под тем же lock. Если scheduler ещё догоняет время, `CatchingUp=true`; клиент обязан дождаться актуального snapshot, не показывать старую фазу как готовую к вводу. `Execute` делает ограниченный catch-up перед проверкой нового решения; при оставшемся backlog возвращает `CatchingUp` без расходования sequence/receipt. После него можно повторить тот же envelope. Поздний запрос с прежним token/round отклоняется; полностью идентичный повтор уже принятой команды получает старый ACK после catch-up.

Без cursor выдаётся полный snapshot с `ResyncRequired=true` и пустым списком старых событий. Поэтому возвращение через два раунда не требует просмотра двух пропущенных боёв. `?afterEventSequence=N` позволяет получить только события после действительного cursor. Устаревший/будущий cursor вызывает полный resync; отрицательный или некорректный query отвергается HTTP-host. У событий есть общий sequence и round, память backlog ограничена. Snapshot и вложенные коллекции отделены от mutable runtime.

Истечение/отзыв токена не останавливает матч. QA registry может создать новую сессию для того же account/side; сама выдача не является публичным HTTP endpoint. Production PlayFab authentication, platform linking и защита от конкурирующих сессий на нескольких устройствах ещё впереди. Внутренний gate S2 использует session receipts; обёртка S3 сохраняет account+operation receipts в SQLite и возвращает прежний ACK новой сессии до повторного исполнения.

## Проверка и пределы доказательства

Команда: `./Tools/Backend/test-local-backend.ps1`. Runtime-тесты используют тот же экспорт контента, ручной monotonic clock и реальные ECS-бои. HTTP-проверка запускает Kestrel и настоящий BackgroundService; QA-код сдвигает тестовый clock напрямую, затем ждёт только через read-only Capture, без HTTP/Pump-вызовов. Это проверяет независимость scheduler от игрока, но не является тестом реального ожидания часа или сетевой потери пакетов.

Проверено: таймеры/автовыборы/камбэк, повторы и граница deadline, ограниченный catch-up и journal, private/detached snapshots, сохранение текущих эффектов, HTTP reconnect в бою и после нескольких раундов, полный фоновый матч до четырёх побед. Подробные актуальные числа — в Backend_Implementation_Plan.md.

Не проверено/не реализовано: distributed storage/fencing, WSS/TLS, Linux/MPS, реальные мобильные обрывы и 100 CCU, экономика/награды/покупки. Локальный restart persistence проверен в S3; Unity presentation через loopback WS — в [S4](Server_Client_Transport.md). `--verify` использует фиксированный QA seed из config и тестовый clock; production allocation должен создать приватный server seed и использовать системный TimeProvider. Числа rate limits/TTL этого host предназначены для QA.

Локальные [S3 persistence](Server_Match_Persistence.md) и [S4 Unity/WS](Server_Client_Transport.md) реализованы и проверены. Далее TLS/auth lifecycle и мобильная fault matrix; distributed storage/lease, Azure adapter и MPS ещё впереди, облачный запуск требует прохождения zero-charge gate.
