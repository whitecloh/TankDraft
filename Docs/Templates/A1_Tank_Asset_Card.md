# Карточка производственного ассета A1 — `<tank-name>`

> Шаблон, не доказательство готовности; поля существующих API сверять с гайдлайном [Docs/Features/A1_Tank_Production_Guideline.md](../Features/A1_Tank_Production_Guideline.md).

## Статус ассета

- [ ] ReferenceReady
- [ ] ViewsReady
- [ ] Raw3DReady
- [ ] OptimizedReady
- [ ] UnityPreviewReady
- [ ] Integrated
- [ ] RuntimeAccepted

## Идентичность и утверждённый источник A1

- Название: `<tank-name>`
- Stable contentId: `<content-id>`
- Тип / роль: `<role>`
- Утверждённый источник A1: `Tools/Previz/Approved/A1-Light/<path>`
- Лист техники и ячейка: `<sheet-N, row-N, column-N>`
- Канонический crop: `<path-or-uri>`
- Лист техники: `<path-or-uri>`
- Вид спереди: `<path-or-uri>`
- Вид сзади: `<path-or-uri>`
- Вид слева: `<path-or-uri>`
- Вид справа: `<path-or-uri>`
- Вид сверху: `<path-or-uri>`
- Вид 3/4: `<path-or-uri>`

## Геометрия

- Инварианты силуэта и пропорций: `<описать>`
- Габариты в авторской системе: `<length × width × height>`
- Габариты в Unity: `<meters or scale>`
- Единицы, базовый масштаб и допуск: `<описать>`
- Ограничения на детали, контур и читаемость роли: `<описать>`

## Генерация и исходные материалы

- Провайдер генератора: `<пусто до фиксации>`
- Версия генератора: `<пусто до фиксации>`
- Дата запуска: `<пусто до фиксации>`
- Настройки: `<пусто до фиксации>`
- Seed: `<пусто до фиксации>`
- Job ID: `<пусто до фиксации>`
- Хеши входных файлов: `<пусто до фиксации>`
- Raw export: `<path-or-uri>`
- Сохранённые артефакты: `<paths-or-uris>`

## Blender

- Версия Blender: `<version>`
- Исходный файл: `<path-or-uri>`
- Состав частей: `<hull / tracks / turret / barrel / role-parts>`
- Pivots и их назначение: `<описать>`
- Оси и ориентация: `<описать>`
- Треугольники основы: `<count>`
- Треугольники контура: `<count>`
- Материалы: `<names-and-purpose>`
- Память текстур: `<estimate-and-method>`

## Unity

- FBX: `<path-or-uri>`
- Import Preset: `<path-or-uri>`
- Материал: `<path-or-uri>`
- Prefab: `<path-or-uri>`
- Каталог: `<path-or-uri>`
- GUID до импорта: `<guid-or-n/a>`
- GUID после импорта: `<guid-or-n/a>`

## Presentation и бой

- Версия presentation adapter: `<version>`
- Pool reset: `<описать / evidence>`
- VFX: `<описать / paths>`
- Маркировка команды: `<описать>`
- HUD: `<описать>`

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

- Ссылки на evidence: `<links>`
- Review визуала владельцем: `<дата / ссылка / комментарий>`
- Это поле не является автоматическим approval gate.

## Нерешённое

- `<вопрос или риск>`
