# Editor: повторный вход после Stop и готовность Photon
Status: transport re-entry and error UI verified in Editor; current saved order remains unsupported by battle prototype
Last reviewed: 2026-09-17

## Причина

Основной отказ кнопки «В бой» подтверждён на свежем сервере: `unsupported_battle_loadout` (HTTP 403). В профиле tester0 выбран `order.smoke_screen`, а текущий сервер допускает только один `content.OrderId` (`order.reinforce_armor`) или пустые слоты. Runtime ContentVersion клиента, обоих SO и server `/readyz` совпадают (`fde24d...c5d2`). Это ограничение реализации приказов, не потеря связи. Отряд и сохранения не менялись; для боя нужно вручную выбрать поддерживаемый приказ, либо отдельно реализовать остальные приказы.

Ошибка дополнительно терялась на границе протокола: `MetaFailureException` сериализовалась web-default как `Body.code`, Fusion ожидал `Body.Code`. Поэтому клиент обновлял авторизацию и показывал общий сетевой Retry. Теперь сервер использует общий `AuthoredContent.Json`, семантический 403 не запускает refresh Lobby, а `unsupported_battle_loadout` показывает текст из NetworkQueueSettings и кнопку закрытия для редактирования отряда. Остальные неопределённые сетевые ошибки сохраняют безопасный повтор прежнего intent.

У владельца сервер был Ready, а Editor получал `GameNotFound`, затем Photon 32746 (аккаунт уже в комнате) и 32749 (аккаунт неактивен, но выполняется новый join). В gateway также был `PhotonCloudTimeout`.

Локально воспроизведён конкретный дефект: normal Stop из MainMenu запускал `FUSION_RECONNECT_BEGIN`. Закрытие Editor-сеанса обрабатывалось как обрыв сети. Первое исправление только флага завершения не закрыло быстрый Stop → Play: повторно получен 32749. Добавлено ожидание штатного Shutdown перед teardown Editor.

## Исправление

- `FusionEditorPlay` при ExitingPlayMode сначала инициирует остановку всех своих соединений, ожидает её завершения (не более 5 секунд), затем завершает Play Mode. Существующее восстановление временных env/startScene сохранено.
- `FusionDedicatedBootstrap.StopAsync`, OnApplicationQuit и OnDestroy отмечают намеренное завершение, отменяют операции и снимают callbacks; намеренная остановка не создаёт новый runner. После задержки reconnect повторно проверяется отмена.
- В gateway heartbeat добавлен `CloudReady = IsCloudReady && IsInSession`. Панель показывает Ready только при исправном authority/gateway и доступной комнате Photon.
- Потеря доступа к комнате не смешивается с process watchdog: существующий матч не останавливается только потому, что Photon временно не принимает новые входы.

Это согласуется с [описанием Photon Quick Rejoin](https://doc.photonengine.com/fusion/v2/manual/connection-and-matchmaking/lost-connection-handling): работа существующего server-сеанса и возможность нового входа при потере cloud connection — разные состояния.

Секреты, аккаунты, экономика, provider settings и сохранения не меняются. Настройки QA и ручного запуска сервера сохраняются. Новый manager требует gateway с полем CloudReady; поставляются совместно.

## Проверка

- ServerManager tests: 7/7 PASS.
- FusionQueueClientTests: 4/4 PASS (включая ровно один Lobby/Join при semantic 403 и сохранение intent при неопределённом ответе).
- RemoteMetaIntegrationTests: 4/4 PASS (включая корректный регистр Code, запрет чужого инвентаря, versioned save и назначение матча с допустимым составом в изолированной fixture).
- Unity compile, dedicated и PC client builds с lifecycle-исправлением PASS; lifecycle-проверка normal Stop → Play → MainMenu/runner Running+CloudReady PASS. Последующее изменение текста/обработки queue проверено в Editor; отдельный PC client после него не пересобирался. Manager с исправленной сериализацией перепубликован и перезапущен.
- Реальный Editor run `Logs/FusionEditor/74e79cce4cfd47cd81712f3cf7e9f3f0/queue.jsonl`: Status Idle → Join UnsupportedLoadout. Активный UI показывает настроенный текст, «Закрыть» возвращает MenuIdle=True, CollectionInteractable=True. Сервер Ready, очередь/матчи пусты.
- На реальном профиле Join отклонён по неподдерживаемому приказу; это не считается успешной проверкой боя. Изменение профиля и расширение приказов в этот патч не входят.
