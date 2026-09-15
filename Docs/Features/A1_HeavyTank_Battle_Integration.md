# A1 Heavy Tank — интеграция в бой

Дата: 2026-09-16. Статус: **`unit.heavy_tank` Integrated в локальный Editor flow с реальным MatchSession; это не миграция всего 3D-представления и не финальное художественное approval**.

## Что подключено

- Новый `BattleTankModelView` переводит XY-поле боя в 3D-модель с фиксированным pitch 65°, поворотом корпуса по Y и обратной проекцией aim для башни.
- `BattleEntityView` выбирает модельную ветку только при наличии адаптера; прежняя sprite-ветка сохранена. Для 3D spawn scale применяется равномерно по XYZ, dynamic muzzle/hit anchors отданы модели.
- `BattleWorldView` передаёт `Shot` в модель: отдача `.12` на `.14 s`, muzzle flash `.045 s` (16 треугольников, shared mesh). `_HitFlash` остаётся instanced shader property и не заменяет team cap или ink.
- Новый prefab: `Assets/TankDraft/Prefabs/Battle/Units/A1/Battle_unit_heavy_tank_A1.prefab`. Старый 2D-prefab сохранён; source A1 model prefab остаётся отдельным вложенным asset.
- Общий `BattleViewCatalog` для `unit.heavy_tank` теперь назначает новый prefab; совпадение catalog в Match и ServerMatch проверено. HP/defense HUD на Z `-1.5`, shadow и actual pools сохранены.

Ручное переключение tier доступно через `A1TankVisual`; automatic mapping к progression не добавлен. `BattleEntityState`, gameplay logic, network state и баланс не менялись.

## Проверки

- 6 целевых EditMode-тестов A1 прошли.
- Candidate validation: 24 случая — 3 tier × 8 направлений, включая aim, muzzle anchors, shot/recoil и flash reset.
- Обычный Editor Play: transient deck `heavy_tank`, `mines`, `tank_destroyer`, `field_artillery` запущен через `MatchNavigation.TryLaunch`, без записи profile и выдачи аккаунту. UI draft → battle → MatchResult завершился на round 7.
- Последний полный проход: 17 choices, 6 battles, 774 shots, 762 impacts. В отдельной R2-проверке на tick 184 было 2 активных heavy-модели, 4 их выстрела, 12 shots, 10 impacts, `Failure=null`.
- Formation/pool runtime validation прошла: first load, new spawn, rebind, reflow, id remap и reset.

Скриншоты: [бой](../../ArtSource/A1/unit.heavy_tank/v002/renders/unity/battle-integration.png), [MatchResult](../../ArtSource/A1/unit.heavy_tank/v002/renders/unity/battle-match-result.png). Логи: `Logs/A1BattleIntegration/{candidate,normal-play-shot,full-match,formation-pool}.txt`.

## Остаётся

Нужно решение о mapping visual tier к progression. Не проверены новый live Fusion матч, прежний standalone PC client не пересобирался, нет WebGL/device FPS и Frame Debugger batch-профиля. Переход остальных юнитов на 3D в этот объём не входит.
