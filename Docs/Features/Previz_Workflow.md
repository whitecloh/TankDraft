# Инструмент превизов
Status: approved starting composition; production UI and final art pending
Last reviewed: 2026-09-12

## Текущий результат

`Tools/Previz/index.html` — локальный редактор разметки поверх кадров приоритетного второго видео. Всего 24 reference frames, 691 редактируемая область; первая typed production surface покрывает главную, ещё 23 frames/состояния остаются отдельным scope. Исходные кадры 576 × 1280 лежат в `Tools/Previz/References`, вне production Assets. Таймкоды и SHA-256 указаны в `References/provenance.json`, охват — в `Reference_Screen_Map.md`.

Подложка по умолчанию видна на 55%. Режимы: оригинал, оригинал с разметкой, только разметка. Панели, кнопки, текст, карточки и объекты можно скрывать по слоям. Клик выбирает самый маленький пересекающийся элемент; доступны перетаскивание, стрелки (1 px), Shift + стрелки (10 px), числовые координаты/размеры, добавление, дублирование, удаление и сброс окна.

Главная имеет authored группы `wallet` (ячейки 120 × 48, spacing 10) и `rewards` (81 × 87, spacing 10, `MiddleCenter`). Группы выбираются в списке; для них редактируются геометрия, alignment и type. Перетаскивание группы переносит всех её членов, а геометрия членов блокируется. Дублирование/удаление группы и изменение membership делаются через authored source; неподдержанные UI-операции явно отклоняются. Остальные 23 окна пока остаются плоскими.

Исходная геометрия хранится в `reference-layouts.js`, редактор — `trace-app.js`, проверка/формат — `trace-core.js`. Схематический инструмент предыдущего этапа сохранён в `schematic.html`: RU/EN, длинные строки и общие loading/error/empty/waiting fixtures остаются там. Они не выдаются за окна, наблюдавшиеся в видео.

Локальный запуск из корня проекта: `python -m http.server 8766 --bind 127.0.0.1 --directory Tools/Previz`. Адрес: `http://127.0.0.1:8766/`. Текущий сервер уже запущен; второй экземпляр не нужен. Статические файлы также можно открыть локально, но HTTP предпочтителен для PNG export.

## Точность и адаптация

Области размечены вручную по видимым границам кадра. Это редактируемые прямоугольные зоны UI и иллюстраций, не копия шрифтов, фигурных рамок, анимаций и игровых компонентов. Владелец сверил и принял стартовую композицию 2026-09-12; повторно утверждать ту же композицию перед production-вёрсткой не требуется. В динамическом бою размечены группы юнитов/эффектов; отдельные particles не становятся UI-элементами.

Viewport 576 × 1280 сохраняет исходные координаты. Для 1080 × 1920 и 1080 × 2400 кадр и геометрия масштабируются одинаково, без искажения пропорций, с полями при необходимости. Это сравнение композиции; responsive anchors и safe area устройств ещё не реализованы. Если пользователь вынес элемент в поле и смена пропорций выведет его за холст, размер отклоняется с сообщением.

## Экспорт и Unity

Экспортируются Layout JSON schemaVersion 1 или 2, PNG текущего вида и Markdown со списком координат. JSON также доступен в раскрываемой панели. Schema v1 остаётся совместимой; v2 добавляет `groups` и `parentId`. Полный контракт и пример v2 приведены в `Tools/Previz/SCHEMA.md`. Правки экранов сохраняются в памяти текущей страницы; перед закрытием нужен экспорт JSON. Произвольный кадр можно выбрать локальным файлом; сам файл не включается в JSON и должен передаваться вместе с ним.

Поле `reference` добавляет происхождение кадра и fitRect; Unity importer поддерживает v1 и v2. Подложка не попадает в production prefab. В v2 группы остаются корневыми (вложенные группы пока не поддержаны), а hierarchy дочерних элементов через `parentId` переносится в RectTransform. Кадры референса не включаются в игровой билд.

В Unity: Tools → TankDraft → Previz → Import Layout JSON. Выход строго `Assets/TankDraft/Art/UI/Previz`; существующий prefab не перезаписывается. Импорт создаёт UGUI placeholder в изолированной preview scene, без игрового runtime. `Assets/TankDraft/Art/UI/Previz` остаётся geometry QA и reference tracing, не production UI. `Arena_Grouped_Previz.prefab` и `_group_wallet_Item.prefab` / `_group_rewards_Item.prefab` — первая placeholder-вёрстка главной с nested shared prefabs, `LayoutElement` и `HorizontalLayoutGroup`; финального арта, ViewModel и game flow в них нет. Typed production foundation ведётся отдельно в `Assets/TankDraft/Prefabs/UI` и описана в `Docs/Features/UI_Conventions.md`.

Дальнейший перенос: сверенный кадр + JSON → authored UGUI prefab через MCP → screenshot с тем же viewport → проверка границ и состояний → привязка ViewModel/команд. Не переносить браузерную фабрику UI в игровой runtime.

## Повторяющиеся элементы

В production UGUI верхние валюты собираются `HorizontalLayoutGroup`, слоты наград — центрированным `HorizontalLayoutGroup`, сетка коллекции — `GridLayoutGroup`. Координаты кадров остаются ориентирами для восстановления общего шага, а не отдельными ручными настройками каждого элемента. Повторяющиеся слоты используют общий authored prefab, единые `LayoutElement` или `cellSize`, `spacing`, `padding` и `childAlignment`; явно authored исключения документируются. LayoutGroup управляет слотами, а эффекты и анимация живут во внутреннем child. После `Bind`/resize authored LayoutGroups замораживаются: rebuild идёт снизу вверх от каждой leaf-группы к root, включая root, чтобы plain `RectTransform` не прерывал UGUI traversal; в idle группы выключены.

Schema v2 и Unity importer уже создают корневые `horizontal`, `vertical` и `grid` LayoutGroup с shared item prefab. У группы единые `cellWidth`, `cellHeight`, `spacingX`, `spacingY`, четыре padding и один из девяти `TextAnchor` alignment. Непосредственные члены группы имеют один `kind`; они не могут принадлежать двум группам или одновременно иметь `parentId`.

## Выполненные проверки

Повтор Unity-проверки через открытый Editor: `Tools/Previz/verify-unity.ps1`. Скрипт создаёт отсутствующие именованные previz/QA-prefabs, существующие не перезаписывает; активная сцена сохраняется. Проверяющий C# хранится вне Assets в `Tools/Previz/Validation`. Дополнительно через MCP проверены отказы 11 некорректным JSON и отказ перезаписать существующий prefab; содержимое существующего файла сохранено.

- `node Tools/Previz/verify.cjs`: 24 reference frames, 72 layout, 27 проверок матрицы групп и обратное соответствие геометрии; legacy schematic suite сохраняет 1056 комбинаций.
- Через Unity MCP `PrevizImporter.ParseAndValidate` принял 100 JSON: 72 trace, 27 групповых и один legacy v1. Перестроены 28 фактических layout (755 RectTransform), подтверждены nested shared prefabs, сцена не изменилась, `CompilationFailed=False`.
- Настоящий UGUI capture `Logs/TankDraftSetup/Arena_Grouped_Unity.png` снят после исправления camera type на `Game`: Preview camera рендерила пустой результат. Это техническая проверка placeholder-вёрстки без финального арта, а не подтверждение production UI.
- В браузере визуально проверены наложения арены, драфта и коллекции; выбор кнопки и числовое редактирование координаты. Это выборочная визуальная проверка, не автоматическое доказательство попиксельного совпадения всех 24 кадров.

Генерация export Blob проверена обработчиками. Сохранение PNG/JSON на диск именно из встроенного браузера не подтверждено; после экспорта остаётся явная ссылка, JSON доступен текстом. Unity runtime, устройства и PvP этими проверками не покрываются.
