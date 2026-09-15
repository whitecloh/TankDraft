# Архитектура TankDraft
Status: local menu and primitive battle implemented; network battle authority still requires a spike
Last reviewed: 2026-09-12

## Перенос из Chibi
Сохраняем composition root и явный startup pipeline, VContainer для lifetime/DI, MessagePipe для типизированных событий приложения, UniTask для отменяемых операций, passive UGUI views и presenters, pools для подготовленных prefabs, адаптеры persistence/content. Не переносим PvE RunFlow, BattleSimulationEngine с Unity-зависимостями и editor/runtime фабрики интерфейса.
Foundation из Chibi — источник проверенных контрактов и приёмов, а не обязательный монолит для копирования. Оптимизация: переносить модуль только с первым потребителем, чтобы не тащить неиспользуемую observability/persistence/UI инфраструктуру и её зависимости. Конкретные платформенные SDK подключаются в адаптерах.

## Границы сборок
| Сборка | Ответственность | Зависит от |
|---|---|---|
| TankDraft.Contracts | Immutable meta/profile/battle definitions, snapshots/events/commands, repository contract | BCL |
| TankDraft.Application | Local profile initialization and equip use case | Contracts |
| TankDraft.Content | Authored SO catalog, initial profile and UI text → immutable definitions | Contracts, Unity |
| TankDraft.Infrastructure | Strict local JSON profile persistence | Contracts, Unity, Unity.Newtonsoft.Json |
| TankDraft.Presentation | Passive prefab views, presenter, collection template pool and navigation | Contracts, Application, Content, Foundation.UI, UGUI, TMP |
| TankDraft.Bootstrap | Main-menu composition root and startup | Contracts, Application, Content, Infrastructure, Presentation, Foundation.UI, VContainer |
| TankDraft.UI.Editor | UI authoring и behavioral/PlayMode harness | Runtime assemblies, Foundation.UI, VContainer, UGUI, TMP, InputSystem, UnityEditor |
| TankDraft.Previz.Editor | Импорт геометрии reference tracing | UGUI, UnityEditor |
| TankDraft.Networking | Конфиг и runtime диагностического Photon transport/rejoin probe; без боевой логики | PhotonUnityNetworking, PhotonRealtime, TMP, Unity |
| TankDraft.Networking.Editor | Локальный Photon setup, authoring/validation/build диагностической сцены | Networking, Photon SDK, TMP, UGUI, InputSystem, UnityEditor |
| TankDraft.Simulation | Fixed-step LeoECS battle, stable IDs, damage/outcome and diagnostic validation | Contracts, Leopotam.EcsLite; noEngineReferences |
| TankDraft.Battle.Content | Authored unit/projectile/zone/rules/scenario SO → immutable battle definitions | Contracts, Content, Unity |
| TankDraft.Battle.Presentation | Passive sprite views/pools, replaceable art roots, viewport and battle HUD | Contracts, Foundation.UI, UGUI, TMP, Unity |
| TankDraft.Battle.Bootstrap | Local battle composition root, fixed-step session and lab controls | Contracts, Simulation, Battle.Content, Battle.Presentation, Foundation.UI, VContainer |
| TankDraft.Battle.Editor | Authoring, production-resolver fixtures and Editor Play Mode harness | Battle runtime assemblies, UI/meta dependencies, UnityEditor |

Эти asmdef существуют. Текущий объём боя, порядок систем и QA описаны в Battle_Prototype.md. Серверный runtime, если будет выбран, использует Contracts/Simulation без UnityEngine. Внешние DTO не содержат ECS entity index, GameObject или ScriptableObject.

## Состояния и команды
Profile: коллекция, уровни/мастерство, слоты приказов, арены, валюты, pass, entitlement. Match: выбранная колода из четырёх типов и приказов, накопленная армия, улучшения в матче, счёт раундов. Round: временные HP/позиции/таймеры/снаряды, уничтожаемые после разрешения раунда.

MatchFlow: Queue → LoadMatch → Offer → CommitChoice → Ready → SimulateRound → RoundResult → Offer либо MatchResult → SettleRewards. Число действий внутри Offer, камбэк и специальные офферы определяет конфиг правил, а не количество экранов. Победа: четыре выигранных раунда. Игрового таймаута/овертайма нет: длительность обеспечивается балансом. Строго одновременная гибель армий и уход игрока требуют явной политики до сетевого spike; сетевые deadlines не являются лимитом длительности боя.

Команды несут matchId, roundId, actorId, sequence, contentVersion. Authority проверяет фазу, deadline, доступность варианта и дубликат sequence. Клиент может показывать pending UI, но не начисляет постоянную награду. Settlement идемпотентен по matchId; retry/reconnect не даёт второй выплаты.

## Бой
Явный порядок: принять легальные команды → spawn/formation → начало раунда и flank → поиск целей → движение → способности/атаки → projectiles → урон/защита/лечение → смерть/призыв → outcome → события представления. Это границы систем, а не правило накопить все смерти за render frame и затем объявить ничью. Контракт порядка попаданий, death hooks и фиксации outcome уточняется по наблюдениям в Reference_Gameplay.md; точно одновременный исход пока не утверждён.

Runtime продвигает бой фиксированными шагами и допускает новые команды во время боевой фазы. Представление интерполирует состояние и проигрывает события уже рассчитанных шагов. Предварительный расчёт всего боя не требуется. Ускоренные прогоны и последующая проверка/replay могут использовать тот же core с журналом входных команд.

Ряд — начальная зона и правило роли, не просто draw order. У каждого определения есть formationRow, targetingPolicy, movementPolicy, abilityIds. Преграды останавливают/взрываются; тяжёлые держат фронт; ПТ выбирают одиночные цели; САУ/дроны работают с тыла. Flank — специальная механика входа в тыл, repair — отдельная политика выбора союзников. Уточнить по референсу допустимость пересечения линий после сближения.

Tick rate, точность координат, порядок разрешения событий и RNG stream — versioned rules. Если применяется серверный replay, его равенство проверяется на Windows/Android/iOS; LeoECS и одинаковый seed сами по себе этого не гарантируют. При расхождении серверный outcome остаётся окончательным.

## Данные и views
Каталог SO → validation уникальности ID/ссылок/рядов/способностей → definition snapshot → simulation. ViewCatalog связывает visualId с заранее настроенными prefab. SpawnService получает события simulation, берёт prefab instance из pool и привязывает runtimeId. Удаление/повторное использование очищает subscriptions/анимации. Баланс не хранится в MonoBehaviour view.
Первый бой использует authored примитивы для юнитов, снарядов и зон урона. Сменный VisualRoot и заранее настроенные anchors выстрела/попадания отделены от runtime binding; замена меша/спрайта/эффекта не меняет simulation radius, range или timing. Полёт и периодический урон зон существуют в simulation, а их view отображает состояние. Визуальные хвосты/вспышки не наносят урон через callbacks анимации.

## Первая проверяемая вертикаль
Одна арена, четыре базовые роли и две особые способности, три оффера, один приказ, камбэк, четыре победы, экран результата. Два клиента и бот используют один формат команд. Сначала prove correctness/reconnect/settlement; затем расширять полный контент и мету. Это порядок реализации полного объёма, не сокращение релизных требований.


