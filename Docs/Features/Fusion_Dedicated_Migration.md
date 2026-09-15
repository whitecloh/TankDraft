# Photon Fusion Dedicated — план перехода
Status: F2/F3 + native replication 2PC Human repeat PASS; заметное улучшение подтверждено, остаточные stalls и F4 открыты
Last reviewed: 2026-09-14

Уточнение 15.09: владелец отложил оптимизацию до основного игрового цикла. Продолжаем R3 по Functional_Release_Plan.md с необходимыми проверками identity/команд/результатов; performance/load после цикла. Остаток F4 и релизные проверки не объявляются завершёнными.

Текущее продолжение F4 — [Fusion_PC_Cold_Restart.md](Fusion_PC_Cold_Restart.md): полное закрытие обоих PC-клиентов40s, exact pending reconciliation и поздний возврат к результату PASS. Далее auth expiry, account/command fault matrix, performance/load. Это не полное завершение F4 или серверной части.

14.09 последнее решение владельца: Android отложен; F4/F5 сначала на PC. Текущие первичные источники, ограничения и реализация восстановления: [Fusion_PC_Recovery.md](Fusion_PC_Recovery.md). Мобильная приёмка остаётся последующим релизным этапом.

Новое явное решение владельца: продолжать закрытую разработку и тесты без шифрования, добавить его позднее. См. [Photon_Plaintext_QA.md](Photon_Plaintext_QA.md): credential-free QA wire, серверные токены остаются на gateway, opt-in AllowPlaintextQa; Custom Auth/allowlist/серверные проверки сохраняются. Прежние строки ниже об ожидании плагина как блокировке gameplay — история до этого решения, не текущий порядок. Полный Photon матч через отдельный QA entry уже проверен; main menu ещё впереди. Актуальное продолжение: Fusion_Gameplay_QA.md.

Текущее продолжение: [Fusion_Native_Replication.md](Fusion_Native_Replication.md). Штатная репликация Fusion и SDK interpolation внедрены; владелец подтвердил заметное улучшение. Отдельные подёргивания остаются: следующий performance шаг — profiler и Android/VPN, затем F4. История меню и прежнего arrival buffer: [Fusion_Menu_and_Presentation.md](Fusion_Menu_and_Presentation.md).

## Решение и границы

Владелец утвердил Photon Fusion 2 Dedicated Server для тестирования и дальнейшего развития. Первая площадка — отдельный серверный процесс на ПК владельца. Photon Public Cloud обеспечивает discovery/connectivity/relay; боевые решения остаются у нашего процесса. Это не PUN MasterClient, не Shared Mode, не Quantum и не Photon Server SDK. Отдельное решение о платных тарифах/публичном production запуске этим документом не принимается.

Сохраняем PlayFab B16D9: Entity Objects для профиля, Economy V2 Inventory/Catalog/Stores, TitleData для версионированных конфигов. Azure выключен. LeoECS Lite, authored content, domain validation, UI/prefabs и правила игры сохраняются. Старый AppId PUN не использовать для Fusion.

Целевой SDK: Fusion 2.1.2 Stable build 2279 (официальный пакет для Unity 6.3), с поставляемым Realtime 5. Отдельно Voice/Quantum/Mirror/Photon Server не устанавливать. Проверить пакет, хэш и компиляцию перед подключением игрового кода.

## Архитектура

### Дополнение 14.09: управление локальным сервером

Реализована браузерная панель, независимая от Unity Editor: `Tools/Fusion/open-server-manager.cmd`. Подробности, проверки и текущие ограничения — `Local_Server_Manager.md`. F1 Windows readiness build готов; полный gameplay cutover ещё не выполнен.

Выбрана композиция: .NET 10 authoritative runtime остаётся единственным источником правил, а Unity Fusion server является отдельным сетевым gateway на том же ПК. Runtime размещён в процессе менеджера; внутренний loopback ingress защищён per-run key. Перенос всех .NET10 библиотек в Unity .NET Standard для первого этапа не требуется. Игровые команды, snapshots и account binding gateway будут подключены в F2; текущий F1 gateway обслуживает только readiness.

Photon dashboard обновлён владельцем: теперь Free 100 CCU, 300 GB, paid subscription 0, burst disabled (проверено 14.09). Ниже F0 20 CCU — исторический снимок. Custom Authentication URL PlayFab установлен, anonymous disabled, reject-if-unavailable enabled. PlayFab add-on установлен владельцем: получение Photon token для Fusion и Chat подтверждено API. Chat App ID `23d5a647-4d6e-4edd-85ad-7e666c04e787` (TanksChat, Development 20 CCU / 10 GB); SDK Chat и игровые chat-соединения не добавлены.

### Проверка F1 и подготовка F2 — 14.09

- Реальный отдельный Windows GameMode.Server и два Windows GameMode.Client через Photon: 60 секунд, по 59 AuthorityReady replies, оба exit code 0. Это только диагностическая связность, не игровой матч или проверка плавности.
- Флаг EncryptionConfig включён, но фактический SDK log сообщает `Photon Cloud Encryption is disabled`. Поставке недостаёт DatagramEncryption native plugin / IPhotonEncryptor. PlayFab SessionTickets и игровые запросы через Fusion не передавались. Custom Authentication токен — отдельный от SessionTicket механизм.
- Запрос совместимого плагина Windows x64 / Android ARM64 / Linux x64 и подтверждения бесплатности отправлен с разрешения владельца. См. `Photon_Encryption_Request.md`. До установки и проверки фактического шифрования игровой Fusion ingress остаётся отключён.
- Подготовлен server-only `FusionAuthorityPeer`: фиксированные маршруты к существующему .NET authority по закрытому loopback, ограничение размера, один запрос на игрока, отмена чтения ответа по deadline. Gateway должен передавать только UserId из `NetworkRunner.GetPlayerUserId`, а не из клиентского payload.
- Private ingress связывает Photon identity с проверенным PlayFab аккаунтом при login/session/queue/socket; чужие lobby/access tokens отклоняются. Регрессия RemoteHost 40/40, Manager 4/4; локальный интеграционный тест реального исходника адаптера проходит lobby → queue → session → snapshot → accepted command → duplicate. Отдельно проверены oversized responses и cancellation после HTTP headers. Fake PlayFab fixture не означает live gameplay.
- Исправлены длина persisted QA server identity и получение ExitCode в PowerShell pair test. Evidence: `Logs/FusionMigration/Checks/fusion-account-binding.trx`, `server-manager.trx`, `Logs/FusionServer/pair-*/result.json`.
- Fusion callback wiring, main-menu cutover, полноценные матчи и reconnect через Fusion ещё впереди. В F1 player logs есть nonfatal Addressables startup errors (settings.json); устранить перед игровой сборкой.

### Продолжение F2 — клиентский протокол, 14.09

- `FusionRequestChannel` сопоставляет request IDs с ответами, ограничивает размер и число запросов в работе, завершает ожидание при cancellation/deadline/disconnect. Ответ отменённого запроса не завершает следующий запрос; после Dispose callbacks от старого соединения игнорируются. Новый runner обязан получить новый экземпляр канала.
- `FusionAuthorityClient` использует прежний формат Lobby/Session/Queue/Socket. OperationId, sequence и payload остаются ответственностью существующего intent/session flow; автоматических повторов внутри адаптера нет. Ответ сервера разбирается строго, наружу из отказа передаётся status без сырого тела.
- Проверка `secureConnection` выполняется перед отправкой и при получении ответа. Будущий SDK adapter должен получать её из фактического состояния авторизованного и зашифрованного канала; JSON/SO флаг не является доказательством. В текущем runtime эти классы ещё не подключены к `NetworkRunner`; fake callback в тесте не подтверждает шифрование Photon. SDK send должен вызываться на Unity main thread, сохраняя привязку callback к исходному соединению.
- Интеграционный тест связывает реальные исходники клиента и `FusionAuthorityPeer` локальным callback, затем закрытым loopback с прежним .NET authority и fake PlayFab. Два аккаунта находят друг друга, один отправляет выбор и его повтор, оба канала закрываются, сервер продолжает матч. После 40 виртуальных секунд и последующих возвратов клиенты получают тот же MatchId и одинаковые Wins0/Wins1/LastWinner/Results; победитель набирает 4 победы. Это LocalOnly автоматизированная проверка, не Human/network/visual приёмка.
- RemoteHost suite **49/49**, включая 8 проверок клиентского канала/парсинга и полный матч; evidence `Logs/FusionMigration/Checks/fusion-client-protocol.trx`. Unity compilation и prefab binding через MCP PASS; dependency foundation 23 projects / 35 refs, scoped diff check PASS.
- Подготовлено исправление Addressables startup diagnostic: `FusionDiagnosticSceneManager` через штатный override Fusion возвращает пустой список сетевых сцен, authored prefab передаёт его в StartGameArgs. Пакеты и игровые сцены не менялись. MCP проверил binding и немедленный пустой результат resolver; новая Windows Player сборка ещё не запускалась, отсутствие ошибки в Player пока не подтверждено.

Следующий шаг после ответа Photon: совместимый plugin + проверка фактического encryption, затем main-thread SDK callback adapter/закрытие старых каналов, подключение существующих queue/credentials/intent/view и полный Fusion Human матч. До этого не удалять действующий HTTP/WSS клиент и не отправлять игровые credentials через diagnostic вход.

### Историческая подготовка F0

- Установлен официальный Fusion 2.1.2 build 2279, SHA256 пакета `852e95d2e080a7c76f8376e5bc054cb5153267140687c4fd66b9e1ca242aea66`. Пакет и восстановимые копии удалённых исходников: ignored `Logs/FusionMigration`.
- AppId `92d5f593-3b30-4493-8168-ca40ae0e55b0`, AppVersion `tankdraft-fusion-qa-v1`, регион `eu`; dashboard подтвердил Fusion 20 CCU Free (60 GB), Free 100 ещё не активирован.
- Через Unity MCP настроены 2 игрока на сессию, encryption enabled, auto host migration disabled. Это конфигурация SDK, не доказательство защищённого соединения.
- Удалены PUN SDK и PUN-only runtime/editor/scene/config/prefab assets; Fusion sample menu/demos также удалены. Realtime 5 из поставки Fusion сохраняется.
- Через Unity Package Manager удалён неиспользуемый `com.unity.multiplayer.center`; отсутствие подтверждено в registered packages. Остальные Unity-пакеты не чистились по предположению об их ненужности.
- Удалены неиспользуемые Backend Server.Azure, Server.Edgegap, EdgegapHost, GsdkLocalProbe, старый Server.PlayFab (GSDK/AllSDK probe), их adapter checks, launch tools и locks. PlayFabSDK в Unity и Server.PlayFab.Identity сохранены.
- AdmissionHttpChecks больше не подтягивает AuthoredContent из удалённого EdgegapHost; fixture теперь принадлежит проверкам admission.
- `TankDraft/Networking/Fusion/Configure Local SDK` и `Validate Local SDK` — воспроизводимая настройка/проверка без запуска соединений. `Tools/Fusion/verify-foundation.ps1` проверяет зависимости и отсутствующие retired SDK.
- Unity SDK validation PASS; RemoteHost 35/35 tests; AdmissionHttp/core 46 checks PASS (loopback/fake PlayFab, не cloud).
- Dependency audit PASS: 20 оставшихся backend projects, 33 ProjectReference без разрывов. Финальный Unity state: compiling=false, compilationFailed=false. Evidence: `Logs/FusionMigration/unity-validation.txt`, `foundation-check.txt`, `Checks/remote-host.trx`. Общий git diff --check находит прежние пробелы в DOTween; изменённый нами scope проходит проверку.
- На момент F0 онлайн-тестов не было; актуальный результат F1 указан выше. Игровой runtime ещё не подключён к Fusion, нагрузочная приёмка не выполнена.

- Dedicated runner работает только в GameMode.Server; клиент — GameMode.Client. При отсутствии сервера клиент не становится хостом.
- Боевой домен не зависит от Fusion. Команды проходят серверную проверку identity, match/round/content version, operation ID, фазы, срока и принадлежности выбора.
- Существующий runtime использует .NET 10. Unity не загружает эти DLL: выбран отдельный доверенный .NET процесс и Fusion gateway с закрытым loopback IPC (дополнение выше). Не копировать независимую вторую реализацию правил.
- View получает immutable snapshots; Fusion adapter не внедряет экономические решения в UI. Сохраняем последовательность событий, интерполяцию и коррекцию после reconnect.
- PlayFab custom authentication для Photon и серверная привязка соединения к проверенной identity. AppId/имя комнаты не являются секретами или авторизацией. Подмена userId, произвольная колода, replay и повторные команды отклоняются.
- Серверный секрет только в server runtime/private environment, не в client prefab, SO, build или логах. Проверять сетевое шифрование до передачи credentials; не отправлять PlayFab session ticket в открытых room properties.
- Отсутствие input не останавливает scheduler: драфт 5 секунд, пропущенный выбор делает сервер; очередь 10 секунд и подходящий серверный бот. Одновременное отключение клиентов не прекращает матч.
- Reconnect связывается с аккаунтом и сохранённым match ID, а не временным PlayerRef. Снимок содержит текущую фазу, дедлайн, счёт и армию. Повторный вход не создаёт второго участника.
- Сбой процесса и пропажа игроков — разные случаи. До принятой durable реализации потерянный незавершённый матч аннулируется без наград/штрафов. Не обещать восстановление по одному сохранённому Photon room.
- Несколько матчей: ограниченный pool экземпляров с изоляцией данных и лимитом ресурсов. Модель runner/process на матч выбрать по измерениям, не создавать неограниченные процессы из клиентского запроса.

## Последовательность и приёмка

| Этап | Работа | Критерий завершения |
|---|---|---|
| F0 | Инвентаризация, документация, установка SDK, удаление неиспользуемого PUN | Unity компилируется; нет старых PUN DLL/скриптов/QA assets и конфликтов Realtime |
| F1 | Минимальная отдельная server/client сцена, authored настройки, PlayFab/Photon конфигурация | Отдельный PC server и два Client соединяются; клиент никогда не становится host; нет секретов в клиентах |
| F2 | Подключить общий authoritative runtime и ограниченный transport adapter | Полный Human матч с теми же правилами, таймерами, снарядами и результатом; domain regression pass |
| F3 | Очередь, server bots, cancel/requeue, repeat matches | Два последовательных Human матча; timeout приводит к явно обозначенному server bot |
| F4 | Reconnect, обе стороны offline, suspend/restart клиента, ошибки/атаки | Возврат в текущую фазу, автодрафт offline; duplicate/stale/foreign commands не меняют состояние |
| F5 | Сначала PC: сети и нагрузка; mobile отложен владельцем | PC на текущем маршруте с VPN где нужен; 10/20 матчей по CPU/RAM/трафику; измеренная плавность и потери. Android и прямой маршрут — отдельная последующая приёмка |
| F6 | Cutover главного меню и окончательная очистка старого стека | MainMenu использует Fusion; старые remote launcher/Edgegap/MPS deploy tools и SDK больше не требуются и удалены |

Проверки выполненных этапов отмечаются по фактическим запускам, не по наличию класса или успешной компиляции. Идентификатор Fusion и вход для SDK предоставлены владельцем.

## Очистка зависимостей

1. Удалить Assets/Photon с PUN только при готовом официальном Fusion package, чтобы импортировать его собственный Realtime без конфликтов. Старый пакет и связанные QA assets предварительно сохранить вне Assets в ignored Logs: они ещё не закоммичены.
2. Удалить PUN-only transport/bootstrap/editor/diagnostic scenes/configs. Сохранить Match/Networking/Core: он используется Backend/TankDraft.Game.Core и не является PUN SDK.
3. Удалить LegacyPhotonIsolation вместе со старым SDK; убрать obsolete define только у целевых сборок через Editor API.
4. Edgegap, MPS/GSDK и Azure probes/SDK удалить после аудита обратных зависимостей. Shared identity/security/match/persistence не удалять вместе с hosting adapters.
5. Пока F2–F4 не завершены, HTTPS/WSS RemoteHost и ServerClient остаются временной рабочей реализацией и регрессионным ориентиром. Не объявлять их неиспользуемыми до переключения клиентов.
6. Удалённые аккаунты, registry и secrets не удалять попутно; новые Edgegap deployments не создавать. Существующие исторические документы сохраняют evidence, но помечаются superseded.

## Расходы и безопасность тестов

Fusion Free 100 CCU: одно приложение, ограничение подключений, включённый трафик. Это не безлимитный relay и не гарантия нулевых списаний при любом использовании. Не включать paid upgrade/burst; ограничить тестеров, время запуска, число комнат, сообщения и трафик. Проверить фактический тариф в dashboard до online теста. Custom Auth обязательна перед открытым доступом.

PC server выключается явно после теста; не открывать firewall/router и не публиковать QA loopback grants автоматически. Для внешнего теста сначала выбрать безопасный direct/relay путь. Домашний PC не защищён от отказа питания/интернета; Photon не переносит его симуляцию в облако автоматически.

## Источники

- https://doc.photonengine.com/fusion/v2/getting-started/sdk-download
- https://doc.photonengine.com/fusion/v2/concepts-and-patterns/dedicated-server-overview
- https://doc.photonengine.com/fusion/v2/manual/connection-and-matchmaking/matchmaking
- https://doc.photonengine.com/photon/current/pricing
- PlayFab_Player_Data_Design.md; Server_Match_Runtime.md; PC_Remote_Stability.md
