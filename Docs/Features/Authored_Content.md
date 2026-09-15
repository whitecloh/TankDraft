# Authoring → логика → prefab
Status: accepted direction; schemas pending architecture stage
Last reviewed: 2026-09-12

Pipeline: подготовленный ScriptableObject/каталог → валидация ссылок/ID → immutable runtime definition → ECS entity/components → presentation binding → instance из подготовленного prefab/pool.
В конфиге: ID, роль/ряд, статы/эффекты/эволюция, цена/гейты, visual prefab/icon, ссылки на VFX/audio. В коде: алгоритм таргетинга, порядок систем, применение команды, проверка правил.
В prefab: иерархия, renderer, башня/гусеницы/точки выстрела, animation/presentation controllers, сериализованные связи. Runtime не добавляет компоненты, чтобы компенсировать недоделанный prefab.
Runtime создаёт ECS entities, DTO, команды, снапшоты и экземпляры prefab: запрет касается конструирования authored содержимого, не существования runtime-состояния.
SO не хранит текущие HP, сетевой owner, номер раунда или cooldown конкретной машины.
Pool: prewarm → acquire/bind/reset → release/unbind; повторный acquire не наследует HP/VFX/subscriptions прошлого экземпляра.
Без Odin: обычный Inspector/PropertyDrawer/EditorWindow; проверки ID, prefab refs, effect kind, row, диапазонов, циклов эволюции/призыва.
Chibi: BattleWorldController.cs хранит отдельные pools и PrewarmBattlePool; UI использует prefab-backed views и UiItemPool. Lab/editor authoring содержит процедурное построение, поэтому фразу «в Chibi ничего не создаётся runtime» буквально переносить нельзя.

