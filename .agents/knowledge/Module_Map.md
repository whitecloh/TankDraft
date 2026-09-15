# Карта реализации
Status: local и network match Windows scope реализован и validated; release/device scope открыт.
Last reviewed: 2026-09-13

| Путь | Назначение / состояние |
|---|---|
| AGENTS.md, .agents | Рабочий протокол TankDraft |
| Docs/Features | Продуктовые и инфраструктурные контракты |
| Backend; Tools/Backend | S1/S2/S3 standalone .NET10 core, security/HTTP, scheduler и authored JSON; Server.Persistence добавляет SQLite durable receipts/replay/outbox. Server_Match_Runtime.md, Server_Match_Persistence.md и Backend_Implementation_Plan.md задают контракты; cloud runtime/deployment и distributed storage/lease ещё впереди |
| Assets/PlayFabSDK; Backend/TankDraft.Server.PlayFab | Официальные Unity SDK и server API/GSDK зависимости, без реального login/cloud calls. Конфиг Unity: Assets/TankDraft/Configs/Infrastructure/PlayFab/Resources. Проверки/ограничения: PlayFab_SDK_Setup.md |
| Backend/TankDraft.GsdkLocalProbe; Tools/Backend/*gsdk*; bootstrap-lma.ps1 | Локальный LMA/GSDK lifecycle + DurableMatch; три сценария, Flush при остановке и запрет повторного ReadyForPlayers после shutdown. Gsdk_Local_Lifecycle.md |
| Assets/TankDraft/Runtime/Match/ServerClient/{Core,Transport,Content,Bootstrap} | TankDraft.Match.ServerClient: immutable wire projection, intent journal, SslStream/WebSocket, authored settings, VContainer session → существующие UI/world; нет клиентской simulation/Photon/PlayFab |
| Assets/TankDraft/Scenes/Gameplay/ServerMatch.unity; Configs/Match/Server; Editor/Networking/ServerClient | Отдельный authored клиент standalone backend и persistent queued build bridge. Tools/Backend/AuthorServerClient.cs/QueueServerClientBuild.cs запускаются через MCP |
| Backend/TankDraft.LocalHost/LocalTlsHost.cs; LocalTlsVerification.cs; Backend/TankDraft.ServerClient.Tests; Tools/Backend/*server-client* | Loopback WSS adapter, scoped TLS и короткоживущие QA credentials, двухклиентный launcher и проверки S4; Server_Client_Transport.md |
| Backend/TankDraft.LocalHost/LocalFaultProxy.cs; LocalFaultSettings.cs; Backend/Config/local-faults.json; Tools/Backend/verify-network-fault-run.ps1 | Ограниченный loopback TCP relay без расшифровки TLS: задержки, jitter, сброс соединений, stall; Network_Fault_Matrix.md |
| .codex | Проектные агенты и MCP |
| Packages/manifest.json, packages-lock.json | Реальные зависимости |
| Assets/ImmortalityLab | Pre-existing deletions в рабочем дереве; субъект удаления не проверялся, не восстанавливать без отдельного решения |
| Docs/Features/UI_Conventions.md | Обязательные UI-конвенции TankDraft: typed hierarchy, Canvas ownership, passive binding, TMP и layout lifecycle |
| Assets/Foundation/Runtime/UI | Ограниченный leaf-срез Foundation UI из Chibi: UIElement/UIScreen/UIWindow/UIPanel/UIItemView/UIButtonView/UIRegistry; без полного UIService/DI/pools |
| Assets/TankDraft/Prefabs/UI | Typed main menu и runtime collection/details/message views; prefab-backed catalog template и authored layouts |
| Assets/TankDraft/Scenes/Frontend/MainMenu.unity | Main-menu local runtime scene with `MainMenuLifetimeScope` |
| Assets/TankDraft/Scenes/Diagnostics/QA/PreviousEditorScene_20260912_160500.unity | Snapshot of the previously open dirty GameScene; unrelated deleted ImmortalityLab remains deleted |
| Assets/TankDraft/Configs/Meta | Meta catalog, 12 unit + 12 order placeholder entries, army rules, local-profile seed and Russian UI text |
| Assets/TankDraft/Runtime/Shared/Contracts; Runtime/Meta/{Application,Content,Infrastructure,Presentation,Bootstrap} | Реализованный local profile/runtime slice: immutable meta/profile, ProfileService, SO conversion, strict JSON persistence, presenter/views и VContainer startup |
| Assets/Plugins/Demigiant/DOTween | Существующая пользовательская установка |
| Assets/TankDraft/Runtime/Identity/PlayFab | UnityPlayFabSessionSource: существующий provisioned account, instance SDK, main-thread login; secure platform credential storage ещё требуется |
| Backend/TankDraft.Server.Admission; Backend/TankDraft.AdmissionHttp; Docs/Features/PlayFab_Admission.md | Проверенная process-local связка identity/assignment/access/command stream; используется RemoteHost, но cloud durability отсутствует |
| Backend/TankDraft.RemoteHost; Backend/Config/remote-host.json; Tools/Backend/verify-remote-host.ps1 | Локально проверенный no-Azure authoritative HTTP/WSS host: bounded queue/matches, every-message auth, short access, offline progression, tombstones/drain; fake PlayFab only, cloud/Unity wiring/durable recovery pending |
| Tools/Backend/publish-remote-container.ps1; verify-remote-container.ps1 | Linux/amd64 non-root RemoteHost OCI archive/verification; local archive not pushed or run in cloud |
| Assets/TankDraft/Editor/Previsualization | TankDraft.Previz.Editor.asmdef, PrevizImporter — editor-only schema v1/v2 JSON → UGUI prefab, groups и element `parentId` |
| Assets/TankDraft/Art/UI/Previz | Arena_Grouped_Previz.prefab и wallet/rewards shared item prefabs — первая placeholder-вёрстка главной; GroupValidation/Hierarchy_Smoke.prefab и два item prefab — QA fixture; Draft_Layout_Smoke.prefab — legacy smoke |
| Assets/Photon | PUN 2.50 core SDK; PhotonServerSettings.asset/.meta локальные ignored, AppId настроен |
| Assets/TankDraft/Runtime/Diagnostics/Photon | TankDraft.Networking: конфиг и runtime двухклиентной transport-диагностики; боевой network adapter находится в Runtime/Match/Networking |
| Assets/TankDraft/Editor/Networking | TankDraft.Networking.Editor: сохранение/восстановление локального Photon setup, authoring/validation/build probe |
| Assets/TankDraft/Scenes/Diagnostics/QA/PhotonTransportProbe.unity | Authored diagnostic prefab + EventSystem; облачный обмен и rejoin, без gameplay |
| Tools/Photon; Docs/Features/Photon_Setup.md | Воспроизводимые команды настройки, сборки и запуска двух Windows Player-клиентов; результаты в ignored Logs |
| Packages/com.slvr.ui-motion-kit | Embedded UI Motion 0.10.6 |
| Tools/Previz | Автономный HTML, verify.cjs, SCHEMA.md, Samples/draft-layout-v1.json; главная использует schema v2 groups, остальные 23 окна плоские |
| Assets/TankDraft/Runtime/Shared/Contracts/Battle; Runtime/Battle/Simulation | Immutable battle contracts and pure C# fixed-step LeoECS; TankDraft.Simulation |
| Assets/TankDraft/Runtime/Battle/{Content,Presentation}; Runtime/Diagnostics/Battle | Battle SO conversion, passive sprite/prefab pools, battle HUD and VContainer session; individual TankDraft.Battle.* asmdefs |
| Assets/TankDraft/Editor/Battle; Tools/Battle | Authoring, reference/balance validation and Play Mode diagnostic harness |
| Assets/TankDraft/Scenes/Diagnostics/BattlePrototype.unity | Local primitive battle lab; отдельная диагностическая сцена |
| Assets/TankDraft/Configs/Battle; Prefabs/Battle; Art/Battle/Primitives | 4 roles, 3 projectiles, damaging zone, 3 scenarios, shared controls, art replacement roots |
| Assets/TankDraft/Runtime/Match | Сквозной local match flow и network prototype: Network Content/Core/Transport/Bootstrap с pinned authority и snapshot path; network runtime прошёл первую Windows/Editor матрицу, полная приёмка открыта |
| Assets/TankDraft/Editor/Networking/Match; Assets/TankDraft/Scenes/Gameplay/NetworkMatch.unity | Network settings/scene authoring, validation и queued Windows build; authored scene/config созданы и проверены в Unity Editor |
| Tools/Photon/run-network-match.ps1; Docs/Features/Network_Match_Prototype.md | Двухклиентный manual/Auto/Faults launcher и протокол evidence; runtime evidence и ограничения зафиксированы в Network_Match_Prototype.md |
| Assets/TankDraft runtime | 217 assets, 39 prefabs, 149 original GUIDs; local/network Windows scope validated. Backend, live economy и device release QA остаются отдельными задачами |

Не считать предлагаемые классы или папки реализованными. Добавлять реальные owning asmdef и точки входа по мере появления кода.
