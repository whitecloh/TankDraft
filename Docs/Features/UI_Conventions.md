# UI Conventions
Status: main-menu runtime collection slice implemented; Editor and automated PlayMode harness passed
Last reviewed: 2026-09-12

Этот документ фиксирует переносимые UI-конвенции Chibi Arena для TankDraft. Он не утверждает готовность runtime, presenter, полного flow, локализации или финального арта.

## Scope и источник

- Источник конвенций: `U:\UNITY_PROJECTS\chibi_arena\Docs\Features\UI_Conventions.md` и `Gameplay_UI_Reference_Structure.md`.
- Перенесён только leaf-срез Foundation UI: `UIElement`, `UIScreen`, `UIWindow`, `UIPanel`, `UIItemView`, `UIButtonView`, `UIRegistry` и сопровождающий `LICENSE`.
- В TankDraft этот срез размещён в `Assets/Foundation/Runtime/UI`, assembly и namespace — `Vareiko.Foundation.UI`. Его asmdef использует только `Unity.ugui`. Собственные View размещены в `Assets/TankDraft/Runtime/Meta/Presentation`: assembly `TankDraft.Presentation`, namespace `TankDraft.UI`, зависимости — Foundation UI, UGUI и TMP. Полный Chibi `UIService`, DI, signals, async window manager и pools не переносились.
- Odin не используется. TMP Essentials импортирован; Unity Localization и production localization flow ещё не готовы.

## Иерархия и владение

- Каноническая иерархия: `UIScreen -> UIWindow -> UIPanel/UIItem -> UIItem`.
- Semantic Unity names (`UIRoot`, `UIMainMenuScreen`, `UIMainMenuHudWindow`, `UIMainMenuArenaWindow`, `UIMainMenuCurrenciesPanel`, `UIMainMenuRewardsPanel`, `UIMainMenuNavigationPanel`) и registry dot-qualified IDs — разные контракты. Имя объекта не заменяет id.
- `UIRoot` — единственный Canvas owner. Screen/window/panel не создают параллельные Canvas roots.
- Runtime UI — presentation layer: typed references, `Initialize(...)`, passive `Bind/Refresh(model)`, `Show/Hide`, `Clear/Release`; gameplay decisions принадлежат command/presenter layer.
- Первая TankDraft typed основа: `UIRoot`, `UIMainMenuScreen`, `UIMainMenuHudWindow`, `UIMainMenuArenaWindow`, `UIMainMenuCurrenciesPanel`, `UIMainMenuRewardsPanel`, `UIMainMenuNavigationPanel` и три shared prefabs находятся в `Assets/TankDraft/Prefabs/UI`.

## Layout, TMP и повторяющиеся элементы

- Authored repeated slots используют shared prefab и `LayoutGroup` + `LayoutElement` (`HorizontalLayoutGroup`, `VerticalLayoutGroup` или `GridLayoutGroup` по задаче). Исключения должны быть осознанно authored.
- На первой главной валюты имеют cell 120×48, награды — 81×87; обе группы используют spacing 10 и MiddleCenter. Нижняя навигация — пять shared buttons: активная вкладка имеет ширину 148, остальные — 107. Размеры задаются LayoutElement, размещение — группой.
- Dynamic groups замораживаются после `Bind`/resize: layout включается только на bind/resize, затем для каждого authored `LayoutGroup` rebuild выполняется снизу вверх от leaf-групп к root, потому что обычный `RectTransform` может прервать UGUI traversal; после этого группы снова отключаются в idle.
- Runtime collection list использует prefab-backed pool от подготовленного template; reuse очищает owned callbacks. Ручные `List + EnsureItem` и Inspector-filled runtime pools не становятся контрактом.
- User-facing text размещается через `TMP_Text` references. Локализация и ownership текста будут добавлены отдельным контрактом; нельзя заранее смешивать localizer и code-driven `SetText` на одном TMP.
- Decorative `Image`/`Text` surfaces не принимают raycast; input принадлежит typed buttons и modal backdrop.

## Статус и границы

- `Assets/TankDraft/Art/UI/Previz` остаётся geometry QA и reference tracing; его placeholder prefabs не являются production UI.
- `PASS 134` — историческая pre-runtime EditMode harness-проверка первого typed root. Актуальный `TypedMainMenuValidation` завершён с `PASS 264`; helper: `Assets/TankDraft/Editor/Meta/TypedMainMenuValidation.cs` (`public Run`). `Tools/Previz/verify-main-menu.ps1` по умолчанию запускает проверки структуры и профиля; с `-PlayMode` запускает асинхронный runtime harness, результат — в status.txt. `verify-typed-ui.ps1` сохранён для отдельной проверки структуры.
- `ProfileBehaviorValidation` завершён с `PASS 38`. `MainMenuPlayModeValidation` завершён с `PASS 31`: два cold start, сохранение unit/order, gates трёх slots, pool reuse, возврат на главную, 576×1280/576×1024, прокрутка и safe-area anchors. Это автоматизированный Editor PlayMode harness через реальные `Button.onClick`, не Test Runner, device, network или ручной touch QA. Evidence: `Logs/TankDraftSetup/RuntimeQA/status.txt` и шесть numbered screenshots.
- Всего reference frames — 24; main-menu runtime collection slice создан, но ещё 23 reference frames/состояния, production localization, полный art pass и device QA не готовы; локальный battle/match flow реализован отдельно (Local_Match.md).
- Не копировать Foundation целиком и не считать базовые компоненты самодостаточным UI-service модулем без их реальных DI/signal/async/pool dependencies.

## Локальный Match UI

`Runtime/Match/Presentation` использует UIMatchScreen → UIMatchHudWindow / UIMatchDraftWindow → UIPanel карточек. Общий MatchCardView prefab задаёт размеры через LayoutElement. Draft окно управляет пересборкой HorizontalLayoutGroup на Bind/resize и отключает группу в idle. Баланс и переходы принадлежат MatchService/MatchSession; окна получают только ViewModel и callbacks.
