# Сохранение и восстановление серверного матча (S3)
Status: local SQLite recovery, durable receipts and result outbox verified; distributed storage/failover and cloud pending
Last reviewed: 2026-09-13

## Объём

S3 добавляет долговечное локальное хранение к [серверному циклу S2](Server_Match_Runtime.md). `DurableMatch` оборачивает тот же `ServerMatchRuntime` и LeoECS бой: решения, баланс и authored-контент не дублируются. HTTP-host теперь работает через эту обёртку. Режим остаётся **LocalOnly**: в этот host не подключены cloud adapters, ключи, ресурсы и платный запуск. Позднее установленные изолированные PlayFab SDK описаны в [PlayFab_SDK_Setup.md](PlayFab_SDK_Setup.md).

Проверено восстановление после потери процесса при сохранном локальном диске. Это не распределённый failover, не восстановление исчезнувшей MPS VM и не production settlement экономики.

## Слои и зависимости

| Файл/проект в Backend | Ответственность |
|---|---|
| TankDraft.Server.Persistence/StoreContracts.cs | Транзакция перехода, identity без bearer token, receipts, outbox, лимиты, fault hooks |
| TankDraft.Server.Persistence/SqliteMatchStore.cs | SQLite WAL/FULL, единая транзакция журнала/receipt/result, проверка цепочки и единственного writer |
| TankDraft.Server.Persistence/MatchRecipe.cs | Исходный контент, настройки/seed/участники, версия сборок и создание того же runtime |
| TankDraft.Server.Persistence/DurableMatch.cs | Commit перед публикацией, повтор ACK, replay, восстановление времени, доставка outbox |
| TankDraft.Server.Match/ServerMatchJson.cs | Общие JSON-конвертеры readonly боевых структур для wire и persistence |
| TankDraft.LocalHost | QA composition root, HTTP, scheduler и проверка restart между запросами |
| TankDraft.Server.Persistence.Tests; TankDraft.Persistence.Probe | Fault suite и отдельный процесс для настоящего Process.Kill |

SQLite зависит только серверная сборка; Unity/общий core не получают зависимость на БД. `Microsoft.Data.Sqlite` **10.0.12** закреплён в csproj и `packages.lock.json`; тестовый скрипт проверяет locked restore. Источник версии: [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12).

## Commit и подтверждение

1. Под lock проверяются identity, размер команды и ранее сохранённый receipt.
2. Runtime применяет допустимый ввод либо продвигается по серверному времени. Вычисляются hash состояния и новые решения с RNG state.
3. Одна SQLite-транзакция сохраняет переход, receipt при его наличии и финальный result outbox при завершении матча. Head и epoch проверяются при commit.
4. Только после успешного commit возвращается ACK или становится доступен новый snapshot.

При штатном shutdown вызывается `Flush()`: один bounded Pump и обязательный commit, даже если логическое время сдвинулось меньше чем на simulation tick и revision не изменилась. Метод использует тот же lock/fail-closed контракт. Dispose только освобождает ресурсы; при аварийном завершении источником остаётся последний commit. Этот случай выявлен и проверен в [локальном GSDK probe](Gsdk_Local_Lifecycle.md).

Ошибка применения или сохранения закрывает текущий `DurableMatch`: дальнейшие Capture/Pump/Execute запрещены. При неизвестном исходе commit состояние нельзя угадывать или продолжать из памяти — новый экземпляр перечитывает БД. Проверены ошибки до транзакции/commit, после применения команды и после commit до публикации ответа.

Receipt определяется парой `accountId + operationId` внутри БД одного матча. Повтор с новой проверенной сессией того же аккаунта возвращает прежний ACK. Изменение любого поля исходного envelope, включая Sequence, означает конфликт. Клиент сохраняет **весь исходный envelope** до получения ответа; смена сессии не требует переписывать старый запрос. Новые операции используют sequence новой сессии.

Доменные отказы, занимающие sequence, сохраняют receipt. Предварительные отказы gate его не создают; их воспроизведение не занимает sequence. `CatchingUp` сохраняет продвижение scheduler как Pump без receipt: тот же envelope можно прислать после завершения catch-up. Ошибки authentication не запускают домен. QA identity registry остаётся в памяти, bearer tokens в БД не записываются.

## Replay и время

БД хранит исходный JSON контента и его SHA-256, правила матча, seed, участников и hash фактических Core/Match/Security/Persistence DLL. При открытии требуется та же версия сборок и данных. Несовпадение версии, потерянная metadata, повреждённый или неполный журнал приводят к отказу без автоматического сброса матча.

Журнал хранит каждый committed Pump/Command с логическим временем, ожидаемым ACK, hash состояния и новыми ручными/автоматическими решениями. Восстановление **воспроизводит журнал от начала матча** и сверяет каждый результат. Так восстанавливаются draft, score, RNG, active ECS entities, снаряды и зоны. Hash/head — контрольная позиция восстановления, а не сериализованный быстрый ECS checkpoint. Время replay растёт с длиной журнала; быстрые checkpoints и измерение стоимости длинных матчей остаются следующими оптимизациями.

Политика локального теста — `PauseProcessDowntime`: после рестарта логическое время продолжается с последней сохранённой позиции. Время выключенного серверного процесса исключается; без изменений состояния clock сохраняется не реже одного раза в секунду при работающем scheduler. Боевые изменения сохраняются при каждом изменившем состояние Pump. Это техническая политика локальной проверки, **не утверждённое продуктовое правило компенсации серверного простоя**.

Отсутствие игрока отличается от остановки сервера: работающий процесс продолжает таймеры, случайные legal choices и все раунды даже без обоих клиентов. Возврат даёт актуальный snapshot либо финал; пропущенные анимации смотреть не нужно. Release catch-up/компенсация при outage и takeover между машинами требуют отдельной реализации и проверки.

## Хранилище и ownership

SQLite работает с `journal_mode=WAL` и `synchronous=FULL`: режим выбран для синхронизации WAL при commit. [SQLite WAL](https://www.sqlite.org/wal.html), [synchronous](https://www.sqlite.org/pragma.html#pragma_synchronous). Исправность файловой системы/носителя и соблюдение flush операционной системой остаются условиями; отключение питания и отказ диска не тестировались.

Эксклюзивный `.writer.lock` защищает БД от второго локального владельца; при открытии растёт epoch, запись проверяет epoch и предыдущий head. Проверены competing writer и устаревший epoch. Это **локальная блокировка и fencing**, не Azure lease и не защита от независимых копий БД на разных VM. Для облачного adapter нужны общая durable storage, CAS/lease и тот же fault suite; локальный диск MPS не считается такой storage.

`Config/local-persistence.json` задаёт LocalOnly, папку `Logs/BackendPersistence`, до 50 000 переходов, 262 144 байт на запись, 16 384 страниц SQLite. Host разрешает только путь под Logs данного checkout, store отвергает сетевые диски. Лимиты предназначены для локальной QA; это не полный дисковый quota для всех WAL/lock файлов. Достижение лимита останавливает матч с ошибкой, а не удаляет старые receipts.

Каждый запуск `--verify` создаёт отдельную БД. Тесты сохраняют свои артефакты в ignored `Logs/BackendPersistenceTests`; автоматического удаления пользовательских файлов нет. Hash цепочки обнаруживает порчу, но не является подписью и не защищает от администратора, способного переписать БД и приложение.

## Финальный результат и outbox

Финальный переход атомарно создаёт событие с устойчивым `ResultId`, связанным с идентичностью и началом данного матча. Outbox доставляется **как минимум один раз**. Если процесс упал после успешного внешнего вызова, но до отметки доставки, повтор содержит тот же ResultId. Получатель обязан дедуплицировать его собственным долговечным ledger.

Тестовый получатель — отдельная SQLite БД с уникальным ResultId: повторные попытки дают одну запись. Это проверка доставки, не выдача реальной валюты/награды и не доказательство exactly-once произвольного внешнего API. PlayFab Economy, покупки, reconciliation и ledger меты относятся к S6.

## Проверка и дальнейшая работа

Команда из корня: `./Tools/Backend/test-local-backend.ps1`.

- Security: 21 тест; Match: 22 теста; Persistence: 18 тестов (добавлены два shutdown Flush regression cases).
- Четыре сценария используют отдельный дочерний процесс и настоящий `Process.Kill`: до commit, после применения до commit, после commit до ответа, а также в активном бою после commit.
- После остановки активного боя восстановлены фаза, effect-сущности и точный hash сохранённого состояния. Отдельно проверены projectile/zone и многократные reopen по ходу полного матча в сравнении с непрерывным исполнением.
- Проверены receipt новой сессии, изменённый payload, CatchingUp без receipt, истёкшая/чужая identity, порча журнала/metadata/версии, конкурирующий writer, outbox при потере подтверждения доставки.
- HTTP: 46 проверок, включая restart во время боя/после финала и повтор ACK новой сессией; domain validation: 13 проверок; два одинаковых полных headless матча, итог 4:2.

Это конечный проверенный набор сценариев, не доказательство отсутствия всех ошибок. Не проверены power loss, потеря/повреждение диска, distributed takeover, Linux/MPS, реальные сети и телефоны, WSS, 100 CCU, живые PlayFab/Azure операции и покупки. NuGet vulnerability scan текущих источников не обнаружил известных уязвимых зависимостей Persistence; это не полный security audit.

Локальный S4 — согласование версий, Unity presentation серверных snapshots/events и reconnect/resync двух Windows Player — проверен: [Server_Client_Transport.md](Server_Client_Transport.md). Далее TLS/auth lifecycle и мобильная fault matrix. Azure storage/lease adapter и production GSDK проходят свои проверки перед облачным запуском; [zero-charge gate](Backend_Implementation_Plan.md) остаётся закрытым.
