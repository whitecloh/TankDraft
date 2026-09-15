# Runtime главного меню и локальной коллекции
Status: implemented local runtime slice; Editor and automated PlayMode harness passed
Last reviewed: 2026-09-12

## Реализованный объём

Сцена `Assets/TankDraft/Scenes/Frontend/MainMenu.unity` запускает локальный поток коллекции. Authoring-конфиги находятся в `Assets/TankDraft/Configs/Meta`: каталог содержит 12 unit и 12 order placeholder-записей, начальный профиль владеет шестью unit и тремя order. В армии четыре слота unit; для order действуют три слота с порогами commander level 1, 20 и 35.

Доступный сценарий: главная → армия → детали unit/order → выбор слота для замены → сохранение → возврат. Кнопка «В бой» запускает полный локальный матч с текущей поддерживаемой колодой через IMatchLauncher (Local_Match.md). Остальные продуктовые функции показывают честное сообщение о недоступности; они не являются реализованными экранами или use case.

## Границы модулей

`TankDraft.Contracts` содержит immutable `ContentDefinition`, `ArmyRules`, `MetaDefinitions`, `ProfileSnapshot`, результаты экипировки и `IProfileRepository`. Они не зависят от Unity UI.

`TankDraft.Content` преобразует authored ScriptableObject в Contracts. Реально используются `ContentEntryAsset`, `ArmyRulesAsset`, `NewProfileAsset`, `MetaCatalogAsset` и `UiTextCatalog`. UI-текст хранится в конфиге на русском; adapter Unity Localization остаётся будущей отдельной задачей.

`TankDraft.Application.ProfileService` загружает либо создаёт профиль в `Initialize`, валидирует ownership, тип и доступность слотов. `Equip` сначала сохраняет candidate snapshot и публикует `Changed` только после успешного save.

`TankDraft.Infrastructure.JsonProfileRepository` хранит strict JSON schema version 1. Запись использует временный файл, atomic replace и backup. Повреждённый или несовместимый файл отклоняется: сервис не сбрасывает его в новый профиль.

`TankDraft.Presentation` содержит passive typed views, модели и `MainMenuPresenter`. Presenter владеет переходами и command callbacks; views не запрашивают services, configs или domain data. Коллекция использует authored template и prefab-backed pool, очищающий callbacks при reuse. Layout groups включаются только для bind/resize rebuild и выключаются в idle.

`TankDraft.Bootstrap.MainMenuLifetimeScope` регистрирует authored configs/views, repository и entry point `MainMenuStartup` через VContainer. Startup валидирует UI и каталог, загружает профиль, создаёт `ProfileService` и presenter, затем выполняет bind. При ошибке он показывает authored startup error surface.

## Persistence и authority

Локальный путь: `Application.persistentDataPath/TankDraft/local-profile-v1.json`. Это только device-local persistence для данного slice, не server authority и не доказательство сетевой синхронизации, live-экономики или миграции сохранений.

## Проверки и ограничения

`ProfileBehaviorValidation` завершён с `PASS 38`: contracts, profile service и strict disk persistence проверены в Editor. Актуальный `TypedMainMenuValidation` завершён с `PASS 264`; прежний `PASS 134` остаётся историческим pre-runtime evidence.

`MainMenuPlayModeValidation` завершён с `PASS 31`. Это автоматизированный Editor PlayMode harness через настоящие `Button.onClick`, а не Test Runner, Android/iOS или ручной touch QA. Он включает два полных cold start, сохранение unit и order, gates трёх order slots, reuse пула, возврат на главную, viewport 576×1280 и 576×1024, прокрутку и safe-area anchors. Evidence: `Logs/TankDraftSetup/RuntimeQA/status.txt` и `01-main.png`, `02-army.png`, `03-card.png`, `04-orders.png`, `05-main-9x16.png`, `06-army-scroll-9x16.png` в той же папке.

Harness использует изолированный QA profile и не меняет пользовательский профиль. PUN warning об App ID закрывался через computer use; сетевые настройки не изменялись. По завершении `MainMenu.unity` открыт в EditMode; открытая ранее dirty `GameScene` сохранена снимком `Assets/TankDraft/Scenes/Diagnostics/QA/PreviousEditorScene_20260912_160500.unity`, а удалённый `ImmortalityLab` не восстанавливался.

Следующий scope — UI/art перенос. Бой, simulation, PvP, bots, backend и live-механики остаются будущими задачами. Главный артефакт пока placeholder, финальный art не утверждён; 12+12 IDs/roles предварительны и не содержат реализованных abilities. Это не полная мета-игра.

## Серверная очередь (2026-09-13)

Основная кнопка «В бой» теперь вызывает NetworkMatchLauncher: authored NetworkQueueSettings → очередь standalone сервера → назначенная ServerMatch scene. Поиск/отмена/повтор отображаются существующим UIMessageWindow. Для запуска нужен локальный launcher; без него показывается подсказка подключения. MatchNavigation сохранён для отдельных legacy локальных проверок, но не подменяет сетевой поиск ботом. Генератор MainMenuRuntimeAuthoring назначает _queueSettings при повторном создании сцены. Подробнее: [Matchmaking_and_Bots.md](Matchmaking_and_Bots.md).
