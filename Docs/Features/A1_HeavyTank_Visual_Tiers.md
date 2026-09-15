# A1 Heavy Tank — visual tiers

Дата: 2026-09-16. Статус: **`unit.heavy_tank` Integrated в локальный Editor battle flow; reusable visual остаётся ручным и не связан с progression**. Полный пакет артефактов: [v002 README](../../ArtSource/A1/unit.heavy_tank/v002/README.md) · [интеграция в бой](A1_HeavyTank_Battle_Integration.md).

`unit.heavy_tank` получил три художественных варианта одного силуэта: Tier 1 — базовый, Tier 2 — усиленный, Tier 3 — максимальный. Каждый вариант использует три меша, один shared material и vertex mask: alpha `0` оставлена для team cap. Чёрный контур запечён в тех же мешах; art-light фиксирован, realtime shadow pass отсутствует по дизайну.

| Tier | Треугольники | Вершины | Меши / submeshes |
| --- | ---: | ---: | --- |
| 1 | 924 | 1 568 | 3 / 3 |
| 2 | 972 | 1 664 | 3 / 3 |
| 3 | 1 144 | 2 000 | 3 / 3 |

В Unity 6000.3.10f1 (D3D12, Built-in, Gamma) проверены импорт, размеры, +Z muzzle, team cap, material sharing, шесть tier/team состояний и 96 Tier 3 экземпляров. Heavy tank также прошёл локальный Editor match с реальными draft, battle и MatchResult; FPS benchmark, Frame Debugger measurement, device/PC/WebGL performance и новый live Fusion матч не выполнялись.

`A1TankVisual` переключает только внешний вид через `SetVisualTier`, цвет команды, yaw/recoil и pool reset. Он не читает progression и не меняет `BattleEntityState`. `BattleViewCatalog` уже назначает A1 prefab для `unit.heavy_tank`; следующий нерешённый вопрос — правило mapping visual tier к progression.
