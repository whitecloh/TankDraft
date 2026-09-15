# SLVR UI Motion Kit

Переносимый UGUI motion package для Chibi Arena и других SLVR-проектов.

## Требования

- Unity `6000.3`;
- UGUI `2.0`;
- Free DOTween, установленный в host project;
- `DOTweenSettings.asset`, созданный через DOTween Utility Panel.

DOTween не вендорится внутрь пакета. Runtime assembly напрямую ссылается на `DOTween.dll` и
использует только core API, поэтому DOTween UI/TMP modules не являются обязательными для Phase 1.

Подробности: [installation.md](Documentation~/installation.md).

## Phase 2 components

- `UiAnimatedButton`: mouse/pen hover, touch-safe press, keyboard/gamepad focus/submit,
  disabled state, semantic feedback и optional focus/shadow/icon/sheen visuals;
- `UiHoverLiftMotion`: reusable `LiftAndParallax` и спокойный `ScaleWave` для карточек с focus fallback и без `Update`;
- `UiPanelTransition`, `UiModalBackdrop`, `UiScreenTransition`; `RewardPop` поддерживает глубокий
  стартовый scale и трёхфазный spring settle;
- `UiStagedReveal`: последовательные fade/scale-шаги для строк статов и popup-контента;
- `UiTypewriterText`: быстрый TMP typewriter через `maxVisibleCharacters`, без substring-аллокаций;
- `UiScrollRectMotion`: reset/center navigation и мягкое top-to-bottom раскрытие существующего `RectMask2D`
  без изменения размеров viewport/content; стартовая видимая доля viewport настраивается отдельно;
- `UiCardCarouselMotion`: snap/drag carousel и constant three-slot loop с off-screen recycle/rebind;
- `UiNavigationItem` / `UiTabMotion`, `UiCardSelectionMotion`, `UiListStagger`;
- `UiTooltipMotion` с hover delay, focus/touch API, Canvas bounds и popup fade/scale entrance;
- explicit VisualRoot/setup commands и edit-time DOTween Preview.

## Phase 3 ambient runtime

`UiIdleMotion` поддерживает breathing, bob, gentle/continuous rotation, glow pulse и semantic
shimmer trigger. Каждый loop проходит общий admission budget, использует deterministic phase
delay и останавливается с освобождением lease при disable, logical hide или modal suppression.

Для CanvasGroup-hidden объектов, которые остаются active, host вызывает `SetVisible(false)`;
повторное `SetVisible(true)` заново применяет quality, intensity и accessibility policy.

`UiAttentionPulse` предоставляет nudge, pulse, error shake, bounce и highlight ring. Компоненты с одним
scope root разделяют единый attention-слот: разные semantic key ставятся в очередь, а повторные запросы
объединяются и перезапускают активный эффект с повышением приоритета. При Reduced Motion перемещение и
shake заменяются highlight ring; error-состояние дополнительно может использовать color, outline и message
fallback до явного `ClearErrorFallback()`.

`UiShaderEffectGraphic` назначает кэшируемые варианты `SLVR/UI/Shimmer`, `SLVR/UI/GradientFlow` и
`SLVR/UI/SoftGlowOverlay` на обычный UGUI `Graphic`. Варианты с одинаковыми параметрами разделяют material;
Low quality и accessibility-настройки оставляют статичную фазу. Все три шейдера поддерживают `Mask`,
`RectMask2D`, sprite atlas alpha и alpha clip. Подробности и batching tradeoffs:
[shaders-and-materials.md](Documentation~/shaders-and-materials.md).

`UiNotificationBadge` показывает count или dot и запускает one-shot entrance только при zero→positive или
увеличении. Rapid updates объединяются в текущую animation, hidden badge не продолжает motion. Urgent idle
pulse является explicit opt-in, по умолчанию достигает scale `1.25`, использует Accent budget и отключается
на Low quality/Reduced Motion. UGUI
`Text` поддерживается напрямую; TMP или host label подключается через `DisplayTextChanged`.

`UiTransientMessage` показывает локализованный host-текст через короткий fade/scale pop, удерживает его
заданное время и lifecycle-safe возвращает скрытое состояние. Компонент не хранит строки и подходит для
validation feedback, ошибок формы и кратких contextual hints.

Editor input smoke покрыт автоматическими PlayMode tests. Android touch и Steam hardware input
проверяются повторно в общем pre-Phase 6 smoke gate.

## Phase 4–5: values, rewards and authoring

- `UiNumberTicker`, `UiProgressMotion`, `UiCurrencyFlyEffect`, `UiCardTransferMotion`, `UiTransientMessage`,
  `UiSelectiveGraphicDimming`, `UiRewardSequence`;
- общий `UiComponentPool<T>` с возвратом при complete/skip/destroy;
- `IUiShowcaseAdapter`, `IUiMotionAudioFeedback`, `IUiMotionHaptics` и optional Spine sample;
- 15 placeholder prefab’ов и standalone `UI Motion Gallery` sample;
- report-first команды `Audit Selected Canvas` и `Audit All UI In Open Scenes`.

Начать подключение к статическому окну: [quick-start.md](Documentation~/quick-start.md).
Компоненты: [components.md](Documentation~/components.md).
Samples и аудит: [samples-and-audit.md](Documentation~/samples-and-audit.md).
Интеграции: [integrations.md](Documentation~/integrations.md).
Платформы и accessibility: [performance-platforms-accessibility.md](Documentation~/performance-platforms-accessibility.md).
