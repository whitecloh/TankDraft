# Архитектура папок
Status: applied through AssetDatabase.MoveAsset; existing GUIDs retained
Last reviewed: 2026-09-13

## Собственный код и контент

Корень игрового проекта — `Assets/TankDraft`. Сначала разделяется ответственность, затем слой. Имена существующих asmdef сохранены, поэтому файловый перенос не переименовывает публичные сборки.

Standalone сервер размещается вне Unity Assets: `Backend/TankDraft.Game.Core` использует linked pure C# исходники; `TankDraft.Server.Security` отделяет identity/command boundary; `TankDraft.Server.Match` владеет scheduler/autochoices/snapshots; `TankDraft.Server.Persistence` оборачивает runtime долговечным journal/replay/receipts/outbox; `TankDraft.LocalHost` — локальный QA composition root. `TankDraft.Persistence.Probe` нужен только для process crash-тестов. Экспортированные данные — `Backend/Content`, QA limits — `Backend/Config`, исходная закреплённая зависимость с лицензией — `Backend/ThirdParty`. Экспорт/проверки — `Tools/Backend`, DB/build/cache/SDK — ignored `Logs` и backend `bin/obj`. В Unity core не добавляются зависимости на SQLite/ASP.NET/облачные SDK. Подробнее: [Backend_Implementation_Plan.md](Backend_Implementation_Plan.md).

| Путь относительно Assets/TankDraft | Назначение |
|---|---|
| Runtime/Shared/Contracts | Общие immutable контракты профиля, каталога, боя и навигации |
| Runtime/Meta/Application | Сценарии работы профиля/колоды |
| Runtime/Meta/Content | Authoring SO мета-контента и конверсия в definitions |
| Runtime/Meta/Infrastructure | JSON repository и настройки хранения профиля |
| Runtime/Meta/Presentation | Пассивные typed экраны/окна и presenter меню |
| Runtime/Meta/Bootstrap | Composition root меню |
| Runtime/Battle/Simulation | Чистый fixed-step LeoECS |
| Runtime/Battle/Content | Authoring боевых параметров |
| Runtime/Battle/Presentation | Пулы, юниты, снаряды, зоны, эффекты и viewport |
| Runtime/Match/Domain, Content, Presentation, Bootstrap | Состояния матча, configs, UI и orchestration |
| Runtime/Diagnostics/Battle, Photon | Лабораторный бой и transport probe; не production-match flow |
| Editor/Meta, Battle, Match, Networking, Previsualization | Editor-only authoring и проверки соответствующих систем |
| Configs/Meta/Rules, Catalogs, Profile, Units, Orders, UI | Правила меты, каталоги, seed/хранилище, отдельные типы контента и тексты |
| Configs/Battle/Rules, Units, Projectiles, Zones, Presentation, Scenarios | Боевые параметры по назначению |
| Configs/Match | Настройки и текст локального матча |
| Configs/Diagnostics/Photon, QA | Настройки диагностических инструментов и изолированных тестов |
| Prefabs/UI | Общие UI-элементы, typed экраны и окна; Match находится в UI/Match |
| Prefabs/Battle/Units, Projectiles, Zones, Effects | Сменные визуальные сущности боя |
| Prefabs/Diagnostics | Лабораторные UI и transport probe |
| Art/Battle/Primitives, Art/UI/Fonts, Art/UI/Previz | Исходные визуальные ассеты по назначению |
| Scenes/Frontend, Gameplay, Diagnostics | Меню, игровой матч и тестовые сцены |

`Assets/Foundation` остаётся отдельной общей библиотекой UI. Photon, DOTween, TextMesh Pro, Resources и Unity Settings сохраняют штатные корни и требования своих пакетов. Файлы SDK не переносятся в игровые feature-папки ради внешней симметрии.

## Правила дальнейшего расширения

- Новый компонент помещать в owning feature и слой; не возвращать общий `Scripts` или свалку `Configs` с разными назначениями.
- Повторяемые UI-объекты собирать общим prefab и LayoutGroup, а не копиями с независимыми координатами.
- Configs содержат авторские значения и ссылки. Runtime-состояние матча/профиля не сохраняется в SO. Новые production сущности создаются из подготовленных prefabs.
- Domain/Simulation не ссылаются на Unity, PUN, SO или View. Composition root связывает зависимости; View не выбирает правила игры.
- Редакторский инструмент обязан создавать контент в тех же папках, где его читает runtime/validator. Переносить asset через AssetDatabase.MoveAsset, сохранять .meta/GUID, обновлять инструмент и документацию в том же патче.
- Не переносить vendor SDK и специальные Unity-папки без отдельной технической причины. Не менять посторонний dirty worktree при наведении порядка.
- Логи, временные source staging, скриншоты и отчёты запусков — в ignored Logs. В Tools лежат воспроизводимые команды, в Docs/Features — контракты и результаты проверок.

Миграция сохранённых ассетов выполнена через Unity MCP. Таблица GUID до/после и проверка находятся в `Logs/TankDraftSetup/FolderMigration`; потерянных GUID — 0. Перенос LocalProfileSettings между сборками дополнен `MovedFrom`, формат JSON-профиля не изменялся.

Воспроизводимая проверка: `Tools/Project/verify-folder-layout.ps1`. После миграции проверены 149 исходных GUID; потерь нет. Проверка также контролирует уникальность GUID, ссылки/scripts собственных prefabs, логические папки configs и наличие build scenes.
