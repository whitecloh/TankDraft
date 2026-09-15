# Временный Fusion QA без шифрования игрового канала
Status: owner exception active; gateway/client/manager rebuilt; startup-fix 2PC Human acceptance PASS; release/fault/device acceptance pending
Last reviewed: 2026-09-15

15.09 продолжение восстановления: сценарий PC → Editor с новым журналом и уже завершённым матчем теперь PASS (Human4:1, Sequence18, штатный возврат в меню). Старый sequence divergence ниже описывает исходный дефект; актуальные границы, native126 PASS и один восстановившийся transport timeout — [Fusion_PC_Cold_Restart.md](Fusion_PC_Cold_Restart.md).

15.09 владелец передал ответ Photon Support: DatagramEncryption plugin бесплатный, direct connection encryption рекомендуют вводить позднее при конкретной необходимости. Файлы плагина не получены; ожидание больше не является блокером разработки. Подробности и причина первоначального обращения: [Photon_Encryption_Request.md](Photon_Encryption_Request.md). Серверная проверка команд, покупки и сохранения не заменяются транспортным шифрованием.

## Обновление 15.09: явная политика шифрования

В authored `NetworkProjectConfig` для временного QA установлено `EnableEncryption=false`. Runtime перед `Photon StartGame` требует одновременно приватный `AllowPlaintextQa=true`; несовпадающие флаги отклоняются до запуска Photon. Это временный opt-in, а не fallback. Конфиг SDK не копируется: его `Serialize/Deserialize` теряет `[NonSerialized] PrefabTable`; сохраняется authored table и используется обычный SDK flow. `Configure` и `verify-foundation` обновлены для этой проверки. Платежи и private economy не менялись. Официальная справка Photon: https://doc.photonengine.com/fusion/v2/manual/advanced/encryption.

Новая полная 2PC приёмка startup-fixes завершена: `game-2bed025d7348428eb5b2a2880a4d87f9`, instance `1e55320b61404de09ba41b7db67c1839`, script PASS Human; round6 и счёт2:4 совпали у обоих клиентов, exit0 у обоих. Целевая encryption ошибка отсутствует в обоих клиентских и серверном логах. Gateway/client собраны 15.09 в 17:45:11 / 17:45:35, Succeeded0errors. Это не live progression acceptance.

После матча повторный вход Editor тем же QA account выявил отдельный незакрытый recovery-сценарий `Server/client command sequence diverged` (`Logs/StartupFix/same-identity-handoff.log`). Ручной правки сохранений, миграций и сбросов не выполнялось; cross-client handoff не принят. Обычный вход из Editor на новом instance 97167e123f5f4c2f9ab3c2bf7ecb168a проверен: MainMenu, один UIRegistry, main_menu.progression зарегистрирован, runner работает; целевых ошибок, исключений и sequence divergence нет. Evidence: Logs/StartupFix/editor-startup.txt и editor-final-audit.json. Editor возвращён в Edit, сервер оставлен Ready.

Владелец прямо разрешил продолжить реализацию без шифрования и добавить его позднее. Это отменяет ожидание DatagramEncryption как блокировку закрытых игровых тестов. Поддержка Photon уже получила запрос; отменять его не нужно. Защищённый публичный релиз по-прежнему требует отдельной приёмки.

## Границы режима

- Отдельный `AllowPlaintextQa=true` в приватных настройках Server Manager и runtime-конфигурации тестового клиента; default false. Это opt-in тестового развёртывания, не автоматический fallback при ошибке шифрования.
- Сохраняются Photon Custom Authentication через PlayFab, запрет anonymous, серверный `GameMode.Server`, список до4 QA аккаунтов, срок запуска30min, ограничения очереди/матчей/сообщений. Экономика и выдача реальных наград не реализованы/не включаются этим режимом.
- Внешний игровой протокол содержит только Operation/Body. PlayFab SessionTicket, Photon token, gateway key, lobby/access bearer tokens не входят в него. Custom Auth SDK остаётся отдельным механизмом подключения к Photon.
- Данные боя передаются без заявленной конфиденциальности и криптографической защиты игрового канала. Режим не обещает защиту от сетевого перехвата/подмены и не является security acceptance релиза.
- Настройки Photon Dashboard, платные тарифы, firewall/router, ключи и права внешних сервисов этим патчем не менялись. Gateway/client и опубликованная панель пересобраны; закрытый QA режим включён в приватном runtime.

## Реализация

`FusionQaClient` → `FusionRequestChannel` с explicit QA flag → reliable Fusion message kind2 → `FusionQaAuthorityPeer` на сервере → закрытый loopback → существующий .NET authority.

Gateway определяет аккаунт через `NetworkRunner.GetPlayerUserId` после Custom Auth и проверяет allowlist. Клиентский payload не выбирает account/side. Только явно включённый `allowPhotonQa` открывает `/v1/fusion-qa/lobby` и `/v1/fusion-qa/session`; маршруты требуют приватный per-run gateway key и заголовок проверенного игрока. Эти trust assertions не доступны через обычный внешний HTTP ingress. Утверждение identity доверяется нашему gateway; это не повторный live PlayFab AuthenticateSessionTicket на каждый запрос.

Lobby/access tokens выдаются только во внутреннем IPC и хранятся в per-player `FusionQaAuthorityPeer`; в клиентские ответы они не попадают. Для Reauthenticate gateway подставляет свой актуальный access token. Домен, scheduler, идентификаторы операций и проверка команд общие с существующим transport.

QA client не принимает старый credential envelope. Парсер отклоняет лишние поля и известные credential/identity поля, ограничивает размеры. Backend дополнительно проверяет команду целиком. SDK sends ставятся в Unity SynchronizationContext; отменённый до отправки запрос проверяется через CanSend. Disconnect уничтожает per-player credentials и pending channel; поздний server reply не направляется в новый peer.

Gateway capability: `plaintext-qa-no-economy`; обычная диагностика остаётся `readiness-only`. `QaClient` доступен после готовности клиентского runner. Боевой queue/session/view подключён отдельным QA entry; обычное меню, отмена очереди и повторный матч ещё впереди. Актуальное evidence: Fusion_Gameplay_QA.md.

## Первоначальная локальная проверка (до сборки Player)

- RemoteHost **57/57**, ServerManager **4/4**. `Logs/FusionMigration/Checks/fusion-plaintext-qa.trx`, `manager-plaintext-qa.trx`.
- Полный LocalOnly матч через QA client/channel/peer и fake PlayFab fixture: два аккаунта, accepted choice + duplicate, оба offline, возврат, общий результат до4 побед. Записанный тестом обмен не содержит lobby/access/auth/server credentials.
- Отдельно: QA routes absent без opt-in, отказ без gateway key/для чужого аккаунта, запрет legacy credential envelope и утечки токена в ответе, сохранение прежних тестов защищённого режима.
- Unity compilation PASS; dependency foundation23projects/35refs, scoped diff check PASS. Это не новая Player сборка, не проверка пакетов на линии и не Human матч через Photon.

Продолжение: реальные Player и первый полный 2PC матч уже проверены (Fusion_Gameplay_QA.md). Далее обычный menu lifecycle, reconnect/боты/повторные матчи. Шифрование возвращается отдельным transport-патчем после получения плагина; правила и серверные проверки не переписываются.
