# TankDraft backend

15.09 актуально: Fusion dedicated gateway → LocalAuthority/RemoteHost .NET10/LeoECS. PlayFab Legacy мета включена для двух QA, Azure отложен. [R3.2 settlement](../Docs/Features/Match_Result_Settlement.md): постоянный SQLite журнал завершений/наград вне временного InstanceId, отдельный Legacy QA writer1CO/лимит10. Незавершённые RemoteHost матчи остаются process-local; NeedsReview не ретраится. Описание F0/no-economy и20s драфта ниже историческое; текущий драфт5s, состояние/ограничения см. Functional_Release_Plan.md.

Актуальный план: [Fusion_Dedicated_Migration.md](../Docs/Features/Fusion_Dedicated_Migration.md). Fusion SDK подключён в Unity; серверное ядро пока .NET 10, перенос scheduler/security и игрового транспорта ещё впереди. Azure/Edgegap/GSDK probes и SDK удалены из активного дерева; PlayFab Identity, match, admission и persistence сохранены. Старый RemoteHost нужен для регрессионной проверки до cutover.
Status: Fusion Dedicated migration F0; legacy HTTP/WSS retained until gameplay cutover
Last reviewed: 2026-09-14

План, security boundaries и ограничения стоимости: [Backend_Implementation_Plan.md](../Docs/Features/Backend_Implementation_Plan.md).


`TankDraft.RemoteHost` — отдельный no-Azure .NET host: PlayFab tester allowlist до 100, opaque lobby access, queue 10 секунд, не более 20 matches и 40 queued players на process. Он выдаёт process-local short match access и проверяет auth на каждом HTTP/WebSocket message. Cancel/requeue создаёт tombstone; early leave отклоняется; match продолжает offline. Current bot fallback использует server autochoices по draft deadline 20 секунд, без think-policy или подбора силы. После 45 минут process входит в drain, после 50 — останавливается; `Config/remote-host.json` задаёт bounded policy. Finished result хранится в памяти 120 секунд, restart теряет его; нет economy/profile/rating mutations. Проверка `Tools/Backend/verify-remote-host.ps1` прошла 14/14 с fake PlayFab и настоящими HTTP/WS; evidence: `Logs/RemoteHost/fa8ed2721ac94077b94539438a5bce21/remote-host.trx`.

OCI linux/amd64 archive RemoteHost локально проверен non-root UID 1654: `Logs/RemoteContainer/3b416b269583481d984c90638aec2ea7`, SHA-256 `68e3d9f93e0dbfe091db609fe0945e44da5ae418cd5ec2645ca40d433d8ad40a`. `publish-remote-container.ps1 -PrepareLocks` готовит locks/archive; `verify-remote-container.ps1` PASS. Archive не pushed, не запускался на Linux/cloud. `--serve` требует native TLS с supplied private PFX; server secret поступает только через `TANKDRAFT_PLAYFAB_SERVER_SECRET_PATH` вне image. Managed Edgegap TLS Upgrade, secret injection, Unity remote queue/client factory/secure identity и cloud two-peer test ещё не приняты. Контракт и cloud boundaries: [Edgegap_NoAzure_Prototype.md](../Docs/Features/Edgegap_NoAzure_Prototype.md).

Прежние PlayFab API/GSDK сохранены в `TankDraft.Server.PlayFab` только для `GsdkLocalProbe`. Локальный lifecycle: bootstrap-lma.ps1 → test-gsdk-lifecycle.ps1, [GSDK probe](../Docs/Features/Gsdk_Local_Lifecycle.md). MPS не входит в Edgegap publish graph.

Из корня проекта, PowerShell:

```powershell
./Tools/Backend/bootstrap-dotnet.ps1
./Tools/Backend/test-local-backend.ps1
```

Bootstrap скачивает официальный Windows x64 SDK 10.0.401 с проверкой SHA-512 в ignored `Logs/BackendSdk`. Системный .NET не меняется. NuGet restore обращается к nuget.org; облачных игровых ресурсов эти команды не создают. Для Linux SDK ставится отдельно согласно `global.json`; Linux-исполнение пока не проверено.

`--verify` поднимает HTTP на `127.0.0.1:18781`, создаёт краткоживущие QA-токены в памяти, проверяет запросы/драфт и независимый от клиентов BackgroundService: бои, автовыборы, полный матч и reconnect с новой сессией. QA clock ускоряется внутри процесса, HTTP-команды изменения времени нет. После проверки host останавливается. Если порт занят — запуск завершается ошибкой, чужой процесс не останавливается. Токены не печатаются и не сохраняются.

S4: `Tools/Backend/run-server-client.ps1` запускает ограниченный loopback WSS host `127.0.0.1:18783` и два Windows Unity Player. `-Automated` добавляет QA выборы, process restart с отзывом access и обрыв связи на 40 секунд. Grant передаётся своим Player через environment; access обновляется по HTTPS, стабильный StreamId сохраняет последовательность команд. Публичного login/allocation нет. Проверки — `Tools/Backend/test-tls-session.ps1` и `test-client-protocol.ps1`; legacy WS verifier сохранён отдельно. Authoring и evidence: [Server_Client_Transport.md](../Docs/Features/Server_Client_Transport.md), [Local_TLS_Auth.md](../Docs/Features/Local_TLS_Auth.md).

| Папка | Ответственность |
|---|---|
| TankDraft.Game.Core | Linked pure C# contracts/domain/simulation из Assets; одна реализация правил |
| TankDraft.Server.Security | LocalOnly, временная identity, проверка команд, process-local receipts |
| TankDraft.Server.Security.Tests | Unit tests защитных границ |
| TankDraft.Server.Match | Single-writer scheduler, серверные deadlines, auto-choice, snapshots/events/results |
| TankDraft.Server.Match.Tests | Fake-clock проверки серверных переходов на authored боевых данных |
| TankDraft.Server.Persistence | SQLite journal/replay, commit-before-ACK, receipts между сессиями, result outbox |
| TankDraft.Server.Persistence.Tests; TankDraft.Persistence.Probe | Fault suite и дочерний процесс для настоящего Process.Kill |
| TankDraft.Server.PlayFab.Identity | Session-ticket verifier: instance settings/SDK DTO и bounded cancellable HTTP, без GSDK |
| TankDraft.Server.Admission; TankDraft.AdmissionHttp | PlayFab account → trusted assignment → короткоживущий access и стабильный stream; HTTP session boundary, пока process-local |
| TankDraft.AdmissionChecks; TankDraft.AdmissionHttpChecks | Offline identity + реальный loopback HTTP/core: retries, revoke, оба offline и возврат к result |
| TankDraft.PlayFabLiveProbe | Операторский bounded login/admission тест двух стабильных PlayFab test identities; ключ только из приватного файла |
| TankDraft.RemoteHost | Local-verified no-Azure authoritative HTTP/WSS host; cloud deployment, Unity wiring and durable recovery pending |
| TankDraft.PlayFabIdentityChecks | Offline identity contract tests, не live service acceptance |
| TankDraft.LocalHost | Ограниченный QA HTTP-host, связка identity → domain, интеграционные проверки |
| Content | Экспорт authored SO/каталогов и SHA-256 content version |
| Config | Лимиты только локального QA |
| ThirdParty/LeoEcsLite | Точная копия уже закреплённой зависимости, лицензия и provenance |

Экспортировать данные: выполнить `Tools/Backend/ExportContent.cs` через Unity MCP (`TankDraftBackendContentExport.Run`). Скрипт читает `MatchSettings.asset` и связанные каталоги, сохраняет backend JSON/hash. Баланс сохраняется; `AllowDebugCommands=false` принудительно задаёт серверная политика. Prefab/view references не попадают в серверную сборку. SHA-256 обнаруживает несовпадение версий, **не является подписью доверенного издателя**.

QA API: Bearer token → `GET /v1/match` (свои offers/army + общий battlefield/results); `POST /v1/commands` (`Choose` или `Order`). Envelope: `MatchId, RoundId, ContentVersion, OperationId, Sequence, CommandKind, Payload`. Payload — JSON-строка `{Token, OfferIndex}` для Choose, `{Token}` для Order. Identity/side берутся из registry. JSON имена регистрозависимы, неизвестные/повторные поля запрещены. Клиент не передаёт победителя, цену или характеристики юнитов. Snapshot/resync и clock contract: [Server_Match_Runtime.md](../Docs/Features/Server_Match_Runtime.md).

Последовательность начинается с 1 для session. Durable receipt привязан к account+operation внутри одного матча: полностью идентичный envelope возвращает старый ACK даже после restart и смены сессии. Изменённый envelope, включая Sequence, с тем же operation ID отклоняется. Доменный отказ после проверки последовательности занимает sequence и receipt; предварительный отказ и CatchingUp их не занимают. Клиент сохраняет весь исходный envelope до получения ответа. ACK публикуется после commit; ошибка сохранения блокирует текущий экземпляр до восстановления.

`--verify` создаёт отдельную SQLite БД в ignored `Logs/BackendPersistence` и проверяет restart во время боя/после финала. `Config/local-persistence.json` ограничивает записи и разрешает только LocalOnly. Зависимость Microsoft.Data.Sqlite 10.0.12 закреплена lockfile, скрипт выполняет locked restore. Дочерние crash-тесты сохраняют БД в `Logs/BackendPersistenceTests`. Сборки и исходный контент закреплены на весь replay; смена версии вызывает отказ. Подробности: [Server_Match_Persistence.md](../Docs/Features/Server_Match_Persistence.md).

**Ограничения:** session registry в памяти; восстановление требует сохранного локального диска и той же сборки. Replay идёт от начала; PauseProcessDowntime исключает серверный простой из логического времени локального теста. File lock/epoch не заменяют distributed Azure lease. Outbox требует дедупликации получателем; пока проверен отдельный test ledger. Нет PlayFab auth, cloud storage, production MPS/GSDK, покупок или нагрузки 100 CCU. `MaxSimulationTicksPerRound` — abort отдельной QA-проверки с ошибкой; серверный scheduler не использует этот cutoff и не назначает победителя по времени.
