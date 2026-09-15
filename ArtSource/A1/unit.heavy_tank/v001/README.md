# A1 pilot — `unit.heavy_tank` v001

Статус: первый локальный пилот собран в Blender. Это путь Codex + `bpy`: превиз → согласованный turnaround → параметрическая 3D-геометрия → проверка Blender → экспорт. Внешний image-to-3D не использовался.

## Входы и исходники

- [Канонический crop](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/reference/canonical.png) — зафиксированный вход из утверждённого A1-листа.
- [Turnaround](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/views/turnaround.png) и [текст запроса](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/views/prompt.txt) — согласование ракурсов.
- Ракурсы получены встроенным ImageGen Codex и механической нарезкой; это не автоматическая 3D-конвертация PNG.
- [Скрипт сборки](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/blender/build_heavy_tank.py), [Blender-исходник](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/blender/heavy_tank_work.blend), [FBX](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/export/HeavyTank_A1.fbx) и [GLB](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/export/HeavyTank_A1.glb).

Сборка выполнена portable Blender 5.2.1: `C:/Users/ramze/.codex/tools/blender/blender-5.2.1-windows-x64/blender.exe`.

## Текущий результат

- [Beauty-рендер](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/renders/beauty.png); [turntable-видео](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/renders/heavy-tank-turntable.mp4): 4 секунды, поворот модели и башни, отдача ствола, рендер Blender.
- [Native Blender validation](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/export/native-validation.json) прошла: поза, контакт с землёй, anchors, башня и ствол проверены в Blender.
- [FBX round-trip](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/export/fbx-roundtrip.json) прошёл как импорт FBX обратно в Blender; это не Unity-приёмка.
- Бюджет из [metrics](U:/UNITY_PROJECTS/TankDraft/ArtSource/A1/unit.heavy_tank/v001/export/metrics.json): 1 440 треугольников основы и 2 880 вместе с контуром; всего 6 мешей (3 основы и 3 контура).
- Сейчас задействованы шесть общих материалов палитры. Toon-превиз Blender не является Unity-шейдером; Unity material/profile ещё впереди.

## Что проверено и что остаётся

Проверены Blender-геометрия, pose, ground contact, anchors и FBX import smoke. Не проверены Unity-импорт, prefab/catalog, presentation, реальный бой, устройство и производительность. Поэтому этот пилот не объявляет готовность 3D-представления в Unity.
