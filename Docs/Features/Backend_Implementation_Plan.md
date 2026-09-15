# План серверной реализации и защитные границы
Status: local S1-S4 verified; Edgegap integration active; production auth/distributed recovery pending; Azure/MPS cost gates closed
Last reviewed: 2026-09-13

Актуальная SDK/hosting интеграция: [Edgegap_Integration.md](Edgegap_Integration.md). PlayFab.Identity/Azure/Edgegap adapters проверяются отдельно; PUN выключен из активного пути, GSDK остаётся legacy MPS probe. B16D9 сохранён. Edgegap Free Tier подтверждён; Azure no-charge и production login/assignment/recovery ещё требуют проверки. Старые S1–S7 ниже — техническая декомпозиция и evidence, не новое поручение включить MPS.

Порядок функционального выполнения после локальных S1–S4 задаёт [Functional_Release_Plan.md](Functional_Release_Plan.md). Этот документ сохраняет технические S1–S7, контракты и evidence; его локальные статусы не означают готовность remote PvP или production cloud.

## Решение

Владелец разрешил реализацию PlayFab/Azure-направления и потребовал безопасность взаимодействий и отсутствие платежей на первых этапах. Цель — доверенная серверная логика; абсолютную невзламываемость программного продукта гарантировать нельзя. Для каждой угрозы ниже заданы защита и проверка. Текущее выполнение ограничено **LocalOnly**: без PlayFab/Azure ресурсов, billing upgrade, live credentials и публичного listener.

Целевой stack: Unity/LeoECS Lite client presentation → WSS → standalone .NET 10 match server + PlayFab GSDK/MPS; PlayFab identity/matchmaking/Economy V2; Azure Functions isolated для меты, Table/Blob/Queue для состояния и доставки операций. WSS пока кандидат: решение закрепляется по latency/трафику/100 CCU spike. Mirror/Unity headless — запасной вариант при измеримой необходимости; PUN остаётся существующим прототипом до миграции клиента.

## Нулевые платежи — обязательное условие облачного запуска

- Azure Budget не останавливает ресурсы; cost data/уведомления запаздывают. Budget/alert/наш watchdog не являются жёстким лимитом расходов. [Microsoft: budgets](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets).
- Azure spending limit действует у определённых credit subscriptions, недоступен на PAYG; произвольный нулевой лимит выставить нельзя, отдельные внешние покупки не покрываются. Нельзя считать его защитой отдельного PlayFab billing. [Microsoft: spending limit](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/spending-limit).
- MPS free allowance — лимит включённого потребления, не доказательство отказа от дальнейших начислений. Standby также потребляет ресурсы, compute и egress считаются отдельно. [MPS billing](https://learn.microsoft.com/en-us/xbox/playfab/multiplayer/servers/billing-for-thunderhead), [PlayFab pricing](https://developer.microsoft.com/en-us/games/products/playfab/pricing/).
- Скриншот владельца показывает $0.00 estimated month-to-date за сентябрь 2026 (обновление 12 сентября, All titles), Profile: Storage 0.22 KB / Free. Это наблюдение не подтверждает условия будущего MPS, окончательный счёт или billing state Azure subscription. Поэтому **гарантированно бесплатный MPS launch не подтверждён**. Если провайдер допускает автоматическое платное продолжение, облако не запускаем. Недостаточно уменьшить max servers или поставить alert.
- В репозитории нет provisioning/deployment операции этого backend. Host принимает `--verify`, `--verify-socket`, `--verify-tls-session`, `--serve-tls-unity`/`--serve-tls-unity-auto` (legacy WS отдельно) с проверенным локальным Player; режим LocalOnly и numeric loopback endpoint. Нельзя включить cloud аргументом или environment URL. LocalHost не ссылается на установленные PlayFab SDK в отдельном Server.PlayFab; здесь нет ключей/внешних игровых вызовов. Это исключает создание облачных расходов данным срезом, но не управляет ресурсами, созданными пользователем или другими проектами в аккаунте.
- Для S1–S4 тесты локальные, включая будущих виртуальных клиентов; реальная внешняя группа 20–100 игроков и 100 CCU capacity пока не проверены. Локальный тест не следует выдавать за бесплатный MPS-тест. Если zero-charge gate не пройден, продолжаем локально; готовность к облаку не означает разрешение его включить.

## Последовательность и критерии приёмки

| Этап | Работа | Проверка завершения | Состояние |
|---|---|---|---|
| S1. Локальная основа | .NET10 сборка общего ядра, экспорт authored content, QA identity/commands/HTTP, ограничения входа | Security tests, настоящий драфт через HTTP, полный матч в headless core | Выполнено в указанном ниже объёме |
| S2. Серверный матч | Match actor/single writer, серверные deadlines, fixed-step combat, автоматические допустимые выборы, snapshots/events | Fake clock; оба игрока offline на 2+ раунда; гонка choice/deadline; одинаковое состояние после reconnect | Выполнено локально; [Server_Match_Runtime.md](Server_Match_Runtime.md) |
| S3. Надёжное восстановление | Durable receipts, journal/replay, ownership/fencing, outbox, recover/resume | Fault injection и Process.Kill, competing writer, повторы после restart | Локальный SQLite slice выполнен: [Server_Match_Persistence.md](Server_Match_Persistence.md). Azure storage/lease и быстрые ECS checkpoints ещё впереди |
| S4. Клиент и протокол | PlayFab-independent auth interface, WSS adapter, version negotiation, interpolation, reconnect/resync UI | Два Unity клиента; loss/jitter/reordering, slow consumer; background/kill/restart телефона; пропуск боёв | Windows loopback WSS slice выполнен: два Unity клиента, process kill/pending retry и socket reconnect. TLS/auth lifecycle: [Local_TLS_Auth.md](Local_TLS_Auth.md); encrypted TCP delay/reset/stall: [Network_Fault_Matrix.md](Network_Fault_Matrix.md); Android USB lifecycle проверен: [Android_Local_QA.md](Android_Local_QA.md); radio-network/production auth ещё впереди; [Server_Client_Transport.md](Server_Client_Transport.md) |
| S5. PlayFab/MPS | GSDK lifecycle, server allocation/ticket, identity validation, queue/bot policy, title API access policy | Local GSDK agent + Linux build; затем 2 реальных клиента только после zero-charge gate | SDK и Windows Local GSDK probe проверены; production host/auth, Linux/MPS ещё впереди; cloud gate закрыт |
| S6. Мета и покупки | Azure Functions, platform login/link, save/CAS, Economy V2, durable intent/outbox/reconciliation, store receipts | Повтор purchase/reward, disconnect/timeout/crash, receipt другой учётки, refund/restore; сначала fake providers | Не начато |
| S7. Эксплуатация | 20/50/100 virtual CCU, security review, secret rotation, monitoring, bounded queues/logs | Soak/abuse/fault matrix, измеренный RAM/CPU/egress, согласованный cloud billing mode | Не начато |

S4 продолжается после локального WSS/auth и encrypted TCP fault slice: device lifecycle, IP packet loss/reordering и нагрузочное измерение трафика/задержки. Azure storage/lease adapter должен выдержать тот же fault suite, что локальный S3. До публичного запуска обязательны distributed recovery, полная S4, S5 и security acceptance.

## Что реализовано в S1

- `Backend/TankDraft.Game.Core` компилирует существующие pure contracts/match/simulation/network core linked source, не дублирует правила и не зависит от Unity/Photon. LeoECS Lite vendored из уже закреплённого UPM commit вместе с исходной MIT-Red лицензией; это не смена библиотеки.
- `Tools/Backend/ExportContent.cs` через Unity MCP читает MatchSettings/Battle/Meta SO и экспортирует JSON + hash. Deck/приказ берутся из initial profile. Боевой баланс сохранён; debug-команды отключены серверной политикой. Loader проверяет hash, schema, deck, ссылки unit IDs и поддерживаемый приказ.
- `LocalSessionRegistry` создаёт временные 256-bit opaque tokens, хранит только SHA-256, ограничивает TTL и число session, поддерживает revoke. Это QA identity, не PlayFab login и не persistent account.
- Серверная identity привязана к match/side. Команда содержит выбор, сервер проверяет content/round/choice token/offer legality. Клиент не задаёт side, army stats или winner. Чужие/неизвестные JSON поля и команды отклоняются.
- `InMemoryCommandGate`: operation fingerprint, последовательность, точный replay acknowledgement, конфликт reuse operation ID, ограниченное число receipts без вытеснения. Исключение callback блокирует gate целиком. Все mutation сериализованы; повреждённое состояние не продолжается молча.
- Kestrel слушает loopback; внешние config/CLI/environment не добавляют endpoints. Auth до domain; global rate limit применяется и к неавторизованным запросам; ограничены body/headers/connections. Cross-origin запросы запрещены, секретов в logs/files нет.
- `--verify` временно запускает реальный HTTP endpoint, проверяет ошибки и драфт двух identity, затем отдельно исполняет реальные полные бои в server core. Host останавливается после проверки; production runtime ещё не включён.

## Угрозы и обязательные защиты release

S2 добавил независимый BackgroundService, monotonic deadlines, Choose/Order, legal autochoices и snapshot/event resync: [Server_Match_Runtime.md](Server_Match_Runtime.md). S3 сохраняет переходы и receipts до публикации, восстанавливает бой replay и доставляет финал через outbox: [Server_Match_Persistence.md](Server_Match_Persistence.md). Область гарантии — перезапуск процесса при сохранном локальном диске.

| Угроза | Защита | Тест/статус |
|---|---|---|
| Клиент подменяет игрока/матч | Проверять PlayFab/platform credentials сервером, выдавать короткий ticket, привязанный к account/match/audience/session; не доверять accountId из JSON | QA binding проверен; настоящий PlayFab auth впереди |
| Перехват/подмена сообщений | TLS/WSS с проверкой сертификата, token rotation/revoke, секреты только на сервере; без ключа signing в Unity build | Локальный TLS, pinning и rotation реализованы; production trust/auth впереди |
| Читер присылает цену/победу/запоздалый выбор | Allowlist DTO + фаза/token/deadline/content/ownership, все вычисления на сервере | Choose/Order и deadline проверены; meta validation впереди |
| Дубли и потерянный ACK | Operation ID + payload hash + durable receipt; повтор возвращает прежний результат | SQLite receipt проверен после restart и смены сессии |
| Два устройства или два серверных владельца | Единственный writer на match, lease с fencing token при каждом commit, session generation и revoke | File lock + epoch/CAS проверены локально; distributed lease и policy двух устройств впереди |
| Disconnect на несколько раундов | Clock/scheduler живут на сервере; сохранить каждый auto-choice и результат; возврат по identity и актуальному snapshot | Offline и локальный persistence/replay проверены; outage policy пока локальная PauseProcessDowntime |
| Падение сервера после внешней выдачи | Durable intent перед side effect, outbox/reconciliation, provider idempotency + собственный постоянный ledger | S3 outbox проверен с отдельным SQLite test ledger; реальные reward/purchase providers относятся к S6 |
| Повтор/чужой IAP receipt | Серверная Google/Apple validation/redemption, account/product binding, transaction ledger; refund/restore обработчики | S6, fake providers до sandbox-покупок |
| Запросы создают расходы/DoS | Gateway admission до expensive API, bounded queues/partitions, per-account/global quotas, отключённый public allocation; provider hard no-charge gate | QA limits есть; cloud protection не подтверждена |
| Повреждённый контент/устаревший клиент | Версия bundle закреплена на весь матч; signed release artifact/build allowlist, validation экспортируемых ссылок | Hash/version проверены; hash не digital signature |
| Утечка секретов/операторские ошибки | Отдельные dev/prod title/subscription, least privilege/managed identity, server keys вне клиента/Git, секреты вне логов, audit/rotation | В S1 ключей нет; IAM перед cloud launch |

HMAC с ключом, выданным клиенту, не даёт anti-cheat. IP/device ID также не являются доказательством личности. Attestation может повысить стоимость злоупотреблений, но не заменяет бизнес-валидацию.

## Контракт S2/S3, который нельзя потерять при реализации

1. Серверный clock задаёт deadline выбора; arrival time определяется сервером. Queue order/deadline resolver дают один исход гонки. Автовыбор — seeded random среди текущих legal offers, записывается один раз вместе с RNG state. Никогда не используется клиентский clock.
2. Combat идёт fixed-step; исход определяется точным временем смерти, при точном совпадении один authority roll. Не вводим минутный combat timeout/HP winner. Все phase transitions выполняются сервером, независимо от подключения клиентов.
3. Match state включает content/protocol version, phase/revision, deadline, armies/offers/commit flags, score, seed/RNG state, simulation checkpoint/accepted events, session mapping и последнюю долговечную позицию журнала. Приватные offers соперника не отправляются клиенту.
4. ACK принятой команды отправляется после durable commit. S3 локально восстанавливает журнал от начала матча; быстрые ECS checkpoints и release outage catch-up/компенсация ещё не реализованы. Локальная PauseProcessDowntime исключает время остановленного процесса; живой сервер продолжает offline-матч. Пока результат внешней операции неизвестен, reconciliation, а не новый reward ID.
5. MPS reallocation не восстанавливает память процесса автоматически. Новая authority читает durable state, получает новый fencing token и отклоняет старого владельца. Resume возвращает текущий snapshot или сохранённый финальный результат; просмотр пропущенных анимаций не требуется.
6. Slow client не останавливает матч и не создаёт бесконечный буфер; ограниченный event backlog, затем snapshot resync. Disconnect одного игрока не превращает его матч в новую allocation.

## Проверено и не проверено

2026-09-13, Windows, локальный SDK 10.0.401:

- Standalone Core и Security: build без warnings/errors.
- Security unit suite: **21 passed**.
- Server match suite: **22 passed** (deadline ±1 tick, Execute/Pump race, replay после deadline, команды/приказы, snapshots, bounded catch-up, одинаковые полные offline-матчи при мелких/крупных шагах scheduler).
- Persistence suite: **18 passed**, включая четыре сценария настоящего Process.Kill, active battle replay, durable receipts, competing writer/epoch, corruption, outbox с отдельным test ledger и два shutdown Flush regression tests.
- Local GSDK: три lifecycle сценария с официальным LMA и 12 LocalOnly boundary checks; отдельный QA host, без реального login или игровых клиентов.
- Local HTTP verification: **46 checks passed** (реальные requests через Kestrel, auth/replay/spoof/size/rate limits, authored draft, BackgroundService, reconnect, wire roundtrip, restart в бою/после финала и старый ACK новой сессии).
- Фоновый матч без запросов клиентов во время ускоренного offline-интервала: **5 раундов, 1:4**, возвращение в бой, через несколько раундов и после финала. Clock ускоряется QA-кодом, progression выполняет BackgroundService.
- Existing MatchDomainValidation: **13 checks passed**.
- Дважды полный headless матч: шесть боёв, итог **4:2**, одинаковые tick counts/outcomes при одинаковом seed. Это не тест одинаковой картинки на двух устройствах.
- Собственные новые backend/tool файлы прошли whitespace-проверку; общий `git diff --check` сообщает существующие trailing whitespace в стороннем DOTween, эти файлы не менялись в данном патче. Unity MainMenu осталась в Edit Mode, scene dirty=false.

S4: 36 клиентских тестов, реальный Kestrel WSS verifier и полный матч двух Windows Unity Player с восстановлением после process kill, смены access и 40-секундного обрыва. Семь раундов, 3:4, 196 общих battle ticks без расхождений. Результаты и границы: [Server_Client_Transport.md](Server_Client_Transport.md), [Local_TLS_Auth.md](Local_TLS_Auth.md).

Непроверено: Linux build, 100 CCU, реальные мобильные радиосети, потеря диска/VM, distributed failover, PlayFab/Azure/MPS, облачный billing и реальные покупки. Android-устройство через USB и локальный сетевой fault matrix проверены в последующих срезах S4. Локальные S3/S4 не являются release-ready облачным сервером; distributed storage/lease остаются обязательными до публичного запуска.

## Очередь и бесшовное обновление (2026-09-13)

В S4 добавлены MainMenu matchmaking, серверный fallback-бот после 10 сек., cancellation/requeue, восстановление queue identity после force-stop телефона. Reauthenticate сохраняет WSS/cursor/buffer; CatchingUp не сбрасывает Connected. После подтверждённого MatchResult сеанс опроса завершается, результат остаётся на экране до возврата в меню. Core 10 тестов + HTTP verifier; client protocol 39 тестов. Runtime/evidence и локальные команды: [Matchmaking_and_Bots.md](Matchmaking_and_Bots.md).
