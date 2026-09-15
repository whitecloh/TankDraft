# PlayFab / Azure / Edgegap: первая интеграция
Superseded 2026-09-14: тестовая площадка меняется на ПК владельца с Photon Fusion Dedicated. План: [Fusion_Dedicated_Migration.md](Fusion_Dedicated_Migration.md). Edgegap control-plane/readiness SDK и команды публикации удалены; ниже — исторический runbook и evidence, не действующая инструкция развёртывания.
Status: Edgegap managed HTTPS, local no-Azure RemoteHost and partial same-PC Windows human acceptance verified; independent-network/device remote PvP pending
Last reviewed: 2026-09-13

## Принятое направление

Последнее поручение владельца — собрать PlayFab/Azure/Edgegap, подключить необходимые SDK, отключить неиспользуемые и провести первые тесты. Это текущий путь R1 из [Functional_Release_Plan.md](Functional_Release_Plan.md). Oracle не используется; PUN host migration был гипотезой и не заменяет dedicated authority. При отключении обоих клиентов сервер продолжает матч и автоходы; результат не аннулируется по правилу из PUN-гипотезы.

Временный R1 prototype утверждён без Azure: один bounded Edgegap instance принимает authenticated queue через PlayFab identity, выдаёт assignment и short match access, обслуживает WSS и authoritative match, затем requeue. Disconnect клиента не останавливает матч. Потеря instance/container явно истекает и voids незавершённый match без рейтинга, наград или иных live economy updates; уникальный server instance ID не даёт stale match возобновиться на новом процессе. Managed deployment и partial same-PC Windows human acceptance проверены: server продолжил матч offline и клиенты rejoin. Независимые сети/device, fault/load и durable remote PvP не приняты. Azure adapters остаются изолированными для отдельного durable path; PlayFab cost gates не снимаются. Подробности: [Edgegap_NoAzure_Prototype.md](Edgegap_NoAzure_Prototype.md).

| Слой | Текущий компонент | Граница |
|---|---|---|
| Unity | PlayFab classic SDK 2.242.260805; UnityPlayFabSessionSource, PlayFabMatchCredentials, существующий ServerClient/WSS | Login/credentials components и DI готовы; remote queue/composition и device login ещё не приняты |
| PlayFab server identity | `TankDraft.Server.PlayFab.Identity`, PlayFabAllSDK 1.229.260805, Newtonsoft.Json 13.0.4 | Session ticket проверяется per-instance Server API; нет доверия client accountId/side |
| Azure storage | `TankDraft.Server.Azure`, Azure.Data.Tables 12.12.0, Azure.Storage.Blobs 12.29.2 | Первые write adapters; не готовые IMatchStore/recovery/lease/экономика |
| Edgegap control plane | `TankDraft.Server.Edgegap`, стандартный .NET HttpClient, официальные REST v2 deploy / v1 status-stop | Organization token только на доверенной стороне; не Unity SDK и не game container |
| Linux process | `TankDraft.EdgegapHost` | Отдельный readiness probe с реальным боевым smoke; multiplayerReady=false, игровые API закрыты |
| Старые SDK | PUN — opt-in `TANKDRAFT_PUN_PROBE`; GSDK — `TankDraft.Server.PlayFab`/`GsdkLocalProbe` | Не входят в активный клиентский/Edgegap путь; исторические проверки сохранены |

Azure Functions/Identity/Queues, Mirror, Party и Edgegap Unity Server Browser SDK не добавляются без текущего потребителя. .NET серверу не нужен Unity hosting plugin. SDK контракты привязаны к официальным [PlayFab AuthenticateSessionTicket](https://learn.microsoft.com/en-us/rest/api/playfab/server/authentication/authenticate-session-ticket?view=playfab-rest), [Edgegap API](https://docs.edgegap.com/docs/api), [Edgegap OpenAPI](https://github.com/edgegap/openapi-specification). NuGet зависимости закреплены lock-файлами.

## Подтверждённые условия аккаунта

13.09.2026 владелец вошёл в Edgegap. В Subscriptions через UI подтверждено `Free ACTIVE`, `No Credit Cards Required`, 1 app / 2 versions, 1 concurrent deployment, до 1.5 vCPU, `Unlimited Deployments (1h uptime limit)`. Платный доступ предлагает отдельное добавление карты; оно не выполнялось. Публичные условия: [pricing](https://edgegap.com/resources/pricing). Количество deployment не равно числу матчей: multi-match worker требует отдельной интеграции и замера.

Владелец активировал Container Registry: UI показывает 10 GB, подготовленный OCI archive занимает 98 873 344 байта. Доступ к push сохранён в закрытом локальном файле вне проекта и не выводится при загрузке. Общий organization API token Edgegap не создавался. Образ загружен в частный реестр, удалённый digest совпал с локальным OCI image.

Free Tier Edgegap не делает бесплатными Azure Storage/Functions/Key Vault/Insights. Azure ресурсы и billing не подтверждены, не создавались; adapter checks не отправляют туда запросы. MPS для B16D9 не активирован из-за overage. Budgets/alerts и наш локальный gate не заменяют ограничение провайдера.

## Реализованные защитные границы

- PlayFab: отдельные instance settings/SDK DTO; cancellation-aware HTTP transport с 10-секундным deadline, fixed endpoint, default TLS, без redirect/cookies/retry, bounded response. Error/expired/неполный ответ не дают identity. Admission связывает verified account с доверенным assignment и стабильным command stream; локальный HTTP/core и client credential checks пройдены. Контракт, ограничения и live runbook: [PlayFab_Admission.md](PlayFab_Admission.md).
- Azure: create-only Tables; Replace с явным ETag и возрастающей revision; routing/player IDs неизменны. Ответ без корректного provider ETag считается неоднозначным. Blob имеет неизменяемое имя match/revision, размер до 1 MiB и `If-None-Match: *`. Это не атомарная транзакция Blob+Tables, не fencing и не runtime checkpoint восстановления.
- Edgegap: фиксированный API host, без redirect в production HttpClient, ограниченные время/размер ответа, допустимые идентификаторы и ресурсы. Один durable launch attempt на операторский run directory; timeout/5xx не запускает повторный POST. Неоднозначность требует сверки с dashboard. Этот file fence не распределённый allocator и не account-wide billing cap.
- Edgegap status: endpoint принимается только для запрошенного deployment, домена `<requestId>.pr.edgegap.net`, TLS-upgraded HTTP/WS порта. Provider READY не доказывает готовность игрового приложения.
- Readiness host: только явный probe mode, проверка injected deployment ID, authored content hash/schema и настоящий ServerMatchRuntime smoke. `/healthz` и `/readyz`; `/v1/*` закрыт с 503. Лимиты запросов/соединений, bounded lifetime до 30 минут, отсутствие внешних player grants и cloud secrets.

## Как проверить локально

Из корня репозитория, PowerShell:

```powershell
./Tools/Backend/verify-playfab-identity.ps1
./Tools/Backend/verify-azure-adapters.ps1
./Tools/Backend/verify-edgegap-adapters.ps1
./Tools/Backend/verify-edgegap-host.ps1
./Tools/Backend/publish-edgegap-container.ps1
```

Первичная подготовка RID locks выполняется явным `-PrepareLocks`; обычный publisher использует locked restore. Для уже проверенного архива `verify-edgegap-host.ps1 -RunDirectory <absolute Logs/EdgegapContainer/run>` проверяет OCI index/manifest/config: pinned base, linux/amd64, UID 1654, entrypoint, порт и archive hash. Загрузка в реестр — отдельный явный шаг `bootstrap-crane.ps1` → `push-edgegap-readiness.ps1 -RunDirectory <run> -UploadReviewedReadinessProbe`. Последний читает credential вне проекта, загружает точный OCI layout и сверяет remote digest; deployment не создаёт. CLI crane 0.22.1 загружен с официального google/go-containerregistry release с проверкой SHA-256.

Первые три проверки используют настоящие SDK/REST DTO, но подменённые внешние ответы. Проверка host запускает HTTP loopback и настоящее боевое ядро. Publisher готовит OCI archive в ignored `Logs/EdgegapContainer`; он не публикует реестр и не создаёт deployment. Наличие archive не равно выполнению Linux-процесса. Точные результаты текущего патча ниже.

## Проверено

- PlayFab identity: 36 offline checks PASS; admission 19 cases, HTTP/core 46 assertions, remote client credentials 18 tests PASS. 13.09.2026 явный live probe PASS (exit 0): два разных players, client login, server verification, assigned seats, lost-reply retry и reconnect rotation; 10 API calls, без tickets/account IDs в output. Evidence: `Logs/PlayFabAdmission/live-20260913-224701/live-acceptance.json`.
- RemoteHost: 23/23 local tests PASS через `verify-remote-host.ps1` с fake PlayFab и настоящими HTTP/WS; result `Logs/RemoteHost/1860abdacf154909b5d08722650ee380/remote-host.trx`. Managed OCI archive `4975774b5e8d795bcebaa9097252fa292388869ccbc9726ef5c93d987c67a55d` pushed and registry digest `sha256:200d757069a0096574d1a27c4a29aefe6d18a56a0c47f9d09b345fca3ea47798` verified; cloud version создана, deployment не создавался.
- Azure Tables/Blob: 15 offline checks PASS.
- Edgegap REST: 30 offline checks PASS (в том числе потерянный ответ, запрет повторного запуска, TLS/foreign-host/ID/размер/duplicate JSON/отмена).
- Locked restore/build PASS; NuGet vulnerability audit для Azure и PlayFab.Identity не нашёл известных уязвимых транзитивных пакетов в текущем источнике. Это не полный security audit.
- Unity CompilationPipeline: Photon/legacy transport/bootstrap/editor assemblies отсутствуют, `TankDraft.Match.ServerClient` остался. Активные сцены MainMenu и Match; новый Player/Android build после изоляции PUN пока не запускался.
- Локальный HTTP host PASS: настоящий бой из 5 раундов, 4:1, без внешних клиентов; health/readiness и `/v1` rejection. SDK adapters загружаются в composition graph без сетевых вызовов.
- Финальный archive: `Logs/EdgegapContainer/d125f623b2fa440bae45a367e3f49584/tankdraft-edgegap-readiness.tar.gz`, SHA-256 `42bffc33ced75dc8ecdc2719223c276f2bb26d5f19016065dc355f19c23862b9`. Base `mcr.microsoft.com/dotnet/aspnet:10.0@sha256:900c2dd83cc0cef53db0aaf786f12fe766ee075b6334750a664c9e77e7a7c0c5` реально закреплён при сборке и отражён в OCI config.
- Registry image digest `sha256:16d8905518183e3bb558268017c25302a9fb4f2e728c46282e5946d6667ae290` проверен после upload; receipt — `registry-upload.json` в том же run directory. Probe version `tankdraft/readiness-42bffc33ced7`, 0.5 vCPU / 1 GiB, срок 5 минут, restart Never. HTTPS port: internal HTTP 8080 + TLS Upgrade и verification; подготовлено перед запуском.

## Первая облачная проверка

Первый облачный probe выполнен 13.09.2026 с отдельным подтверждением владельца. Deployment `d409c9b6a7a7` (Dallas) перешёл в Ready в 19:10:53 UTC; cold start — 11.04 s. В 19:11:25 UTC проверены `https://d409c9b6a7a7.pr.edgegap.net:31492/healthz` и `/readyz`: HTTP 200 со штатной проверкой TLS. Content hash совпал, SDK assemblies загрузились, настоящий ServerMatchRuntime завершил 5 раундов со счётом 4:1. `/v1/match` вернул 503; `multiplayerReady=false`. Live PlayFab/Azure calls и внешние игровые клиенты не использовались.

Остановка принята провайдером в 19:11:36 UTC; после обновления dashboard подтверждён `Terminated`. Единственный тестовый контейнер остановлен раньше лимита 5 минут, аккаунт остался Free Tier; paid billing не включался. Evidence: `cloud-runtime-evidence.json` и `cloud-cleanup-evidence.json` рядом с registry receipt в указанном выше run directory. Это приёмка Linux hosting/core/HTTPS, а не завершение R1.

## Следующие обязательные шаги R1

1. OCI packaging, private registry upload и первый Linux smoke в Edgegap выполнены. Повторные облачные проверки проводить отдельными ограниченными запусками с обязательной остановкой и сверкой Free Tier.
2. PlayFab B16D9 real login/session validation выполнена; ключ остаётся вне Unity/репо/Logs. Проверить Azure account/no-charge режим отдельно; Azure adapters/emulator не являются durable cloud composition.
3. Подключить production identity к короткоживущим assignment/resume tickets, существующему WSS и серверной очереди. Удалённый тест не должен использовать LocalSessionRegistry или выданные QA grants.
4. Реализовать облачный journal/checkpoint, чтение/recovery, authority fencing и result ledger. Write adapters этого этапа не закрывают durability. Заранее закрывать admission перед часовым лимитом, восстанавливать уже подтверждённое после смены контейнера.
5. Два клиента из независимых сетей: Human match, оба offline, возврат к текущему состоянию/результату, rematch и server bot fallback. Только после этого R1 принят; затем R2–R8 по мастер-плану.

Покупки, live Economy и реальные награды этим patch не включаются. Число безопасных одновременных матчей и гарантии восстановления ещё не измерены.
