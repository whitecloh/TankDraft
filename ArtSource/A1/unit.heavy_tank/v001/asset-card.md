# Карточка производственного ассета A1 — `Тяжёлый танк`

> Рабочая карточка первого пилота. Результат Blender не подтверждает готовность Unity или runtime.

## Статус ассета

- [x] ReferenceReady
- [x] ViewsReady
- [x] Raw3DReady
- [ ] OptimizedReady
- [ ] UnityPreviewReady
- [ ] Integrated
- [ ] RuntimeAccepted

## Идентичность и утверждённый источник A1

- Название: `Тяжёлый танк`
- Stable contentId: `unit.heavy_tank`
- Тип / роль: `тяжёлый танк`
- Утверждённый источник A1: `Tools/Previz/Approved/A1-Light/technique-reference/sheet-1.png`
- Лист техники и ячейка: `sheet-1, row-1, column-2`
- Канонический crop: `ArtSource/A1/unit.heavy_tank/v001/reference/canonical.png`
- Лист техники: `Tools/Previz/Approved/A1-Light/technique-reference/sheet-1.png`
- Вид спереди: `ArtSource/A1/unit.heavy_tank/v001/views/front.png`
- Вид сзади: `ArtSource/A1/unit.heavy_tank/v001/views/rear.png`
- Вид слева: `ArtSource/A1/unit.heavy_tank/v001/views/left.png`
- Вид справа: `ArtSource/A1/unit.heavy_tank/v001/views/right.png`
- Вид сверху: `ArtSource/A1/unit.heavy_tank/v001/views/top.png`
- Вид 3/4: `ArtSource/A1/unit.heavy_tank/v001/views/three-quarter.png`

## Геометрия

- Инварианты силуэта и пропорций: корпус, траки, башня и ствол собраны отдельными контролируемыми частями по turnaround.
- Габариты в авторской системе: bounds из native validation: `[-1.0887, -1.2185, 0.0]` → `[1.0887, 1.6200, 1.7000]`.
- Габариты в Unity: N/A — Unity-импорт не выполнен.
- Единицы, базовый масштаб и допуск: оси Blender: Z-up, forward +Y; FBX экспорт: forward -Z, up Y, apply unit.
- Ограничения на детали, контур и читаемость роли: светлый A1, чёрный отдельный контур; проверка с игровой камеры впереди.

## Генерация и исходные материалы

- Провайдер генератора: N/A — локальная сборка Codex + `bpy`; ракурсы: встроенный ImageGen Codex.
- Версия генератора: N/A для 3D.
- Дата запуска: 2026-09-15.
- Настройки: `views/prompt.txt`; параметры геометрии — `blender/build_heavy_tank.py`.
- Seed: N/A.
- Job ID: N/A.
- Хеши входных файлов: `reference/source.json` и `views/views-manifest.json`.
- Raw export: `export/HeavyTank_A1.fbx`, `export/HeavyTank_A1.glb`.
- Сохранённые артефакты: `reference/`, `views/`, `blender/`, `export/`, `renders/`.

## Blender

- Версия Blender: portable Blender 5.2.1.
- Исходный файл: `blender/heavy_tank_work.blend`.
- Состав частей: корпус, траки, башня, ствол и отдельная геометрия контура.
- Pivots и их назначение: `TurretRoot`, `BarrelRecoil`, `MuzzleAnchor`, `HitAnchor`; см. `export/native-validation.json`.
- Оси и ориентация: Z-up, forward +Y в Blender; FBX forward -Z/up Y.
- Треугольники основы: 1440.
- Треугольники контура: 1440; всего с контуром 2880.
- Материалы: шесть общих материалов палитры: milk, offwhite, charcoal, gray, blue, outline black emission.
- Память текстур: N/A — текстурный бюджет и Unity profile не определены.

## Unity

- FBX: `export/HeavyTank_A1.fbx`.
- Import Preset: N/A — Unity-импорт не выполнен.
- Материал: N/A — текущий toon preview Blender не является Unity-шейдером.
- Prefab: N/A.
- Каталог: N/A.
- GUID до импорта: N/A.
- GUID после импорта: N/A.

## Presentation и бой

- Версия presentation adapter: N/A — не интегрирован.
- Pool reset: N/A.
- VFX: N/A.
- Маркировка команды: цветная часть палитры в Blender-preview; Unity binding не сделан.
- HUD: N/A.

## Визуальные и производительные проверки

| Поле | Baseline | Новый ассет | Evidence |
| --- | --- | --- | --- |
| Целевое устройство | `<значение>` | `<значение>` | `<link>` |
| Разрешение | `<значение>` | `<значение>` | `<link>` |
| Количество юнитов | `<значение>` | `<значение>` | `<link>` |
| CPU | `<значение>` | `<значение>` | `<link>` |
| GPU | `<значение>` | `<значение>` | `<link>` |
| Память | `<значение>` | `<значение>` | `<link>` |
| Draw calls | `<значение>` | `<значение>` | `<link>` |

## Доказательства и review владельца

- Ссылки на evidence: `export/native-validation.json`, `export/fbx-roundtrip.json`, `export/metrics.json`, `renders/beauty.png`.
- Review визуала владельцем: ожидается.
- Это поле не является автоматическим approval gate.

## Нерешённое

- Оптимизация геометрии уложена в текущий бюджет, но Unity material/profile, импорт, prefab/catalog и runtime/device QA ещё не выполнены.
