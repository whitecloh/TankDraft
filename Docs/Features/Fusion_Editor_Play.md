# Fusion: запуск из Unity Editor
Status: startup and fresh Editor PASS; completed-match PC-to-Editor handoff PASS; pending multi-device cases remain open
Last reviewed: 2026-09-15

15.09 продолжение: исправлено принятие авторитетного номера команды новым/отставшим журналом без pending. На том же сервере после 2PC Human4:1 Editor с отсутствующим журналом восстановил Sequence18, тот же MatchResult и вернулся в меню. Сброса/копирования журнала не было. Подробная приёмка и её пределы — [Fusion_PC_Cold_Restart.md](Fusion_PC_Cold_Restart.md), `Logs/FusionHandoff`. Старое описание sequence divergence ниже — исходное воспроизведение до этой правки. Один recoverable transport timeout/reconnect остаётся отдельным наблюдением; перенос конфликтующего pending между устройствами не объявляется решённым.

## Обновление 15.09: политика временного QA transport

Перед `Photon StartGame` Editor flow проверяет authored `NetworkProjectConfig.EnableEncryption=false` вместе с приватным `AllowPlaintextQa=true`; несовпадение отклоняется до подключения. `Configure` и `verify-foundation` обновлены. SDK config не клонируется через `Serialize/Deserialize`, потому что при таком копировании теряется `[NonSerialized] PrefabTable`: сохраняется authored table и normal Fusion flow. Это временный режим закрытого QA без шифрования; платежи и private economy не менялись. Подробности и ссылка Photon: `Photon_Plaintext_QA.md` — https://doc.photonengine.com/fusion/v2/manual/advanced/encryption.

Новая полная 2PC приёмка startup-fixes завершена: `game-2bed025d7348428eb5b2a2880a4d87f9`, instance `1e55320b61404de09ba41b7db67c1839`, script PASS Human; round6 и счёт2:4 совпали у обоих клиентов, exit0 у обоих. Целевые encryption/UI Id ошибки и `NullReferenceException` отсутствуют в обоих клиентских и серверном логах. Gateway/client собраны 15.09 в 17:45:11 / 17:45:35, Succeeded0errors. Это проверяет startup-fixes, не live progression.

После матча повторный вход Editor тем же QA account выявил отдельный незакрытый recovery-сценарий `Server/client command sequence diverged` (`Logs/StartupFix/same-identity-handoff.log`). Ручной правки сохранений, миграций и сбросов не выполнялось; cross-client handoff не принят. Обычный вход на свежий instance 97167e123f5f4c2f9ab3c2bf7ecb168a проверен: MainMenu, один UIRegistry, main_menu.progression зарегистрирован, runner работает. Целевых ошибок, исключений и sequence divergence нет; evidence Logs/StartupFix/editor-startup.txt и editor-final-audit.json. Editor возвращён в Edit, сервер оставлен Ready.

15.09 Legacy QA: исправлен запуск helper из Editor с унаследованным PS7 PSModulePath. Реальный private capture показал Get-Acl/CouldNotAutoloadMatchingModule; теперь переменная удаляется только у дочернего Windows PowerShell. Глобальная среда не меняется. После импорта обычный Play → MainMenu, живой серверный профиль: EquipAsync/повторное чтение/восстановление исходной армии PASS, Owned9, валюты0 (Logs/MetaLegacy/editor-equip.txt). Синхронные MCP diagnostics с ожиданием async/subprocess на main thread запрещены: такой probe блокировал Editor; для приёмки использован немедленный return и асинхронная запись результата в файл.

## Как запускать без Codex

1. Двойным кликом Tools/Fusion/open-server-manager.cmd открыть панель в обычном браузере.
2. Нажать «Запустить сервер», дождаться «Готов к подключениям».
3. В Unity открыть MainMenu и нажать обычный Play. Подготовка входа занимает несколько секунд.
4. Нажать «В БОЙ». При отсутствии соперника через10s сервер назначит бота.

Из другой сцены можно запустить тот же flow через меню **TankDraft → Networking → Fusion → Play in Editor**. Пункт **Use Fusion for MainMenu Play** включён по умолчанию; отключение оставлено только для явной проверки legacy local diagnostics. В обычном Fusion-пути run-matchmaking.ps1 и USB не используются.

Editor использует существующего QA tester0; одновременно второй клиент должен использовать tester1. Прямой запуск MainMenu ранее не создавал FusionSessionContext и выбирал NetworkMatchLauncher, откуда приходил старый текст ошибки про run-matchmaking.ps1. Кроме того, в EditorBuildSettings были только MainMenu и локальный Match, поэтому после назначения серверного матча нельзя было загрузить ServerMatch.

## Реализация

Editor-only FusionEditorPlay перехватывает Play из MainMenu/FusionGameClient. Сначала отменяет неподготовленный старт и ждёт завершения отмены через EditorApplication.update, затем запускает приватный helper. Это исправляет гонку однократного delayCall, который мог выполниться до окончания отмены Play.

Tools/Fusion/prepare-editor-play.ps1 читает только loopback панель с её control token, требует Ready и согласованный AllowPlaintextQa; готовит вход и повторно проверяет тот же instance. Новый режим FusionQaSetup --prepare-editor обновляет только токен существующего tester0 (Client/LoginWithCustomID CreateAccount=false + GetPhotonAuthenticationToken). Серверные ключи не читаются этим режимом, server identity/настройки/экономика не меняются. Private input ACL/reparse checks сохранены. Ошибки provider не печатаются в Unity.

Перед Play добавляются/включаются authored FusionGameClient, MainMenu, ServerMatch в EditorBuildSettings через Unity API; прочие сцены сохраняются. playModeStartScene временно указывает на FusionGameClient. Runtime путь — .codex-secrets/TankDraft/fusion-editor-run/gateway.json, identity token — прежний private fusion-client-0-auth.json; стабильный intent journal — fusion-editor-resume вне проекта. В игровых assets/build settings нет credentials. Flow загружает MainMenu через существующий FusionClientLifetimeScope/DI, без нового runtime service locator или ручной сборки объектов.

Environment override и прежняя playModeStartScene сохраняются в Editor SessionState и восстанавливаются после Stop, включая domain reload. Auto-queue/auto-play/fault flags очищаются на время ручного Editor Play. Код независим от MCP/Codex; MCP использован только для настройки и проверки.

## Проверено

- Воспроизведён исходный запуск MainMenu с legacy transport.
- Helper .NET build PASS,0errors/0warnings; PowerShell syntax PASS; Unity compilation и импорт нового Editor-кода выполнены.
- Обычный Play из MainMenu → FusionGameClient → MainMenu с разрешённым через DI **FusionMatchLauncher**, QaClient готов.
- Нажата authored StartBattle_Button: серверная очередь Searching10s → Matched/Bot. Первый проход выявил отсутствующую ServerMatch в build settings; исправлено.
- Повторный обычный Play после исправления восстановил то же назначение на живом сервере: ServerMatch, Draft round4, счёт1:2, Connected=true, NativePresentation=true. Evidence: Logs/FusionEditor/e4f17c031b944fffa1afefe547eed7a7; исходная queue диагностика — da4761764981424d8281e1955b0a4c65.
- Матч дошёл до MatchResult1:4, round5, Connected=true. После Stop через MCP проверены восстановленная MainMenu, playModeStartScene=null, снятый Armed и прежнее пустое runtime environment.
- Negative test: сервер drain→Stopped; обычный Play отклонён на подготовке, Editor остался вне Play, без runtime override и legacy fallback. Console даёт FUSION_EDITOR_PREPARE_FAILED с указанием панели. Сервер и Player-процессы после проверки остановлены, панель доступна; следующий ручной запуск — шаги выше. Source/docs diff check PASS. Коммит/push не выполнялись.

Это Editor integration acceptance; Windows Player/runtime transport не менялся и новые Player builds не требуются. Android, expiry/double-account/load остаются следующими задачами основного плана.
