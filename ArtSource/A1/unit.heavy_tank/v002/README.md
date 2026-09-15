# A1 heavy tank v002 — три визуальных tier

Статус: **`unit.heavy_tank` Integrated в локальный Editor battle flow**. Reusable 3D-asset вручную переключает visual tier и не связан с progression; live Fusion, device acceptance и переход остальных юнитов на 3D не выполнены.

## Результат

- Исходники: [Blender-скрипт](blender/build_heavy_tank_v002.py), [`.blend`](blender/heavy_tank_v002.blend), [Tier 1 FBX](export/HeavyTank_A1_Tier1.fbx), [Tier 2 FBX](export/HeavyTank_A1_Tier2.fbx), [Tier 3 FBX](export/HeavyTank_A1_Tier3.fbx).
- Unity-артефакты: [prefab](../../../../Assets/TankDraft/Prefabs/Battle/Units/A1/HeavyTank_A1.prefab), [visual config](../../../../Assets/TankDraft/Configs/Art/A1/HeavyTank_A1_Visual.asset), [lookdev-сцена](../../../../Assets/TankDraft/Scenes/Art/A1_HeavyTank_Lookdev.unity), [shader](../../../../Assets/TankDraft/Art/A1/Shared/Shaders/A1VertexInk.shader) и [material](../../../../Assets/TankDraft/Art/A1/Shared/Materials/A1_VertexInk.mat).
- [Сравнение с Unity](renders/unity/comparison.png) и [верхний вид](renders/unity/comparison-top.png). Исходный [v001](../v001/README.md) сохранён без перезаписи.

Визуал — светлый плоский cel shading с фиксированным art-light. Чёрный контур запечён в тех же трёх мешах; отдельный realtime shadow pass не используется по дизайну. В Blender проверялись линейный цвет и маска вершины: alpha `0` назначена только team cap.

## Геометрия и импорт

| Tier | Треугольники | Вершины в Unity | Меши / submeshes | Экономия от v001 2 880 tris |
| --- | ---: | ---: | --- | ---: |
| 1 | 924 | 1 568 | 3 / 3 | 67,9% |
| 2 | 972 | 1 664 | 3 / 3 | 66,25% |
| 3 | 1 144 | 2 000 | 3 / 3 | 60,28% |

У каждого tier один общий материал и три меша с одним submesh каждый. Read/Write выключен; камеры, lights, animation import и tangents не импортируются. [Unity validation](metrics/unity-validation.json) прошла в Unity 6000.3.10f1, D3D12, Built-in, Gamma: dimensions, +Z muzzle, team cap, shared material и все шесть сочетаний tier/team подтверждены.

Для сравнения, v001 содержал 2 880 треугольников, шесть мешей, шесть материалов и 13 GLB primitives. В v002 уменьшение треугольников составляет 67,9%, 66,25% и 60,28% для Tier 1–3 соответственно.

## Поведение reusable visual

`A1TankVisual` даёт только визуальные API: `SetVisualTier(1..3)`, `SetEnemyTeam(bool)`, `SetTurretYaw`, `SetRecoil`, `ResetForPool` и anchors `ActiveMuzzle`, `ActiveHit`, `ActiveTurret`, `ActiveBarrel`. В Inspector доступны `Visual Tier` и `Enemy Team`.

```csharp
visual.SetVisualTier(3);
visual.SetEnemyTeam(true);
```

Открыть lookdev-сцену, выбрать любой танк и менять эти два поля в компоненте `A1TankVisual`. Смена уровня сохраняет направление башни и величину отдачи. Повторная сборка ассета — меню `TankDraft / Art / A1 / Import heavy tank tiers`; рядом доступны рендеры, создание lookdev-сцены и validation.

Все три tier resident, активен только выбранный. Tier не связан с уровнем, progression или `BattleEntityState`: решения о будущем mapping остаются открыты. Production `BattleViewCatalog` назначает A1 prefab для `unit.heavy_tank`; старые 2D-prefab и sprite-ветка сохранены.

## Нагрузка и границы проверки

Повторное использование проверено на 96 highest-tier экземплярах: 96 × 1 144 = 109 824 треугольника, 288 активных mesh renderer; меши и материал общие. 96 — это обычные слоты ростера: максимум 12 на тип × 4 типа × 2 стороны. Summons и projectiles не входят в это число. `MaxEntities = 2000` — лимит simulation, а не число танков.

Instancing включён и shader property поддерживает per-instance цвет. Реальные batch counts не измерялись через Frame Debugger, FPS benchmark и device/PC/WebGL профиль не выполнялись. Для следующего измерения: [Unity manual: optimizing draw calls](https://docs.unity3d.com/6000.0/Documentation/Manual/optimizing-draw-calls.html).

## Проверки и далее

Шесть целевых EditMode-тестов A1 прошли. Candidate validation прошла 24 случая: 3 tier × 8 направлений, включая aim, muzzle anchors, shot/recoil и flash reset. Локальный Editor MatchSession завершился через draft, battle и MatchResult; [бой](renders/unity/battle-integration.png), [MatchResult](renders/unity/battle-match-result.png), подробности — в [A1_HeavyTank_Battle_Integration](../../../../Docs/Features/A1_HeavyTank_Battle_Integration.md).

Открыты: решение о mapping tier к progression, новый live Fusion матч, PC/WebGL/device performance и FPS/batch profiling. Переход остальных юнитов на 3D не входит в эту интеграцию.
